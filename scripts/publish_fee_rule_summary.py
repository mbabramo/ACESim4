"""Publish Table 5 from all saved cost levels, without explanatory text.

python -B scripts/publish_fee_rule_summary.py --article-directory PATH
Uses the article's existing LuaLaTeX table style; requires lualatex and pdftoppm.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

import build_fee_rule_comparisons as comparisons


TITLE = "Table 5 - Overall results summary"
STEM = "overall-results-summary"
ROW_ORDER = (
    ("Filing share", "Filing share"),
    ("Answering share", "Answering share"),
    ("Meritorious-plaintiff shortfall", "Meritorious-P shortfall"),
    ("Nonliable-defendant burden", "Nonliable-D burden"),
    ("Liable-defendant excess burden", "Liable-D excess burden"),
    ("Gross outcome error", "Gross outcome error"),
    ("Total expenditures", "Total expenditures"),
)


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def latex(counts: list[dict]) -> str:
    lookup = {(r["risk_preferences"], r["first_fee_rule"], r["second_fee_rule"], r["outcome"]): r
              for r in counts}
    lines = [r"\documentclass[10pt,border=5pt,varwidth=17cm]{standalone}",
             r"\usepackage[T1]{fontenc}",
             r"\usepackage{lmodern,booktabs,tabularx,array,amsmath}",
             r"\begin{document}", r"\begingroup", r"\small",
             r"\setlength{\tabcolsep}{5pt}", r"\renewcommand{\arraystretch}{1.20}"]
    for risk, label in zip(comparisons.RISKS, ("Risk neutral", "Risk averse")):
        lines += [r"\noindent\textbf{" + label + r"}\par\smallskip",
                  r"\noindent\begin{tabularx}{\linewidth}{@{}>{\raggedright\arraybackslash}Xccc@{}}",
                  r"\toprule",
                  r"\textbf{Outcome} & \textbf{American $\to$ Trial} & \textbf{American $\to$ Complete} & \textbf{Trial $\to$ Complete} \\",
                  r"\midrule"]
        for metric, row_label in ROW_ORDER:
            cells = []
            for first, second in comparisons.PAIRS:
                count = lookup[risk, first, second, metric]
                cells.append(" / ".join(str(count[d]) for d in comparisons.DIRECTIONS))
            lines.append(row_label + " & " + " & ".join(cells) + r" \\")
        lines += [r"\bottomrule\end{tabularx}\par\medskip"]
    lines += [r"\endgroup", r"\end{document}", ""]
    return "\n".join(lines)


def run(command: list[str], work: Path) -> None:
    result = subprocess.run(command, cwd=work, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("Command failed: " + command[0] + "\n" + result.stdout[-5000:] + result.stderr[-2000:])


def publish(article: Path) -> dict:
    article = article.resolve()
    source = article / "Results/Aggregated Data/Sources/welfare-outcomes.csv"
    cases = comparisons.read_cases(source)
    counts = comparisons.summarize(comparisons.compare(cases, comparisons.DEFAULT_TOLERANCE))
    order = {metric: index for index, (metric, _) in enumerate(ROW_ORDER)}
    counts.sort(key=lambda r: (comparisons.RISKS.index(r["risk_preferences"]),
                              order[r["outcome"]],
                              comparisons.PAIRS.index((r["first_fee_rule"], r["second_fee_rule"]))))
    counts_text = comparisons.csv_text(counts)
    tex = latex(counts)
    denominators = {risk: len({(c.family, c.cost) for c in cases if c.risk == risk}) for risk in comparisons.RISKS}
    record = {
        "title": TITLE,
        "source": {"path": str(source.relative_to(article)), "sha256": sha(source)},
        "generators": [{"path": str(path), "sha256": sha(path)} for path in
                       (Path(__file__).resolve(), Path(comparisons.__file__).resolve())],
        "included_costs": [comparisons.decimal_text(c) for c in sorted({c.cost for c in cases})],
        "excluded_costs": [],
        "included_cases": len(cases), "matched_settings_by_risk": denominators,
        "difference": "second fee rule minus first fee rule",
        "count_order": list(comparisons.DIRECTIONS),
        "tie_tolerance": str(comparisons.DEFAULT_TOLERANCE),
        "row_order": [metric for metric, _ in ROW_ORDER],
        "participation_denominator": "all potential disputes",
        "answering_share_definition": "share of potential disputes that are filed and answered (D Answers)",
        "explanatory_text": False, "counts": counts,
    }
    table = article / "Tables"
    manifest_path = article / "manuscript-exhibits.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    with tempfile.TemporaryDirectory(prefix="fee-rule-summary-") as directory:
        work = Path(directory)
        (work / (STEM + ".tex")).write_text(tex, encoding="utf-8", newline="\n")
        (work / (STEM + ".csv")).write_text(counts_text, encoding="utf-8", newline="\n")
        (work / (STEM + ".json")).write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        previous_tex = table / "Sources" / (TITLE + ".tex")
        reuse_renders = (previous_tex.is_file()
                         and previous_tex.read_text(encoding="utf-8-sig") == tex
                         and all((table / (TITLE + ext)).is_file() for ext in (".pdf", ".png")))
        prior = {Path(e['Output']).suffix: e for e in manifest['Exhibits'] if e['Exhibit'] == TITLE}
        reuse_renders = reuse_renders and all(
            ext in prior and Path(prior[ext]['Output']) == path.relative_to(article)
            and prior[ext]['Sha256'] == sha(path)
            for ext, path in [('.tex', previous_tex), ('.pdf', table / (TITLE + '.pdf')),
                              ('.png', table / (TITLE + '.png'))])
        if not reuse_renders:
            run(["lualatex", "-interaction=nonstopmode", "-halt-on-error", STEM + ".tex"], work)
            log = (work / (STEM + ".log")).read_text(encoding="utf-8", errors="replace")
            if "Overfull" in log or "Missing character" in log:
                raise ValueError("Table has a layout/font warning; inspect the LaTeX layout")
            run(["pdftoppm", "-singlefile", "-r", "180", "-png", STEM + ".pdf", STEM], work)
        (table / "Sources").mkdir(parents=True, exist_ok=True)
        entries = []
        for extension in (".pdf", ".png", ".tex", ".json", ".csv"):
            destination = (table if extension in (".pdf", ".png") else table / "Sources") / (TITLE + extension)
            if not (reuse_renders and extension in (".pdf", ".png")):
                shutil.copy2(work / (STEM + extension), destination)
            entries.append({"Exhibit": TITLE, "Source": str(source.relative_to(article)),
                            "Derived": True, "SourceSha256": sha(source),
                            "Output": str(destination.relative_to(article)), "Sha256": sha(destination)})
    manifest["Exhibits"] = [e for e in manifest["Exhibits"] if e["Exhibit"] != TITLE] + entries
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return {"pdf": str(table / (TITLE + ".pdf")), "counts": len(counts),
            "settings_by_risk": denominators}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--article-directory", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(publish(args.article_directory), indent=2))


if __name__ == "__main__":
    main()

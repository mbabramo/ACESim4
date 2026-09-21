"""Build full-grid and cost-filtered pairwise tables from saved article results.

Python 3.10+; standard library only. Does not solve games or modify source results.
See Fee-rule-comparisons.md for the command, output schema and interpretation.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
import hashlib
import io
import json
from pathlib import Path


RISKS = ("Risk Neutral", "Moderately Risk Averse")
RULES = ("American", "Trial Fee-Shifting", "Complete Fee-Shifting")
PAIRS = ((RULES[0], RULES[1]), (RULES[0], RULES[2]), (RULES[1], RULES[2]))
DIRECTIONS = ("Lower", "Tied", "Higher")
METRICS = (
    ("Meritorious-plaintiff shortfall", "Plaintiff shortfall contribution", "damages per potential dispute"),
    ("Nonliable-defendant burden", "Nonliable defendant contribution", "damages per potential dispute"),
    ("Liable-defendant excess burden", "Liable defendant contribution", "damages per potential dispute"),
    ("Total expenditures", "Real Litigation Costs", "damages per potential dispute"),
    ("Gross outcome error", "Outcome Error Before Legal Costs and Fee Transfers", "damages per potential dispute"),
    ("Filing share", "P Files", "probability per potential dispute"),
    ("Answering share", "D Answers", "probability per potential dispute"),
)
# These settings must agree across fee rules within a matched comparison.
TEXT_SETTINGS = (
    "Specification", "Signal Structure", "Quality Distribution", "Quality-Truth Link",
    "Liability Signal Shaping", "Damages Signal Shaping", "Allow Abandon and Defaults",
    "Filing and Answering", "Integration Method", "Noise to Produce Case Strength",
    "Quadrature Order",
)
NUMBER_SETTINGS = (
    "Number of Offers", "Number of Signals", "Number of Court Signals", "CARA Alpha",
    "Party Signal Sigma", "Court Signal Sigma", "Probability Truly Liable",
    "Proportion of Costs at Beginning", "Relative Costs",
)
REQUIRED = frozenset(("Comparison Family", "Risk Aversion", "Costs Multiplier",
                      "Comparison Fee Rule", "OptionSetName", "Filter") +
                     TEXT_SETTINGS + NUMBER_SETTINGS + tuple(m[1] for m in METRICS))
DEFAULT_TOLERANCE = Decimal("0.00001")


@dataclass(frozen=True)
class Case:
    family: str
    risk: str
    cost: Decimal
    rule: str
    option_set: str
    values: tuple[Decimal, ...]
    settings: tuple


def number(value: str, context: str) -> Decimal:
    try:
        result = Decimal(value)
    except (InvalidOperation, TypeError, ValueError) as error:
        raise ValueError(f"Invalid numeric value for {context}: {value!r}") from error
    if not result.is_finite():
        raise ValueError(f"Nonfinite value for {context}: {value!r}")
    return result


def decimal_text(value: Decimal) -> str:
    text = format(value, "f")
    return text.rstrip("0").rstrip(".") if "." in text else text


def direction(delta: Decimal, tolerance: Decimal) -> str:
    if delta < -tolerance:
        return "Lower"
    if delta > tolerance:
        return "Higher"
    return "Tied"


def read_cases(source: Path) -> list[Case]:
    cases, seen_options, groups = [], set(), defaultdict(dict)
    with source.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        headers = reader.fieldnames or []
        if len(headers) != len(set(headers)):
            raise ValueError("Duplicate source column names")
        missing = REQUIRED - set(headers)
        if missing:
            raise ValueError("Missing source columns: " + ", ".join(sorted(missing)))
        for line, row in enumerate(reader, 2):
            context = f"source row {line}"
            if None in row or any(row.get(k) is None or not row[k].strip() for k in REQUIRED):
                raise ValueError(f"Missing values or malformed CSV at {context}")
            if row["Filter"] != "All":
                raise ValueError(f"Expected population Filter=All at {context}")
            family, risk, rule, option = (row[k] for k in (
                "Comparison Family", "Risk Aversion", "Comparison Fee Rule", "OptionSetName"))
            if risk not in RISKS or rule not in RULES:
                raise ValueError(f"Unknown risk preference or fee rule at {context}")
            if option in seen_options:
                raise ValueError(f"Duplicate option set: {option}")
            seen_options.add(option)
            cost = number(row["Costs Multiplier"], context + " cost")
            if cost <= 0:
                raise ValueError(f"Cost multiplier must be positive at {context}")
            values = tuple(number(row[column], context + " " + column) for _, column, _ in METRICS)
            if any(value < 0 or value > 1 for value in values[-2:]):
                raise ValueError(f"Filing/answering probability outside [0,1] at {context}")
            if values[-1] > values[-2]:
                raise ValueError(f"Answering share exceeds filing share at {context}")
            settings = tuple(row[k] for k in TEXT_SETTINGS) + tuple(
                number(row[k], context + " " + k) for k in NUMBER_SETTINGS)
            prior = number(row["Probability Truly Liable"], context + " truth prior")
            if not 0 < prior < 1:
                raise ValueError(f"Truth prior must be strictly between zero and one at {context}")
            case = Case(family, risk, cost, rule, option, values, settings)
            key = (family, risk, cost)
            if rule in groups[key]:
                raise ValueError(f"Duplicate fee-rule cell: {key}, {rule}")
            groups[key][rule] = case
            cases.append(case)
    if not cases:
        raise ValueError("The source contains no cases")
    for key, members in groups.items():
        if set(members) != set(RULES):
            raise ValueError(f"Incomplete fee-rule comparison: {key}")
        reference = members[RULES[0]]
        if any(case.settings != reference.settings for case in members.values()):
            raise ValueError(f"Non-fee settings differ across rules: {key}")
    by_family_cost = defaultdict(set)
    for family, risk, cost in groups:
        by_family_cost[family, cost].add(risk)
    for key, preferences in by_family_cost.items():
        if preferences != set(RISKS):
            raise ValueError(f"Incomplete preference coverage: {key}")
    return sorted(cases, key=lambda c: (RISKS.index(c.risk), c.cost, c.family, RULES.index(c.rule)))


def filter_costs(cases: list[Case], excluded: set[Decimal]) -> list[Case]:
    available = {case.cost for case in cases}
    absent = excluded - available
    if absent:
        raise ValueError("Excluded costs absent from source: " + ", ".join(
            decimal_text(x) for x in sorted(absent)))
    retained = [case for case in cases if case.cost not in excluded]
    if not retained:
        raise ValueError("Cost exclusion would remove every case")
    return retained


def compare(cases: list[Case], tolerance: Decimal) -> list[dict]:
    if not tolerance.is_finite() or tolerance < 0:
        raise ValueError("Tie tolerance must be finite and nonnegative")
    groups = defaultdict(dict)
    for case in cases:
        groups[case.family, case.risk, case.cost][case.rule] = case
    records = []
    for (family, risk, cost), members in groups.items():
        for first, second in PAIRS:
            a, b = members[first], members[second]
            for index, (label, field, units) in enumerate(METRICS):
                delta = b.values[index] - a.values[index]
                records.append({
                    "model_family": family, "risk_preferences": risk,
                    "cost_multiplier": decimal_text(cost), "first_fee_rule": first,
                    "second_fee_rule": second, "outcome": label, "source_column": field,
                    "units": units, "first_value": str(a.values[index]),
                    "second_value": str(b.values[index]), "second_minus_first": str(delta),
                    "direction": direction(delta, tolerance),
                    "first_option_set": a.option_set, "second_option_set": b.option_set,
                })
    return records


def summarize(records: list[dict]) -> list[dict]:
    counts = defaultdict(Counter)
    for record in records:
        counts[record["risk_preferences"], record["first_fee_rule"],
               record["second_fee_rule"], record["outcome"]][record["direction"]] += 1
    return [{"risk_preferences": risk, "first_fee_rule": first, "second_fee_rule": second,
             "outcome": metric, **{key: counts[risk, first, second, metric][key] for key in DIRECTIONS},
             "matched_settings": sum(counts[risk, first, second, metric].values())}
            for risk in RISKS for first, second in PAIRS for metric, _, _ in METRICS]


def csv_text(records: list[dict]) -> str:
    buffer = io.StringIO(newline="")
    writer = csv.DictWriter(buffer, fieldnames=list(records[0]), lineterminator="\n")
    writer.writeheader()
    writer.writerows(records)
    return buffer.getvalue()


def case_details_markdown(title: str, records: list[dict], counts: list[dict],
                          tolerance: Decimal) -> str:
    """Identify opposite-direction cases and ties without imposing a hypothesis."""
    groups = defaultdict(list)
    for row in records:
        groups[row["risk_preferences"], row["first_fee_rule"],
               row["second_fee_rule"], row["outcome"]].append(row)
    lines = [f"# {title}: exceptions and ties", "",
             "Counts are **decreases / ties / increases**, for second fee rule minus first. "
             "For each outcome, the tables list cases going against the more frequent strict "
             "direction. Ties are listed separately. When decreases and increases are equally "
             "frequent, all strict cases are shown and no dominant direction is assigned. "
             "Frequency describes this design grid; it does not establish a universal finding "
             "or statistical significance.", "",
             "Values use the same source columns and units as the main comparison table. "
             "The party burdens are population-weighted contributions. All displayed values "
             "retain source precision; probability differences are not percentage points. "
             f"Absolute differences at most {decimal_text(tolerance)} count as ties.", ""]
    previous = None
    for count in counts:
        risk, first, second, outcome = (count[k] for k in (
            "risk_preferences", "first_fee_rule", "second_fee_rule", "outcome"))
        header = risk, first, second
        if header != previous:
            lines += [f"## {risk}: {first} -> {second}", ""]
            previous = header
        rows = groups[risk, first, second, outcome]
        lower, higher = count["Lower"], count["Higher"]
        if lower == higher:
            dominant = None
            label = "All strict cases (no dominant direction)"
            selected = [r for r in rows if r["direction"] != "Tied"]
        else:
            dominant = "Lower" if lower > higher else "Higher"
            label = "Opposite-direction cases"
            selected = [r for r in rows if r["direction"] not in (dominant, "Tied")]
        lines += [f"### {outcome}", "",
                  "Counts: **" + " / ".join(str(count[d]) for d in DIRECTIONS) + "**. "
                  + ("More frequent strict direction: **" + dominant.lower() + "**."
                     if dominant else "No dominant strict direction."), ""]
        if selected:
            lines += [label + ":", "",
                      "| Model family | Cost | First rule | Second rule | Difference | Direction |",
                      "|---|---:|---:|---:|---:|---|"]
            for row in selected:
                lines.append("| " + " | ".join(row[k] for k in (
                    "model_family", "cost_multiplier", "first_value", "second_value",
                    "second_minus_first", "direction")) + " |")
            lines.append("")
        else:
            lines += [label + ": none.", ""]
        tied = [r for r in rows if r["direction"] == "Tied"]
        if tied:
            lines += ["Tied cases (difference shown to distinguish exact from tolerance ties):", "",
                      "| Model family | Cost | Difference |", "|---|---:|---:|"]
            for row in tied:
                lines.append("| " + " | ".join(row[k] for k in (
                    "model_family", "cost_multiplier", "second_minus_first")) + " |")
            lines.append("")
        else:
            lines += ["Tied cases: none.", ""]
    return "\n".join(lines)


def markdown(title: str, cases: list[Case], counts: list[dict], excluded: set[Decimal],
             tolerance: Decimal) -> str:
    lookup = {(r["risk_preferences"], r["first_fee_rule"], r["second_fee_rule"], r["outcome"]): r
              for r in counts}
    costs = ", ".join(decimal_text(x) for x in sorted({c.cost for c in cases}))
    lines = [f"# {title}", "", "Each cell reports **decreases / ties / increases** in the outcome "
             "when moving from the first fee rule to the second. The comparison is second minus first. "
             "A decrease in a named party's shortfall or burden benefits that party; it does not by "
             "itself establish an overall welfare ranking.", "", f"Included cost multipliers: **{costs}**. "
             f"Included source cases: **{len(cases)}**."]
    if excluded:
        lines += ["Excluded cost multipliers: **" + ", ".join(decimal_text(x) for x in sorted(excluded)) + "**."]
    for risk in RISKS:
        denominator = len({(c.family, c.cost) for c in cases if c.risk == risk})
        lines += ["", "## " + ("Risk neutral" if risk == RISKS[0] else "Risk averse"),
                  "", f"Every cell totals **{denominator} matched settings**.", "",
                  "| Outcome | American -> Trial | American -> Complete | Trial -> Complete |",
                  "|---|---:|---:|---:|"]
        for metric, _, _ in METRICS:
            cells = []
            for first, second in PAIRS:
                entry = lookup[risk, first, second, metric]
                if entry["matched_settings"] != denominator:
                    raise ValueError("Inconsistent comparison denominator")
                cells.append(" / ".join(str(entry[key]) for key in DIRECTIONS))
            lines.append("| " + metric + " | " + " | ".join(cells) + " |")
    lines += ["", "## Scope and accounting", "",
              "Comparisons hold model family, cost and preferences fixed. A family available at fewer "
              "costs contributes only its available matched settings; the fifteen-offer case is retained "
              "whenever cost 1 is included. Denominators are calculated from the selected source rows.", "",
              f"Differences with absolute value at most **{decimal_text(tolerance)}** count as ties. "
              "The tolerance is in damages per potential dispute for monetary outcomes and in probability "
              "units for filing and answering. With the default tolerance, the probability threshold is "
              "0.001 percentage points. Values are compared before display rounding.", "",
              "The three burden columns use population-weighted contributions, not the conditional "
              "diagnostic columns. The truth prior is held fixed within each comparison, so rescaling "
              "to the corresponding truth-conditional averages preserves the sign of each difference. "
              "The tie tolerance here is applied to the population-weighted values. Expenditures use "
              "real litigation costs; gross outcome error is before legal costs and separate fee transfers. "
              "These five measures are distinct and are not summed.", "",
              "Filing and answering are shares of all potential disputes. Answering counts disputes "
              "that are filed and answered, including those ending in later default. It is not "
              "conditional on filing and is not one minus the nonanswer share. "
              "This script compares aggregate shares; "
              "it does not inspect individual answering strategies or infer a causal mechanism.", "",
              "The counts describe selected equilibria on the retained parameter grid, not empirical "
              "frequencies or all possible equilibria. The separate multiple-start study remains a "
              "distinct sensitivity exercise and is not pooled into these counts.", "",
              "Source: `Results/Aggregated Data/Sources/welfare-outcomes.csv`. Companion counts and "
              "comparison CSVs retain the denominators, individual values, differences, and option-set "
              "identifiers. The provenance JSON records source and generator hashes and the cost selection.", ""]
    return "\n".join(lines)


def build(article: Path, output: Path, excluded: set[Decimal], tolerance: Decimal) -> dict:
    source = article.resolve() / "Results/Aggregated Data/Sources/welfare-outcomes.csv"
    cases = read_cases(source)
    selected = filter_costs(cases, excluded)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    generator = Path(__file__).resolve()
    generator_hash = hashlib.sha256(generator.read_bytes()).hexdigest()
    contents, totals = {}, {}
    for stem, title, view, omitted in (
        ("all-costs", "Pairwise fee-rule comparisons: all costs", cases, set()),
        ("filtered-costs", "Pairwise fee-rule comparisons: selected costs", selected, excluded),
    ):
        records = compare(view, tolerance)
        counts = summarize(records)
        denominators = {risk: len({(c.family, c.cost) for c in view if c.risk == risk}) for risk in RISKS}
        manifest = {
            "source": {"path": str(source), "sha256": source_hash},
            "generator": {"path": str(generator), "sha256": generator_hash},
            "source_cases": len(cases), "included_cases": len(view),
            "excluded_cases": len(cases) - len(view),
            "included_costs": [decimal_text(x) for x in sorted({c.cost for c in view})],
            "excluded_costs": [decimal_text(x) for x in sorted(omitted)],
            "matched_settings_by_risk": denominators, "metric_comparisons": len(records),
            "direction": "second fee rule minus first fee rule", "count_order": list(DIRECTIONS),
            "tie_tolerance": str(tolerance), "tie_rule": "absolute difference <= tolerance",
            "metrics": [{"outcome": label, "source_column": column, "units": units}
                        for label, column, units in METRICS],
            "included_option_sets": sorted(c.option_set for c in view),
            "validation": {"unique_cases": True, "complete_rule_and_preference_coverage": True,
                           "matched_non_fee_settings": True, "finite_numeric_values": True},
        }
        contents[stem + ".md"] = markdown(title, view, counts, omitted, tolerance)
        contents[stem + "-case-details.md"] = case_details_markdown(title, records, counts, tolerance)
        contents[stem + "-counts.csv"] = csv_text(counts)
        contents[stem + "-comparisons.csv"] = csv_text(records)
        contents[stem + "-provenance.json"] = json.dumps(manifest, indent=2) + "\n"
        totals[stem] = {"cases": len(view), "settings_by_risk": denominators,
                        "metric_comparisons": len(records)}
    # Validate both views before creating or replacing any generated output.
    output.mkdir(parents=True, exist_ok=True)
    for name, content in contents.items():
        (output / name).write_text(content, encoding="utf-8", newline="\n")
    return totals


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--article-directory", required=True, type=Path)
    parser.add_argument("--output-directory", type=Path,
                        help="Default: article Supplemental materials/Generated pairwise comparisons")
    parser.add_argument("--exclude-costs", nargs="+", default=["0.25", "4"], metavar="COST",
                        help="Costs removed from the second report; default: 0.25 4")
    parser.add_argument("--tolerance", default=str(DEFAULT_TOLERANCE),
                        help="Absolute tie tolerance in source units; default: 0.00001")
    args = parser.parse_args(argv)
    try:
        excluded = {number(x, "excluded cost") for x in args.exclude_costs}
        tolerance = number(args.tolerance, "tie tolerance")
        output = args.output_directory or args.article_directory / "Supplemental materials/Generated pairwise comparisons"
        totals = build(args.article_directory, output, excluded, tolerance)
    except (ValueError, OSError) as error:
        parser.error(str(error))
    print(json.dumps(totals, indent=2))
    print("Reports written to:", output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

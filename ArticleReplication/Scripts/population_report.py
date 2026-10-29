"""Validate a completed pooled population experiment and produce its paper comparison.

Requires matplotlib for standalone scientific figures; all other work uses the
standard library. Read-only with respect to simulations and article references.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import html
import json
import math
import os
from pathlib import Path
import statistics
from datetime import datetime

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


WELFARE = {
    "MeritoriousPlaintiffShortfall": "Meritorious plaintiff\nshortfall",
    "NonliableDefendantBurden": "Nonliable defendant\nburden",
    "LiableDefendantExcessBurden": "Liable defendant\nexcess burden",
    "GrossOutcomeError": "Gross outcome\nerror",
    "RealLitigationExpenditures": "Litigation\nexpenditures",
}
EVENTS = ["Filing", "AnsweringUnconditional", "Trial", "Settlement"]
COLORS = {"article": "#4b5563", "gini": "#236b9a", "liu": "#c75c25"}


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(path, value):
    Path(path).write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def policy(policy):
    return policy["Player"], tuple(tuple(row) for row in policy["Probabilities"])


def validate(run, reference):
    completed, summary, manifest = [read(run / name) for name in ("completed.json", "summary.json", "repetitions.json")]
    assert completed["Passed"] and summary["Passed"]
    cases = manifest["Cases"]
    expected_cases = {c["Id"] for c in cases}
    assert len(expected_cases) == completed["ArticleCases"] == summary["ArticleCases"]
    assert completed["CombinedRuns"] == 2 * len(cases)
    assert completed["Repetitions"] == summary["Repetitions"] == len(manifest["Runs"])
    assert len(cases) == 74 and len(manifest["Runs"]) == 10, "This report requires the full ten-repetition article catalog."
    accepted = {}
    originals = {}
    replacements = {}
    for item in manifest.get("Replacements", []):
        for record in read(Path(item["run"]) / "completed.json")["Results"]:
            assert record["Status"] == "FullGameCceVerified"
            replacements[(item["repetition"] - 1, record["CaseId"], record["Selector"])] = record
    for index, source in enumerate(manifest["Runs"]):
        records = read(Path(source) / "completed.json")["Results"]
        assert len(records) == 2 * len(cases)
        for record in records:
            key = index, record["CaseId"], record["Selector"]
            originals[key] = record
            selected = replacements.get(key, record)
            assert selected["Status"] == "FullGameCceVerified", key
            assert max(selected["NormalizedGains"]) <= selected["Settings"]["MaximumNormalizedGain"], key
            accepted[key] = selected
    assert len(accepted) == 2 * len(cases) * len(manifest["Runs"])
    details, counts, rounds = [], [], []
    selected_results, index = {}, {}
    for c in cases:
        case_id = c["Id"]
        receipt = read(run / "combined" / case_id / "completed.json")
        assert receipt["Passed"] and receipt["Case"] == c
        final = Path(receipt["FinalRun"])
        game = read(final / case_id / "game.json")
        baseline = read(final / case_id / "baseline.json")["Outcomes"]
        original = read(reference / case_id / "validation.json")
        assert game["Fingerprint"] == original["GameIdentity"], case_id
        # All ten repetitions and any repair must retain the same article profile.
        for source in manifest["Runs"]:
            first = read(Path(source) / case_id / "game.json")
            assert first["Fingerprint"] == game["Fingerprint"]
            assert first["SavedPrimarySha256"] == game["SavedPrimarySha256"]
        for replacement in manifest.get("Replacements", []):
            candidate = Path(replacement["run"]) / case_id / "game.json"
            if candidate.exists():
                assert read(candidate) == game
        with (reference / case_id / "replayed-report-full-precision.csv").open(encoding="utf-8-sig", newline="") as f:
            old_row = next(row for row in csv.DictReader(f) if row["Filter"] == "All")
        differences = {name: abs(baseline[name] - float(old_row[column])) for name, column in
                       [("Filing", "PFiles"), ("AnsweringUnconditional", "DAnswers"), ("Trial", "Trial"), ("Settlement", "Settles")]}
        differences.update({f"NativeUtility{p}": abs(baseline["NativeUtilities"][p] - float(old_row[column])) for p, column in enumerate(["PWelfare", "DWelfare"])})
        differences.update({name: abs(value - original["Welfare"]["Headline"][name]) for name, value in baseline["Welfare"].items()})
        assert max(differences.values()) < 1e-8, (case_id, differences)
        assert receipt["Rounds"][-1]["PoliciesAdded"] == 0
        assert receipt["Rounds"][-1]["Statuses"] == ["FullGameCceVerified", "FullGameCceVerified"]
        union = final / case_id / "union-population.json"
        assert sha(union) == receipt["FinalArchiveSha256"]
        shared = read(union)
        shared_set = set(map(policy, shared["Policies"]))
        shared_probes = list(map(policy, shared["Probes"]))
        audits = {r["Selector"]: r for r in read(receipt["Audit"])["Rows"]}
        for method in ("gini", "liu"):
            directory = final / case_id / method
            result = read(directory / "result.json")
            archive = read(directory / "population.json")
            oracle = read(directory / "independent-oracle-check.json")
            audit = audits[method]
            assert result["Status"] == "FullGameCceVerified"
            assert result == next(r for r in receipt["Results"] if r["Selector"] == method)
            assert archive["GameHash"] == game["Fingerprint"]["CompleteSha256"]
            assert archive["Keys"] == shared["Keys"]
            assert set(map(policy, archive["Policies"])) == shared_set
            assert list(map(policy, archive["Probes"])) == shared_probes
            assert [sum(p["Player"] == i for p in archive["Policies"]) for i in (0, 1)] == receipt["CommonPopulationCounts"]
            weights = result["JointWeights"]
            assert len(weights) == math.prod(receipt["CommonPopulationCounts"])
            assert all(math.isfinite(w) and w >= 0 for w in weights) and abs(math.fsum(weights) - 1) < 1e-9
            assert max(result["NormalizedGains"]) <= result["Settings"]["MaximumNormalizedGain"]
            assert oracle["Passed"] and max(oracle["Differences"]) <= 1e-8
            assert audit["SourceResultSha256"] == sha(directory / "result.json")
            assert audit["ArchiveSha256"] == sha(directory / "population.json")
            assert max(audit["NormalizedCceGains"]) <= result["Settings"]["MaximumNormalizedGain"]
            for p in (0, 1):
                assert abs(audit["NormalizedCceGains"][p] - result["NormalizedGains"][p]) < 1e-8
            details.append({"CaseId": case_id, "Selector": method, "NormalizedCceGain": max(result["NormalizedGains"]),
                            "IndependentOracleDifference": max(oracle["Differences"]), "BaselineMaximumDifference": max(differences.values()),
                            "ProductNashGain": max(audit["NormalizedProductNashGains"]), "PlayerPolicyCounts": receipt["CommonPopulationCounts"],
                            "SameFinalPopulation": True, "ResultSha256": sha(directory / "result.json"), "ArchiveSha256": sha(directory / "population.json")})
            selected_results[(case_id, method)] = result
            index[(case_id, method)] = directory
        counts.extend(receipt["CommonPopulationCounts"])
        rounds.append(len(receipt["Rounds"]))
    # Independently recompute the summary's levels and each paired fee-rule contrast.
    def value(outcomes, metric):
        if metric in EVENTS:
            return outcomes[metric]
        if metric == "PlaintiffUtility":
            return outcomes["NativeUtilities"][0]
        if metric == "DefendantUtility":
            return outcomes["NativeUtilities"][1]
        return outcomes["Welfare"][metric]
    for row in summary["Comparisons"]:
        record = selected_results[(row["CaseId"], row["Selector"])]
        metric = row["Measure"]
        assert abs(row["Combined"] - value(record["Outcomes"], metric)) < 1e-12
        assert abs(row["Article"] - value(record["Baseline"], metric)) < 1e-12
        values = [value(accepted[(i, row["CaseId"], row["Selector"])]["Outcomes"], metric) for i in range(len(manifest["Runs"]))]
        assert row["IndividualVerifiedRuns"] == len(values)
        assert abs(row["IndividualMinimum"] - min(values)) < 1e-12
        assert abs(row["IndividualMaximum"] - max(values)) < 1e-12
    for row in summary["FeeContrasts"]:
        a, b = [selected_results[(row[k], row["Selector"])] for k in ("AmericanCase", "CompleteCase")]
        metric = row["Measure"]
        combined = value(b["Outcomes"], metric) - value(a["Outcomes"], metric)
        article = value(b["Baseline"], metric) - value(a["Baseline"], metric)
        sign = lambda x: 0 if abs(x) < 1e-6 else (1 if x > 0 else -1)
        assert abs(row["CombinedCompleteMinusAmerican"] - combined) < 1e-12
        assert abs(row["ArticleCompleteMinusAmerican"] - article) < 1e-12
        assert row["CombinedDirectionMatches"] == (sign(combined) == sign(article))
        values = [value(accepted[(i, row["CompleteCase"], row["Selector"])]["Outcomes"], metric)
                  - value(accepted[(i, row["AmericanCase"], row["Selector"])]["Outcomes"], metric)
                  for i in range(len(manifest["Runs"]))]
        assert abs(row["IndividualMinimum"] - min(values)) < 1e-12
        assert abs(row["IndividualMaximum"] - max(values)) < 1e-12
        assert row["IndividualDirectionMatches"] == sum(sign(v) == sign(article) for v in values)
        assert row["IndividualRuns"] == len(manifest["Runs"])
    valid = {"Passed": True, "ArticleCases": len(cases), "VerifiedCombinedCces": len(details), "AcceptedIndividualRuns": len(accepted),
             "OriginalFailedSelections": sum(r["Status"] != "FullGameCceVerified" for r in originals.values()),
             "ReplacedSelectorResults": len(replacements), "SamePopulationComparisons": len(cases), "GameFingerprintMatches": len(cases),
             "MaximumNormalizedCceGain": max(r["NormalizedCceGain"] for r in details),
             "MaximumIndependentOracleDifference": max(r["IndependentOracleDifference"] for r in details),
             "MaximumBaselineOutcomeDifference": max(r["BaselineMaximumDifference"] for r in details),
             "ProductsAlsoApproximateNash": sum(r["ProductNashGain"] <= 1e-5 for r in details),
             "MainProductsAlsoApproximateNash": sum(r["ProductNashGain"] <= 1e-5 for r in details if r["CaseId"].startswith(("baseline__", "cost-multiplier__"))),
             "PolicyCountMinimum": min(counts), "PolicyCountMedian": statistics.median(counts), "PolicyCountMaximum": max(counts),
             "MaximumPoolingRounds": max(rounds), "Cases": details, "SummarySha256": sha(run / "summary.json")}
    assert valid["ProductsAlsoApproximateNash"] == summary["ProductsAlsoApproximateNashAt1eMinus5"]
    status = read(run / "status.json")
    end = datetime.fromisoformat(status["UpdatedUtc"].replace("Z", "+00:00"))
    source = read(run / "batch-plan.json")["Settings"].get("repetitions_from")
    start = datetime.fromisoformat(read(Path(source or run) / "status.json")["StartedUtc"].replace("Z", "+00:00"))
    valid["CompletedUtc"] = status["UpdatedUtc"]
    valid["TotalWallMinutesIncludingRepair"] = (end - start).total_seconds() / 60
    valid["PoolingWallMinutes"] = completed["ElapsedSeconds"] / 60
    return summary, valid, index


def findings(summary):
    rows = summary["Comparisons"]
    fees = summary["FeeContrasts"]
    main = [r for r in fees if r["Family"] in ("baseline", "cost-multiplier")]
    headline = list(WELFARE)[:3]
    directions = []
    for risk in ("rn", "ra"):
        for method in ("gini", "liu"):
            for cost in (.25, .5, 1, 2, 4):
                candidates = [r for r in main if r["Selector"] == method and r["CostMultiplier"] == cost and
                              (r["AlphaP"] == 0) == (risk == "rn") and r["Measure"] in headline]
                assert len(candidates) == 3
                directions.append({"Risk": risk, "Selector": method, "CostMultiplier": cost,
                                   "HeadlineMatches": sum(r["CombinedDirectionMatches"] for r in candidates),
                                   "IndependentMatches": {r["Measure"]: r["IndividualDirectionMatches"] for r in candidates}})
    high = [r for r in main if r["AlphaP"] == 0 and r["CostMultiplier"] == 4 and r["Measure"] == "NonliableDefendantBurden"]
    baseline = [r for r in rows if r["Family"] == "baseline" and r["Measure"] == "Settlement"]
    spreads = sorted([{"CaseId": r["CaseId"], "Selector": r["Selector"], "Measure": r["Measure"],
                       "RangePercentagePoints": 100 * (r["IndividualMaximum"] - r["IndividualMinimum"])} for r in rows if r["Measure"] in EVENTS],
                     key=lambda r: r["RangePercentagePoints"], reverse=True)
    outside = [r for r in rows if r["Combined"] < r["IndividualMinimum"] - 1e-8 or r["Combined"] > r["IndividualMaximum"] + 1e-8]
    return {"HeadlineDirections": directions, "HighCostRiskNeutralNonliableBurden": high, "BaselineSettlement": baseline,
            "MaximumInitializationRateRanges": spreads[:10], "CombinedMetricsOutsideIndividualRange": len(outside),
            "ComparedMetrics": len(rows), "Interpretation": "Pooled selection can lie outside the range of separate runs because weights are reoptimized and new responses are added. Ranges are not confidence intervals."}


def figures(summary, output):
    main = [r for r in summary["FeeContrasts"] if r["Family"] in ("baseline", "cost-multiplier")]
    plt.rcParams.update({"font.size": 10, "axes.spines.top": False, "axes.spines.right": False, "svg.fonttype": "none"})
    fig, axes = plt.subplots(2, 5, figsize=(16, 7.5), sharex=True)
    for row, risk in enumerate(("rn", "ra")):
        for col, (metric, label) in enumerate(WELFARE.items()):
            ax = axes[row, col]
            relevant = [r for r in main if r["Measure"] == metric and (r["AlphaP"] == 0) == (risk == "rn")]
            article = sorted([r for r in relevant if r["Selector"] == "gini"], key=lambda r: r["CostMultiplier"])
            ax.plot([r["CostMultiplier"] for r in article], [r["ArticleCompleteMinusAmerican"] for r in article],
                    color=COLORS["article"], linestyle="--", marker="s", markersize=4, label="Article Nash")
            for method, marker in (("gini", "o"), ("liu", "^")):
                data = sorted([r for r in relevant if r["Selector"] == method], key=lambda r: r["CostMultiplier"])
                x = [r["CostMultiplier"] for r in data]
                ax.fill_between(x, [r["IndividualMinimum"] for r in data], [r["IndividualMaximum"] for r in data], color=COLORS[method], alpha=.12, linewidth=0)
                ax.plot(x, [r["CombinedCompleteMinusAmerican"] for r in data], color=COLORS[method], marker=marker, markersize=4.5, label="Combined " + ("Gini" if method == "gini" else "Liu"))
            ax.axhline(0, color="#939da9", linewidth=.7)
            ax.set_xscale("log", base=2)
            ax.set_xticks([.25, .5, 1, 2, 4], [".25", ".5", "1", "2", "4"])
            ax.grid(axis="y", alpha=.18)
            if row == 0:
                ax.set_title(label, fontsize=11)
            if col == 0:
                ax.set_ylabel(("Risk neutral" if risk == "rn" else "Risk averse") + "\nComplete − American")
            if row == 1:
                ax.set_xlabel("Cost multiplier")
    handles, labels = axes[0, 0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="upper center", bbox_to_anchor=(.5, .94), ncol=3, frameon=False)
    fig.suptitle("Fee-shifting effects after pooling ten independent JPSRO runs", fontsize=16, y=.995)
    fig.text(.5, .015, "Bands: minimum–maximum across ten paired independent runs (not confidence intervals). Units: damages = 1.", ha="center", fontsize=10)
    fig.subplots_adjust(left=.075, right=.985, top=.80, bottom=.12, wspace=.35, hspace=.36)
    for suffix in ("png", "svg", "pdf"):
        fig.savefig(output / ("fee-effects." + suffix), dpi=180)
    plt.close(fig)
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.5), sharey=True)
    for ax, risk in zip(axes, ("rn", "ra")):
        for offset, method, label in ((-.25, "article", "Article Nash"), (0, "gini", "Combined Gini"), (.25, "liu", "Combined Liu")):
            values = []
            for fee in ("american", "complete"):
                r = next(r for r in summary["Comparisons"] if r["CaseId"] == f"baseline__standard__{fee}__{risk}__cost-1" and r["Selector"] == ("gini" if method == "article" else method) and r["Measure"] == "Settlement")
                values.append(100 * r["Article" if method == "article" else "Combined"])
            ax.bar([offset, 1 + offset], values, width=.24, color=COLORS[method], label=label)
        ax.set_xticks([0, 1], ["American rule", "Complete fee shifting"])
        ax.set_title("Risk neutral" if risk == "rn" else "Risk averse")
        ax.set_ylim(0, 100)
        ax.grid(axis="y", alpha=.18)
    axes[0].set_ylabel("Settlement rate (%)")
    fig.legend(*axes[0].get_legend_handles_labels(), loc="upper center", ncol=3, frameon=False, bbox_to_anchor=(.5, .94))
    fig.suptitle("Settlement at cost multiplier 1", fontsize=15, y=.995)
    fig.subplots_adjust(top=.75, bottom=.13, left=.08, right=.98)
    for suffix in ("png", "svg", "pdf"):
        fig.savefig(output / ("settlement." + suffix), dpi=180)
    plt.close(fig)


def write_html(output, run, summary, valid, evidence, index):
    esc = lambda x: html.escape(str(x))
    link = lambda path: Path(os.path.relpath(path, output)).as_posix()
    main_directions = {(r["Risk"], r["Selector"], r["CostMultiplier"]): r for r in evidence["HeadlineDirections"]}
    high = {r["Selector"]: r for r in evidence["HighCostRiskNeutralNonliableBurden"]}
    assert all(main_directions[("rn", method, cost)]["HeadlineMatches"] == 3 for method in ("gini", "liu") for cost in (.25, .5, 1, 2))
    assert high["gini"]["CombinedCompleteMinusAmerican"] < 0 < high["liu"]["CombinedCompleteMinusAmerican"], "Review the high-cost interpretation for this new dataset."
    high_table = "".join(f'<tr><td>{label}</td><td>{value:+.4f}</td></tr>' for label, value in
                         [("Article Nash", high["gini"]["ArticleCompleteMinusAmerican"]), ("Combined Gini", high["gini"]["CombinedCompleteMinusAmerican"]), ("Combined Liu", high["liu"]["CombinedCompleteMinusAmerican"])])
    direction_table = ""
    for risk in ("rn", "ra"):
        for cost in (.25, .5, 1, 2, 4):
            direction_table += f'<tr><td>{"Risk neutral" if risk == "rn" else "Risk averse"}</td><td>{cost:g}</td>'
            for method in ("gini", "liu"):
                d = main_directions[(risk, method, cost)]
                direction_table += f'<td>{d["HeadlineMatches"]}/3</td>'
            direction_table += "</tr>"
    settlement = {(r["CaseId"], r["Selector"]): r for r in evidence["BaselineSettlement"]}
    ra_a, ra_c = "baseline__standard__american__ra__cost-1", "baseline__standard__complete__ra__cost-1"
    settlement_text = f'The article’s settlement rate falls from {100*settlement[(ra_a,"gini")]["Article"]:.1f}% to {100*settlement[(ra_c,"gini")]["Article"]:.1f}% with complete fee shifting. Combined Gini instead rises from {100*settlement[(ra_a,"gini")]["Combined"]:.1f}% to {100*settlement[(ra_c,"gini")]["Combined"]:.1f}%; combined Liu rises from {100*settlement[(ra_a,"liu")]["Combined"]:.1f}% to {100*settlement[(ra_c,"liu")]["Combined"]:.1f}%.'
    grouped = {}
    for row in summary["Comparisons"]:
        grouped.setdefault((row["CaseId"], row["Selector"]), {})[row["Measure"]] = row
    comparison_rows = ""
    for key, measures in sorted(grouped.items()):
        r = measures["Filing"]
        comparison_rows += f'<tr data-case="{esc(key[0])}" data-method="{key[1]}"><td><a href="{esc(link(index[key]/"result.json"))}">{esc(key[0])}</a></td><td>{key[1]}</td>'
        for metric in EVENTS:
            v = measures[metric]
            comparison_rows += f'<td>{100*v["Article"]:.2f} → {100*v["Combined"]:.2f}</td>'
        comparison_rows += f'<td>{r["Player0Policies"]} × {r["Player1Policies"]}</td><td>{r["NormalizedCceGain"]:.2g}</td></tr>'
    ranges = "".join(f'<tr><td>{esc(r["CaseId"])}</td><td>{r["Selector"]}</td><td>{r["Measure"]}</td><td>{r["RangePercentagePoints"]:.1f} pp</td></tr>' for r in evidence["MaximumInitializationRateRanges"][:5])
    body = f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Combined JPSRO and Liu results — 74 article games</title><style>
:root{{font:16px/1.55 system-ui,sans-serif;color:#23374d;background:#f2f5f8}}body{{max-width:1240px;margin:30px auto;padding:0 22px 60px}}h1{{font-size:2.1rem;line-height:1.2}}h2{{font-size:1.35rem}}section{{padding:24px;margin:22px 0;background:white;border:1px solid #dce4ec;border-radius:10px}}.lead{{border-left:5px solid #2d7790}}.facts{{display:flex;gap:16px;flex-wrap:wrap}}.fact{{padding:12px 18px;background:#eff5f8;border-radius:7px}}.fact strong{{display:block;font-size:1.45rem}}a{{color:#175f94}}table{{border-collapse:collapse;width:100%;font-size:.9rem}}th,td{{padding:9px;border-bottom:1px solid #dde6ec;text-align:left;vertical-align:top}}th{{background:#eef3f7}}img{{max-width:100%;height:auto}}.scroll{{overflow:auto;max-height:620px}}.scroll th{{position:sticky;top:0}}.note{{color:#53687b;font-size:.9rem}}input,select{{font:inherit;padding:7px;margin:5px;border:1px solid #b7c5d3;border-radius:5px}}code{{font-size:.85em;overflow-wrap:anywhere}}@media(max-width:700px){{body{{padding:0 10px}}section{{padding:16px}}h1{{font-size:1.7rem}}}}
</style><body><p class="note">ACESim4 · completed 28 October 2026 · ten repetitions, pooled policies</p>
<h1>One combined result per game and selector</h1>
<section class="lead"><p>All ten repetitions and the combined calculations are complete. Policies from both selectors were pooled, exact duplicates removed, weights recomputed, and profitable full-game responses added until both selectors passed on the same final population.</p>
<div class="facts"><div class="fact"><strong>{valid['ArticleCases']}</strong>article games</div><div class="fact"><strong>{valid['AcceptedIndividualRuns']:,}</strong>accepted individual results</div><div class="fact"><strong>{valid['VerifiedCombinedCces']}</strong>verified combined CCEs</div><div class="fact"><strong>10</strong>repetitions per game/selector</div></div>
<p><strong>Qualitative agreement is partial.</strong> Lower-cost risk-neutral headline effects persist. At the highest cost, the nonliable-defendant comparison depends on selection: Gini preserves the article’s direction, while Liu reverses it. Risk-averse comparisons vary more.</p></section>
<section><h2>Main fee-shifting comparisons</h2><p>For risk-neutral cost multipliers 0.25, 0.5, 1, and 2, both combined selectors preserve all three headline directions: lower meritorious-plaintiff shortfall, lower nonliable-defendant burden, and higher liable-defendant excess burden.</p>
<p>At multiplier 4, the earlier single-run reversal under Gini disappears after pooling; Liu’s reversal remains. The following values are complete fee shifting minus the American rule for nonliable-defendant burden, in fractions of the unit damages award:</p><table><thead><tr><th>Result</th><th>High-cost risk-neutral change</th></tr></thead><tbody>{high_table}</tbody></table>
<p>Across the ten individual runs, the high-cost nonliable-defendant direction matches the article in {high['gini']['IndividualDirectionMatches']}/10 Gini runs and {high['liu']['IndividualDirectionMatches']}/10 Liu runs. The combined result is newly optimized; it is not a majority vote or an average of these runs.</p>
<a href="fee-effects.png"><img src="fee-effects.png" alt="Five welfare effects across five cost multipliers, showing article Nash, combined Gini, combined Liu, and individual-run ranges for risk-neutral and risk-averse games"></a>
<p class="note">Shading is the range across ten paired individual runs, not a confidence interval. <a href="fee-effects.svg">SVG</a> · <a href="fee-effects.pdf">PDF</a> · <a href="{esc(link(run/'feecontrasts.csv'))}">All numerical fee contrasts</a>.</p>
<details><summary>How many of the three headline directions match the article?</summary><table><thead><tr><th>Risk</th><th>Cost multiplier</th><th>Gini</th><th>Liu</th></tr></thead><tbody>{direction_table}</tbody></table><p class="note">Signs within 0.000001 of zero are classified as zero. This table compares the article’s selected Nash profile with the selected CCEs; it is not a general theorem about either solution set.</p></details></section>
<section><h2>Risk-averse settlement remains a substantial difference</h2><p>At cost multiplier 1, {settlement_text}</p><a href="settlement.png"><img src="settlement.png" alt="Settlement percentages at ordinary cost, comparing the article and both combined selectors under American and complete fee-shifting rules"></a><p class="note"><a href="settlement.svg">SVG</a> · <a href="settlement.pdf">PDF</a>. All rates are unconditional.</p></section>
<section><h2>The solution concept matters</h2><p>All 148 combined results satisfy the full-game approximate coarse-correlated-equilibrium criterion. The maximum gain from an unrestricted unilateral deviation is {valid['MaximumNormalizedCceGain']:.8g} after dividing by the player’s fixed terminal utility range, below the 0.00001 target. The existing article response engine independently checks these values.</p><p>A deviation commits to a replacement policy before the player learns its policy recommendation. Deviations conditioned on that recommendation would require correlated-equilibrium checks beyond the CCE criterion.</p>
<p>Only <strong>{valid['ProductsAlsoApproximateNash']} of 148</strong> independent product-marginal profiles also passes the approximate-Nash test; <strong>{valid['MainProductsAlsoApproximateNash']} of 40</strong> main-game profiles does. The reported economic outcomes use the joint weights, so they must be presented as CCE outcomes. These results change both equilibrium selection and the solution concept; they do not contradict the article within its Nash framework.</p></section>
<section><h2>What pooling resolves—and what remains sensitive</h2><p>Each initial run begins with a uniform policy and two random policies per player and then discovers best responses. The final common populations contain {valid['PolicyCountMinimum']}–{valid['PolicyCountMaximum']} policies per player (median {valid['PolicyCountMedian']:g}), with up to {valid['MaximumPoolingRounds']} rounds of pooling and further expansion. No policy receives weight merely because it appeared in more repetitions.</p>
<p>Weights are optimized over the union population. A single combined result is convenient to present, but it is not uniquely implied by the game. Gini and Liu can select different outcomes, and Liu still depends on its declared similarity features, frozen probes, bandwidth, and positive-support floor. No second, independent ten-run pool was tested.</p>
<p>Individual runs remain useful as sensitivity evidence. The largest observed event-rate ranges include:</p><div class="scroll"><table><thead><tr><th>Game</th><th>Selector</th><th>Rate</th><th>Range across ten runs</th></tr></thead><tbody>{ranges}</tbody></table></div>
<p>{evidence['CombinedMetricsOutsideIndividualRange']} of {evidence['ComparedMetrics']} combined metric values lie outside the individual-run range. This can occur because pooling changes the feasible population, reoptimizes weights, and discovers additional responses. It is not a sampling error bar.</p></section>
<section><h2>Suggested paper presentation</h2><p>For a CCE robustness section, Liu can serve as the main specification if economic similarity is the intended selection rule; retain Gini alongside it as a selection-sensitivity check. Report the economic outcomes and full-game deviation tolerance. Put the ten-run ranges and direction counts in a robustness appendix. Keep the article’s Nash results visible when comparing solution concepts.</p>
<blockquote>For each game, we ran ten independent JPSRO(CCE) initializations under maximum-Gini and economic-affinity relative-entropy selection. We pooled the discovered policies, removed exact behavioral duplicates, and continued response expansion and selection until both objectives satisfied a normalized full-game unilateral-deviation tolerance of 10<sup>−5</sup> on the same final population. Economic outcomes were computed from the resulting joint policy distributions. Individual-run ranges describe initialization sensitivity and are not confidence intervals.</blockquote>
<p class="note">The economic-affinity metric uses normalized utilities for both players plus filing, unconditional answering, and trial probabilities against a fixed probe bank. Settlement and welfare are reported separately. The metric and positive-reference floor are explicit adaptations of Liu et al.’s construction.</p></section>
<section><h2>Browse all combined outcomes</h2><p class="note">Rates are percentages, displayed as article → combined. Click a case for complete outcomes, weights, and diagnostics.</p><input id="query" placeholder="Filter by case, risk, cost…" aria-label="Filter cases"><select id="method" aria-label="Selector"><option value="">Both selectors</option><option value="gini">Gini</option><option value="liu">Liu</option></select><span id="count"></span>
<div class="scroll"><table><thead><tr><th>Case</th><th>Selector</th><th>Filing</th><th>Answering</th><th>Trial</th><th>Settlement</th><th>Policies P × D</th><th>Max. normalized gain</th></tr></thead><tbody id="results">{comparison_rows}</tbody></table></div></section>
<section><h2>Validation and provenance</h2><p>All {valid['GameFingerprintMatches']} game fingerprints match the article reference. The same original article profiles were used across repetitions and pooling. Replaying their native outcomes differs from the validated article data by at most {valid['MaximumBaselineOutcomeDifference']:.3g}. The largest discrepancy between the two full-game response engines is {valid['MaximumIndependentOracleDifference']:.3g}.</p>
<p>One original Liu selection failed its numerical check. Its same-seed rerun after the validated numerical improvement passed, and that replacement is identified explicitly; all original files are retained. Every case/selector now has ten accepted repetitions. The combined calculations finished at {esc(valid['CompletedUtc'])}, about {valid['TotalWallMinutesIncludingRepair']:.0f} minutes after the original batch started, including the performance repair; optimized pooling itself took about {valid['PoolingWallMinutes']:.0f} minutes.</p>
<ul><li><a href="validation.json">Full 148-result validation</a> · <a href="findings.json">Findings and sensitivity counts</a></li><li><a href="{esc(link(run/'summary.json'))}">Full combined summary</a> · <a href="{esc(link(run/'comparisons.csv'))}">Outcome comparisons CSV</a> · <a href="{esc(link(run/'feecontrasts.csv'))}">Fee-rule contrasts CSV</a></li><li><a href="{esc(link(run/'batch-plan.json'))}">Settings and runtime identities</a> · <a href="{esc(link(run/'repetitions.json'))}">Repetitions and replacement provenance</a></li></ul>
<p>Method references: <a href="https://proceedings.mlr.press/v139/marris21a.html">Marris et al., JPSRO</a> and <a href="https://arxiv.org/html/2502.20170v2#S3.SS3">Liu et al., affinity and relative-entropy selection</a>. This implementation covers the CCE route, not recommendation-conditional CE or Liu’s logit-limit Nash route.</p></section>
<script>const rows=[...document.querySelectorAll('#results tr')];function filter(){{const q=document.querySelector('#query').value.toLowerCase(),m=document.querySelector('#method').value;let n=0;for(const row of rows){{const show=(!m||row.dataset.method===m)&&row.dataset.case.toLowerCase().includes(q);row.hidden=!show;if(show)n++}}document.querySelector('#count').textContent=n+' results'}}document.querySelector('#query').addEventListener('input',filter);document.querySelector('#method').addEventListener('change',filter);filter();</script></body></html>'''
    (output / "assessment.html").write_text(body, encoding="utf-8")


def main():
    if not __debug__:
        raise RuntimeError("Run without Python optimization flags: validation assertions must remain enabled.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", required=True)
    parser.add_argument("--article-primary", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    run, reference, output = map(lambda p: Path(p).resolve(), (args.run, args.article_primary, args.output))
    if output.exists() or output in run.parents or output in reference.parents or output == run or output == reference:
        raise ValueError("Choose a new report directory without overwriting source artifacts.")
    summary, valid, index = validate(run, reference)
    evidence = findings(summary)
    output.mkdir(parents=True)
    save(output / "validation.json", valid)
    save(output / "findings.json", evidence)
    figures(summary, output)
    write_html(output, run, summary, valid, evidence, index)
    save(output / "report-receipt.json", {"Passed": True, "Run": str(run), "ArticlePrimary": str(reference),
                                         "GeneratorSha256": sha(Path(__file__)), "Files": {p.name: sha(p) for p in output.iterdir() if p.is_file()}})
    print(json.dumps({k: v for k, v in valid.items() if k != "Cases"}, indent=2))
    print("Report:", output / "assessment.html")


if __name__ == "__main__":
    main()

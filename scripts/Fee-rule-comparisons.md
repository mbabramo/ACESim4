# Reproduce the fee-rule robustness comparisons

From the ACESim4 repository, run:

```powershell
python -B scripts/build_fee_rule_comparisons.py --article-directory "C:/Users/Admin/source/repos/correlated-signals-article"
```

This command generates both the full-grid table and the table excluding cost
multipliers **0.25 and 4.0**. It reads the saved
`Results/Aggregated Data/Sources/welfare-outcomes.csv` in the article repository.
It requires Python 3.10 or later, with no third-party packages. It does not solve
games or modify the source results. Run it after refreshing the saved welfare
results; it is a separate analysis step, not part of `diagrams all`.

The default destination is
`Supplemental materials/Generated pairwise comparisons` in the article
repository. Comprehensive findings belong here; particular tables can later be
selected for publication in the article's `Tables` folder.
Each view (`all-costs` and `filtered-costs`) produces five files:

- `.md`: readable tables, separately for risk neutrality and risk aversion.
- `-case-details.md`: the model families, costs and exact values of cases going
  against the more frequent strict direction, with ties listed separately.
  If decreases and increases are equally frequent, it lists all strict cases
  without assigning a dominant direction. This is a descriptive index, not a
  statistical outlier test.
- `-counts.csv`: decreases, ties, increases and denominator for every cell.
- `-comparisons.csv`: every matched comparison, with both source values, their
  difference, direction, source column and option-set identifiers.
- `-provenance.json`: source and generator paths and SHA-256 hashes, included
  cases and costs, denominators, tolerance and metric definitions.

The present 276-case source yields 46 matched settings per risk preference and
1,932 individual metric comparisons. Excluding the two extreme costs leaves
168 source cases, 28 matched settings per risk preference and 1,176 metric
comparisons. Both views include the fifteen-offer case at cost 1. These counts
are calculated from the source rather than fixed in the script. The current
risk-averse cases use symmetric CARA alpha 2.

## Direction and accounting

Columns compare **American to Trial**, **American to Complete**, and **Trial to
Complete**. Each cell is **decreases / ties / increases** in the named outcome
when moving from the first rule to the second. Differences are second minus
first. For example, `35 / 2 / 9` for plaintiff shortfall under American to Trial
means shortfall is lower under Trial in 35 settings, tied in 2, and higher in 9.

The absolute tie tolerance is `0.00001`, inclusive. Decimal source values are
compared before display rounding. Monetary outcomes are in damages per potential
dispute. Filing and answering are probabilities per potential dispute, so the
default tolerance for these outcomes equals 0.001 percentage points.

The seven outcomes use these exact source columns:

| Outcome | Source column |
|---|---|
| Meritorious-plaintiff shortfall | `Plaintiff shortfall contribution` |
| Nonliable-defendant burden | `Nonliable defendant contribution` |
| Liable-defendant excess burden | `Liable defendant contribution` |
| Total expenditures | `Real Litigation Costs` |
| Gross outcome error | `Outcome Error Before Legal Costs and Fee Transfers` |
| Filing share | `P Files` |
| Answering share | `D Answers` |

The first three are population-weighted contributions. They are not the
similarly named conditional diagnostic columns. The fixed truth prior within
each fee comparison means rescaling to corresponding conditional averages
preserves the sign of each difference; the tie tolerance here applies to the
population-weighted values. Filing and answering shares both use all potential
disputes as their denominator. Answering counts disputes that are filed and
answered, including those that later end in default. It is not conditional on
filing: up to saved rounding, it equals filing share minus nonanswer share,
not one minus nonanswer share. This aggregate comparison does not audit
answering strategies or identify mechanisms.
The counts concern the selected equilibria on the parameter grid; the separate
multiple-start equilibrium study is not pooled into them.

The separate direct answering audit is reproducible with:

```powershell
python -B scripts/verify_complete_answering.py --article-directory "C:/Users/Admin/source/repos/correlated-signals-article"
```

It checks every answering information set, including off-path sets, in the
routine Complete Fee-Shifting cases. Its JSON output is in this collection's
`Sources/answering-strategy-audit.json`, with hashes of the underlying action
reports. The current 92 cases contain 920 answering information sets.

## Options and validation

To choose a destination or explicitly specify the cost exclusions:

```powershell
python -B scripts/build_fee_rule_comparisons.py --article-directory "C:/Users/Admin/source/repos/correlated-signals-article" --output-directory "C:/Users/Admin/source/repos/correlated-signals-article/Supplemental materials/Generated pairwise comparisons" --exclude-costs 0.25 4.0
```

`--exclude-costs` changes only the second view; the first always includes all
source costs. Exclusions use numeric equality (`4` and `4.0` are equivalent).
`--tolerance` optionally changes the absolute tie tolerance for both views.
The source must provide complete matched fee-rule and risk-preference coverage.
The script rejects duplicate cases, missing or nonfinite values, differing
non-fee settings within a comparison, unknown fee/preference labels, invalid
probabilities, absent excluded costs and an empty retained grid. Both views are
validated before any report is written. Rerunning with unchanged inputs and
options deterministically replaces the ten generated files.

Run the focused tests from the code repository:

```powershell
python -B -m unittest discover -s scripts/tests -p test_fee_rule_comparisons.py
```

The tests check comparison direction and boundary ties, exact-decimal
differences, numeric cost exclusion, retention of the fifteen-offer case,
expected changes in counts, matched-case integrity, source preservation and
repeatable output.

The separate publication command creates `Table 5 - Overall results summary`
in the article's `Tables` folder, with editable sources, using all costs:
0.25, 0.5, 1, 2 and 4. It places filing and answering shares first, followed by the five monetary
measures in the article's order, without explanatory text. LuaLaTeX and
Poppler's `pdftoppm` must be on PATH.

```powershell
python -B scripts/publish_fee_rule_summary.py --article-directory "C:/Users/Admin/source/repos/correlated-signals-article"
```

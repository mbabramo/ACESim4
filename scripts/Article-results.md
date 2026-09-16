# Correlated-signals article results

The routine design has 276 cases: 184 CS004 cases plus 92 CS006EF Complete
Fee-Shifting cases. Each retained transformation crosses American, Trial Fee-Shifting and
Complete Fee-Shifting with risk neutrality and symmetric CARA alpha 2, at costs
0.25, 0.5, 1, 2 and 4. Mandatory participation cases are excluded. Multiple-start
equilibrium analysis remains a separate `--plan multiple-equilibria` workflow.

From a committed source tree, run:

```powershell
.\scripts\Rebuild-ArticleResults.ps1
```

This runs the retained production suite, aggregates reports, then generates all
figures and tables. It uses all processors by default; `-Processors N` limits it.
Saved equilibrium profiles are revalidated against the current game, and reports
are regenerated. A failed equilibrium validation stops the run. A missing
profile is solved normally. Source/build manifests prevent mixing incompatible
production runs. Do not bypass these checks for final results.

The nine ten-offer families are Baseline, Low noise, High noise, the three
alternative merits distributions/architectures, Direct binary-state signals,
All costs avoidable, and All costs sunk. Each contains 30 cases. The 15-offer
sensitivity check adds six cases at cost 1. Transformations are applied separately.

For a clean report rebuild, first commit and use `Prepare-ArticleRebuild.ps1`
with the directories holding reusable `CS004`/`CS006EF` equilibrium files.
It obtains the expected filenames from the C# `export-case-matrix` command,
stages and hashes the available profiles, clears only this repository's ReportResults,
then seeds `Run records/Retained study`. The original 124 profiles remain valid
inputs; 152 additional cases complete the expanded design. The provenance
record lists missing cases explicitly. No fixed cache count is assumed.
Preserve the returned provenance record with the final run records. Separate
analyses and their exact source profiles belong in the article's Supplemental
materials and must not be replaced with profiles from a report rebuild.
Use `Prepare-ArticleRebuild.ps1 -ResultsDirectory <fresh-directory>` with the
source directories to seed an isolated rebuild; this option never clears an
existing destination. Then use `Rebuild-ArticleResults.ps1 -ResultsDirectory`
with the same directory. It generates the same organization before the verified files
are copied into Results. Routine chart requests require the complete case matrix;
missing fee/risk cases cause an error before any figure or table is replaced.

To regenerate diagrams from completed raw reports without running production:

```powershell
.\scripts\Rebuild-ArticleResults.ps1 -DiagramsOnly
dotnet run --project LitigCharts -c Release -- diagrams results --config ReportResults/article-diagrams.json --jobs 32
```

`-List` / `--list` validates the collection without writing diagram files.
`-SourcesOnly` / `--sources-only` writes editable sources and data. The direct
diagram command also supports `--compile-only` and `--output-root`.

## Organization

- `Aggregated Data/<Specification>/<Risk Neutral|Risk Averse|Risk Comparison>`
  contains related figures and tables together, with one file per cost. A Risk
  Comparison folder holds the six-case comparison. Every retained family has
  Risk Neutral, Risk Averse and Risk Comparison folders for its tables and charts.
- `Individual simulations/<Specification>/<Risk>/<Fee rule>` contains each
  case's six report diagrams and its source records.
- `Sources` below each display folder contains standalone TeX and CSV/JSON data.
  PDF/PNG files sit directly in the corresponding display folder. Cost multipliers
  appear in filenames, not in the artwork.
- `Run records/Retained study` retains raw worker reports, combined data,
  coordinator state, logs and provenance. Case folders expose convenient copies
  of individual inputs with short cost-based names; numerical source records
  remain identifiable by full option-set name in the batch records.
- `Run records/diagram-inventory.json` records the generated collection.

A complete 276-case run generates 2,024 routine exhibits: 1,656 individual diagrams,
138 welfare tables, 138 disposition charts and 92 four-panel strategy figures.
These are planned counts, not a claim that a particular output directory is complete;
its generated inventory records completion. Filing and answering
use nested circles, outlined squares and larger outlined diamonds for American,
Trial Fee-Shifting and Complete Fee-Shifting, respectively, where available.
These fixed-size markers are centered on the exact signal and probability.
Participation strategies use symbols without connecting lines.
The lower panels show separate offer strips for each available fee rule and preserve continue/exit
histories and mixing probabilities. No conditional mean-offer substitution is
used. The history legend appears when an exit-committed offer history is reached;
the probability-size key appears only when displayed offers mix. All welfare
measures and disposition shares average over potential disputes.

The five welfare columns are the three population-weighted net monetary burdens,
gross outcome error before legal costs and separate fee transfers, and real
expenditures. These are distinct measures, not additive components of one index.
Definitions and calculation checks accompany the aggregated data.

Supplemental materials is reserved for separate solution-path, game-tree,
liability-signal, multiple-equilibrium and strategy-change analyses. Standard
parameter variations remain in Results. There is no archive directory.

After validating the complete collection, copy it to the article's Results and
refresh the numbered four main figures and three main tables with caption and
source records. Git history retains superseded material; no push is part of this workflow.

## Organizing completed cases and publishing the collection

While production continues, `LitigCharts completed-cases --input <raw production records>
--output <article Results> --jobs 32` imports only cases marked complete by their
coordinator. It shares the final generator's individual-case layout and TeX processing.
It verifies previous source/render hashes before reusing diagrams, records completed
case input/output hashes in Run records/completed-cases.json, and skips unchanged
imports on repetition. It does not replace aggregate comparisons or modify production.
`--list` inspects the same completion state without writing anything.
`python -B scripts/publish_article_results.py verify-completed --results <article Results>`
checks every imported source/render hash and PDF/PNG before recording incremental
verification. Diagram compilation retries timeouts and known MiKTeX cache races
once, serially after the parallel batch; repeated failures remain explicit errors.

After the entire routine collection is generated, run from ACESim4:

```powershell
.\scripts\Publish-ArticleResults.ps1 -ResultsSource <completed Results> -ArticleDirectory <article repository> -Python <python with pypdf>
```

The publisher derives required coverage from the C# case matrix, checks complete
production manifests, every routine PDF/PNG, all printed three-decimal welfare
values, and the previous collection's reused equilibria/reports. It stages and
hash-checks a copy before replacing only Results, then refreshes main Figures 3/4
and Table 2 and their captions/source manifest. The other main exhibits, including
all pages of Table 3, and separate supplemental analyses are preserved. The model
primitives table's data references are updated to the expanded reports. `-VerifyOnly`
does not replace article outputs. Visual review and the final commit remain explicit
completion steps. No scripts are required inside the article repository.

Numbered main renders may be retained when the standalone TeX is byte-identical
and all previous TeX/PDF/PNG hashes match the manuscript manifest. This permits
refreshing source records while an unchanged PDF is open. The canonical result
and numbered copy then share those verified renders; `main-render-reuse.json`
records the reuse and the artifact-hash inventory is updated.

# Correlated-signals article results

The routine study has 124 cases: 114 CS004 cases plus ten CS006EF Complete
Fee-Shifting cases. The baseline crosses American, Trial Fee-Shifting and
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

For a clean report rebuild, first commit and use `Prepare-ArticleRebuild.ps1`
with the directories holding the 124 retained `CS004`/`CS006EF` equilibrium files.
It stages and hashes those profiles, clears only this repository's ReportResults,
then seeds `Run records/Retained study` with exactly 124 equilibrium files.
Preserve the returned provenance record with the final run records. Separate
analyses and their exact source profiles belong in the article's Supplemental
materials and must not be replaced with profiles from a report rebuild.

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
  Comparison folder exists only when both preferences are available. Baseline,
  Low noise and Offer-grid sensitivity pair their RN/RA cases. Other retained
  extensions currently have RN results only.
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

There are 937 routine exhibits: 744 individual diagrams, 68 welfare tables,
68 disposition charts and 57 four-panel strategy figures. Filing and answering
use nested circles, outlined squares and larger outlined diamonds for American,
Trial Fee-Shifting and Complete Fee-Shifting, respectively, where available.
These fixed-size markers are centered on the exact signal and probability.
The lower panels show separate offer strips for each available fee rule and preserve continue/exit
histories and mixing probabilities. No conditional mean-offer substitution is
used. All welfare measures and disposition shares average over potential disputes.

The five welfare columns are the three population-weighted net monetary burdens,
gross outcome error before legal costs and separate fee transfers, and real
expenditures. These are distinct measures, not additive components of one index.
Definitions and calculation checks accompany the aggregated data.

Supplemental materials is reserved for separate solution-path, game-tree,
liability-signal, multiple-equilibrium and strategy-change analyses. Standard
parameter variations remain in Results. There is no archive directory.

After validating the complete collection, use the article repository's
`scripts/Import-ArticleResults.ps1`, then `scripts/assemble_manuscript_exhibits.py`
to produce the numbered four main figures and three main tables. Git history
retains superseded material; no push is part of this workflow.

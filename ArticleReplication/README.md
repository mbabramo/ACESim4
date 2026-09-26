# Correlated-signals article replication

The public command is `run`. Scientific settings come from `ArticlePlan.cs` and optional command-line overrides. No input settings file, provenance record, integrity manifest, prior report or article checkout is required.

```sh
dotnet run --project ArticleReplication -c Release -- run --output /path/new-results --input /path/saved-solves --workers 4
```

Omit `--input` to calculate the selected solves afresh. Missing saved solves are computed by default. Use `--missing wait` to validate only available solutions without launching missing solves. An output directory must be new; the coordinator never deletes or publishes over an existing repository.

For a fresh source snapshot, locked dependency restore and Release rebuild of all referenced projects before execution:

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source /path/code --output /path/new-rebuild --input /path/saved-solves --workers 4
```

The output contains `ReportResults` (fresh calculations, audits and reusable solve files), `article` (the existing Results, Supplemental materials, Tables and Figures organization), and execution/build records. `rebuild` places these under `run`. Logs and hashes are generated output, not required inputs.

## Optional saved solves

Only these files are consumed by `run --input`:

- `Equilibria/<case>.equ`: complete primary equilibrium probabilities, revalidated in the full game.
- `Search/<case>/start-00000.equ`: complete approximate profile, or an explicit `NoEquilibriumFound` record for that initialization and budget.
- `Histories/<case>.history`: the optional solver path, with its strategy frames and native pivot snapshots.

These are readable, versioned JSON formats. A failed attempt means that this search found no acceptable equilibrium; it is not a proof of nonexistence. Changed search budgets/cutoffs require recomputation, except an already-triggered identical early stopping rule within both budgets. Mathematical validation is mandatory for every reused strategy. History diagnostics and native-to-policy projections are recalculated against the current full game; these trajectory checks are distinct from an algebraic pivot equivalence proof.

`ReportResults/Shortcuts` contains the same narrow files for the next run. Welfare and strategic decompositions, tremble responses, grouping, numeric reports, figures and tables are always recomputed. No decomposition receipt or old chart is a shortcut.

## Settings and stages

Defaults are in `CorrelatedSignalsSettings`. Useful overrides include `--costs 0.25,0.5,1,2,4`, `--starts 50`, `--pivots 20000`, `--cutoff 0.005`, `--noise 0.1,0.4`, `--grids 8x15,12x8,8x8`, `--extensions false`, and `--trial-only false`. Costs must include the reference multiplier 1. Fifty starts for each of the four core games means 200 attempts.

`--steps Primary,MultipleStarts,Welfare,Strategic,Trembles,Histories,StandardReports,Exhibits,Manuscript` is the default. Primary is required. Trembles includes the selected multiple-start profiles. Manuscript requires its contributing calculation/exhibit stages. `--cases case-id,...` permits a bounded test of the same full games. Never interpret a selected subset as a full article release.

The primary stage uses the ordinary exact solver with its existing seed, completion and validation conventions. MultipleStarts uses the separate stable floating-point search and its existing immediate/capped acceptance criteria. Saved complete profiles retain off-path strategies and agreement decisions. StandardReports runs the existing report generator and LitigCharts, including editable sources and previews. Exhibits and manuscript numeric bindings use native C# generators and embedded authored/layout sources.

Each numerical worker is single-threaded. `--workers` plus `--other-workers` may not exceed 32. `--reserve-cases` leaves specified unavailable cases pending. A temporary local guard also reserves the two original unfinished grid cases on the development machine; no frozen worker or original build is modified.

## Tools, migration and verification

See [INSTALL.md](INSTALL.md) for installed .NET, TeX, fonts, PDF utilities and the optional MSBuild container target. Dependencies are not copied into the output folder.

`export-shortcuts --run PASSED_RUN --output NEW_DIR` migrates validated older results. `export-history-shortcuts --histories OLD_HISTORY_PACKAGE --output NEW_DIR` converts old streams. These migration commands may read legacy records; public `run` does not. Legacy `reproduce`, `pack-*` and settings files remain only for migration/regression compatibility.

`test-shortcuts` tests stopping-policy compatibility and failed-attempt semantics. Separate `verify-primary-reproduction`, `verify-multiple-reports`, `verify-strategic-tables` and figure/table verifiers compare current results against explicitly supplied regression references. Such references are never required replication inputs.

`verify-saved-solves` compares complete primary/search profiles and scientific CSV fields; `verify-history-reproduction` compares every native history record exactly. [VALIDATION.md](VALIDATION.md) records executed fresh/reuse, Windows/Linux and full-generation checks.

Full collection validation, visual review and eventual live-repository migration remain separate release gates. Completion records retain `CompleteArticle: false` until full release evidence is assembled.

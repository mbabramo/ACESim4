# Correlated-signals article replication

## Journal replication

No programming experience is required. Install and start Docker, then create a new folder called `replication`. If using saved solutions, download the [saved-solutions ZIP](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip) and extract its contents into `replication/solutions`. That folder should directly contain `Equilibria`, `Search` and `Histories`. Skip the download to compute from scratch.

Open PowerShell (Windows) or a terminal (Linux) in the `replication` folder. Choose one command; no paths need editing.

**With saved solutions:**

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/solutions:/inputs:ro" -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-05 run --input /inputs --output /output/run --missing wait --workers 4
```

**From scratch, without saved solutions:** skip the solutions download and run this command in the same new `replication` folder. It calculates equilibria and solver histories as well, which can take days or longer.

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-05 run --output /output/run --missing compute --workers 4
```

Docker downloads the software and creates the output folders automatically. Leave the terminal open until the command finishes; then open **`replication/output/run/article`**. No GitHub login, source checkout, .NET or TeX installation is required. Use a new folder to repeat the exercise: an existing run is never overwritten. See [INSTALL.md](INSTALL.md) for Docker installation links and platform requirements.

Both commands regenerate **Results**, **Tables**, **Figures** and **Supplemental materials**. The author-maintained manuscript and bibliography remain separate. The saved-solutions route verifies the equilibria and histories before recalculating analyses and exhibits.

### Alternative: rebuild and run natively

Follow the [step-by-step instructions for running without Docker](INSTALL.md#run-without-docker): install .NET, TeX/fonts and PDF tools, extract the code into `replication/code`, and, if using saved solutions, extract those into `replication/solutions`. No Docker, Visual Studio or Git installation is needed. From `replication/code`, choose one command:

**With saved solutions:**

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --input ../solutions --missing wait --workers 4
```

**From scratch, without saved solutions:** skip the solutions download and run from `replication/code`.

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --missing compute --workers 4
```

The new directory `replication/output/run/article` contains **Results**, **Tables**, **Figures** and **Supplemental materials**. The separately maintained manuscript and bibliography are not part of this default command. The output directory must not already exist and must be outside the code checkout. With the complete supplied archive no equilibrium searches or history recreations are needed. For a calculation without shortcuts, omit `--input ../solutions` and change `--missing wait` to `--missing compute`; exact solves may take much longer. Commands work in PowerShell and a Linux shell after the prerequisites are installed.

## Commands

The public command is `run`. Scientific settings come from `ArticlePlan.cs` and optional command-line overrides. No input settings file, provenance record, integrity manifest, prior report or article checkout is required.

```sh
dotnet run --project ArticleReplication -c Release -- run --output /path/new-results --input /path/saved-solves --workers 4
```

Omit `--input` to calculate the selected solves afresh. Missing saved solves are computed by default. Use `--missing wait` to validate only available solutions without launching missing solves. An output directory must be new; the coordinator never deletes or publishes over an existing repository.

For a fresh source snapshot, locked dependency restore and Release rebuild of all referenced projects before execution:

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source /path/code --output /path/new-rebuild --input /path/saved-solves --workers 4
```

The output contains `ReportResults` (fresh calculations, audits and reusable solve files), `article` (the existing Results, Supplemental materials, Tables and Figures organization), and execution/build records. `rebuild` places these under `run`. The author-maintained **Article and bibliography** folder is separate: default replication neither generates nor replaces it. Logs and hashes are generated output, not required inputs.

## Optional saved solves

Only these files are consumed by `run --input`:

- `Equilibria/<case>.equ`: complete primary equilibrium probabilities, revalidated in the full game.
- `Search/<case>/start-00000.equ`: complete approximate profile, or an explicit `NoEquilibriumFound` record for that initialization and budget.
- `Histories/<case>.history` or `<case>.history.gz`: the optional solver path, with its strategy frames and native pivot snapshots. Gzip changes only storage; every frame is validated after decompression.

These are readable, versioned JSON formats. A failed attempt means that this search found no acceptable equilibrium; it is not a proof of nonexistence. Changed search budgets/cutoffs require recomputation, except an already-triggered identical early stopping rule within both budgets. Mathematical validation is mandatory for every reused strategy. History diagnostics and native-to-policy projections are recalculated against the current full game; these trajectory checks are distinct from an algebraic pivot equivalence proof.

`ReportResults/Shortcuts` contains the same narrow files for the next run. Welfare and strategic decompositions, tremble responses, grouping, numeric reports, figures and tables are always recomputed. No decomposition receipt or old chart is a shortcut.

## Settings and stages

Defaults are in `CorrelatedSignalsSettings`. Useful overrides include `--costs 0.25,0.5,1,2,4`, `--starts 50`, `--pivots 20000`, `--cutoff 0.005`, `--noise 0.1,0.4`, `--grids 8x12,12x8,8x8`, `--extensions false`, and `--trial-only false`. A grid without a risk suffix applies to both risk preferences. The article uses matched American/British comparisons at 8/12 and 12/8 for both risk preferences, with 8/8 as the control. The 15-based grids are excluded. Costs must include the reference multiplier 1. Fifty starts for each of the four core games means 200 attempts.

`--steps Primary,MultipleStarts,Welfare,TruthSensitivity,Strategic,Trembles,Histories,StandardReports,Exhibits` is the default. Primary is required. Trembles includes the selected multiple-start profiles. Supplemental utility curves are generated with Exhibits independently of the manuscript. `--cases case-id,...` permits a bounded test of the same full games. Never interpret a selected subset as a full article release.

Authors may add `--manuscript true` to compile the embedded manuscript snapshot in the fresh output directory after its contributing calculation/exhibit stages. This is optional and is not part of the journal's default replication command. It does not write to the separately maintained manuscript folder.

The primary stage uses the ordinary exact solver with its existing seed, completion and validation conventions. MultipleStarts uses the separate stable floating-point search and its existing immediate/capped acceptance criteria. Saved complete profiles retain off-path strategies and agreement decisions. StandardReports runs the existing report generator and LitigCharts, including editable sources and previews. Exhibits and manuscript numeric bindings use native C# generators and embedded authored/layout sources.

Each numerical worker is single-threaded. `--workers` plus `--other-workers` may not exceed 32. `--reserve-cases` leaves specified unavailable cases pending. Reservations are explicit command-line choices, not hidden machine-dependent settings. The current article plan excludes the still-running 8/15 and 5/15 backups; none of their workers, frozen builds or outputs is modified.

## Tools, migration and verification

See [INSTALL.md](INSTALL.md) for installed .NET, TeX, fonts, PDF utilities and the optional MSBuild container target. Dependencies are not copied into the output folder.

`export-shortcuts --run PASSED_RUN --output NEW_DIR` migrates validated older results. `export-history-shortcuts --histories OLD_HISTORY_PACKAGE --output NEW_DIR` converts old streams. These migration commands may read legacy records; public `run` does not. Legacy `reproduce`, `pack-*` and settings files remain only for migration/regression compatibility.

`test-shortcuts` tests stopping-policy compatibility and failed-attempt semantics. Separate `verify-primary-reproduction`, `verify-multiple-reports`, `verify-strategic-tables` and figure/table verifiers compare current results against explicitly supplied regression references. Such references are never required replication inputs.

`verify-saved-solves` compares complete primary/search profiles and scientific CSV fields; `verify-history-reproduction` compares every native history record exactly. [VALIDATION.md](VALIDATION.md) records executed fresh/reuse, Windows/Linux and full-generation checks.

Full collection validation, visual review and eventual live-repository migration remain separate release gates. Completion records retain `CompleteArticle: false` until full release evidence is assembled.

After copying a completed fresh collection into the article repository, the maintainer can check every file and directory with:

```sh
dotnet run --project ArticleReplication -c Release -- verify-delivery --generated /path/fresh-run/article --published /path/article-repository --output /path/outside-both/delivery-check.json
```

This read-only command accounts for every file on both sides, compares bytes without normalization, and rejects extra or missing directories, including empty leftovers. Only the top-level `.git` and author-maintained `Article and bibliography` entries are excluded. The verification record must be outside both collections. `test-delivery-verification --output NEW_DIR` exercises missing/extra files, changed bytes, empty directory leftovers and the author-folder boundary. These are publication checks, not input-bundle requirements.

If rendering or a manuscript dependency fails after scientific stages pass, `finish-reporting --run CALCULATION_RUN --output NEW_DIRECTORY --workers N --other-workers N` regenerates presentation without repeating calculations. If all reports have already rendered, `finish-manuscript --reporting REPORTING_RUN --output NEW_DIRECTORY` checks their retained evidence and rebuilds only the current authored manuscript. Failed attempts remain intact. `index-collection --run PASSED_RUN` refreshes portable documentation; execution records stay outside the published article folder.

The optional full [saved-solutions release](https://github.com/mbabramo/correlated-signals-article/releases) contains 74 primary profiles, 200 search outcomes and four compressed histories. Large HTML histories use adjacent local JavaScript chunks; keep those folders with the viewers. No recorded frame or numerical value is omitted.

### Truth-formula sensitivity

The default `TruthSensitivity` step (which can be omitted with `--steps`) revalidates the selected main-cost equilibria and freshly integrates alternative truth probabilities over posterior merits. It holds merits, signals, court decisions, payoffs and strategies fixed. No equilibrium search is started by this step. Include `Primary` and supply positive, distinct `--truth-exponents` containing 1. The approved default exponents are 0.5, 1, and 2; override them with `--truth-exponents`. The formula is `q^k / (q^k + (1-q)^k)`.

For example, after selecting strengths, run `run --input SAVED_SOLVES --output NEW_DIRECTORY --extensions false --trial-only false --steps Primary,TruthSensitivity --truth-exponents 0.5,1,2 --missing wait --workers 2 --other-workers 2`. Worker counts must reflect concurrent work. Output automatically includes full per-profile replay audits under `ReportResults/TruthSensitivity` and an unrounded CSV, readable cost/risk tables, and validation record under `article/Results/Aggregated Data/Truth sensitivity`. The default exhibit and manuscript stages do not generate a numbered truth-sensitivity figure; the separate `truth-figure` command remains available if a figure is wanted. Identity-map welfare must reproduce the primary assessment within the existing reporting tolerance; complete strategy, strategic game and best-response gains must remain unchanged, and real expenditures must be exactly invariant across maps. Signs in the CSV are arithmetic signs, with no tolerance-based relabeling of small differences.

## Endogenous litigation: independent starts and the staged comparison

The endogenous study has separate numerical stages. The original-method stage runs
ordinary generalized regret-matching CFR from ten explicit random starting profiles.
Gini and Liu are different, selection-guided searches, each with ten starts; their
pooled stage unions discovered strategies and solves again. They are not ten Nash
results followed by two averages. Keep these stages and their equilibrium concepts
separate in every report.

Build once into a new runtime folder, then freeze that folder. From the code root:

```sh
dotnet publish ArticleReplication/ArticleReplication.csproj -c Release --artifacts-path ../traditional-build -o ../traditional-runtime -p:BuildInParallel=false -m:1
python ArticleReplication/Scripts/endogenous_traditional.py prepare --input ../../outputs/endogenous-population-inputs --assembly ../traditional-runtime/ArticleReplication.dll --output ../../outputs/endogenous-traditional --workers 10 --repetitions 10 --iterations 1000 --check-every 100
python ArticleReplication/Scripts/endogenous_traditional.py run --run ../../outputs/endogenous-traditional
```

The ten seeds are 1729 + 989 times the zero-based repetition index. Within each
information set, independent exponential action weights are normalized (Dirichlet
with all parameters one). Initial regrets are zero; the explicit starting behavior
and initial strategy tallies are archived. No saved equilibrium, Gini/Liu result or
other repetition warms the search. Both players use independent behavior, rather
than a correlated distribution over complete strategy pairs.

The existing 1,000-iteration regret-matching updates and best-average-exploitability
checkpoint rule are retained. As in the legacy equilibrium file, the primary result
is the checkpoint's current behavioral profile; its average profile is separately
archived and audited, never substituted silently. Checks use unrounded probabilities.
Routine report generation and legacy equilibrium file output/reuse are disabled;
the update equations are unchanged. The fresh entry is tested for regular/Fast CFR
parity on uniform and randomized starts. Every worker uses DOTNET_PROCESSOR_COUNT=1
and disables library parallelism; the controller refuses more than ten workers.

Each run saves its request, actual scalar solver settings, game fingerprint, seed
archive, final current/average archives, final training arrays, outcomes and independent
Nash audit. The native GEBR and detached policy-tree best-response utilities must
agree. Raw and normalized Nash gains, grades at 1e-7 and 1e-5, and monetary certainty-
equivalent gains are separate fields. The default economic screen is 0.1% of the
0.001 activity benefit. It is a diagnostic, not a bound on welfare error. A completed
job is not automatically a verified equilibrium. Failed numerical targets remain
visible; no automatic tolerance change, profile substitution or convergence retry
occurs. A fixed budget may recover the same equilibrium repeatedly or fail to verify
some candidates. Report all those outcomes before drawing multiplicity conclusions.

`batch.json` binds the code revision, controller hash, full numerical runtime hashes,
input catalog, settings and seed schedule. Only `status.json` and the append-only
progress ledger change. Completed attempts and evidence are immutable. Resume with
`run --run RUN --resume` after diagnosis: completed results are hash-checked and skipped,
and still-live children are adopted using PID plus creation identity. Interrupted
attempts remain preserved and are not blindly repeated. Three worker/validation
failures stop further dispatch. Optional progress-file sharing locks do not detach
worker supervision. A previous pilot can be imported with `prepare --reuse-root PILOT`
only when its full scientific settings, runtime, case and seed identities match;
imported results retain their original evidence paths and count once.

To join the work done in pieces, record a versioned study recipe after the runtime
and batch are frozen:

```sh
python ArticleReplication/Scripts/endogenous_study.py record --workspace WORKSPACE --traditional-run RUN --source-snapshot SOURCE_TAR --output NEW_RECIPE.json
python ArticleReplication/Scripts/endogenous_study.py verify --recipe NEW_RECIPE.json
```

The recipe indexes published inputs, the original independent-start manifest, both
replacement indices, frozen prior configurations/runners/runtimes, validation receipts,
versioned analysis/report inputs, this new traditional batch, and the exact code source
archive. Its stage graph distinguishes original-method searches, Gini/Liu searches,
pooling, scientific validation and reporting. Exact argument arrays document rebuilding,
starting/resuming, and replaying reports. Internal paths are relative to the declared
workspace; `verify --workspace MOVED_WORKSPACE` supports relocated indices. Embedded
paths in older numerical archives need a separately recorded relocation, not rewriting
historical receipts. Replaying saved evidence and recomputing numerical results are
different operations. A new recipe version can bind later completed outputs without
changing the earlier recipe. Existing qualified pooled results and economic/objective
limitations remain qualified; reproducibility is not a numerical-accuracy certificate.
The traditional-result report series is added after its numerical validation.

After a complete traditional batch, run the read-only independent audit:

```sh
python ArticleReplication/Scripts/audit_endogenous_traditional.py --run RUN --population-manifest ORIGINAL_REPETITIONS.json --output NEW_AUDIT.json
```

This reconstructs the actual starting probabilities with an independent implementation
of the seeded random generator, checks all current/average behavioral archives,
compares strategic game hashes with the earlier population experiment, and checks
money-equivalent gains with 60-digit decimal arithmetic. Optional `--repeat-root`
checks that a separately rerun identical seed/settings pair produces identical
strategies, numerical audits and outcomes. The audit preserves all numerical grades
and reports exact profile distinctness without claiming multiple exact equilibria.

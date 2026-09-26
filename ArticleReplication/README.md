# Correlated-signals replication — isolated implementation

This C# coordinator consumes a resolved article plan, validates saved computational results, and invokes the numerical and LitigCharts projects. It never runs a solver merely because an input is missing. The temporary external-jobs overlay reserves running cases by both case ID and scientific game identity, including in scratch-mode planning.

The refined target is [code plus optional computational results](PortableReplicationContract.md): an empty output directory, no required NonGenerated folder or earlier article checkout, data-free presentation templates in the code repository, and all result-dependent content generated automatically. The current commands below are an intermediate implementation, not yet that portable contract.

Deployment uses installed .NET, TeX, fonts and PDF utilities. `doctor` checks them without copying installations into article directories. `dotnet build -p:BuildReplicationContainer=true` builds an optional Linux image through the same C# project. See [INSTALL.md](INSTALL.md) for commands, versions and the remaining integration limits.

## Current commands

`pack` and `pack-computations` import legacy records for migration and regression testing. They are not the final input contract. `pack-histories` packages completed pivot streams and their evidence. See [ComputationCaches.md](ComputationCaches.md) for the precise boundary between reusable computations and generated reports.

`export-primary-cache --run DIR --output NEW_DIR` exports only primary equilibrium vectors and game identities from a passed run. `reproduce` emits the same cache automatically in `ComputationCache`. A subsequent run consumes it through `--solutions`, without old action/outcome reports or audits. `verify-primary-reproduction` performs a separate exact scientific comparison against a reference run.

`reproduce` runs explicitly selected implemented steps into a fresh directory. Raw outputs go into `ReportResults`, reader-facing files into `article`, and requests/commands/validation alongside them. Existing directories are refused. No cleanup or live-repository publication occurs.

`rebuild` archives and hashes actual source (Git is optional), checks prerequisites, restores locked dependencies, builds ACESimBase, LitigCharts and this coordinator in Release with one MSBuild worker, records all executable hashes, and invokes `reproduce`. Example (substitute local paths and an available shared worker budget):

```powershell
dotnet run --project ArticleReplication -c Release -- rebuild --source C:\isolated\source --solutions C:\saved\solutions --histories C:\saved\histories --settings C:\isolated\source\ArticleReplication\correlated-signals.saved-results.json --external-jobs C:\isolated\external-jobs.json --output C:\isolated\fresh-rebuild --workers 1 --other-workers 2
```

`self-test --solutions DIR --output NEW_JSON` checks the canonical case set, exact frozen specifications, changed-cost propagation, reservations and native policy/welfare plots. It starts no solve.

## Implemented paths

- **Primary:** initialize each full game, check its complete identity, load the complete saved profile, check every action, normalization, full unilateral best response, accounting, fresh reports and off-path completion. Optional legacy regression inputs additionally check old reports, complete scientific profiles and welfare exactly.
- **Welfare:** run the established four-corner C# evaluator and compare all endpoints and main decomposition rows.
- **Strategic:** verify the original passed full diagnostic receipts, decompressed result hashes and unchanged complete endpoints before reusing coalition calculations. Missing endpoint-dependent comparisons remain pending.
- **Histories:** validate complete saved streams, original pivot counts, input hashes and complete endpoint policies using the existing trajectory criteria. Rebuild viewers without replaying pivots. This is not a new exact-arithmetic equivalence proof.
- **StandardReports:** reconstruct the six ordinary per-case diagrams using the game’s existing report generator. Execute `LitigCharts final-article-results` for standard individual, aggregate welfare/disposition/participation, signal and structural diagrams. Require PDFs, previews, editable sources, input identities and an output inventory under the existing Results/Supplemental materials structure.
- **MultipleStarts:** validate all recorded initializations and stopping rules, revalidate each accepted full profile, preserve rejected attempts, regenerate complete-link grouping, range summaries and Figure 8. This stage has its own approximate acceptance criteria.
- **Trembles:** compute the specified unilateral responses afresh, run independent response checks and regenerate individual and aggregate reports.
- **Exhibits:** native complete policies, Figures 1–8, Tables 1/2/3/5 and strategic/welfare supplements, populated from current validated computations and the central plan. Rendering reads no old figure/table TeX. Separate verification commands compare scientific values with explicitly supplied regression references.
- **Manuscript:** export the authored template and bibliography embedded in the source project, generate numeric bindings and compile with current numbered exhibits. Authored prose and existing revision flags are preserved. The historical static utility illustration remains an explicitly preserved source asset.

The scientific authority is `ArticlePlan`, built on the existing C# case factory. Changing `MainCostMultipliers` changes cases, comparisons, standard groups, cost-series data and figure rows. There is no second case list in a reporting script. The plan still contains externally reserved cases; incomplete comparisons are never represented by zeros.

## Still required before full migration

Fresh exact/approximate solving is not yet connected, so `FromScratch` is rejected before dispatch. All-stage computation-cache production/reuse, removal of remaining legacy approximate/tremble input dependencies, whole-collection coverage/visual QA, updated full Linux tests and final live-repository migration remain required. A successful selected-stage test is **not** a complete article release certificate; completion records retain `CompleteArticle: false`.

The numerical `Entry`, `Tremble` and `Verify` helpers retain the previously executed reproduction checks. Temporary workspace protections remain in `Pipeline` until the isolated implementation is ready for general use.

Every child numerical process uses `DOTNET_PROCESSOR_COUNT=1` and single-thread numerical-library settings. Compiler concurrency is explicit and must fit the shared 32-computation budget, including external work. No automatic monitor is installed.

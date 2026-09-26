# Correlated-signals replication — isolated implementation

This C# coordinator consumes a resolved article plan, validates saved computational results, and invokes the numerical and LitigCharts projects. It never runs a solver merely because an input is missing. The temporary external-jobs overlay reserves running cases by both case ID and scientific game identity, including in scratch-mode planning.

The refined target is [code plus optional computational results](PortableReplicationContract.md): an empty output directory, no required NonGenerated folder or earlier article checkout, data-free presentation templates in the code repository, and all result-dependent content generated automatically. The current commands below are an intermediate implementation, not yet that portable contract.

## Current commands

`pack` creates a portable, hash-verified saved-profile/render-input bundle from a reviewed article and its existing reproduction distribution. `pack-histories` packages completed pivot streams with their original receipts and input objects. These are computational results, not authored assets.

`reproduce` runs explicitly selected implemented steps into a fresh directory. Raw outputs go into `ReportResults`, reader-facing files into `article`, and requests/commands/validation alongside them. Existing directories are refused. No cleanup or live-repository publication occurs.

`rebuild` archives a clean committed source checkout, restores locked dependencies, builds ACESimBase, LitigCharts and this coordinator in Release with one MSBuild worker, records all executable hashes, and invokes `reproduce`. Example (substitute local paths and an available shared worker budget):

```powershell
dotnet run --project ArticleReplication -c Release -- rebuild --source C:\isolated\source --solutions C:\saved\solutions --histories C:\saved\histories --settings C:\isolated\source\ArticleReplication\correlated-signals.saved-results.json --external-jobs C:\isolated\external-jobs.json --output C:\isolated\fresh-rebuild --workers 1 --other-workers 2
```

`self-test --solutions DIR --output NEW_JSON` checks the canonical case set, exact frozen specifications, changed-cost propagation, reservations and native policy/welfare plots. It starts no solve.

## Implemented paths

- **Primary:** initialize each full game, check its complete identity, load the complete saved profile, check every action, normalization, full unilateral best response, accounting, report replay and off-path completion. Compare the scientific profile and welfare fields exactly with the frozen audit.
- **Welfare:** run the established four-corner C# evaluator and compare all endpoints and main decomposition rows.
- **Strategic:** verify the original passed full diagnostic receipts, decompressed result hashes and unchanged complete endpoints before reusing coalition calculations. Missing endpoint-dependent comparisons remain pending.
- **Histories:** validate complete saved streams, original pivot counts, input hashes and complete endpoint policies using the existing trajectory criteria. Rebuild viewers without replaying pivots. This is not a new exact-arithmetic equivalence proof.
- **StandardReports:** reconstruct the six ordinary per-case diagrams using the game’s existing report generator. Execute `LitigCharts final-article-results` for standard individual, aggregate welfare/disposition/participation, signal and structural diagrams. Require PDFs, previews, editable sources, input identities and an output inventory under the existing Results/Supplemental materials structure.
- **Exhibits:** native complete policy exhibits and dynamic main cost-series welfare figure; other custom layouts currently recompile data-bound cached TeX. Changed extension/search specifications are rejected until those renderers are ported, rather than publishing stale layouts.
- **Manuscript:** compile the protected authored source with its existing bibliography and generated numbered exhibits, without editing its prose.

The scientific authority is `ArticlePlan`, built on the existing C# case factory. Changing `MainCostMultipliers` changes cases, comparisons, standard groups, cost-series data and figure rows. There is no second case list in a reporting script. The plan still contains externally reserved cases; incomplete comparisons are never represented by zeros.

## Still required before full migration

Fresh exact/approximate solving and full multiple-start/grouping/tremble stage orchestration are not yet connected. `FromScratch` is therefore rejected before dispatch. `MultipleStarts` and `Trembles` are rejected when selected. The complete native replacement of cached custom layouts, independent whole-collection coverage/visual QA, portable source distribution, and final live-repository migration remain required. A successful selected-stage test is **not** a complete article release certificate; completion records retain `CompleteArticle: false`.

The copied numerical `Entry`, `Tremble` and `Verify` helpers retain the previously executed reproduction checks. The latter two are retained for consolidation, not represented as integrated stages. Temporary workspace protections remain in `Pipeline` until the isolated implementation is ready for general use.

Every child numerical process uses `DOTNET_PROCESSOR_COUNT=1` and single-thread numerical-library settings. Compiler concurrency is explicit and must fit the shared 32-computation budget, including external work. No automatic monitor is installed.

# Replication validation — simplified input contract

These tests ran in isolated directories. Live code/article repositories and frozen workers were not modified, and automatic monitoring remains disabled. The original two grid cases were reserved, not duplicated. This is implementation evidence, not a full article-release certificate.

## Saved solves only

`simple-full-v1` completed with an input directory containing only 72 primary `.equ` files, 200 search `.equ` records and four `.history` files. It required no input settings file, hash/provenance manifest, old report, cached decomposition or article checkout. It produced:

- 72 individually revalidated complete primary profiles and 200 checked search records (199 accepted profiles, one unsuccessful attempt).
- 142 freshly computed strategic directions (71 pairs), full coalition/tie/off-path checks and independent 24-order allocations; 34 fresh welfare pairs.
- 203 profiles and 6,496 fresh tremble response checks with independent validation.
- Four regenerated trajectory viewers; 13,680 frames checked against current game diagnostics and native projections, with maximum diagnostic difference zero.
- 228 native custom PDFs, 685 standard LitigCharts diagrams, independent coverage of 2,055 standard source/PDF/preview files, and the manuscript with generated numeric bindings.

`simple-full-science-equivalence-v1.json` compares complete primary/search scientific profiles and numeric reports exactly with the previous validated collection. Only runtime seconds, path metadata and line endings are excluded. Strategic table cells/unrounded selections, all grouping/figure values and all 6,496 tremble report rows also match exactly. No audit tolerance substitutes for these equality comparisons.

`simple-roundtrip-v1` consumes the files emitted by that run and revalidates all four core profiles, 200 search attempts and four histories, starting zero solves. The current history reader additionally checks every display coordinate, information-set uniqueness, offsets and the actual initial profile.

## Fresh calculation and rejection checks

`simple-fresh-search-v1` recalculates the first approximate start of each core game. All four complete strategies, acceptance decisions, stopping pivots and scientific reports exactly match the saved results (`simple-fresh-search-equivalence-v1.json`).

`simple-fresh-exact-v2` solves the full risk-neutral American cost-one game from scratch, records its history, revalidates it and emits reusable shortcuts. Its complete strategy and reports match exactly. Every one of the 316 history frames, including entering/leaving variables, native Z/W values, projections and diagnostics, matches the retained history exactly (`simple-fresh-history-equivalence-v1.json`). This is not a new full-tableau algebra proof; the separate exact ECTA adoption certificate remains preserved.

The first exact run completed its 315 pivots but found a Windows file-sharing error while packaging the history. That attempt and its frames are retained. Closing the writer before packaging fixes the error; the successful second run verifies the complete path. Completed strategies are now saved before downstream history export.

Sixteen focused shortcut checks cover finite failed attempts, malformed records, seed/cutoff/budget changes, early-stop compatibility and rejection of invalid probabilities. Changed budgets trigger recomputation unless the identical early stopping event already falls within both budgets. A normalized uniform primary file is rejected by full unilateral best response (`simple-invalid-policy-v1`). A failed `.equ` records a finite unsuccessful search, not mathematical nonexistence.

## Rebuild, Linux and presentation

`simple-rebuild-v1` passed source snapshot, locked restore, Release rebuild of all dependencies and the public `run` command, without Git metadata in its rebuilt checkout.

`simple-linux-core-v1` passed with networking disabled and read-only saved inputs: four primary profiles, 200 search records, fresh strategic/welfare calculations, native numbered exhibits and 47 standard diagrams/141 files. Full scientific profile/report equality, welfare, grouping and strategic-table equality against Windows passed. Installed toolchains and MSBuild-built image identities are retained under `container-builds`.

`simple-linux-fresh-exact-v1` also ran with no inputs and networking disabled. Its complete equilibrium, scientific reports and all 316 native/diagnostic history frames are exactly identical to the Windows fresh solve (`simple-linux-fresh-equivalence-v1.json` and `simple-linux-fresh-history-equivalence-v1.json`).

Representative regenerated participation/welfare figures and a strategic table were visually inspected on Linux/Windows; the approved typography, marker shapes and bottom legends were preserved. This is sampled review, not certification of every page or browser interaction.

## Evidence and remaining release work

The workspace `article-replication-csharp-20260926` retains tests, immutable build directories, container source snapshots, all failed attempts and executed command logs. Public reproduction commands are in [README.md](README.md) and [INSTALL.md](INSTALL.md). Explicit `verify-*` commands use earlier results only as optional regression references; replication itself never requires those references.

Whole-article release still requires the two reserved profiles, final collection-wide visual/browser QA and publication/migration checks. All completion records therefore retain `CompleteArticle: false`. A full no-input run of every expensive exact case has deliberately not been repeated; fresh/reuse parity is tested on the representative exact game and four approximate starts above. APT repository resolution is not pinned to an immutable snapshot, although base image digests and installed package versions are recorded.

Truth-formula sensitivity (2026-09-27): Release build `builds/truth-v2` passed. `truth-identity-main-v1` reproduced the identity truth assessment on all 20 main profiles. `truth-sensitivity-main-v1` used the approved default exponents 0.5, 1, 2, freshly revalidated all 20 profiles, and produced 150 welfare-comparison rows (2 risk assumptions x 5 costs x 5 measures x 3 maps). All comparisons preserved their baseline sign, with no sign-rounding tolerance. Full strategy, strategic game and BR gains were unchanged; real costs were exactly invariant across maps. No solves were started. Invalid zero, duplicate, nonfinite and missing-identity exponent lists were rejected before computation. The new stage is included in default public `run`/`rebuild` and remains selectable through `--steps`; exponents are C# defaults or CLI parameters. Executed command: `dotnet builds/truth-v2/ArticleReplication.dll run --input tests/simple-full-v1/ReportResults/Shortcuts --output tests/truth-sensitivity-main-v1 --steps Primary,TruthSensitivity --extensions false --trial-only false --missing wait --workers 2 --other-workers 2` (paths relative to the isolated replication workspace). This is a targeted stage validation; the entire article/container was not rebuilt again for this addition.

## Final selected-grid collection (October 4, 2026)

The default plan now uses matched risk-averse 8-signal/12-offer equilibria and retains risk-neutral 8-signal/15-offer equilibria. Both risk preferences at 12/8 and 8/8 are included. The first validated completion was selected independently of the welfare results.

A fresh snapshot and locked Release rebuild of the main checkout revalidated all 74 primary profiles and 200 search records (199 accepted, one finite unsuccessful attempt), starting no exact, approximate or history solves. Complete strategies and 3,142,912 scientific numeric cells equal the retained references exactly; elapsed time and path metadata are excluded, never numerical differences.

Fresh calculations passed for 36 welfare pairs, 146 strategic directions (73 pairs), 203 tremble profiles/6,496 checks, and 150 truth-sensitivity rows. All four histories passed replay/projection checks across 13,680 frames with zero diagnostic difference. This trajectory verification remains distinct from the preserved exact ECTA tableau-equivalence certificate. The baseline 64-versus-128-point quadrature test passed; the maximum change across 1,622 probabilities was 4.3068659749678773e-10.

Presentation produced 234 custom PDFs and 695 standard LitigCharts diagrams, with 2,085 standard source/PDF/preview files checked against the expected coverage. A MiKTeX font-cache race interrupted the first rendering attempt; its logs remain preserved outside the repositories. The renderer now retries that specific failure serially. Native reporting/manuscript recovery commands validate retained evidence and rebuild presentation without repeating scientific calculations. Missing citations in the current authored manuscript were repaired; its 25-page layout and prose were retained.

All numbered exhibits and representative new-grid/supplemental outputs were visually reviewed. Manual interactive viewer review remains a separate release gate: the desktop browser security policy blocked local HTML inspection. The large British risk-averse viewer retains every frame via byte-identical adjacent script chunks, each below GitHub's file-size limit. Its optional compressed history belongs in the saved-solutions release archive. The current Docker engine was unavailable, so the updated image has not been rebuilt; the earlier Linux parity tests above remain the available container evidence. No complete no-input rerun of the costly case collection is claimed.

The isolated delivery workspace retains all successful and failed attempts and executed commands. Published article folders exclude temporary build/execution receipts. Saved inputs contain only 74 primary .equ files, 200 search .equ files and four optional compressed histories; settings, generated reports and provenance are not inputs.

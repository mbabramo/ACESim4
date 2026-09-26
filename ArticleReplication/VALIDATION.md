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

# Replication validation

## Container release status — October 4, 2026

The author has approved accepting tiny platform-dependent differences in floating-point reporting. The release comparator now permits an absolute difference of at most **1e-14** in derived reporting values and records every nonzero difference with both values. Complete strategies (including off-path probabilities), game identities, best-response results, search decisions, thresholds and pivot counts remain exact. Exact-arithmetic solver checks and individual equilibrium acceptance criteria are unchanged. Nonfinite values, changed structure/labels and larger reporting differences still fail. This is a cross-platform release-comparison rule, not a change to any calculation or displayed precision.

Each primary report also exports a full-precision CSV before its existing six-significant-figure formatting. The release check compares these values at the same 1e-14 limit. If a last-bit difference straddles a formatting midpoint, a displayed CSV difference is accepted only when both displayed numbers are reproduced by the existing formatter from their independently validated unrounded values. These display differences are recorded separately; there is no blanket 1e-6 allowance for CSV values.

The updated comparator passes 14 focused tests, including rejection of a one-bit strategy change. A fresh Windows replay of all 74 profiles reproduces every previous scientific result and formatted report exactly after adding the full-precision export. A separate Linux replay of all 74 profiles passes the new primary comparison: complete strategies and best-response results are exact, with 4,405 last-bit differences across the full-precision reporting fields (maximum 1.7763568394002505e-15). The additional raw CSV precision reveals differences that were hidden by the earlier formatted reports. Four displayed CSV cells straddle the same midpoint: 0.31573049999999997 on Linux versus 0.3157305 on Windows, displayed as 0.31573 and 0.315731 respectively. Both displays are reproduced by the unchanged formatter. This is a primary-profile comparison, not yet a complete container release or public-download test.

The first complete Linux container run generated the collection and passed its individual audits, but failed the then-required exact comparison with the published Windows results. The image was not published. Separate replays of all 74 primary profiles on Linux .NET 9.0.20 and 9.0.11 confirmed identical complete strategy vectors and best-response results. Five profiles have the same 11 last-bit reporting differences on both runtime versions (maximum absolute difference 5.56e-17). Disabling hardware intrinsics does not change them. The raw terminal-row comparison preserves row counts, order and monetary outcomes, but shows differing terminal probabilities; one polarized-merits quadrature node also differs. Those earlier checks applied exact equality; the author subsequently approved the bounded reporting rule above. The container command is not yet a validated public release.

The beginner instructions are mirrored in the generated collection README. Release testing also includes the exact copy-and-paste command, using a directory with spaces, followed by a full anonymous image-download test. Neither that future test nor a successful full Linux release is claimed here.

## Current journal download test — October 4, 2026

A fresh clone of the public `correlated-signals` branch at `cec938cf8a9e265a6759ded96e0475b9d1cbf690`, an empty NuGet cache, a newly extracted saved-solutions archive and a new output directory passed the documented full `rebuild` command. It completed in about 27 minutes on Windows with 27 workers. A journal may choose fewer workers; no scientific settings depend on that choice.

The command rebuilt its dependencies, individually revalidated all 74 primary profiles and 200 search records (199 accepted), and started zero expensive solves. Complete saved strategies and numeric reports matched their regression references exactly. It freshly calculated 36 welfare pairs, 150 strategic directions, 6,496 tremble checks and 150 truth-sensitivity rows, and checked 13,680 history frames. It generated 238 custom PDFs and 697 standard diagrams: 4,331 files in the four research-output folders, with all 812 relative Markdown links resolving. All 28 Table 5 comparisons are audited, with no pending row.

Default replication generated no manuscript folder. The author-maintained manuscript is separate; its separately reviewed optional build is 25 pages. All 11 numbered exhibits and representative supplemental outputs were visually reviewed; 21 reviewed previews remain identical in the clean GitHub test. Numeric history replay and static viewer checks passed, but browser security restrictions prevented manual testing of interactive viewer controls. The updated full collection has been tested on Windows; the earlier Linux core and fresh-solve parity checks below do not constitute a fresh full Linux/container test.

The saved-solutions download contains only 74 primary `.equ` files, 200 search `.equ` records and four compressed histories. It contains no settings, provenance, old reports or cached decompositions. The source instructions and archive are linked from the repository README. Temporary execution evidence remains outside the repositories. No complete no-input rerun of every expensive game is claimed.

## Earlier validation records

The chronological records below describe earlier tests and their then-pending work. The current status above supersedes their pending-grid and pending-repeat statements.

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

The default plan uses both risk preferences and both fee rules at 8 signals/12 offers, 12/8 and the 8/8 control. The missing risk-neutral 8/12 pair was computed and validated before selecting this matched collection. All 15-based grids are omitted from the published plan; the two older active backups remain untouched.

Fresh snapshots and locked Release rebuilds of the main checkout revalidated all 74 primary profiles and 200 search records (199 accepted, one finite unsuccessful attempt), starting no exact, approximate or history solves. The earlier mixed-grid collection compared 3,142,912 scientific numeric cells; the matched-grid collection passed the same native complete-strategy/report equality checks against the corresponding references, including the two newly computed profiles. Elapsed time and path metadata are excluded, never numerical differences. Nineteen focused shortcut/planning checks pass, including matched grid coverage and exclusion of 15-based grids.

The subsequent matched-grid rebuild passed fresh calculations for 36 welfare pairs, 150 strategic directions (75 pairs), 203 tremble profiles/6,496 checks, and 150 truth-sensitivity rows. Complete primary/search strategies and scientific reports again equal their preserved references exactly. The tremble and truth-sensitivity CSV files also agree cell for cell with the preceding collection. All four histories passed replay/projection checks across 13,680 frames with zero diagnostic difference. This trajectory verification remains distinct from the preserved exact ECTA tableau-equivalence certificate. The baseline 64-versus-128-point quadrature test passed; the maximum change across 1,622 probabilities was 4.3068659749678773e-10.

The matched-grid one-command rebuild produced 238 custom PDFs and 697 standard LitigCharts diagrams, with 2,091 standard source/PDF/preview files checked against expected coverage. The complete collection has 4,337 files. All 28 Table 5 comparisons are audited, with no pending rows; its unrounded differences match the validated profiles exactly. The generated manuscript binds 33 numerical phrases to current data, and 26 citation keys, 23 cross-references and 817 relative Markdown links resolve. A MiKTeX font-cache race interrupted an earlier collection's first rendering attempt; its logs remain preserved outside the repositories. The renderer now retries that specific failure serially. Missing citations in the current authored manuscript were repaired. Bibliography item spacing is set to zero to retain the earlier compact layout without changing body type or prose.

All numbered exhibits and representative new-grid/supplemental outputs were visually reviewed. Manual interactive viewer review remains a separate release gate: the desktop browser security policy blocked local HTML inspection. The large British risk-averse viewer retains every frame via byte-identical adjacent script chunks, each below GitHub's file-size limit. Its optional compressed history belongs in the saved-solutions release archive. The current Docker engine was unavailable, so the updated image has not been rebuilt; the earlier Linux parity tests above remain the available container evidence. No complete no-input rerun of the costly case collection is claimed.

The isolated delivery workspace retains all successful and failed attempts and executed commands. Published article folders exclude temporary build/execution receipts. Saved inputs contain only 74 primary .equ files, 200 search .equ files and four optional compressed histories; settings, generated reports and provenance are not inputs.

## Journal replication and the separate manuscript

The subsequent full one-command rerun finished successfully, again validating all 74 primary profiles and 200 search records with no new exact, approximate or history solves. The author's later clarification separates the manuscript from journal replication: the default command now generates Results, Tables, Figures and Supplemental materials. `--manuscript true` is an explicit author-only opt-in for compiling the embedded manuscript snapshot in a fresh output.

Targeted checks confirm that defaults exclude Manuscript, opt-in adds only that stage, explicit exclusion works, default presentation creates no manuscript folder, and its README does not link to a missing local manuscript. Utility curves still compile independently; their rendered pixels are identical to the full rerun. All 19 existing shortcut/planning checks pass. No solver, game, numerical setting, saved input or scientific calculation changed for this separation, and no further complete scientific rerun is claimed for it.

# Replication implementation validation — 2026-09-26

This is a tested implementation checkpoint, not a complete article-release certificate. All checks used isolated directories and launched **zero equilibrium solves**. Existing grid workers, frozen builds and live repositories were left untouched. Automatic monitoring remains disabled.

## Executed checks

| Check | Result |
|---|---|
| Release build, SDK 10.0.401, target net9.0 | Passed on Windows and inside the Linux container; 20 pre-existing nullable warnings in the imported numerical helpers |
| C# / MSBuild container creation | Passed; source context snapshotted before Docker build, base digests pinned, image identity and package inventory retained |
| Windows prerequisite and rendering check | Passed: installed tools, Latin Modern, PDF preview and merge |
| Linux prerequisite and rendering check | Passed on Debian 12 / .NET 9.0.20, including Clear Sans for standard reports |
| Linux core-case replication, network disabled, read-only input mount | Four complete risk-neutral/risk-averse American/British profiles identical to the reference science; full best responses and numeric replay passed |
| Linux standard LitigCharts | 47 diagrams, 141 source/PDF/PNG artifacts; independent filing/hash/coverage checks passed |
| Native Figures 1–6 | Exact comparison of signal masses, reached strategy supports and coordinates, omissions, disposition values/styles, all extracted histories and displayed worked-path bindings passed |
| Native Tables 1 and 5 | Every displayed cell, heading and panel order matched the approved reference exactly |
| Plan/cache/isolation regression checks | 91 checks passed, including Windows-style path rejection on either OS and preservation of external job reservations |
| Rebuild from downloaded-style source with no Git metadata | Passed: source snapshot, tool preflight, locked restore, Release build, four complete profile revalidations and 47 standard diagrams |
| Visual inspection | All six native main figures, four table pages and representative Linux standard outputs reviewed; approved presentation retained |

The previous Windows full saved-results test covered 72 primary profiles and 685 standard diagrams. That test still depended on cached custom TeX for several other outputs, so it did not establish the final computation-only-input contract. The two missing grid profiles remain explicit pending rows; unavailable comparisons are never replaced with zeros.

## Retained evidence

The isolated implementation workspace retains `tests/container-build-v4`, `tests/doctor-linux-v4`, `tests/linux-standard-pilot-v4`, `tests/native-main-figures-v2`, `tests/native-main-equivalence-v2.json`, `tests/native-main-tables-v2`, `tests/native-main-table-equivalence-v1.json`, and `tests/self-test-v16.json`. These contain actual commands, complete logs, source/build/input/output hashes and individual validations. Earlier failed attempts are retained: preflight caught an incorrect font-file name; Linux rendering exposed missing Clear Sans support and then missing font binaries. The final container includes both and passes the rendering tests. No solver or validation tolerance was changed to make a test pass.

Use the commands in [INSTALL.md](INSTALL.md). `main-figures` / `main-tables` need a previously passed primary run; their verification commands compare against an explicitly supplied approved reference. Those references are test or migration inputs, not sources used to generate the new figures/tables.

## Remaining integration work

The portable full-replication contract remains incomplete: consistent all-stage cache production/reuse without legacy reporting inputs, fresh-computation providers, updated complete Linux tests, full collection checks and eventual migration to the main directories. APT package versions are recorded, but its repository resolution is not yet frozen to an immutable snapshot. No-input full article execution remains unavailable. These limitations are explicit in completion records.

## Native collection and minimal primary cache checkpoint

- `native-collection-v1` passed on Windows: 72 complete primary profiles, 200 approximate attempt records (199 accepted full profiles), 142 cached strategic directions, 34 welfare pairs, four histories, 228 freshly generated custom PDFs, 685 standard diagrams and the manuscript with generated numeric bindings. Inputs supplied no figure/table TeX, PDF or authored source. The two unavailable grid cases remained externally reserved. This run excluded Trembles because that stage was tested separately.
- `native-tremble-stage-v1` passed: 203 profiles, 6,496 fresh response checks, independent response validation, and exact equality of all 6,496 report rows against retained reference results.
- `native-multiple-equivalence-v1.json` verifies every grouping, maximum within-group distance and all 995 Figure 8 values/coordinates. `native-strategic-equivalence-v2.json` verifies every Table 2/3 cell and full selected decomposition data. These are test references, not figure generators.
- `primary-minimal-cache-v1` uses only equilibrium vectors and case/game identities. Its four complete core profiles, best-response gains, welfare and output CSVs exactly match `primary-regression-cache-v1`, which also runs the optional legacy comparisons. Evidence: `primary-cache-equivalence-v1.json`. Both paths regenerate action and outcome reports.
- `primary-cache-roundtrip-v1` consumes the cache emitted by `primary-minimal-cache-v1`; exact full-profile, best-response, welfare and CSV comparison passed. `primary-cache-rejections-v1` rejects changed file hashes, an invalid probability even with recomputed file hashes, a mismatched complete game and a proposed calibration change.

These later changes have not yet received a new full Linux collection test. The minimal primary format does not yet replace all legacy dependencies in approximate/tremble stages. Old failed attempts and immutable build snapshots remain available for diagnosis.

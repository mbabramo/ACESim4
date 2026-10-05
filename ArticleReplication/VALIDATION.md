# Replication validation

## Published release

The public image is `ghcr.io/mbabramo/acesim-correlated-signals:2026-10-05`, built from source `78d126cb790fdec1e2d4ee99db40875025970e9a`. Its immutable digest is `sha256:458aaec62f39755f7059eefc92e3238ed2bb5e14d1f42b2a1eb30a17699962da`. It includes the current approximate-American comparison, expanded replication instructions and consecutive Table 4 numbering.

The [build, full replication and publication run](https://github.com/mbabramo/ACESim4/actions/runs/37252374414) passed. An independent [public-download run](https://github.com/mbabramo/ACESim4/actions/runs/37257837276) downloaded the same digest without registry credentials and executed the documented command from a directory containing spaces. Both full runs used the public saved-solutions archive, networking disabled and four single-threaded workers. Research generation took about 71 and 70 minutes, respectively.

Both runs produced **4,405 research files** and passed every default stage. Every file and directory was accounted for against article commit `0d080991296db4099ea5af2326787a45a813ad4d` and between the two independent runs. **There were no numerical differences and no numerical tolerance was needed.** Complete strategies and best-response results match exactly. The only substantive document change relative to the article reference is the updated container version in README; all other differences are timestamps, paths, tracking hashes, formatting or PDF compression/metadata. The current article README matches the new generated README exactly. The author-maintained manuscript and bibliography were not regenerated or overwritten.

The container also passed the 11 focused approximate-comparison tests. Reversal macros and tremble groups compare each British outcome with the separate maximum across all accepted American approximate profiles of the same risk specification, using strict greater-than without a tolerance. Existing collection counts remain 49 British risk-averse profiles, 11 plaintiff reversals, four defendant reversals and zero joint reversals. Prior targeted regeneration verified every retained macro and all tremble rows/statistics; a stale group label was rejected. The unused `ArticleUsualBritishRa` macro was removed. Current table generators and release verification use Table 4.

The previous `2026-10-04.1` image remains available. Earlier Windows/Linux verification and fresh-solve tests described below remain applicable; the new full runs verified the latest reporting code against the same scientific collection without starting expensive solves.

## Scientific checks

- All **74 primary profiles** passed complete saved-strategy replay, normalization, full unilateral best-response, outcome accounting and game-identity checks. The selected grids include both risk preferences and both fee rules at **8 signals/12 offers, 12/8 and the 8/8 control**. The 15-based grids are excluded.
- All **200 multiple-start records** were checked: **199 accepted approximate profiles** and one finite unsuccessful attempt. Acceptance criteria, starting profiles, seeds, cutoffs and stopping pivots are unchanged.
- Fresh analysis covered **36 welfare pairs**, **150 strategic directions**, **6,496 tremble checks** and **150 truth-sensitivity rows**. All **28 outcome-summary comparisons** are audited; there are no pending rows.
- Four saved histories passed replay and projection checks across **13,680 frames**. This is separate from the exact ECTA tableau-equivalence tests.
- The supplied shortcuts started **zero exact, approximate or history solves**. The inputs contain only primary equilibria, multiple-start records and optional compressed histories. Decompositions, tremble experiments and reporting are recalculated; settings and old reports are not inputs.

Complete strategies, including off-path probabilities, game identities and best-response results are exactly identical to their scientific references. Exact ECTA arithmetic, pivot selection and individual equilibrium acceptance checks have not been relaxed.

## Cross-platform file comparison

The earlier Linux-to-Windows comparison permits the author's approved absolute **1e-14** difference only in derived floating-point reporting. Every accepted nonzero difference is recorded. The largest observed difference was **1.7763568394002505e-15**. Four displayed CSV cells straddle a six-significant-digit rounding midpoint; they were accepted only after comparing the full-precision values and reproducing both displayed values with the unchanged formatter. There is no blanket tolerance for displayed values or strategy probabilities.

All 4,405 files in the new release were independently compared against the current research collection. There were no missing or extra files or directories. All **935 PNG previews** were byte-identical, and decoded document content and resources in all **937 PDFs** were identical. The comparison classified 2,885 files as byte-identical, 936 as identical PDF documents with different metadata/compression, 499 as JSON formatting/metadata differences, 84 as CSV elapsed-time differences, and one as the documented README version update. No scientific value differed.

Between the two new independent Linux runs, **2,921 files were byte-identical**; 936 PDFs differed only in document metadata/compression, 499 JSON files only in formatting/output metadata, and 49 CSV files only in elapsed time. Their directory inventories matched exactly, with no numerical differences or tolerance required. Metadata differences are accounted for explicitly rather than presented as byte-identical files.

## Fresh-solve and rejection tests

Representative Windows and Linux runs calculated the full risk-neutral American cost-one equilibrium without inputs. Complete strategies, scientific reports and all 316 history frames (315 pivots) matched. Fresh runs of the first approximate start of each of the four core games reproduced their saved strategies, acceptance decisions and stopping pivots.

The release also passed 19 shortcut/planning checks and 14 reporting-comparison checks. These include rejection of incompatible seeds, budgets and cutoffs; invalid probabilities; nonfinite or materially changed reports; and a one-bit change in a strategy probability. Separate delivery tests reject missing/extra files and directories, including empty leftover directories.

## Presentation and scope

All numbered exhibits and representative supplemental outputs were visually reviewed. The container PNGs are byte-identical to the reviewed collection, and PDF content/resources match. Static viewer code and compressed trace payloads match, and every history frame was numerically checked. **Manual testing of interactive viewer controls remains outstanding:** the available browser tool blocked local HTML inspection.

Default replication generates Results, Tables, Figures and Supplemental materials, not the manuscript. The author-maintained article and bibliography are preserved separately; `--manuscript true` is an optional author-only build of the embedded snapshot. The separately reviewed article PDF is 25 pages.

A new no-input solve of every expensive game has not been performed; representative fresh/reuse parity and full saved-profile validation are the evidence above. The tested container platform is Linux x86-64. Native Windows reproduction is also tested; instructions for both routes are in [INSTALL.md](INSTALL.md). Temporary execution logs, failed attempts and delivery backups are retained outside the public repositories.

## Consecutive article table numbering

The outcome-summary table is now Table 4, following Tables 1-3. The current generator, collection links, inventory, release verification and optional embedded author snapshot use the corrected number. The previous Table 4 welfare exhibit had become Figure 7. The current container includes the corrected numbering; no numerical calculations change with this renumbering.

The renumbering passed an isolated Release build and C# regeneration from the 74 validated primary profiles. All displayed cells and panel ordering match the published table, and all 28 full-precision comparisons match the previous native generation exactly. Six full-precision fields differ from the previously published Linux calculation by at most 5.56e-17, consistent with the already validated platform roundoff; the published table payload is retained byte for byte. The nine renamed table assets retain identical contents, and the 4,405-file collection inventory is consistent. The latest author manuscript was preserved, rebuilt to 25 pages, and visually checked on pages 21-22 with Table 4 and its cross-reference resolved. No equilibrium solves were launched.

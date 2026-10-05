# Replication validation

## Published release

The complete Windows rebuild and two independent complete Linux container runs passed. The public image is `ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1`, built from source `c44ccd6f6ec28f38ee4ad5d410768207afd26fe5`. Its immutable digest is `sha256:3a7056e3218c11c0f65299be9f3aa9425b879bc6f8b0d08d002a3b8d8421b7e9`.

The [build/test/publication run](https://github.com/mbabramo/ACESim4/actions/runs/37233263723) passed before publication. A separate [public-download test](https://github.com/mbabramo/ACESim4/actions/runs/37239180680) downloaded the image without registry credentials and executed the exact beginner command from a directory containing spaces. It regenerated the entire research collection with networking disabled and four single-threaded workers. Generation took about 55 minutes; the whole public-download workflow took about 58 minutes. The public saved-solutions archive was downloaded independently.

Both full Linux runs produced 4,405 research files and passed every default stage. Every generated file and directory has been accounted for. Article commit `840eefd43b6f2028d1ad3a71ea88b12bc17f3111` matches the second run byte for byte after synchronization; the author-maintained manuscript and Git metadata are excluded from that delivery check.

The article README was subsequently expanded to give four complete commands: Docker or native execution, each with saved solutions or from scratch. The current C# documentation generator reproduces that expanded README exactly. This changes instructions only; that documentation update left all other published research files unchanged. The immutable `2026-10-04.1` image still generates the earlier, shorter README, which links to the current online instructions. All four documented commands use capabilities already present in that tested image; no new image or full scientific rerun is claimed for this documentation update.

The current source also updates the manuscript's reversal macros and tremble-group labels to compare with **every accepted American approximate profile**, using the separate maximum for each outcome measure. This replaces the exact American benchmark. Regeneration from saved results confirmed the same 49 British risk-averse profiles, 11 plaintiff reversals, four defendant reversals and zero joint reversals; every retained macro, all 203 tremble-profile statistics and all 6,496 response-check rows are unchanged. Eleven focused comparison tests pass, including strict ties, separate maxima and empty/nonfinite input rejection; a deliberately mislabeled tremble profile is rejected before producing manuscript values. The obsolete `ArticleUsualBritishRa` macro is removed, and the optional embedded manuscript uses the revised wording. No solves were launched or live author files changed. This source update has not been incorporated into the immutable image, whose earlier benchmark happens to give the same classifications and numbers for the supplied collection.

## Scientific checks

- All **74 primary profiles** passed complete saved-strategy replay, normalization, full unilateral best-response, outcome accounting and game-identity checks. The selected grids include both risk preferences and both fee rules at **8 signals/12 offers, 12/8 and the 8/8 control**. The 15-based grids are excluded.
- All **200 multiple-start records** were checked: **199 accepted approximate profiles** and one finite unsuccessful attempt. Acceptance criteria, starting profiles, seeds, cutoffs and stopping pivots are unchanged.
- Fresh analysis covered **36 welfare pairs**, **150 strategic directions**, **6,496 tremble checks** and **150 truth-sensitivity rows**. All **28 outcome-summary comparisons** are audited; there are no pending rows.
- Four saved histories passed replay and projection checks across **13,680 frames**. This is separate from the exact ECTA tableau-equivalence tests.
- The supplied shortcuts started **zero exact, approximate or history solves**. The inputs contain only primary equilibria, multiple-start records and optional compressed histories. Decompositions, tremble experiments and reporting are recalculated; settings and old reports are not inputs.

Complete strategies, including off-path probabilities, game identities and best-response results are exactly identical to their scientific references. Exact ECTA arithmetic, pivot selection and individual equilibrium acceptance checks have not been relaxed.

## Cross-platform file comparison

The Linux-to-Windows comparison permits the author's approved absolute **1e-14** difference only in derived floating-point reporting. Every accepted nonzero difference is recorded. The largest observed difference was **1.7763568394002505e-15**. Four displayed CSV cells straddle a six-significant-digit rounding midpoint; they were accepted only after comparing the full-precision values and reproducing both displayed values with the unchanged formatter. There is no blanket tolerance for displayed values or strategy probabilities.

All 4,405 files were independently compared before delivery. There were no missing or extra files. All **935 PNG previews** were byte-identical, and decoded document content and resources in all **937 PDFs** were identical. Other differences were line endings, paths, timestamps, tracking hashes, compression headers, report inventory updates, the README readiness notice or the reporting differences described above.

The two independent Linux runs required **no numerical tolerance**: 2,885 files were byte-identical; 936 PDFs differed only in document metadata/compression; 499 JSON files differed only in formatting or output metadata; and 85 CSV files differed only in elapsed time. Their directory inventories matched exactly. The subsequent byte-for-byte synchronization check is a separate delivery check, not a substitute for this independent comparison.

## Fresh-solve and rejection tests

Representative Windows and Linux runs calculated the full risk-neutral American cost-one equilibrium without inputs. Complete strategies, scientific reports and all 316 history frames (315 pivots) matched. Fresh runs of the first approximate start of each of the four core games reproduced their saved strategies, acceptance decisions and stopping pivots.

The release also passed 19 shortcut/planning checks and 14 reporting-comparison checks. These include rejection of incompatible seeds, budgets and cutoffs; invalid probabilities; nonfinite or materially changed reports; and a one-bit change in a strategy probability. Separate delivery tests reject missing/extra files and directories, including empty leftover directories.

## Presentation and scope

All numbered exhibits and representative supplemental outputs were visually reviewed. The container PNGs are byte-identical to the reviewed collection, and PDF content/resources match. Static viewer code and compressed trace payloads match, and every history frame was numerically checked. **Manual testing of interactive viewer controls remains outstanding:** the available browser tool blocked local HTML inspection.

Default replication generates Results, Tables, Figures and Supplemental materials, not the manuscript. The author-maintained article and bibliography are preserved separately; `--manuscript true` is an optional author-only build of the embedded snapshot. The separately reviewed article PDF is 25 pages.

A new no-input solve of every expensive game has not been performed; representative fresh/reuse parity and full saved-profile validation are the evidence above. The tested container platform is Linux x86-64. Native Windows reproduction is also tested; instructions for both routes are in [INSTALL.md](INSTALL.md). Temporary execution logs, failed attempts and delivery backups are retained outside the public repositories.

## Consecutive article table numbering

The outcome-summary table is now Table 4, following Tables 1-3. The current generator, collection links, inventory, release verification and optional embedded author snapshot use the corrected number. The previous Table 4 welfare exhibit had become Figure 7. The existing immutable container still uses the old Table 5 filename; no numerical calculations change with this renumbering.

The renumbering passed an isolated Release build and C# regeneration from the 74 validated primary profiles. All displayed cells and panel ordering match the published table, and all 28 full-precision comparisons match the previous native generation exactly. Six full-precision fields differ from the previously published Linux calculation by at most 5.56e-17, consistent with the already validated platform roundoff; the published table payload is retained byte for byte. The nine renamed table assets retain identical contents, and the 4,405-file collection inventory is consistent. The latest author manuscript was preserved, rebuilt to 25 pages, and visually checked on pages 21-22 with Table 4 and its cross-reference resolved. No equilibrium solves were launched.

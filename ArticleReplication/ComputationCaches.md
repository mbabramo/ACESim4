# Computation caches, not article assets

The optional input is a shortcut for expensive computation. The C# article plan determines the game, cases, numerical conventions and requested stages. A proposed input cannot define those settings. Reports, audits, tables, figures and manuscript numeric bindings are generated outputs.

| Cached computation | Reason to retain it | Reuse requirement |
|---|---|---|
| Complete equilibrium vector | Avoid repeating its solve | Matching complete game and selected case; normalization, complete-vector round trip, unrestricted best response, accounting and fresh reports |
| Pivot history | Recreate solution-path viewers without repeating the solve | Matching inputs, numerical protocol, recorded stream and endpoint; trajectory criteria remain distinct from exact-arithmetic equivalence |
| Multiple-start attempt record, including failures | Account for every requested initialization and avoid rerunning the search | Matching seed, stopping policy and numerical conventions; accepted full profiles are revalidated under the approximate criterion |
| Coalition best-response computation | Avoid repeating a strategic decomposition | Matching complete endpoint policies and conventions, full coalition/tie/off-path validation evidence |

File hashes, game identities, settings, dependency identities and producer receipts accompany the computations. These are provenance, not additional scientific results. They do not replace scientific validation.

Old action reports, outcome CSVs, complete report JSONs and prior audits are useful **optional regression fixtures**. They must not be required to generate their replacements. Old figure/table TeX, PDF and chart coordinates are never computational inputs. Static authored text, bibliography, layout and artwork belong to the code project.

## Implemented primary format

`correlated-signals-computations-v3` currently supports the primary stage. `inputs/primary.json` binds each proposed complete vector to its case and complete game fingerprint. `computations/primary/<case>/equilibrium.equ` stores that vector. `bundle.json` lists integrity hashes and `producer.json` identifies the producing validation and assembly. The old expected-audit/profile fields are null. There are no old action reports, outcome reports, audits, figure sources or PDFs.

`reproduce` emits this cache after successful primary validation. `export-primary-cache --run DIR --output NEW_DIR` uses the same producer for an existing passed run. Either cache is directly accepted by the same `reproduce --solutions DIR` consumer. Every reuse reruns the scientific checks and regenerates reports. The retained source calibration is checked before accepting proposed inputs.

The four core cases have been reproduced from this minimal format with identical complete scientific profiles, full best-response gains, welfare and report CSVs compared with the legacy regression-input path. No tolerance was substituted for these equality comparisons. Existing audit tolerances remain additional checks.

## Remaining consolidation

The older `pack`, `pack-approximate` and `pack-computations` commands are migration adapters. The computational-v2 pack still carries legacy reference data. Approximate and tremble stages still consume some of those migration inputs, and their cache producers have not yet been consolidated into v3. Fresh-computation providers are not yet connected; omitted or incompatible caches do not silently start simulations. Therefore the complete fresh-or-reuse architecture is not finished.

The final contract requires each stage to expose compute, export and validate/reuse through the same C# coordinator. A completed run must produce the cache a later run can consume. Tests may compare against separately retained historical reports, but ordinary replication must not need them. No-input execution, all-stage cache round trips and full Windows/Linux collection tests are required before declaring completion.

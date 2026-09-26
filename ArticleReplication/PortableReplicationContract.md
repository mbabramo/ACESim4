# Target replication contract: code plus optional computational results

This records the user's refined requirement. It is a target contract, not a claim that the current prototype implements it. Only the correlated-signals article is in scope for this implementation.

## User interface

The user downloads the code, follows documented Windows or Linux instructions to install the supported .NET SDK, TeX distribution, required fonts and PDF utilities, and supplies an output directory. An optional input directory contains proposed computational results to validate and reuse. The output directory can initially be empty; it requires no NonGenerated/NonAutogen folder and no earlier article checkout. Standard project dependency restore may require network access. The replication command does not download or copy the SDK, TeX installation, fonts or PDF-tool installations into the source, input or output directories.

Proposed command (not implemented yet):

```
dotnet run --project ArticleReplication -c Release -- run --article CorrelatedSignals --output ./replicated --input ./solutions
```

Omitting input requests fresh computation for all selected stages. Execution settings select stages and resource limits. The full scientific case/comparison/exhibit plan is derived from the existing C# authority. Neither a second manually maintained case list nor old article outputs define what gets generated.

## What belongs in the code repository

- C# numerical, validation, decomposition, reporting and orchestration code.
- Versioned article settings, case selection, exhibit definitions, excerpt-selection rules and formatting conventions.
- TeX templates and styles containing layout and authored text, with explicit data bindings for result-dependent content. Templates must not contain an old result's plot coordinates, scientific table cells or derived numeric captions as substitutes for bindings.
- Authored manuscript/bibliography and truly static artwork/source assets, organized with the article adapter. A separate preservation folder in the output repository is unnecessary.
- Tool/dependency versions, hashes and licensing information, reproducible source identity and collection-validation rules.

Every quantity derived from model settings or computation must be filled by code: table entries, plot coordinates, legends/categories when data-dependent, selected strategy/action probabilities, decomposition excerpts, convergence summaries, sample sizes, captions and numeric manuscript references. A generated TeX macro/data file can provide manuscript numbers while preserving authored prose. Layout constants and bibliographic numbers are not simulation results. Numerical changes cannot automatically rewrite an author's interpretation; affected result-dependent assertions need explicit checks/author flags rather than invented replacement prose.

## Optional input directory

Computational caches may contain complete equilibrium strategies, solver histories, start-level records and other expensive computational results, together with scientific identities and sufficient validation/provenance data. They must not supply figure/table TeX, finished PDFs, old chart coordinates, reporting executables or a previous article tree as hidden prerequisites.

Bind a cache to the intended case, numerical conventions, initialization/start and selection role. Recompute applicable validation using the current code before reuse. Keep exact-primary equivalence, accepted approximate profiles and trajectory replay criteria distinct. A valid alternative equilibrium is not silently substituted for the designated primary result. Missing results are computed under the selected execution policy; invalid/mismatched results are reported explicitly, never relabelled as compatible. Keep reusable outputs under ReportResults so a completed run can provide the next run's input directory.

Resolve the model and any derived calibration from the central scientific settings before accepting a cache. An optional proposed solution cannot define the model against which it is judged. Calibration results may themselves be cached only under matching inputs and their own verification rules.

Use portable data schemas and logical relative identifiers. Historical absolute paths may remain provenance text, but cannot be required for execution. Importing legacy files is a migration function, not the public data format or authority for validation.

## Output and execution

Preserve the familiar reader-facing Results, Figures, Tables, Supplemental materials and Article and bibliography folders, populated entirely by the current code/templates and validated computational results. ReportResults holds reusable computation and validation; commands/build/tool identities/logs are retained separately. Existing output content must not be destructively cleared without an explicit ownership/archive policy.

The coordinator must work from a downloaded source archive as well as a Git checkout. Git cannot be a mandatory runtime prerequisite. Use a release source manifest when Git metadata is absent, and record actual source/tool identities. Document tested versions and installation steps for .NET, TeX, fonts and PDF utilities on Windows and Linux. Discover installed tools through PATH or explicit configuration. Before expensive work, verify the required executables, versions, TeX packages and fonts with a small rendering check and give specific installation guidance for anything missing. Record tool versions and identities as metadata; do not copy their installations into the article directory. Do not silently change the approved typography or rendering engine. Current rendering uses installed lualatex, bibtex, pdftoppm and pdfunite; the prerequisite documentation and comprehensive preflight remain to be completed.

An optional container provides the same entry point. Keep its versioned recipe with the code and pin its dependencies. Tool installations belong inside the container image, not in the mounted article directory. Mount proposed computational inputs read-only and a separate output directory writable. The container is an additional deployment option, not a requirement for native Windows/Linux replication. Its currently tested scientific/rendering scope is recorded in VALIDATION.md; full article portability requires completing the remaining integrations below.

Test both operating systems in actual execution. A net9.0 target alone does not establish portability. Audit native/Windows-only dependencies, path semantics, case sensitivity, process invocation and legacy serialization on the selected article path. Separate mathematical/data equality from PDF metadata and platform raster differences; investigate scientific differences, and inspect rendering on each platform.

## Acceptance test

1. Start with a fresh code distribution, empty output directory, optional computational-only inputs, and documented system prerequisites (or the optional container). No previous article folder or cached report sources may be reachable as runtime prerequisites. Tool installations stay outside all mounted/output article folders.
2. Execute the single command, validate every reused result, and populate ReportResults before producing the dependent collection.
3. Check complete expected coverage, scientific values, source/data bindings, placement, references, tool identities and visual presentation; missing items fail or are explicitly pending under an external-work overlay.
4. Change one central scientific setting (initially the cost set) and verify all affected cases, comparisons, excerpts, tables, figures, captions and inventories follow it without stale results.
5. Repeat on Windows and Linux. Test from-scratch providers separately using appropriate bounded fixtures before any costly full execution; cached reproduction does not prove fresh-solving integration.

During development, the two existing grid solves remain reserved for import by case ID and scientific game identity. Never duplicate, restart, rebuild or otherwise disturb them. This temporary deployment overlay is separate from the portable article plan. Do not launch the full no-input computation as part of implementing this contract.

## Current implementation gap

Native generation now covers Figures 1–8, Tables 1/2/3/5 and the strategic/welfare supplements; Rendering.cs reads no old scientific TeX. Authored templates live in the code project, with generated manuscript numeric bindings. Multiple-start/grouping and tremble stages are integrated and tested. Source snapshots, prerequisites and an MSBuild-driven container are implemented. A Windows computational-v2 run generated the complete currently available collection, including 685 standard diagrams and 228 custom PDFs, but the migration pack still supplied old reports to some validators.

The primary stage now produces and consumes a minimal equilibrium-and-identity cache without old report inputs; see ComputationCaches.md. The remaining stages need the same cache contract, fresh providers, current complete Windows/Linux tests and whole-collection release checks. No-input execution remains unavailable. Existing external reservations and two pending grid cases remain explicit. This contract is not yet fully satisfied.

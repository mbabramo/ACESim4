# Portable replication contract

The implementation exposes `run --output NEW_DIR [--input SOLVES_DIR]` and `rebuild --source CODE_DIR --output NEW_DIR [--input SOLVES_DIR]`. The public contract is described in [README.md](README.md).

The optional input directory contains only saved complete solutions, per-start unsuccessful search records in the same `.equ` format, and optional solver histories. No input provenance, hash manifest, settings file, prior report or cached decomposition is required. All scientific defaults are in C#, with explicit CLI overrides. Missing solves are computed unless `--missing wait` or a reservation defers them.

The same numerical methods and downstream generation operate with or without shortcuts. Reuse skips the numerical search and revalidates a supplied complete strategy. A saved failed start records its finite search outcome, not nonexistence. Histories are independently checked against current game diagnostics and endpoint policies. Reusing histories does not establish a new exact pivot-algebra proof.

All decomposition, sensitivity, grouping, numeric reporting, standard LitigCharts output and custom exhibits are freshly generated. Authored text, bibliography, templates and static illustrations are source-controlled project resources. Data-bound manuscript numbers are generated; authored interpretation is not automatically rewritten. No non-generated folder is required in the output repository.

System dependencies are installed normally, or supplied by the optional Linux container built through MSBuild. Neither approach copies SDK/TeX/font installations into the article directory. Output build/command/validation records are for review, not scientific inputs.

The implemented command and tested selected cases must not be confused with completed release validation. A whole collection still requires complete coverage, current-platform tests, visual QA and explicit release evidence before replacing the live repository. Original running grid cases remain reserved during isolated development.

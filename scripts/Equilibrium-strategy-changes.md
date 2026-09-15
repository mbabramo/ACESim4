# Equilibrium strategy changes

The supplemental workflow compares the same saved equilibrium profiles used by
the strategy figures and welfare reports. For each cost it generates every
directed fee-rule comparison within each risk level, and every directed risk
comparison within each fee rule. Reverse comparisons are calculated separately.

Run `scripts/Rebuild-ArticleSupplemental.ps1` to prepare, calculate and render
the collection. See [Article-supplemental.md](Article-supplemental.md) for
scheduling, prerequisites and cache verification. The current six scenarios at
five costs produce 90 comparisons and 90 tables.

## Individual commands

Calculate a request, retaining tie and off-path completion checks:

```powershell
dotnet run --project LitigCharts -c Release -- equilibrium-changes --request <calculation-request.json> --calculate-only
```

Render its verified saved calculations:

```powershell
dotnet run --project LitigCharts -c Release -- equilibrium-publication --request <calculation-request.json> --output <table-directory> --previews <temporary-QA-directory>
```

`--previews` is optional. The table command validates the request, numerical
outputs and source fingerprints before selecting rows. It writes one PDF and
PNG per contrast in the output directory, with sibling `Sources/Tex` and
`Sources/Json` directories for layouts and selected values. Pass the workflow's
`Tables` directory as `--output`.

## Output organization

Under `Equilibrium strategy changes`, `Data/cost-N/<contrast>/` contains
one request, a calculation manifest and the full numerical result. Data holds computed
results and reproduction records; the C# implementation and shared methodology
explain the calculations. There is no
representation subfolder. `Tables` contains the rendered comparisons;
`Sources/Profiles` preserves exact equilibrium and action-report inputs, and
`Sources/Process Logs` contains execution records. Table layouts and selected
values are in `Sources/Tex` and `Sources/Json`, with matching filenames. The planner generates a
reader-facing README and copies the shared methodology template to
`Methodology and Explanation.md` at the workflow root.

## Interpretation and selection

The [methodology template](templates/equilibrium-strategy-methodology.md) defines
the direct-first decomposition, conditional action-loss selection, offsetting
effects, grouping rules, reach and sensitivity flags, units and limitations.
It is the shared explanation for all tables. Four contributions plus the
explicit selection residual account for the observed strategy change before
rounding. Undefined conditional counterfactuals remain labeled as such.

Tables select qualifying coordinates from each saved-equilibrium calculation.
The auxiliary mixing searches and intersection across alternative profiles are
disabled. Native mixed strategies in the saved equilibria remain intact,
including action-specific offer probabilities when a scalar offer would be
misleading. The separate multiple-equilibrium study remains independent.

Use `scripts/verify_article_supplemental.py --output <supplemental-directory>
--changes-only` to check coverage, fingerprints, accounting and every printed
number against its TeX source. Inspect changed PDF/PNG layouts before use.

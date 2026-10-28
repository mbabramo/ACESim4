# Integration with the completed article baseline

The integration starts from the completed correlated-signals revision
`ab48c8f4baa895dfa3d23f030f49d7062c778512` and merges the eight CFR, benchmark,
and workflow commits through `764f76fd53367438e8e82a3477f7bf92fad26235`.
The earlier refactoring fixtures alone did not validate the final article:
they had been captured against the older September 20 game catalog.

The final article's case factory, agreement-to-bargain rules, explicit offer
grids, ECTA implementations, approximation/search conventions, and solver
tracing are retained. Predictive CFR remains an explicitly selected algorithm.
The endogenous article's model and numerical conventions remain unchanged.

## Endogenous launcher correction

The published base launcher used the console's global game choice when
constructing the endogenous option matrix. With that choice set to correlated
signals, precaution transformations failed with an invalid generator cast.
The endogenous launcher now selects its existing `PrecautionNegligenceGame`
factory itself, including single-option execution. The console choice and
correlated-signals launcher are unchanged.

Baseline comparisons use that same explicit endogenous factory on the
published revision. This is a launch correction, not a change to its payoffs,
chance probabilities, solver parameters, or article model.

## Validation scope

Validation uses the published saved-solutions bundle: 74 exact primary
profiles, 200 approximate-search attempt records (199 accepted), and four
saved solver histories. Saved solutions are replayed in the complete games;
no absent results are replaced and no new long-running production searches
are started. Complete profiles include off-path actions and agreement choices.

The numerical replication stages are invoked as follows, with separate input
and output directories:

```powershell
dotnet run --project ArticleReplication -c Release -- run `
  --input SAVED_SOLUTIONS --output FRESH_OUTPUT `
  --steps Primary,MultipleStarts,Welfare,TruthSensitivity,Strategic,Trembles,Histories `
  --missing wait --workers 6 --other-workers 2
```

The published coordinator's comparison commands include
`verify-primary-reproduction` and `verify-saved-solves`, each taking
`--generated`, `--reference`, and `--output`. They compare complete scientific
profiles, best responses, search classifications, and report values exactly;
elapsed seconds and explicitly identified path provenance are excluded.

Additional before/after characterization covers:

- All 2,268 endogenous option names, variable settings, and printed descriptions,
  against the original explicit model factory.
- Sixteen two-point endogenous CFR runs, each 100 iterations: Regular/Fast,
  costs 1/4, risk neutral/averse, and fee multipliers 0/1. Current and average
  strategies, training arrays, and all retained report cells are compared exactly.
- Four two-point agreement-enabled exact solves, covering both risk settings
  and American/complete fee shifting. Initial LCPs, every exact tableau/basis/
  determinant hash, pivot choices, and final profiles are compared.
- Both existing equilibrium-report fixtures, including individual,
  correlated-profile and behavioral-average reports and restored state.
- Predictive-CFR unit checks and the console archive/outcome self-test.

Tests for the older exit-fee suite were updated to the already-published
92-case extension plan, current fee labels, and allowed American-to-complete
fee-rule intervention. The old assertions failed identically on the unmodified
published baseline. No production behavior was changed to satisfy them.

## Recorded results (October 28, 2026)

- 215 focused tests passed. Four tests initially failed only because an isolated
  build directory had no solution ancestor; they passed with an explicit
  `ACESIM_REPORT_RESULTS_DIRECTORY`. No production change was needed for this.
- The benchmark archive/outcome replay self-test passed.
- All 74 complete primary profiles, full best responses, welfare values, and
  ordinary report values matched the completed Windows article collection exactly.
- All 74 full-precision CSV reports matched a fresh replay of unmodified
  `ab48c8f4` exactly, excluding the `Seconds` column. This newer diagnostic export
  is absent from the earlier Windows collection.
- All 200 search-attempt classifications and all 199 accepted complete profiles
  and scientific reports matched the completed article collection exactly.
- All 150 strategic contrast JSON files were byte-identical. The replay also
  checked off-path and tie cases, all 16 coalitions per player, and all 24
  component orders.
- The 36 welfare pairs, 20 truth-mapping profiles (150 rows), and 203 tremble
  profiles passed. Thirty-two best responses were independently checked.
  All 158 generated CSV tables matched exactly, excluding elapsed seconds.
- Every frame in all four saved histories was reevaluated in the current game:
  13,680 frames, including 13,676 saved pivots, with zero diagnostic difference.
  No full-size production equilibrium searches were started.
- All 24 before/after characterization files were byte-identical: the endogenous
  catalog, sixteen reduced CFR runs, four exact solves, two report fixtures, and
  their completion record. The four fresh exact solves used 244 pivots in total;
  their initial LCPs, pivot-by-pivot exact state hashes, and final policies matched.

The numerical downstream replay was built before the endogenous-only launcher
correction. That correction is not used by the correlated-signals coordinator;
all 74 primary cases and the endogenous characterization were additionally run
with the final build. The final article factory, ECTA implementation, and
`ArticleReplication`/`LitigCharts` sources are unchanged from `ab48c8f4`.

The [machine-readable validation receipt](validation/article-integration-20261028.json)
records counts, comparison scope, baseline revisions, and hashes of the detailed
local receipts. The full local evidence is in `outputs/article-integration` in
the integration task workspace. Numerical CSV comparison excludes only the
explicit `Seconds` column; profile comparison excludes identified file-path
provenance. No numerical tolerance was substituted for equality comparisons.

These checks distinguish numerical replay from fresh equilibrium selection.
They do not constitute a new solve of every full-size article configuration,
or a new visual review of all rendered figures and PDFs. The four broader
organization improvements remain separate follow-up work.

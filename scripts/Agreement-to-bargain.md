# Observable agreement-to-bargain robustness study

`--plan agreement-to-bargain` is the separate `CS007AB` thirty-setting design.
It crosses the three baseline fee rules with risk neutrality and CARA alpha 2,
at cost multipliers 0.25, 0.5, 1, 2, and 4. Each setting requests one exact
initialization. Matching saved CS004/CS006EF baseline equilibria are comparison
inputs only; they never seed or solve CS007AB. There is no multiple-start search.

The sequence is filing, answering, private exit commitments, simultaneous
agreement choices, disclosure of both choices, and (only if both agree)
the existing simultaneous offers. Refusal routes to the same exit/trial
resolution as nonoverlapping offers, with no independent cost or fee trigger.

From a clean source checkout (an isolated, committed reproducible snapshot is
appropriate when the user's original working tree has unrelated changes):

```powershell
dotnet test ACESimTest -c Release --filter 'FullyQualifiedName~AgreementToBargainTests|FullyQualifiedName~LitigGameTests|FullyQualifiedName~CorrelatedSignalsMultipleEquilibriaReportTests'
dotnet build ACESimDistributedSaturate -c Release
.\scripts\Run-ArticleAgreementToBargain.ps1 -OutputDirectory <study> -Workers 30 -SkipBuild
```

The existing process scheduler assigns settings to workers and preserves
completed tasks. Hidden CS007AB workers set `DOTNET_PROCESSOR_COUNT=1`, and
the launcher disables nested optimization parallelism. Reduce `-Workers`
for limited memory. Output filenames and cache identities include the new
master report prefix and agreement option identity. Every attempt logs UTC
times, arithmetic, prior index, seed, and pivot/cycling limits. Failures and
fallbacks remain explicit. Poll the batch no more than hourly.

After workers finish and aggregation succeeds:

```powershell
LitigCharts\bin\Release\net9.0\LitigCharts.exe agreement-study-audit --input <study>\Sources\Production --output <study>
LitigCharts\bin\Release\net9.0\LitigCharts.exe agreement-study-audit --plan baseline-single --input <retained-baseline-production> --output <study>\Baseline
python -B scripts\agreement_study_decompositions.py --study <study> --exe LitigCharts\bin\Release\net9.0\LitigCharts.exe --jobs 30
python -B scripts\agreement_study_exhibits.py --study <study> --jobs 4
```

The audit reloads every individual saved profile; checks normalization and
full current-profile best responses; reproduces action and numeric reports;
and enumerates joint history probabilities. Monetary conservation is checked
at unrounded terminal values against costs implied by the disposition,
independently of six-significant-digit display formatting. Unreached
conditional probabilities are null. Off-path prescriptions, including any
uniform completion of an unspecified off-path information set, are retained.

The five existing headline monetary columns are population-weighted plaintiff
shortfall, nonliable defendant burden, liable defendant burden, gross outcome
error before fees/costs, and real litigation expenditures. Refusal is a stage
event, not a new terminal disposition. Comparisons use each profile's own
outcomes, retain mixed action distributions, and distinguish reached behavior from off-path prescriptions. Each comparison
uses one verified equilibrium per model; it makes no claim of uniqueness.

The article deliverable is `Supplemental materials/Agreement to bargain`.
Do not run the routine publisher or replace numbered figures, tables, or text.
Use the generated inventory to connect each exhibit to its profile JSON and
saved-strategy/action-report hashes. Visually inspect PDF/PNG outputs before
delivery. Findings must distinguish this single-equilibrium robustness check from claims
about all equilibria. The equilibrium-decomposition analysis uses the saved new
profiles and full unilateral best responses; it does not solve more equilibria.

The 90 directed within-model decompositions compare each fee pair in both
directions within each risk level, and each risk pair in both directions within
each fee rule, at all five costs. Four opponent components (participation,
offers, exits, agreement) use 16 coalitions and 24 replacement orders. Explicit
selection residuals and tie/off-path completion checks are retained.

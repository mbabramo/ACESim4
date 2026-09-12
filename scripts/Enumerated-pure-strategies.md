# Exhaustive monotone pure strategies

This is an opt-in solver (`GameApproximationAlgorithm.EnumeratedPureStrategies`) using `IStrategiesDeveloper`, the existing launcher factory, ordinary game-tree evaluation, GEBR best responses, `ReportCollection`, and `InformationSetActionReport`. Production algorithm defaults and the validated article run plans are unchanged. The experiment entry points never invoke distributed processing or the production suite.

From the repository root, build and run a tiny benchmark first, then the requested baseline:

```powershell
dotnet run --project LitigCharts -c Release -- pure run --output C:\Experiments\PureTiny-001 --signals 2 --offers 2 --fee both --workers 2 --verify-all
dotnet run --project LitigCharts -c Release -- pure run --output C:\Experiments\PureBaseline-001 --signals 5 --offers 5 --fee both --workers 2 --verify-all
```

The output root must be fresh, or use explicit `--resume` for the same source/options/numerical representation. Each fee rule has its own `CSPURE-v1-n5-m5-American` or `...-British` directory. Matrix data are immutable; resume validates source, options, catalog and the initialized tree, then reads completed blocks and computes only missing blocks. Checksums reject corruption. A lock prevents concurrent writers. Interrupted `.partial` files are never treated as completed checkpoints. Reports are created in new directories so an earlier analysis is not replaced.

Without `--verify-all`, the default verifies the 12 strongest profiles plus the representatives of both clusterings at every requested tolerance. Existing opponent responses are reused. Candidate timing and the extrapolated all-opponent time are printed before an optional all-opponent pass. Responses run serially on one evaluator; the worker limit controls immutable terminal-contribution accumulation, with disjoint matrix rows. Initialization and verification do not share mutable evaluators between workers.

For matrices only, use the console's opt-in command:

```powershell
dotnet run --project ACESimConsole -c Release -- --enumerated-pure --output C:\Experiments\PureMatrices-001 --signals 5 --offers 5 --fee both --workers 2
```

Regenerate reports or change tolerances without computing payoffs again:

```powershell
dotnet run --project LitigCharts -c Release -- pure report --input C:\Experiments\PureBaseline-001\CSPURE-v1-n5-m5-American --output C:\Experiments\PureReports-002 --tolerances 0,0.0001,0.001,0.005,0.01,0.025 --outcome-radius 0.025 --strategy-radius 0.01
```

An existing outcome cache permits reports without reinitializing the game. To create an outcome cache or calculate additional responses, the original source state must still match. `--verify 20` or `--verify-all` on the report command requests additional verification. `--repository <path>` specifies the repository root when the current directory differs. Never put results inside the production results directories.

## Model and strategy domain

`LitigGameEnumeratedPureLauncher.CreateOptions` first constructs and validates the current focused article option matrix and selects the baseline at cost multiplier 1 and the requested fee rule. It changes only the party signal and offer counts and the experimental identifier/metadata. It preserves continuous uniform merits Q, T|Q ~ Bernoulli(Q), 64-point Gauss–Legendre integration, party/court sigma 0.2, binary court outcomes, one-point damages, and the published 0.15 filing/answering plus 0.15 trial costs per party. It does not use `smallerTree` as a substitute for configuring continuous merits.

On the model's shared plaintiff-favorable signal scale, P files increasingly and abandons decreasingly; D answers decreasingly and defaults increasingly. Both monetary offers increase weakly. No symmetry is imposed. Exit is committed before offers but implemented after unsuccessful bargaining, so exit-planning types retain offers and the monotone offer schedule crosses the exit boundary.

For k participating types, choose one of k+1 continuation boundaries and one of C(k+m-1,k) weakly increasing offer schedules. Nonparticipating types have no retained continuation/offer choices. Thus S(n,m) = sum[k=0..n] (k+1) C(k+m-1,k). At n=m=5, there are 21 participation/exit configurations, 1,302 strategies per party and 1,695,204 profiles. All full information sets receive valid deterministic completions; omitted actions cannot create uniform mixing. Catalog boundaries are zero-based and half-open: P participates at signal >= n-k and continues at >= n-c; D participates at < k and continues at < c.

## Payoffs and deviations

The reusable solver traverses the existing game tree once, compiling each terminal's utility, chance probability and compatible strategy IDs. Workers accumulate terminal contributions in fixed tree order. No litigation payoff formula is reimplemented. A separate SequenceForm initialization serves as a numerical reference in tests. Both use terminal utility rounding and chance rationalization on the `MaxIntegralUtility` grid (currently 100,000), with `RoundOffChanceDigits` also recorded (currently 6). Collapsed terminal lotteries retain their existing internal integration before terminal utility rounding.

For every cell, rP = max(row alternatives) UP - UP and rD = max(column alternatives) UD - UD; epsilonRestricted = max(rP,rD). The two individual gains and all tied best-response IDs are retained. The numerical tie/equilibrium tolerance is 1e-9 expected-utility units. This is numerical verification of the rounded finite game, not symbolic exactness. Near-equilibrium tolerances are separately configurable in reporting.

GEBR in `BestResponse.cs` optimizes entire own strategies at information sets from deeper decisions backwards, including continuations after changing earlier choices. Each response is cached by opponent ID and replayed to verify its claimed utility. An unrestricted best pure response also bounds mixed deviations in this finite expected-utility game. No mixed equilibrium profiles are searched. With both 1,302-strategy catalogs, at most 2,604 responses verify all profiles for one fee rule.

## Reporting and clustering

`LitigCharts/EnumeratedPureReport.cs` filters separately for EACH requested epsilon threshold before clustering. It uses deterministic greedy leader clustering ordered by epsilon, then row and column, with L-infinity distance and first matching representative. The two clusterings are independent:

- Outcome features: unconditional filing, answering, settlement, implemented abandonment/default, trial, expenditures, and the two existing truth-relative monetary measures. Baseline damages equal 1, so monetary and probability features use the documented article units. Default radius 0.025.
- Reached-strategy features: chance probability mass on each party/signal/decision/action. Exit commitments receive weight only on unsuccessful bargaining paths, because settlement makes the exit continuation ineffective; offers still count on settled paths. Zero-reach actions and full-profile completions have no weight. This describes realized strategy similarity, not unrestricted strategic equivalence. Default radius 0.01.

CSV output distinguishes restricted pure numerical equilibria, restricted near-equilibria, candidates outside tolerance, and unrestricted verification results. Cluster counts are not equilibrium-selection probabilities. SVG plots show settlement against trial, color by maximum deviation gain, and up to 20 representative cluster labels; all clusters are in JSON. The existing information-set report is exported for strongest candidates and outcome representatives with candidate/profile headings; its single-action utility losses are descriptive, not the verification statistic. Strategy representatives are verified and identified in the cluster JSON and catalog.

Outcome accounting replays existing game logic, expands collapsed endings and the existing posterior truth distribution, and retains unrounded money within leaves. The outer chance weights match the matrix. Reported monetary outcomes can therefore differ slightly from rounded utility minus initial wealth. `DefendantExcessNetBurden` and `PlaintiffRecoveryShortfall` are the article-facing labels for `FalsePositiveExpenditures` and `FalseNegativeShortfall`; `NetOutcomeFidelityLoss` is their sum. Net litigation expenses include fee transfers.

## Files and checks

- `run.json`, `catalog.json`: complete option/numerical/restriction/source identity and stable strategy IDs, thresholds and offer schedules. Source identity contains commit, dirty status, and hashes of tracked/untracked source/configuration files; no commit is required.
- `terminals.json.gz`: reusable compiled terminal contributions and paths.
- `payoffs-NNNNNN.bin`: up to 64 rows per block, row-major interleaved P/D binary64 utility pairs. A BinaryWriter string header and fingerprint precede integer dimensions. SHA-256 occupies the last 32 bytes.
- `profile-gains.bin`: row-major rP/rD pairs with header, fingerprint, dimensions and trailing SHA-256. Epsilon is the maximum of the two numbers.
- `restricted-best-responses.json`: per-opponent maximum utilities and all best-response indices within the numerical tie tolerance.
- `verification/br-PLAYER-OPPONENT.json`: unrestricted utility and deterministic own actions in information-set order; at most one calculation per key.
- `outcome-cache.json.gz`: reusable terminal monetary/outcome accounting and reached-action features.
- `complete.json`: matrix/scoring completion, initialization/solve time, terminal count and worker limit. Each report's `summary.json` separately records verification coverage, clustering completion, sensitivity and timing.

JSON and compressed artifacts have adjacent `.sha256` files. Completed files are never replaced with different bytes. Changes in economic tolerance and cluster radii only require a fresh report directory, not a new matrix run.

Run the focused tests:

```powershell
dotnet test ACESimTest --filter FullyQualifiedName~EnumeratedPureStrategiesTests
```

They cover counts/uniqueness against brute force, all monotonicity directions, exit/offer handling, full pure completions, both fee rules, tree/reference payoff agreement, tied best responses, unrestricted whole-strategy brute force, rounding identity, probability and monetary accounting, workers/checkpoints/corruption, off-path cluster invariance and report regeneration.

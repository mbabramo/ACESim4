# Predictive and discounted CFR+ solvers

`GameApproximationAlgorithm.PredictiveCFR` selects a separate solver family for existing game definitions. Existing algorithm enum values and defaults are unchanged. The standalone publication benchmark and its CLI are documented in [predictive-cfr-benchmark.md](predictive-cfr-benchmark.md).

## Select and run

Given an existing launcher and a fully set up `gameDefinition`:

```csharp
using ACESim;
using ACESimBase.GameSolvingSupport.Settings;

var settings = launcher.GetEvolutionSettings();
settings.Algorithm = GameApproximationAlgorithm.PredictiveCFR;
settings.TotalIterations = 1000;
settings.PredictiveCFR = new PredictiveCFRSettings
{
    Variant = PredictiveCFRVariant.PDCFRPlus,
    AlternatingUpdates = true,
    ReportCurrentStrategy = false
};

var solver = (PredictiveCFR)launcher.GetStrategiesDeveloper(
    Strategy.GetStarterStrategies(gameDefinition), settings, gameDefinition);
await solver.Initialize();
await solver.RunAlgorithm(gameDefinition.OptionSetName);
```

The existing launcher path also supports selection through `EvolutionSettings.Algorithm` and `EvolutionSettings.PredictiveCFR`. Set these in the launcher's settings or the option set's `ModifyEvolutionSettings` callback.

`RunIteration()` performs one complete iteration without reporting, callbacks, or file I/O. `RunAlgorithm()` runs until `CompletedIterations == TotalIterations`, subject to the existing best-response stopping target, and invokes `IterationCompleted` after each complete iteration before reporting. Thus `TotalIterations` is a total horizon when resuming, not an additional iteration count.

Only the solver's sequential full-tree traversal is implemented. It visits every action, including branches with zero opponent or chance reach, and requires perfect recall: unequal own reach across histories of an information set raises `NotSupportedException`. `GeneralizedVanillaFlavor` does not select Fast, GPU, or unrolled execution for this family.

## Update rules and defaults

Iteration numbering starts at `t = 1`, with zero base regrets and a uniform policy. Let `r_t` be the complete information-set counterfactual regret vector, summed across its histories with opponent and chance reach. There is no division by the sum of opponent reach. Let `[x]+` clip each component at zero, and `N(x)` normalize a nonnegative vector, using a uniform distribution when its sum is zero.

Define `d(k) = k^alpha / (k^alpha + 1)`, with `d(0) = 0`:

| Variant | Base regret `R_t` | Next policy `sigma_(t+1)` | Average weighting defaults |
| --- | --- | --- | --- |
| `CFRPlus` | `[R_(t-1) + r_t]+` | `N(R_t)` | Iteration weight `t^1` |
| `PCFRPlus` | `[R_(t-1) + r_t]+` | `N([R_t + r_t]+)` | Iteration weight `t^2` |
| `DCFRPlus` | `[d(t-1) R_(t-1) + r_t]+` | `N(R_t)` | `alpha = 1.5`, `gamma = 4` |
| `PDCFRPlus` | `[d(t-1) R_(t-1) + r_t]+` | `N([d(t) R_t + r_t]+)` | `alpha = 2.3`, `gamma = 5` |

Clipping happens after all histories of an information set have contributed. Prediction determines the next policy and never overwrites base regrets.

For CFR+/PCFR+, the average accumulator adds `t^p * rho_t * sigma_t`, where `rho_t` is own reach. For DCFR+/PDCFR+, it follows `X_t = ((t-1)/t)^gamma * X_(t-1) + rho_t * sigma_t`. The average policy is `N(X_t)`. Own reach is recorded once per information set, and the contribution uses the current policy before its replacement. Zero own reach adds no averaging mass. Alternating updates finish and update one player's information sets before traversing the next player; `AlternatingUpdates = false` opts into simultaneous updates.

Nullable `Alpha`, `Gamma`, and `AveragingPower` override the table defaults. Alpha and gamma affect discounted variants; averaging power affects CFR+/PCFR+. Exponents must be finite, alpha positive, and gamma/averaging power nonnegative. Nonfinite iteration weights or accumulated values fail explicitly.

The formulas follow Xu et al., *Minimizing Weighted Counterfactual Regret with Optimistic Online Mirror Descent*, IJCAI 2024: [paper](https://arxiv.org/abs/2404.13891), [DOI 10.48550/arXiv.2404.13891](https://doi.org/10.48550/arXiv.2404.13891). The authors' [reference implementation](https://github.com/rpSebastian/PDCFRPlus) uses a `+1.5` denominator in its DCFR+ implementation; this solver deliberately uses the paper's `+1` denominator.

## Reporting and legacy differences

The new family ignores legacy regret discounting, reach normalization, trembles, pruning, opponent sampling, annealing, and generalized traversal-flavor settings. In particular, it does not inherit the legacy continuous `0.99` regret discount. Selecting a predictive variant alone does not compose it with these enhancements.

Normal `RunAlgorithm()` reports and measures best response against the exact average policy by default. Set `PredictiveCFRSettings.ReportCurrentStrategy = true` to select the current policy for both. Reporting temporarily disables probability rounding and restores the caller's legacy reporting settings afterward. It does not replace the final learning state with an earlier best checkpoint; `BestBecomesResult` defaults to false for this family. Final policies and checkpoints represent the last completed iteration. The benchmark records best observed profiles separately.

## Checkpoints and copying

```csharp
PredictiveCFRState checkpoint = solver.CaptureState();
// Persist checkpoint with an application-approved serializer.
// Initialize another PredictiveCFR on the identical game/scenario first:
otherSolver.RestoreState(checkpoint);
otherSolver.RunIteration();
```

`CaptureState()` detaches arrays containing base regrets, current prediction policies, average accumulators, settings, and the completed-iteration clock. `RestoreState()` validates information-set keys and dimensions, matches keys independently of discovery order, and clones the supplied arrays. It updates both learning and status clocks. Keep the same game parameterization, payoffs, chance probabilities, and scenario: the checkpoint's structural keys do not fingerprint those values. The checkpoint does not include the game definition, report history, or callback subscriptions. Existing probability-only strategy exports are candidate policies, not solver-resume checkpoints.

`DeepCopy()` initializes independent learning nodes and predictive settings, then restores a detached checkpoint. It shares `GameDefinition` and other reference-valued legacy settings following the repository's existing convention; it is not a complete isolated game clone. `ResetLearningState()` clears learning arrays and the iteration/status clocks.

## Build and verify

Run from the repository root with a .NET 9-compatible SDK:

```powershell
dotnet build ACESimTest/ACESimTest.csproj -c Release
dotnet test ACESimTest/ACESimTest.csproj -c Release --no-build --filter FullyQualifiedName~PredictiveCFRTests
dotnet test ACESimTest/ACESimTest.csproj -c Release --no-build --filter "FullyQualifiedName~PredictiveCFRTests|FullyQualifiedName~SameResultsRegularAndFastCFR|FullyQualifiedName~CollapsingDecisionsGivesEquivalentUtilities"
```

Tests cover numerical updates, discount and prediction ordering, raw counterfactual regret, zero reach, history aggregation before clipping, alternating updates, convergence to a known 2x2 equilibrium, checkpoint persistence and reordered keys, independent learning copies, reset, and reporting/resume parity. The combined command also checks existing regular/Fast parity and equivalent utilities when collapsing decisions. This selection passed all 20 tests during integration.

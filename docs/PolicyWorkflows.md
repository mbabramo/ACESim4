# Policy, execution, evaluation, and reporting boundaries

The shared workflow code now has an explicit home under `ACESimBase/GameSolvingSupport`.
The existing article entrypoints forward to these services, so callers can migrate without
changing solver behavior or saved article files. No JPSRO or Liu selector is implemented.

| Responsibility | API | Existing callers |
| --- | --- | --- |
| Detached behavioral choices | `Policies.BehavioralProfile`, `PlayerPolicy`, `InformationSetPolicy` | Shared core for article diagnostics and benchmark archive reads |
| Capture, validate, install | `Policies.PolicyAccess` | Article pressure diagnostics delegate semantic mapping and installation here |
| Flat equilibrium files and completion | `Policies.FlatEquilibriumFiles`, `FlatEquilibriumPolicies` | Existing developer equilibrium-file methods remain compatibility entrypoints |
| Benchmark archive | `Policies.BenchmarkProfileArchive` | Console benchmark saves and replays its existing version-1 gzip/JSON files |
| Evaluate a supplied profile | `Evaluation.PolicyEvaluator` | New shared utility evaluator, including cross-pairs of player policies |
| Repetition identity, reset, retries | `Execution.RepetitionRunner` | Launcher local and distributed repetition paths |
| Batched prior search and fallback | `Execution.SequenceFormPriorSearch` | SequenceForm delegates its historical exact/inexact protocol |
| Recovery provenance | `Execution.EquilibriumSearchResult` | Immutable retained vectors and discovery counts, separate from reporting |
| Article equilibrium reports | `Reporting.EquilibriumReportWriter`, `EquilibriumRecoveryReport` | SequenceForm and the old developer report entrypoints |
| Native outcome enumeration | `Reporting.NativeOutcomeReportWriter` | Benchmark receives reports, utilities and path mass together |
| Initialization, scenario setup, solve and postprocessing | `Execution.StrategyDevelopmentWorkflow` | `DevelopStrategies` delegates the full workflow |
| Recorded-scenario correlation and pruning | `Execution.ScenarioCorrelationWorkflow` | Existing scenario-recording and compatibility paths |
| PCA persistence, utility models and candidate exploration | `Analysis.PrincipalComponentWorkflow` | Base compatibility methods and solver-specific virtual hooks |
| Model-quality observations and crossovers | `Analysis.ModelSuccessTracker` | Iteration tracking hooks |
| Generated correlation calculations | `Evaluation.CorrelatedValueCalculator` | Calculations over recorded iteration policies |
| Scenario/PCA artifact paths | `Execution.WorkflowFiles` | Shared naming for execution and analysis |

These are supported workflows, not deprecated features. Names describe responsibilities;
disabled-by-default settings do not imply that a feature should be removed. The first pass
extracted the smaller policy/repetition/reporting operations. The follow-up moves 62 additional
methods out of `StrategiesDeveloperBase` into the components above, removing roughly 1,250
lines from the base file (3,526 to 2,277; originally 3,728 before either pass). This is
composition into separate classes, not a partial-class split.

The base retains public forwarding methods, solver-specific virtual hooks, and existing
serialized fields. Internal accessors let the components use those fields without changing
their serialized names, types, or visibility. The generated calculator's compiled type is
cached inside a nonserialized component. Moving serialized storage itself would require a
checkpoint migration; it is not necessary to relocate the workflow implementation.

## Policies are data, not solver state

The immutable policy types copy their inputs and contain behavioral probabilities and
information-set/action identities. They contain no regret arrays, prediction history,
best-response scratch values, recovery counts, selection weights, or claim of equilibrium.
Player policies can be detached from the profile in which they were discovered.

```csharp
var first = PolicyAccess.CaptureCurrent(developer, "first");
// Capture another policy after training, replay, or another initialized solve.
var second = PolicyAccess.CaptureAverage(otherDeveloper, "second");
var cross = BehavioralProfile.Combine("cross", developer.GameDefinition.OptionSetName,
    first.ForPlayer(0), second.ForPlayer(1));
var evaluation = PolicyEvaluator.Evaluate(developer, cross);
```

`PolicyEvaluator` reads the supplied policies against the initialized game's chance law and
terminal utilities. It does not install them, change training arrays, or calculate a
best-response certificate. `PolicyAccess.ApplyCurrent` is the explicit mutating operation:
it validates the entire mapping first, then writes both current and opponent-facing current
probabilities. It leaves averages, regrets, and iteration counters alone.

The semantic key preserves the article's player/decision/observed-history mapping. Action
labels are checked as well. `OptionSet` is optional provenance, not a complete game identity.
Compatible layouts can have different utilities and chance probabilities. No payoff cache
is introduced; any future cache must also bind the complete evaluated game specification.

Article `Profile`/`Strategy` records remain adapters because their saved schema includes
reach, donor and off-path-completion metadata. `ToBehavioralProfile` removes those annotations
for generic policy operations; article-specific interpretation stays in the article layer.

## Existing execution and reporting semantics

`RepetitionRunner.Request` makes the existing run identity explicit. It preserves
`StartGameNumber + Repetition`, reset before later repetitions, twenty retries, backoff,
and immediate propagation of unrolling and sequence-form failures. It does not redefine
`GameNumber` as a universal random seed or promise independent initializations for every solver.

`SequenceFormPriorSearch` preserves the sequential initial exact solve, inexact batch,
and exact fallback based on recovery counts, as well as the parallel branch. Numerical
prior generation, seeds, pivot budgets, arithmetic, verification, and exact-vector
deduplication are unchanged. Recovery counts describe how often a profile was recovered;
they are not selection weights or an additional equilibrium certificate.

`EquilibriumReportWriter` preserves profile ordering, naming, native report calculations,
and restoration of the first current profile. The article's `-Corr` report pools the retained
matched profiles with equal profile weight. Its `-Avg` report uses the historical behavioral
average calculation, which does not in general describe an equilibrium. The compatibility
writer still uses the existing training/history fields where required to reproduce those
reports; future population storage must not use `InformationSetNode.PastValues` as its archive.

`NativeOutcomeReportWriter` preserves the native report filters, conditional denominators,
latent-state expansion, mass check, and optional utility cross-check. It operates on the
developer's selected `ActionStrategy` and temporarily enables reporting mode. It is a live
reporting operation, not the detached-profile evaluator, and must not run concurrently with
training on the same developer. The old `GenerateBenchmarkReportsAsync` method remains a shim.

## Replay, reset, and resume

* `BenchmarkProfileArchive.LoadForReplay` installs saved current and normalized average policies
  using the existing schema. It now validates the whole archive before writing anything.
  It is not a training checkpoint: regrets and iteration counters are not restored.
* Predictive CFR `CaptureState`/`RestoreState` remain the training-resume API, including the
  iteration clock, prediction history and learning arrays.
* Generic `IStrategiesDeveloper.Reinitialize` now dispatches to predictive CFR's full learning
  reset. Previously it could reset nodes while leaving the predictive iteration clock intact.
  The base implementation for older solvers is unchanged.
* Legacy `-equ.csv` ordering, rational parsing, zero-vector completion and output formatting
  are retained. The extracted reader closes its file handle, fixing the old reader's leak.

## Validation and remaining scope

Before moving production code, two reduced-grid article replay fixtures were committed at
`a338b366`. They were captured from `607a19ca`, tagged
`predictive-cfr-before-refactor-20261028`. They cover correlated signals and endogenous
litigation using two explicit nonuniform profiles. The refactored path reproduces every CSV
cell except elapsed seconds, all report suffixes, the restored current profile, average
profiles, and recorded historical policies exactly. These are replay fixtures, not new
solved equilibria or full production article regenerations.

Additional tests cover semantic mapping and atomic rejection, detached snapshots, cross-player
and hidden-chance utility calculations, no training-state mutation, archive replay, prior
fallback and recovery counts, repetition/reset/retry semantics, predictive reset and resume,
and existing article diagnostics. The stale heading test now uses the article's current
"Trial Fee-Shifting" display terminology while keeping historical "British" option-set IDs.
The console outcome self-test verifies saved current/average profile replay, exact utility
agreement, and restricted/full native-report agreement.

Initial validation on 2026-10-28: Release console build succeeded without warnings; all 98 selected
tests passed (none skipped), and `--predictive-cfr-benchmark --self-test-outcomes` passed.
The selected test classes are `BehavioralPolicyTests`, `ExecutionWorkflowTests`,
`PredictiveCFRTests`, `SequenceFormRecoveryTests`, `EquilibriumWorkflowCompatibilityTests`,
`LitigGameCorrelatedSignalsArticleLauncherTests`, `CorrelatedSignalsMultipleEquilibriaReportTests`,
`InformationSetActionReportTests`, `TaskCoordinatorTests`, `PrecautionNegligenceDisputeGeneratorTests`,
`ArticleWorkedPathExtractionTests`, `EquilibriumMixingSearchTests`, and `InformationSetPressureAnalysisTests`.
This is targeted validation, not a full repository test run.

The follow-up adds `WorkflowExtractionTests` for scenario/restriction ordering, PCA observation
scheduling and override dispatch, model-quality observation layout, paired historical policies,
generated-expression equality, and the serialized base-field schema. A syntax-token comparison
of all 62 extracted method bodies found no executable changes beyond references to the new
host/components (ignoring source line endings). All 107 selected tests pass, as do the Release
console build and benchmark outcome self-test. Full production article runs and an end-to-end
PCA surrogate search are not part of these checks.

Scenario setup retains its existing ordering, including the current placement of the solver
call after setup. Correlation heuristics retain their current selection rules. This extraction
does not repair or reinterpret either behavior. Any change to those algorithms should be
separate from relocating their implementation and should have its own numerical validation.

Future JPSRO work can build separate player populations from these immutable policies and
evaluate cross-pairs with the common evaluator. It will still require a correctly hidden
opponent-policy mixture response oracle, population expansion, incentive checks, and explicit
joint weights. A Liu selector and economic-similarity definitions belong above those services,
outside policy objects and solver training state. No placeholder implementations of those
algorithms or changes to article selection rules are included here.

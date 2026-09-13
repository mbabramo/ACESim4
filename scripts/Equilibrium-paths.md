# ECTA equilibrium paths

Run the optional diagnostic workflow from ACESim4:

    dotnet run --project LitigCharts -c Release -- equilibrium-paths --request "C:/Users/Admin/source/repos/correlated-signals-article/Supplemental materials/Equilibrium solution paths/equilibrium-paths.request.json"

The default command calculates and validates the original paths, then creates
four standalone animations and one combined case selector. Use `--calculate-only`
for the data phase. For already completed traces, use:

    dotnet run --project LitigCharts -c Release -- equilibrium-paths --request "C:/Users/Admin/source/repos/correlated-signals-article/Supplemental materials/Equilibrium solution paths/equilibrium-paths.request.json" --render-only

Rendering lives in LitigCharts/EquilibriumPathAnimation.cs. It verifies the
completed manifest, metadata, frame and input hashes, consecutive pivot stream,
initial uniform probabilities, and final diagnostic before rendering. It refuses
layouts that would omit information sets. No solver settings need editing, and
render-only never invokes the equilibrium solver.

The request references the verified source selection and names four original
ordinary-cost equilibria: American/British crossed with risk neutrality/moderate
risk aversion. Each is solved independently under its own fixed rules, starting
from the original exact ECTA seed-zero uniform prior. Saved equilibrium files
are references for validation, NEVER starting strategies. This is not a regime
transition or a warm start from another equilibrium.

Each selection supplies the original production log. Replay must match its pivot
count and all saved action probabilities (tolerance 1e-10), including the same
uniform completion of zero-realization histories. The original four logs record
209, 235, 403 and 975 pivots respectively. Output is
`Supplemental materials/Equilibrium solution paths`; the combined viewer is
`all-equilibrium-solution-paths.html`.
No production equilibrium file or cache is changed. MaxPivots=0 is unlimited,
as in the original exact run.

## Optional hooks

- ECTALemke.PivotObserver receives independent copies of the raw augmented LCP
  solution after every completed pivot. When null, snapshots are not constructed.
- ECTARunner.BeforeSolve and PivotObserver expose the tree and its current prior.
- SequenceForm.TraceECTA provides an explicit diagnostic solve with callbacks,
  outside production report/save and multi-prior workflows.
- ECTAStrategyDiagnostics supplies strategy projection and efficient incentive
  evaluation without changing the tableau or the prior.

## What a step means

Step zero is the original uniform covering-vector prior. Every
subsequent frame is one actual Lemke pivot, including degenerate pivots that do
not change behavior. No interpolated profiles or strategic adjustments are
invented. ECTA is a numerical path, not behavioral learning or best-response
dynamics. Neither exploitability nor its auxiliary variable must be monotone.

The raw state includes x (in Z), w, and the auxiliary variable z0. For display,
form realization weights x + z0 * prior and normalize the children separately
at each information set. At zero total weight, retain the prior's local policy.
This construction starts at the prior and ends at the equilibrium realization
plan, with explicit prior completions at unreached sets. Every frame records the
largest root/flow-constraint residual before local normalization: a nonzero value
means the behavioral pair is a projection, not the raw realization plan.

## Distances and action advantages

Epsilon is the maximum of the two players' unrestricted unilateral gains in the
game's rounded utility units; NashConv is their sum. Best responses
optimize all own continuation decisions. Independent GEBR controls verify the
start and end. These measures are not geometric distances to a specific profile.

Local action Q conditions on the acting player's information set, with all
continuation behavior held fixed at this frame. Advantage = Q(action) minus the
current mixed-policy expected Q. It is not a difference from the final payoff.
LocalGap = max(Q) minus current expected Q. OutsideSupportGap restricts this to
actions with probability at most 1e-10. SupportSpread detects unequal values
among actions already played. Checking only unused actions would miss full-
support disequilibrium.

Counterfactual reach includes chance and opponent probabilities but omits own
earlier choices. A zero denominator means undefined incentives (null), not zero.
Conditional incentives at an actually unreached own history may be defined;
positive local gaps there need not contradict Nash equilibrium. All complete
strategies, including both private exit-commitment offer histories, are exported.
Prior completion at an unreached history is not identified equilibrium behavior.

Native diagnostics additionally report the auxiliary variable and violations of
original-LCP feasibility and complementarity. The original slack is w - d*z0;
the augmented constraint is w = Mz + q + d*z0. These use normalized matrix units,
not the original utility scale, and are not welfare comparisons.

## Files and safeguards

Each original solve has a JSON metadata/validation file and a JSONL stream with every
frame, full probabilities, Q values, advantages, reaches, local/global gaps, and
raw LCP state. Metadata fixes the action ordering once, preserving semantic keys,
signals, players, and exit histories. A completed manifest fingerprints results.
Failed runs leave partial JSONL for diagnosis but do not produce a new successful
manifest. Do not use partial runs as completed equilibrium paths.

Source action reports, original logs and equilibrium profiles are validated and
hash-checked. A different selected equilibrium fails validation, even if it is
also a Nash equilibrium. A directory with a completed manifest cannot be reused
silently. Select a new directory to rerun a completed set.

## Compatibility and optional explicit priors

The production runner historically ignored supplied custom profiles outside
scenario tracing. Its default behavior is preserved so existing production
settings retain the same equilibrium selection. The optional diagnostic wrapper
can explicitly opt into a supplied prior; a null prior invokes the original
generator, with configurable seed. The article workflow uses ONLY null + seed 0.

The optional prior setter previously restarted its index within/across players
and mutated the input array. It now validates atomically, consumes the complete
ordered vector, applies its optional floor, and leaves caller/chance data alone.
Failed solver attempts are no longer read as successful equilibria. Tests verify
that observation leaves both uniform and explicitly seeded solves unchanged.

## Animation

Open any generated HTML file in a browser; it is self-contained and needs no
server or network service. The data are losslessly gzip-compressed, decoded by
the browser's DecompressionStream API. Full-precision JSONL remains the canonical
data. Play runs only the selected case and stops at its equilibrium. At the end,
Play restarts that same case. Select another independent solve from the Case
menu; its playback starts at its own uniform prior. The slider and Step buttons
are restricted to the selected case as well.

P is above D. Every own-signal information set is shown in Enter, Offer/continue,
Offer/exit, and Exit panels. Signal increases upward, with low signals at the
bottom and high signals at the top; offer amount increases
rightward. Binary actions are Yes then No. Blue fill is probability; orange
corners show positive Q minus current mixed-policy Q, with a fixed square-root
scale for the whole case. Reached probability zero is faint blue, distinct from
the plain white of actually unreached rows (actual reach at most 1e-10).
Unreached rows have no hatching, completion dots, borders or advantage corners.
Their stored strategies and conditional diagnostics remain in the data and
hover details, explicitly labeled off path; they are not depicted as played behavior.

The slider and Step buttons retain all pivots. Playback can skip identical
probability vectors (tolerance 1e-12) without dropping data. Optional smooth color
transitions blend only the blue probability fills, for at most 220 ms and no more
than 75% of a playback interval. These fades are visual only: no intermediate
strategies or utilities are generated or evaluated. Readouts, hover values,
advantage corners and reach markings always refer to the destination pivot.
An unreached destination row is white throughout the transition, regardless of
the preceding pivot's reach or stored probabilities.
Explanatory notes are kept in the documentation, not in the viewer. Reduced-motion preferences
disable smoothing. Pause, scrubbing and PNG export snap to the exact recorded
frame. Epsilon and z0 are displayed outside the grid, and hover/tap gives precise
action details.

Player-control regression tests (fake DOM/canvas and clock; no browser dependency):

    node --test scripts/tests/equilibrium-path-player.test.cjs

The C# EquilibriumPathAnimationTests additionally validate packed information-set
ordering, coverage and source/frame fingerprints.

The active `equilibrium-paths.request.json` sits beside the animations and raw
traces. The source-equilibrium request sits beside its calculation in
`Results/Equilibrium diagnostics/Original`. Each has an adjacent
`.recorded-request.json` preserving its exact historical bytes; relocated trace input
fingerprints reference these originals without changing their hashes. Only
metadata paths and the rendered viewer changed during the directory cleanup;
the JSONL pivot streams did not change.

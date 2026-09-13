# Equilibrium-preserving mixing exploration

Run through the existing C# chart/report entry point:

```text
dotnet run --project LitigCharts -c Release -- equilibrium-mixing --request <request.json>
```

The article's `equilibrium-mixing.request.json` selects the four ordinary-cost
equilibria: American/British crossed with risk neutrality/moderate risk aversion.
Additional saved profiles can be added to `Sources`. Mixing always starts from
the saved equilibrium, not a `ProfileFile` override. This does not run ECTA,
replace production equilibria, or revise existing animations or paper figures.

`equilibrium-mixing-tight.request.json` repeats the four equilibria and both
search orders with a tenfold tighter acceptance limit and conditional tie
tolerance, retaining its results in a separate `Tighter tolerance check`
subdirectory. Search-order sensitivity is distinct from numerical tolerance.

## Objective and scope

"As mixed as possible" is operationalized as maximizing the equally weighted
mean of the normalized quadratic/Gini mixing score

```text
M(p at an information set) = (1 - sum_a p(a)^2) / (1 - 1 / action count).
```

It is zero for a pure policy and one for a uniform distribution over the entire
action menu. This makes the optimization a convex quadratic problem within each
decision block. It is not maximum Shannon entropy or maximum support cardinality.
The report also gives exp(Shannon entropy), the effective number of actions, for
interpretation. These measures choose a diagnostic representative, not a new
behavioral prediction.

The objective includes only information sets with positive source reach above
the configured threshold. That set and its weights stay fixed. Source-unvisited
policies, including uniform loader fallbacks, cannot change. Eligible sets that
become unvisited during exploration are skipped too: arbitrary off-path mixing
must not count as progress. Root best-response checks still consider ALL
deviations, including moves into off-path histories.

## Joint decision blocks and deviation constraints

The supported article protocol has one bargaining round, simultaneous offers,
private precommitted exit choices, and endogenous filing/answering. A player's
information sets for the same decision (for example all plaintiff offer sets
across signals and reached exit branches) are mutually exclusive on a path.
Their probabilities can therefore vary jointly while every fixed complete
deviation's root payoff comparison is affine. This joint block matters: adjusting
one signal at a time can become stuck when a feasible improvement needs offsetting
changes at different signals. A regression test explicitly covers that case.

At each block:

1. Keep the rest of BOTH players' complete policies fixed.
2. Admit currently supported actions and zero-probability actions tied in
   conditional utility under the current actual continuation policy. Preserve the
   separate continue/exit offer information sets; never aggregate them.
3. Minimize the weighted sum of squared action probabilities, with a separate
   unit-mass simplex for every variable information set. Weights retain the
   normalization by the FULL action menu, not just the admitted candidates.
4. Check BOTH players' unrestricted best responses with the existing GEBR/replay
   diagnostic. If a player gains too much, retain its full deviation policy.
5. Evaluate that deviation at simplex vertices, holding all other simplexes at
   their current policies, to recover an affine deviation constraint. Replay its
   predicted gain at the proposal as an independent linearity check. Add the cut
   and solve again. The focal player's full-deviation payoff is constant because
   that deviation replaces the entire variable block.
6. Accept a block only after a full profile passes the gain limit and improves the
   fixed mixing objective. All changes in a block must be applied TOGETHER; partial
   application of a saved block has not been verified.

The QP uses the already referenced ALGLIB package, with bound and general linear
constraints. Constraint rows are centered separately within each simplex and
scaled before optimization. Tiny negative probability roundoff is cleaned up,
then the resulting distribution is checked by the full game oracle. No solver
success status substitutes for equilibrium verification.

Default acceptance is maximum unilateral root gain <= 1e-9 reported utility
units, with independent replay checks at 1e-7 and conditional tie admission at
1e-10. Generated cuts target one quarter of the acceptance limit, or the current
profile's gain from that particular deviation plus a small numerical margin,
whichever is greater (capped at the acceptance limit). This leaves a safety margin
without making a previously accepted profile infeasible solely by tightening the
tolerance in a later block. The default score-improvement threshold is 1e-7, so
microscopic score gains need not accumulate across sweeps. These are not
percentage-of-damages tolerances. All
source controls must already satisfy the acceptance limit: the search does not
silently repair a materially approximate source equilibrium.

## Local, not global, maximality

Forward and reverse decision-block orders restart from the SAME original profile.
The higher scoring verified result is selected, with maximum gain breaking exact
score ties. The original and both results are retained. The joint equilibrium
constraints across players and decision stages are not globally convex; even a
converged block search is not a certificate of global maximum mixing or a
characterization of all equilibria. It can miss jointly improving changes across
different decision stages/players, disconnected families, or support expansions
requiring temporary moves outside the current tie menu.

Stopping at a sweep/cut limit, an unsuccessful QP, or a numerical failure is
reported explicitly. A cut-limited or failed block leaves its policy unchanged.
Every final profile receives a fresh two-player unrestricted best-response check;
source-unvisited policies and source-file SHA-256 hashes are checked unchanged.

## Outputs

- `<id>.md`: initial/final mixing scores, order sensitivity, full changed action
  distributions, effective support, root best-response gains, and aggregate
  disposition probabilities.
- `<id>.json`: request/source/core-assembly fingerprints, source profile and
  action values, both runs, all accepted blocks and member changes, solver audits,
  final profiles, conditional values/beliefs/reaches, and aggregate outcomes.
- `<id>-forward.json`, `<id>-reverse.json`: completed-run checkpoints.
- `<id>-forward-profile.json`, `<id>-reverse-profile.json`: each order's semantic
  profile, available for explicit search-order sensitivity decompositions.
- `<id>-profile.json`: selected semantically keyed full behavioral profile,
  directly consumable by `InformationSetPressureAnalysis.Apply`.
- `README.md`: methodology and links to completed sources.

Aggregate outcomes partition potential disputes into not filed, not answered,
settlement, trial, and exit after failed bargaining. The last category combines
abandonment/default and does not imply a new disposition convention for the
article's existing figures. They use actual path probabilities, not conditional
averages over information sets. All saved production profiles and action reports
are read-only inputs, validated against the initialized rounded article game.

## Separate decomposition experiment

The article's `equilibrium-changes-mixed.request.json` uses the selected profiles
as explicit `ProfileFile` inputs to the existing decomposition engine. Its output
is `Supplemental materials/Equilibrium changes after mixing`, not `Tables` or the
existing equilibrium-change directory. See `Equilibrium-strategy-changes.md` for
the compact focus filter and its limitations. No mixing representative should be
substituted into original ECTA solution-path animations.

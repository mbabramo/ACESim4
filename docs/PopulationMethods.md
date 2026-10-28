# Population solving and economic affinity

The optional `population` command implements two-player, perfect-recall
**JPSRO(CCE)**. It expands separate player policy populations using unrestricted
best responses and selects joint weights over all cross-pairs. It offers:

- **Maximum Gini CCE:** minimize the sum of squared joint weights subject to
  restricted-game CCE incentive constraints.
- **Liu economic affinity CCE:** obtain per-player affinity-entropy reference
  weights, then minimize relative entropy to their product subject to the same
  incentive constraints.

These are normal-form coarse correlated equilibria over complete policies.
A player's deviation commits to a replacement policy before learning its policy
recommendation; it can still condition on the game's ordinary information.
Recommendation-conditional CE and Liu's logit-limit Nash method are not implemented.
An approximate CCE need not be an approximate Nash equilibrium.

The implementation follows [Marris et al. (2021), JPSRO](https://proceedings.mlr.press/v139/marris21a.html)
and the affinity/relative-entropy construction in
[Liu et al. (2025), section 3.3 and appendix A](https://arxiv.org/html/2502.20170v2#S3.SS3).
The litigation outcome metric and positive-reference floor below are declared
adaptations. Similarity never changes incentive payoffs.

## Ownership

| Component | Responsibility |
|---|---|
| `Evaluation/PolicyGameTree` | Detached full tree, semantic policy checks, pair evaluation, hidden mixtures, unrestricted responses |
| `Population/JpsroCoordinator` | Separate populations, incremental pair cache, expansion, termination, full-game gains |
| `Population/CoarseCorrelatedSelector` | Restricted CCE constraints and joint-weight optimization |
| `Population/AffinityReference` | Similarity kernel and per-player reference optimization |
| `Reporting/JointPolicyOutcomeReplay` | Joint-weighted native terminal replay and latent expansion |
| `ArticleReplication/PopulationExperiment` | Article catalog, initialization, frozen probes, archives, experiment output |
| `ArticleReplication/PopulationOutcomes` | Native litigation outcomes and article welfare accounting |
| `ArticleReplication/PopulationAudit` | Product-marginal Nash incentives versus joint CCE incentives |

Policies remain immutable behavioral data. Selection weights are not stored in
strategies, regret arrays, historical policy slots, or initialization counts.
The existing article solvers, defaults, report suffixes, saved files, and
`ArticleReplication run` route are unchanged.

## Full-game response checks

The mixture draws an opponent's complete policy privately, once before play.
For each opponent information set, its realization-equivalent behavioral
probabilities use mixture weights multiplied by the probability of that
player's preceding own action sequence. This is not an arithmetic average of
action probabilities. Log-domain normalization handles very unlikely sequences.
The tree constructor checks perfect recall and rejects incompatible layouts.

The response oracle chooses one action per information set using full-tree
counterfactual reach. It independently replays the resulting response against
each opponent policy. The article harness also compares its response value with
the existing GEBR engine at the realization-equivalent opponent marginal.
These are floating-point full-tree computations, not sampled response estimates.

The CCE gain is best-response utility against the opponent marginal **minus
utility under the joint distribution**. Each gain is divided by that player's
full terminal utility range, fixed when the game is constructed. Raw gains and
scales are saved too. The default target is a maximum normalized gain of `1e-5`;
restricted-game incentives use `1e-8`. Clipping negative gains to zero is only
for reporting the violation, not for altering payoffs or constraints.

`FullGameCceVerified` requires the full-game target. `IterationLimit`,
`MetaSolverLimit`, and `NoNewResponse` retain their measured gains and are not
convergence claims. A completed experiment can contain unconverged runs; inspect
each result's status. The Gini selector uses a primal QP for up to 2,048 joint
profiles, otherwise a bounded convex dual. Relative entropy uses the bounded
dual. Optimization termination codes and residuals accompany the weights.
The primal QP API does not expose multipliers, so its duality gap is null.

## Economic similarity

The article adapter uses five equally weighted features:

1. Plaintiff solver utility, normalized by its fixed terminal minimum/range.
2. Defendant solver utility, normalized the same way.
3. Unconditional filing probability.
4. Unconditional answering probability.
5. Unconditional trial probability.

Each policy is evaluated against four fixed opponent probes: the published
primary policy, a uniform policy, and two seeded random policies. Probes have
equal weight and stay fixed throughout population expansion, archive restarts,
and duplication checks. Changing initial population seeds does not change the
probe bank. Conditional answering is reported separately; ratios are formed
after joint aggregation. Settlement and the article's five welfare measures are
reported but do not enter this version of the similarity metric.

Let `D` be mean squared feature difference across these probes/features.
The kernel is `K[i,j] = exp(-D[i,j] / (4 * bandwidth²))`, following the paper's
`(2 sigma)²` denominator. With `p=1`, each column is normalized to unit Euclidean
norm, and reference optimization minimizes `||U x||²` on the simplex. Default
bandwidth is `0.1`. Entropy and KKT diagnostics describe this unfloored optimum.

For the relative-entropy step, the reference needs positive support. We mix
`1e-6` reference mass into a distribution that gives equal total weight to each
exact economic-signature class, split equally within that class. This explicit
floor is an adaptation, not an invariance theorem. It is saved in settings.
Near-identical policies remain distinct incentives. Only exactly identical
behavioral policies are deduplicated when entering the population.

Clone invariance in idealized block kernels and robustness to nearly identical
economic signatures are different claims. The tests cover both, and the
article experiments measure sensitivity to five near copies per player and
bandwidths `0.05`, `0.1`, and `0.2`. Do not assume general representation
invariance from the method's name.

## Running article experiments

Use the same saved-solutions bundle as the published article, with a fresh,
separate output directory:

```powershell
dotnet run --project ArticleReplication -c Release -- population `
  --input SAVED_SOLUTIONS --output NEW_POPULATION_RESULTS `
  --cases main --selectors gini,liu --iterations 150
```

`--cases baseline` selects four cost-one games; `main` selects all 20 main
cost/risk/fee cases; `all` selects the final 74-case catalog. A comma-separated
list of exact case IDs is also accepted. Grids, chance law, payoffs, numerical
conventions, and agreement decisions come from `FinalArticleCaseFactory`.

The default `--seed-mode independent` starts each player with a uniform policy
and two random policies. `--initial-seed 1729` controls the initial random bank;
use a different integer for an initialization check. The published profile is
used for comparison and as one fixed similarity probe, but is not in this
initial population. `--seed-mode article` additionally seeds the published
profile. That run can terminate immediately on the supplied equilibrium, which
is a compatibility check rather than evidence of independent discovery.

Other options are `--epsilon`, `--bandwidth`, `--floor`, and
`--report-outcomes false` (skip native welfare reporting). Each result retains
complete policies, row-major joint weights, marginals recoverable from them,
settings, per-iteration gains, independent oracle checks, economic results, and
source/game fingerprints. `population.json` stores each player's probabilities
in the declared layout; `union-population.json` combines the two selectors'
discovered policies.

To continue expansion or reselect from a saved population:

```powershell
dotnet run --project ArticleReplication -c Release -- population `
  --input SAVED_SOLUTIONS --output NEW_RESTART_RESULTS `
  --cases EXACT_CASE_ID --seed-archive PATH_TO_POPULATION_JSON
```

This starts a new iteration history from the archived policies and frozen
probes. It is not a CFR training-state resume. A different game fingerprint or
policy layout is rejected. To distinguish CCE from Nash incentives afterward:

```powershell
dotnet run --project ArticleReplication -c Release -- population-audit `
  --run POPULATION_RESULTS --output NEW_AUDIT_JSON
```

The audit evaluates the product of the policy marginals and checks its Nash
gains. Correlation between policy labels alone can be behaviorally redundant,
so total variation is reported alongside the actual incentive calculation.

## Interpretation and validation

JPSRO stops when there is no sufficiently profitable unrestricted response. It
does not enumerate all equilibria or all behaviorally different policies.
Even a fully certified CCE can depend on initialization, finite population,
selection objective, probes, bandwidth, and numerical settings. Nor does the
selected finite-population objective prove a global entropy optimum over the
entire extensive-form policy space.

The unit suite covers hidden policy identity, history-dependent mixtures,
underflowing sequences, imperfect-recall rejection, correlated outcome replay,
restricted incentive constraints, discovery of missing RPS responses, iteration
limits, exact clones, near copies, and unchanged training state. The existing
article/CFR/workflow regression selection is also run. Numerical experiment
results and their scope are recorded in the
[28 October 2026 validation receipt](validation/population-methods-20261028.json).

Both selectors reached the full-game CCE target in all 74 article games (148
runs), with independently checked response values. The 229-test regression
selection passed. The original game fingerprints match, and replaying the
original profiles changes outcomes by at most `1.6e-12`.

Qualitative agreement is partial: the three headline fee-shifting directions
persist for risk-neutral cost multipliers 0.25 through 2, but the nonliable
defendant burden comparison reverses at multiplier 4. Risk-averse outcomes are
more sensitive. Only 29 of the 148 product-marginal profiles also pass the
approximate-Nash threshold (3 of the 40 main runs). These comparisons therefore
change both selection and the solution concept; they do not establish a
contradiction within the article's Nash results. Initialization and near-copy
checks also show that the selected population result is not unique.

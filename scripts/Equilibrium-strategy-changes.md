# Changed equilibrium strategies

From the ACESim4 repository:

    dotnet run --project LitigCharts -c Release -- equilibrium-changes --request "C:/Users/Admin/source/repos/correlated-signals-article/equilibrium-changes.request.json"

--calculate-only runs diagnostics; --render-only assembles verified saved
calculations. The pressure command remains an alias, but the old table generator
has been deleted (recoverable in checkpoint e0f7f45b).

## Question and accounting convention

For each changed decision reached in both selected endpoint equilibria, compare
the actual old and new policies. Unchanged rows are omitted. Changes in reach
are recorded separately; an unplayed endpoint action is never manufactured.

Let x be the original policy coordinate and y the target coordinate. With the
new rules fixed, v(S) is the focal player's complete unrestricted best response
against an opponent whose components in S use their actual target-equilibrium
policies and whose remaining components use their original policies.

Compute all eight subsets of entry, offers, and exit:

- Direct = v(empty) - x.
- For each of the six orders, replace opponent components cumulatively and
  record the marginal increment v(S + component) - v(S).
- Each opponent contribution is its average marginal increment across those
  six orders.
- Retain residual = y - v(all). Direct + the three opponent contributions +
  residual must equal y - x, before rounding.

This is a **direct-first** convention. Interactions with the primitive change
are assigned to opponent adjustment. It is not a unique causal attribution.
Full direct explanation implies zero net opponent contribution only when the
selected all-opponent response matches the observed target coordinate.

Each response optimizes all focal continuation choices. This is neither the
two-round dynamics exercise nor the fixed-current-policy regret/misalignment
proposal. It uses the existing GEBR algorithm; it does not solve new equilibria.

## Selection, mixtures, and reach

Retain original probability on optimal actions (within 1e-10 by default),
renormalizing it if some original support ceases to be optimal. If no original
probability can be retained, choose the first optimum. Independently replay the
entire selected policy to verify its root payoff against GEBR (1e-7 by default).
Never retain a strictly inferior original action merely for smoothness.

An observed target mix may differ from this original-preserving response even
when both are optimal. Its selection residual remains in JSON. The table says
**Selection-dependent** rather than disguising the residual as one of four
mechanisms. Low/high optimal-action selections flag other tie-sensitive
allocations. They are stress tests, not exhaustive identification bounds.

Pure offer comparisons use exact monetary grid coordinates only if all
compared primary and sensitivity policies are pure. Otherwise decompose each
changed offer-action probability separately, never the mean offer.

Intermediate hybrids may not reach a history that both endpoint equilibria
reach. Their conditional policies can still be compared if opponent-and-chance
reach is positive; those rows receive a double dagger. If that reach is zero,
the table suppresses the attribution as **Undefined counterfactual**.
Posterior beliefs always reflect the hybrid opponent's selection.

Opponent policies unvisited in their donor equilibria are audited even if
exposed only by a focal deviation. Low/high completion stresses alter only
these donor-unvisited policies. Completion-sensitive allocations receive a
star, not an assertion that all hybrids are credible equilibria.

## Reports and verification

- EquilibriumChangeDecomposition.cs: eight-subset construction, allocation,
  changed-row selection, exact action-share representation, sensitivity flags.
- InformationSetPressureAnalysis.cs: reusable profile mapping and validated
  unrestricted best response and independent policy replay.
- ArticlePressureAnalysis.cs: source loading, source equilibrium controls,
  matched intervention checks, calculation manifest and file hashes.
- EquilibriumChangeTables.cs: change-only PDF/TeX/TXT and grouped paper tables.

The article request selects four fee/preference contrasts at costs 1 and 4.
Ordinary-cost comparisons are bundled in Tables/Equilibrium strategy changes.
High-cost comparisons and all eight per-contrast reports are in
Supplemental materials/Equilibrium changes. Full JSON contains source and
target strategies, primary and sensitivity responses, action values, reaches,
beliefs, exclusions, and explicit residuals. Original production files are
hash-checked before and after calculation and again before rendering.

The earlier Supplemental materials/Information-set pressure output is retained
as historical research, not presented as additive accounting.

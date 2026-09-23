# Agreement-stage equilibrium decompositions

These 90 directed comparisons use the 30 individually verified agreement-enabled equilibria: all six fee-rule/risk combinations at costs 0.25, 0.5, 1, 2 and 4. Each fee change is evaluated in both directions within each risk level; each risk change is evaluated in both directions within each fee rule. Costs stay fixed. A risk comparison changes both parties' preferences. The agreement stage is present at both endpoints.

The calculations use saved complete strategies. They do not solve additional equilibria, impose monotonic strategies, or interpret comparisons as adjustment dynamics.

The direct contribution changes the fee rule or preferences while holding the opponent's original policy fixed and optimizing every focal-player decision. Four separate opponent components then replace policies with their target-equilibrium values: participation (filing/answering), offers, private exit commitments, and agreement to bargain. All 16 subsets are evaluated for each player. Marginal contributions are averaged across all 24 replacement orders, conditional on the new primitive. This Shapley accounting is a convention for allocating interactions, not causal identification.

Direct, the four opponent contributions, and an explicit remaining contribution sum to the observed strategy change before rounding. Remaining is the target policy minus the selected best response to the target opponent. It can reflect payoff-equivalent choices and is not silently assigned to an opponent component. Mixed offers retain action-specific probabilities.

Primary best responses preserve and renormalize the original policy on optimal actions within the existing tie tolerance. Low/high action selections and low/high completions of donor-unvisited policies test sensitivity. All-target-opponent utility must match target equilibrium utility. These checks are illustrative sensitivity tests, not exhaustive bounds.

Tables use the article's existing selection rule: changed, commonly reached decisions where the original local distribution loses utility under the target opponent with own continuation reoptimized; plus unchanged decisions with strict, offsetting direct and opponent effects. All other coordinates remain in the full JSON. Newly reached and no-longer-reached histories are listed separately because they lack two reached endpoint policies. An empty selected table does not imply that the complete strategies coincide.

Conditional comparisons remain defined when chance-and-opponent reach is positive even if the focal player avoids the history. Such intermediate histories carry an asterisk. Zero chance-and-opponent reach makes attribution undefined. Table probabilities are percentage points; pure offers are fractions of damages. Risk-neutral and CARA utilities are not comparable welfare units across risk specifications.

Every source strategy/action report, calculation, request, executable and rendered output is fingerprinted. Each table comes from its own directed comparison. The accompanying enabled-versus-disabled outcome and strategy comparisons use separately verified equilibria; these within-model decompositions do not claim to decompose the structural addition of a new decision stage.

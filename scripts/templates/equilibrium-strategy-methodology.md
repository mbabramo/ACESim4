# Methodology and Explanation

These comparisons examine how a change in a fee rule or in the parties' risk preferences affects equilibrium strategies. Each decomposition separates the effect of changing the rule or preferences while holding the opponent's strategy fixed from contributions associated with the opponent's equilibrium entry, offers and exit decisions. The accounting describes selected equilibria and a specified set of counterfactual best responses. It does not identify unique causal mechanisms or model a process through which litigants adjust to equilibrium.

## Scope and files

At each cost, the three rules are American, Trial Fee-Shifting and Complete Fee-Shifting. All six directed fee changes are calculated within each available risk preference. All ordered risk changes are also calculated within each fee rule, changing both parties' preferences together. A comparison holds costs fixed and changes only one of these two dimensions. Reverse comparisons are computed independently; reversing the direction does not simply reverse the signs of the contributions.

With risk neutrality and symmetric CARA risk aversion at coefficient 2, there are 18 comparisons per cost, or 90 across the five standard cost multipliers. The README lists the available tables. The table collection provides candidate rows and panels for manuscript selection; it does not prescribe what belongs in the article or appendix.

The Calculations folder contains four types of records:

| Folder | Role |
|---|---|
| Original | Full strategy-change calculations using the equilibria originally computed by the solver. These supply the displayed table values. |
| Mixing | Searches for alternative profiles with more mixing among utility-tied actions, subject to checks of both players' unrestricted best responses. There is one search record per endpoint profile and search setting. These records supply inputs to the next two folders. |
| Mixed | Recalculates each directed comparison using the mixed profiles selected by the main search. |
| Mixed tighter check | Recalculates each directed comparison using profiles from the search with tighter gain and tie tolerances. |

Original, Mixed and Mixed tighter check contain the same directed comparisons, arranged by cost and contrast. Mixing contains the profile searches used to construct the two alternative sets of endpoints. These searches and recalculations assess sensitivity to the representation of equilibrium indifference; they are separate from the multiple-start equilibrium study.

Tables contains the PDF and PNG displays. Tables/Sources contains the C#-generated TeX and JSON: selected coordinates, unrounded values, scenario identifiers and input fingerprints. Each JSON file links to this shared explanation. Scenario-specific information is retained in those records and in the filenames; the methodology is common to all tables. The full calculations retain excluded coordinates, original and hybrid policies, reach probabilities, utility comparisons and sensitivity checks.

Sources/Profiles contains frozen copies of the equilibrium and action-report inputs. The equilibrium CSV gives the saved strategy probabilities; the action report connects them to information sets and reported values. Preserving the exact input bytes makes the calculations reproducible even if routine reports are later regenerated or reserialized. The profile-provenance.json file identifies these inputs and their hashes. They are inputs, not extra equilibrium estimates. Sources/Process Logs records execution of the comparisons, mixing searches and table generation.

## Reading a table

Each row concerns a decision at the acting party's own signal and observed history. The signal uses a common scale of plaintiff case strength: a higher signal favors the plaintiff and disfavors the defendant. Offer information sets also distinguish the party's private commitment to continue or exit if bargaining fails. A commitment to abandon or default is not an unconditional probability of actual abandonment or default, since settlement may occur first.

| Column or notation | Meaning |
|---|---|
| Decision and Signal | The party's decision and its own signal; consecutive signals can be grouped when their strategies and contributions agree. |
| Original → Target | The strategy coordinate in the original and target selected equilibria. |
| Direct | The change in the best-response coordinate after changing the rule or preferences while retaining the original opponent's strategy. |
| Opponent entry | The contribution associated with replacing the opponent's filing or answering strategy. |
| Opponent offers | The contribution associated with replacing the opponent's offers. |
| Opponent exit | The contribution associated with replacing the opponent's exit commitments. |
| Remaining | The difference between the target equilibrium coordinate and the selected best response against the fully updated opponent. This residual remains explicit, including when nonzero. |
| Sensitive | Whether the tie or donor-unvisited-completion checks change a contribution in the original-profile accounting. “At some signals” means this occurs for only part of a grouped signal range. |
| Asterisk | At least one intermediate best response does not actually reach that information set, although its conditional comparison remains defined. |
| Dash | The conditional comparison is undefined; no numerical allocation is imputed. |

Filing, answering and exit commitments are probabilities, displayed as percentages. Their changes and contributions are percentage points: moving from 87.8% to 100% is an increase of 12.2 percentage points. Pure offers and demands use fractions of the normalized award; a move from 0.75 to 0.05 is a change of −0.70. Amounts are used only when the endpoint strategies and the counterfactual strategies entering the accounting are pure. Otherwise, an offer row identifies a particular available action and its probability. Mixed offers are not replaced by mean offers.

These rows are conditional strategy comparisons. The article's disposition and welfare measures instead average across all potential disputes. Neither a conditional action-value difference nor a strategy contribution is a population welfare effect. Reported utility units also depend on the preference specification.

## Best responses and the decomposition

Let θ₀ and θ₁ denote the original and target rule or preference specifications, and let σ⁰ and σ¹ denote the corresponding selected equilibrium profiles. Partition the opponent's strategy into entry, offers and exit commitments. Under θ₁, construct all eight combinations of original and target opponent components. Holding each complete hybrid opponent strategy fixed, compute an unrestricted best response for the focal player, jointly optimizing that player's current and subsequent decisions. No monotonicity restriction is imposed.

Write z(S) for the resulting strategy coordinate when the opponent components in S have been replaced by their target values. Write x and y for the original and target equilibrium coordinates. The direct contribution is:

$$D = z(\varnothing) - x.$$

For each of the six orders of replacing the three opponent components, record the marginal change from replacing component k. Its contribution averages those increments:

$$C_k = \frac{1}{6}\sum_{\pi}\left[z(S_{\pi,k}\cup\{k\})-z(S_{\pi,k})\right],$$

where Sπ,k contains the components preceding k in order π. The complete accounting identity is:

$$y-x = D+C_{\mathrm{entry}}+C_{\mathrm{offers}}+C_{\mathrm{exit}}+R,$$

$$R = y-z(\{\mathrm{entry},\mathrm{offers},\mathrm{exit}\}).$$

This convention applies the direct intervention first. Interactions between that intervention and the opponent's adjustment enter the opponent contributions. Contributions can exceed the net change or have the opposite sign because other contributions offset them. Remaining is an endpoint-selection residual, not another behavioral mechanism.

## Which coordinates appear

The table selection is restricted to information sets reached with positive probability in both endpoint equilibria. It includes two kinds of coordinates, evaluated separately in Original, Mixed and Mixed tighter check and retained only when they qualify in all three.

### Changed actions

A changed information set qualifies for the focus criterion if, against the target opponent and with subsequent own decisions optimized, its original local policy assigns probability mass greater than 10⁻⁶ to actions whose conditional utility is more than 10⁻⁶ below the best action. The utility threshold uses the target specification's reported utility units. This compares local actions, not the payoff loss from reverting the entire strategy.

Disjoint strategy supports do not suffice: an action absent from the target support can remain optimal. The displayed set is the intersection of the focus selections in the three representations. Consecutive signals are grouped when their original and target distributions and numerical contributions agree within 10⁻⁶; sensitivity flags may differ within a group.

The Remaining column always preserves the endpoint-selection residual. Changes involving newly reached or no-longer-reached histories, and redistribution among actions that remain optimal, are outside this restricted display. Their absence does not establish that they are unimportant or that the full strategies coincide.

### Unchanged actions with offsetting effects

An unchanged equilibrium action can conceal counteracting incentives. Additional rows retain information sets whose original and target local distributions agree within 10⁻⁶, but whose direct best response changes. Against the original opponent under the target specification, the original local policy must assign probability mass greater than 10⁻⁶ to actions more than 10⁻⁶ below the best conditional action, with subsequent own decisions optimized. This excludes switches caused solely by selecting differently among tied or nearly tied actions.

The best response to the fully updated opponent must reproduce the target local distribution within 10⁻⁶. All eight conditional comparisons must be defined, Direct must be nonzero, and Remaining must be no larger than 10⁻⁶ in the reported coordinate. These requirements must hold separately for the original profiles and both tested mixed representations.

For these rows, x equals y and R is numerically zero, so the direct contribution is offset by the opponent contributions. One example is plaintiff filing at signal 0.35 under American fees: filing occurs under both risk neutrality and risk aversion. Changing preferences with the original opponent fixed produces a best response of not filing (−100 percentage points), while the opponent-offer contribution is +100 points. This illustrates offsetting incentives under the accounting convention; it does not establish a uniquely necessary causal mechanism.

An empty table means that no coordinates satisfy the implemented selection criteria. Displayed values always come from Original. Passing the selection in all three representations does not imply that the numerical allocation is identical across them; sensitivity flags remain relevant.

## How the mixing checks work

The auxiliary search increases the mean normalized quadratic mixing score:

$$\frac{1-\sum_a p_a^2}{1-1/A},$$

over information sets reached in the original equilibrium, where A is the full number of available actions. A player's information sets for the same decision are varied jointly. Candidate actions comprise the current support and actions tied in conditional utility under current continuation play. Source-unvisited policies remain fixed.

Each proposed complete profile is checked against both players' unrestricted best responses. A utility tie for the acting player alone does not justify adding an action to equilibrium support: doing so can change the opponent's incentives.

The main search uses a maximum unilateral root-payoff gain constraint of 10⁻⁹ in reported utility units and examines both forward and reverse decision-block orders. The higher-scoring verified result is selected. The tighter search uses forward order, a gain constraint of 10⁻¹⁰, and a conditional tie threshold tightened from 10⁻¹⁰ to 10⁻¹¹. The separate final best-response validation tolerance is 10⁻⁷. Each search is limited to six sweeps, and its stopping record is retained.

These are local searches. They do not establish global maximal mixing, uniqueness, or robustness across every equilibrium. Common row selection establishes stability only across the tested representations, and even those representations can produce different decompositions.

## Ties, reach and numerical verification

Primary best responses retain and renormalize original probability on actions that remain optimal within the 10⁻¹⁰ tie tolerance. When no such probability remains, the first optimal action is selected. Alternative low- and high-action selections test tie sensitivity. Separate checks replace opponent policies unvisited in their donor equilibrium with extreme low- or high-action completions. These checks are not bounds over all admissible completions.

An intermediate best response may not reach an information set reached in both endpoint equilibria. Its conditional continuation comparison is still defined when chance-and-opponent reach is positive; this reach calculation omits the focal player's own prior action probabilities. An asterisk identifies such unreached intermediate responses. If chance-and-opponent reach is zero, the conditional allocation is undefined and shown as a dash. This is distinct from tie or completion sensitivity.

Complete best responses are independently replayed to check root utilities and conditional action values. Saved profiles and action reports are checked against the initialized game. Accounting identities are verified before rounding. Table generation retains the input fingerprints; the verification script checks those fingerprints and the printed values against the generated sources. Execution and completion records are supporting provenance, not a substitute for these scientific checks.

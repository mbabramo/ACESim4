# Methodology and Explanation

These comparisons examine how a change in a fee rule or in the parties' risk preferences affects equilibrium strategies. Each decomposition separates the effect of changing the rule or preferences while holding the opponent's strategy fixed from contributions associated with the opponent's equilibrium entry, offers and exit decisions. The accounting describes selected equilibria and a specified set of counterfactual best responses. It does not identify unique causal mechanisms or model a process through which litigants adjust to equilibrium.

## Scope and files

At each cost, the three rules are American, Trial Fee-Shifting and Complete Fee-Shifting. All six directed fee changes are calculated within each available risk preference. All ordered risk changes are also calculated within each fee rule, changing both parties' preferences together. A comparison holds costs fixed and changes only one of these two dimensions. Reverse comparisons are computed independently; reversing the direction does not simply reverse the signs of the contributions.

With risk neutrality and symmetric CARA risk aversion at coefficient 2, there are 18 comparisons per cost, or 90 across the five standard cost multipliers. The README lists the available tables. The table collection provides candidate rows and panels for manuscript selection; it does not prescribe what belongs in the article or appendix.

Data contains the full numerical results, requests and provenance records, arranged by cost and directed contrast. It stores the computed values; the C# implementation performs the calculations explained below. Every comparison uses the saved equilibrium profiles underlying the strategy figures and outcome reports. Mixed strategies present in those saved equilibria are preserved; no auxiliary search changes their mixing probabilities. The separate multiple-start equilibrium study examines other recovered equilibria and their outcome dispersion.

Tables contains the PDF and PNG displays. Sources/Tex contains the editable C#-generated table layouts. Sources/Json contains selected table coordinates, unrounded values, scenario identifiers and input fingerprints. Matching filenames connect these sources with their PDF and PNG exhibits. Each JSON file links to this shared explanation. Scenario-specific information is retained in those records and in the filenames; the methodology is common to all tables. The full calculations retain excluded coordinates, original and hybrid policies, reach probabilities, utility comparisons and sensitivity checks.

Sources/Profiles contains frozen copies of the equilibrium and action-report inputs. The equilibrium CSV gives the saved strategy probabilities; the action report connects them to information sets and reported values. Preserving the exact input bytes makes the calculations reproducible even if routine reports are later regenerated or reserialized. The profile-provenance.json file identifies these inputs and their hashes. They are inputs, not extra equilibrium estimates. Sources/Process Logs records execution of the comparisons and table generation.

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

The table selection is restricted to information sets reached with positive probability in both saved endpoint equilibria. It includes two kinds of coordinates, evaluated on those same profiles. Tie and off-path completion checks remain explicit; selection does not require stability across alternative equilibria.

### Changed actions

A changed information set qualifies for the focus criterion if, against the target opponent and with subsequent own decisions optimized, its original local policy assigns probability mass greater than 10⁻⁶ to actions whose conditional utility is more than 10⁻⁶ below the best action. The utility threshold uses the target specification's reported utility units. This compares local actions, not the payoff loss from reverting the entire strategy.

Disjoint strategy supports do not suffice: an action absent from the target support can remain optimal. Consecutive signals are grouped when their original and target distributions and numerical contributions agree within 10⁻⁶ and their definedness and intermediate reach patterns match; sensitivity flags may differ within a group.

The Remaining column always preserves the endpoint-selection residual. Changes involving newly reached or no-longer-reached histories, and redistribution among actions that remain optimal, are outside this restricted display. Their absence does not establish that they are unimportant or that the full strategies coincide.

### Unchanged actions with offsetting effects

An unchanged equilibrium action can conceal counteracting incentives. Additional rows retain information sets whose original and target local distributions agree within 10⁻⁶, but whose direct best response changes. Against the original opponent under the target specification, the original local policy must assign probability mass greater than 10⁻⁶ to actions more than 10⁻⁶ below the best conditional action, with subsequent own decisions optimized. This excludes switches caused solely by selecting differently among tied or nearly tied actions.

The best response to the fully updated opponent must reproduce the target local distribution within 10⁻⁶. All eight conditional comparisons must be defined, Direct must be nonzero, and Remaining must be no larger than 10⁻⁶ in the reported coordinate.

For these rows, x equals y and R is numerically zero, so the direct contribution is offset by the opponent contributions. One example is plaintiff filing at signal 0.35 under American fees: filing occurs under both risk neutrality and risk aversion. Changing preferences with the original opponent fixed produces a best response of not filing (−100 percentage points), while the opponent-offer contribution is +100 points. This illustrates offsetting incentives under the accounting convention; it does not establish a uniquely necessary causal mechanism.

An empty table means that no coordinates satisfy the implemented selection criteria. Displayed values describe the selected saved equilibria. A different equilibrium or a different selection among tied best responses can produce a different decomposition; sensitivity flags and the separate multiple-equilibrium analysis help define the scope of the interpretation.

## Ties, reach and numerical verification

Primary best responses retain and renormalize original probability on actions that remain optimal within the 10⁻¹⁰ tie tolerance. When no such probability remains, the first optimal action is selected. Alternative low- and high-action selections test tie sensitivity. Separate checks replace opponent policies unvisited in their donor equilibrium with extreme low- or high-action completions. These checks are not bounds over all admissible completions.

An intermediate best response may not reach an information set reached in both endpoint equilibria. Its conditional continuation comparison is still defined when chance-and-opponent reach is positive; this reach calculation omits the focal player's own prior action probabilities. An asterisk identifies such unreached intermediate responses. If chance-and-opponent reach is zero, the conditional allocation is undefined and shown as a dash. This is distinct from tie or completion sensitivity.

Complete best responses are independently replayed to check root utilities and conditional action values. Saved profiles and action reports are checked against the initialized game. Accounting identities are verified before rounding. Table generation retains the input fingerprints; the verification script checks those fingerprints and the printed values against the generated sources. Execution and completion records are supporting provenance, not a substitute for these scientific checks.

## Full comparison packet and manuscript selection

The full strategy-analysis packet uses seven ordinary-cost panels, in this order: American to Trial Fee-Shifting under risk neutrality; Trial to Complete under risk neutrality; American to Trial under risk aversion; Trial to Complete under risk aversion; then risk neutral to risk averse within American, Trial and Complete Fee-Shifting. Both parties' preferences change in the last three panels.

Every coordinate satisfying the criteria above enters its panel, including qualifying offer changes and unchanged actions with offsetting effects. There is no additional manual row selection. Matching consecutive signals may be grouped. Sensitive rows, residuals and mixed-offer action probabilities remain explicit. The combined PDF has continued pages; its ordinary PNG is page one and additional PNGs use numbered page suffixes. Its manuscript caption is separate from the diagrams, in Sources/manuscript-strategy-mechanisms-caption.txt.

The short manuscript table, `selected-strategy-mechanisms`, selects three risk-neutral rows from this packet: P's demand at 0.35 under American to Trial fees, D's answering at 0.65-0.95 under Trial to Complete fees, and P's filing at 0.25-0.35 under Trial to Complete fees. The latter filing contribution is completion-sensitive at 0.35, as recorded in the full packet. The short display has no sensitivity column or asterisks. All its intermediate conditional comparisons are reached; opponent-exit and remaining contributions are zero and are omitted from the display. The source JSON retains diagnostic fields.

The code assembles both displays from fingerprint-checked saved calculations. `LitigCharts equilibrium-manuscript --input <Equilibrium strategy changes> --article <article repository>` refreshes the numbered Table 2 with the short selection and updates its source manifest. The supplemental scheduler generates both canonical displays after the seven individual tables. Full packet files and qualifying coordinates remain available online.

Payoff-grid sensitivity is distinct from the recorded tie/completion checks. In the current ordinary-cost American-to-Trial RN calculation, P filing at signal 0.25 has a +12.2 Direct entry induced by a roughly 0.000005 utility gap associated with terminal-payoff rounding. This is not evidence of a substantive positive direct incentive from trial fee shifting. The caption qualifies the recorded value pending the broader numerical audit; inclusion in this table and successful arithmetic checks do not establish payoff-grid robustness.

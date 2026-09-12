# Information-set pressure analysis

Use the existing C# LitigCharts entry point, not production-setting changes:

```text
dotnet run --project LitigCharts -c Release -- pressure --request "C:/Users/Admin/source/repos/correlated-signals-article/information-set-pressure.request.json"
```

The article request selects fee-rule and symmetric-risk-aversion interventions at
cost multipliers 1 and 4, with the original ten-signal/ten-offer continuous-merits game.
Each intervention changes one primitive family only. Opponent participation, offers,
and exit are mapped by player, decision code, and labeled observed history. A profile
cannot be substituted merely because its vector has the same length: each input must
reproduce its own saved semantic and numerical action-value report first.

## Calculations

For original equilibrium (P0,D0), intervention G1, and target equilibrium (P*,D*):

1. Direct: P1=BR_G1(D0), D1=BR_G1(P0), both against original opponents.
2. Dynamics: recompute a full response with only the opponent's participation, only
   offers, only exit, or all components from its step-one response. The all-components
   pair is P2=BR_G1(D1), D2=BR_G1(P1), not a converged or observed adjustment path.
3. Equilibrium decomposition: use the same component replacements from D* or P*
   instead. Full replacement must reproduce target-equilibrium payoff up to the
   verification tolerance, not necessarily the same mixed strategy.

All focal continuation choices are optimized without monotonicity restrictions. The
existing generalized extensive-form best-response routine (GEBR) is used, not a new
equilibrium solver. The ordinary saved action report uses equilibrium continuation;
the new diagnostic uses optimized continuation and is not calculated by subtracting
those existing reports.

## Reach and utility semantics

- Actual reach follows the reported focal response and fixed hybrid opponent.
- Focal counterfactual reach includes chance and opponent actions but omits the focal
  player's prior action probabilities. With perfect recall, normalization gives the
  player's posterior at a reached information set and a well-defined forced-own-path
  posterior at an otherwise unplayed own history if opponent/chance reach is positive.
- Actual conditional utility is null at zero actual reach. Counterfactual action values
  may still exist there. Both values and beliefs are null at zero opponent/chance reach.
- An independent tree evaluator recomputes reach, posterior opponent-signal weights,
  and action utilities, checks normalization, and verifies GEBR's selected policy payoff
  and optimized action values. It does not condition choices on hidden full histories.
- At opponent information sets, the focal-deviation exposure weight can exceed one
  because alternative focal histories can merge there. It is an exposure diagnostic,
  not an opponent belief or a normalized probability.
- Raw utility levels across risk preferences are not comparable welfare measures.
  Gains always compare policies in the same intervention game against the same opponent.

## Ties and off-path completions

The main response uses GEBR's strict maximum and first action on exact ties. All action
gaps and near-tie indicators are retained. A second first-round donor selects the highest
action within the numerical tie tolerance at each set; replay must achieve the optimal
root payoff within the verification tolerance before it is used in the dynamics check.
This is a documented near-tie sensitivity, not enumeration of all best responses.

At zero counterfactual reach, the focal response keeps the original complete policy
instead of inheriting stale solver scratch data. Such donor completions are identified.
Opponent donor-unvisited sets are audited when either actually newly reached OR exposed
by a possible focal deviation. The latter matters because arbitrary off-path behavior
can discourage entry without being played. Low/high completion checks replace only
donor-unvisited opponent policies with the first/last available action. These tests are
illustrative stress tests, not exhaustive bounds, alternative equilibria, or permission
to interpret every hybrid as a credible continuation.

## Outputs and reproduction

`--calculate-only` omits publication assembly. `--render-only` requires the completed
manifest, an unchanged request, and unchanged JSON calculation hashes. The output
directory cannot lie in a production input directory. Saved profiles and source reports
are hashed before and after calculation and are never rewritten. The manifest also
records the core assembly hash and numerical rounding settings.

Each contrast has a four-page PDF, standalone TeX, caption/interpretation TXT, exact JSON,
action CSV, posterior CSV, sensitivity CSV, and page PNGs. The folder README links the
tables. `pressure-analysis-summary.csv` gives all root gains and checks. No diagrams are
combined with these tables, and no existing main-article figure or table is replaced.

A star on a PDF column identifies exposure to donor-unvisited opponent policies,
including policies reachable only through a focal deviation. It is a warning to consult
the completion sensitivity, not a claim that every such policy changes the result.

### Reading the revised tables

Headings derive the direction from the selected game definitions: American to British
with preferences fixed, or risk neutral to moderately risk averse with the fee rule
fixed. Original and Target name the appropriate regimes and show equilibrium levels.
All five middle columns show changes **from Original**, not from the preceding column.
Probability changes are percentage points; pure-offer changes are fractions of damages
calculated from the exact production offer grid.

A dash means unchanged and a blank means that the history is unreached. A `new` cell
shows its level when the original history was unreached. If either offer policy is mixed,
the changed cell is labeled `level` and retains the actual policy rather than subtracting
an invented average offer. Unchanged mixtures remain a dash; endpoint columns retain
all mixing probabilities. The action/belief/sensitivity CSVs and calculation JSONs
remain unchanged level records.

The former Q/C offer-history abbreviations are now spelled out: `Abandon` for P,
`Default` for D, and `Continue` for either. These are the player's own private commitments
chosen before offers, not observed actions by the opponent. Both histories still make
an offer; the commitment operates only if bargaining fails.

Tests cover full best response versus exhaustive unrestricted pure plans in a small
game, replay and state restoration, semantic mapping, component isolation, posterior
selection, own off-path versus zero opponent reach, preserved completions, and table
mixing/blank semantics. The production run additionally validates all source action
rows and source/target exploitability controls.

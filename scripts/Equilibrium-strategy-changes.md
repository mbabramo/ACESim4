# Changed equilibrium strategies

From the ACESim4 repository:

    dotnet run --project LitigCharts -c Release -- equilibrium-changes --request "C:/Users/Admin/source/repos/correlated-signals-article/Replication/Requests/equilibrium-changes.request.json" --calculate-only

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
when both are optimal. The table now retains the four numerical contributions
and shows its selection residual in **Remaining**, with an asterisk. The four
contributions plus Remaining equal the observed change; the remainder is not
presented as a fifth mechanism. Low/high optimal-action selections flag other tie-sensitive
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
section sign, not an assertion that all hybrids are credible equilibria. Asterisks
are reserved for unmatched endpoint residuals; daggers retain tie sensitivity.

## Relative-payoff supplement

For each information set with a partial or undefined policy decomposition, show
one additional comparison, not one row per changed offer probability. Let d(a)
be target probability minus original probability. Normalize positive d(a) to
unit mass and, separately, negative d(a) to unit mass. Compare the conditional
utility of the gaining-action mixture against the losing-action mixture. This
weights the actual probability transfer, not mean offer amounts. For an original
pure 0.85 demand changing to a mixture of 0.75 and 0.85, it is Q(0.75) - Q(0.85).

Calculate that payoff gap in the original and target references and all eight
saved opponent-component hybrids. Apply the same direct-first allocation to
the gaps. Original/target Q uses actual endpoint continuations; hybrid Q uses
optimized focal continuations. Therefore the first column is explicitly labeled
Direct/reopt., and any target-reference versus all-target-opponent continuation
mismatch stays in Remaining. It is not silently assigned to opponent changes.

Payoff supplements are appended after the strategy tables. All their PDF values
are 1000 times utility differences for readability; JSON and text keep unscaled
values. These are never percentage points, money, or a welfare comparison across
utility specifications. Preference-regime comparisons depend on the chosen
utility normalization. They can explain why an action becomes competitive but
cannot determine an exact equilibrium mixing probability or reconstruct dynamics.

Tie/completion stress tests apply to payoff allocations separately from policy
allocations. A hybrid off the focal best-response path is marked but remains
conditionally defined if chance-and-opponent reach is positive. Missing Q values
or zero chance-and-opponent reach yield null gaps and no fabricated allocation.
All intermediate gaps, weights, flags and residuals are exported in
`*-payoff-gaps.json`; combined publication JSON also includes them. Render-only
derives these quantities from the verified saved calculations, without changing
the source calculation JSON, its manifest, the production equilibria or the
ECTA trace inputs.

## Reports and verification

- EquilibriumChangeDecomposition.cs: eight-subset construction, allocation,
  changed-row selection, exact action-share representation, sensitivity flags.
- InformationSetPressureAnalysis.cs: reusable profile mapping and validated
  unrestricted best response and independent policy replay.
- ArticlePressureAnalysis.cs: source loading, source equilibrium controls,
  matched intervention checks, calculation manifest and file hashes.
- EquilibriumChangeTables.cs: change-only PDF/TeX/TXT and grouped paper tables.

The article request selects four fee/preference contrasts at costs 1 and 4.
The four accepted ordinary-cost TeX/PDF pairs and editable methodology are in
`Tables/Equilibrium strategy changes`. The superseded combined and per-contrast
presentation reports have been removed. All eight original calculations,
including the high-cost cases, remain in `Results/Equilibrium diagnostics/Original`.
Full JSON contains source and
target strategies, primary and sensitivity responses, action values, reaches,
beliefs, exclusions, and explicit residuals. Original production files are
hash-checked before and after calculation and again before rendering.

Active equilibrium request files are in `Replication/Requests`, with exact
historical copies in `Replication/Recorded requests`. The old combined-table
renderer remains available for diagnostics, but its request no longer writes
to `Tables`. Use `equilibrium-publication` below for the accepted presentation.

## Diagnostic profile replacements and compact focus

An optional `ProfileFile` on a source loads a semantic behavioral profile instead
of using the saved equilibrium as the endpoint. The saved equilibrium/action
report are still validated together first: their 480 rows are NOT claimed to
describe the replacement. The replacement must match the option set and complete
semantic action menus, have valid probabilities, and pass BOTH unrestricted
best-response controls. Conditional values, beliefs and donor reaches are
recomputed. Its own SHA-256 is included in the manifest and checked again before
reading/rendering cached results. Ordinary requests without overrides work as
before. No saved equilibrium file is rewritten.

For the four mixed ordinary-cost profiles:

```text
dotnet run --project LitigCharts -c Release -- equilibrium-changes --request <equilibrium-changes-mixed.request.json> --calculate-only
dotnet run --project LitigCharts -c Release -- equilibrium-change-focus --request <equilibrium-changes-mixed.request.json> --original <equilibrium-changes.request.json>
```

The second command writes experimental `Focused strategy changes.md` and JSON
beside the separate calculation, not into the original table or animation folders.
It compares original and mixed endpoints and retains ALL changed information sets
in the audit output, with one row per set. For each set, G is the set of actions
whose probability increases by more than `PolicyTolerance`. Decompose probability
assigned to G, in percentage points, using the same eight-coalition calculation.
This is not a mean offer or an average of action utilities. The full calculation
still contains the separate action-share rows; aggregation can mask offsetting
redistribution within G, so it is explicitly a compact summary.

The focus subset requires original probability mass greater than 1e-6 on actions
that lose more than `NearTie` (default 1e-6 target-utility units) relative to the
best action against the target opponent, with ALL subsequent own choices
reoptimized. Fixed-target-continuation losses are exported separately. The test
avoids mistaking an unfavorable off-path own completion for a necessary action
change. It is neither a full-strategy regret measure nor a welfare comparison.

This filter allows overlapping supports and can reject disjoint supports when
the actions remain tied. It can be applied to original equilibria too: increasing
mixing is not required for the filter, and does not guarantee a shorter table.
Changes omitted from the focused portion can still be important for opponent
incentives or outcomes. The actual mixture, support, and opponent's incentive
constraints may change even when the player's actions are tied. Remaining and
tie/completion sensitivity are never forcibly zeroed. The compact allocation
does not reconstruct equilibrium dynamics or resolve equilibrium selection.

## Four publication tables

`equilibrium-publication` renders the accepted compact table as four separate
standalone LaTeX/PDF pairs. It reads the original, selected-mixed and
forward/tighter calculation manifests (including their input hashes), intersects
their focus sets, and takes every displayed coordinate and contribution from
the ORIGINAL calculation. It does not reuse the mixed-report aggregation of
probability on gaining actions. The current selection has 39 information sets
grouped into 26 rows (2, 6, 12 and 6 rows in the four tables).

```text
dotnet run --project LitigCharts -c Release -- equilibrium-publication --original <equilibrium-changes.request.json> --mixed <equilibrium-changes-mixed.request.json> --check <equilibrium-changes-mixed-forward.request.json> --output <article/Tables/Equilibrium strategy changes> --previews <temporary-QA-directory>
```

Quote paths containing spaces. The command compiles through the existing
LaTeX compiler, sending raster QA previews to the separate requested directory.
The publication directory contains only the four `.tex`/`.pdf` pairs and the
author-owned `Methodology.tex` fragment. There are no generated table notes,
README files, request files, calculation JSON, or PNG previews in this directory.
The command never writes `Methodology.tex`, so the author may edit it freely.

In the article repository, the three request arguments are in
`Replication/Requests`. Their cached inputs are in `Results/Equilibrium diagnostics`:
`Original`, `Mixed`, and `Mixed tighter check`. The `Mixing` subdirectory retains
the profile searches and their tighter-tolerance checks. Cached manifest input
and output hashes are still verified. `OriginalRequest` preserves each original
calculation request fingerprint alongside the relocated active request.

Consecutive signal rows with the same numerical policies and contributions may
be combined despite differing sensitivity flags. The last column then reports
`No`, `Yes`, or `At some signals` depending on whether none, all, or some of the
group's signals have a tie/off-path-sensitive original-profile allocation.
The compact layout rejects nonzero remainders, undefined allocations and
mixed-offer probability coordinates rather than silently mislabeling or dropping
those quantities. Adding them requires an explicit layout extension.

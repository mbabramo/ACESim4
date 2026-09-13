# Article diagrams

Optional ECTA numerical-path exports are described in
[Equilibrium paths](../scripts/Equilibrium-paths.md). The equilibrium-paths
command records every pivot and its complete strategies and incentive gaps,
then generates standalone heat-map animations. Use --render-only to regenerate
animations from verified traces. It does not overwrite production equilibria.

Run these commands from the ACESim4 repository. Requires .NET 9; PDF generation
also requires LuaLaTeX and Poppler's pdftoppm on PATH (or configured executable paths).
No PowerShell script or separate LaTeX project is used.

The article repository contains `article-diagrams.json`. All paths in that file
resolve relative to the file, not the shell's current directory. Change the
aggregate CSV path there when moving to a new completed results batch.

```text
dotnet run --project LitigCharts -c Release -- diagrams all --config "C:/Users/Admin/source/repos/correlated-signals-article/article-diagrams.json" --list
dotnet run --project LitigCharts -c Release -- diagrams worked-path --config "C:/Users/Admin/source/repos/correlated-signals-article/article-diagrams.json"
dotnet run --project LitigCharts -c Release -- diagrams publication --config "C:/Users/Admin/source/repos/correlated-signals-article/article-diagrams.json"
dotnet run --project LitigCharts -c Release -- diagrams all --config "C:/Users/Admin/source/repos/correlated-signals-article/article-diagrams.json" --jobs 4
```

## Changed-equilibrium diagnostics

    dotnet run --project LitigCharts -c Release -- equilibrium-changes --request "C:/Users/Admin/source/repos/correlated-signals-article/Replication/Requests/equilibrium-changes.request.json" --calculate-only

This separate command validates saved equilibria and computes all eight subsets
of actual opponent entry, offer, and exit replacements using unrestricted full
best responses. Direct incentives come first; the remaining contributions average
marginal replacements over all six orders. Only changed decisions reached in both
endpoint equilibria are displayed. Ties, selection residuals, and intermediate
reach are explicit. Mixed offers are never replaced by mean offers.

--calculate-only writes full diagnostic JSON and fingerprints; --render-only
assembles verified calculations. The pressure command is a compatibility alias,
not the deleted seven-column table generator. The ordinary-cost bundle goes to
the article's Tables folder; high-cost and individual comparisons go to
Supplemental materials/Equilibrium changes. No production equilibrium is solved
or overwritten. This calculation is not implicitly included in diagrams all.

See [the calculation guide](../scripts/Equilibrium-strategy-changes.md).

## Other targets

Main publication tables use the same C# project, with a separate saved-data command:

```powershell
dotnet run --project LitigCharts -c Release -- tables --request "C:/Users/Admin/source/repos/correlated-signals-article/publication-tables.json"
```

This generates four descriptive, unnumbered PDFs with editable manuscript TeX fragments,
PNG previews, TXT captions, and JSON source records/hashes and cell calculations.
Add `--sources-only` to omit compilation. Paths are relative to the request file.
Table generation does not load or solve equilibria and is not part of `diagrams all`.
It validates exact report selection, dispositions, monetary accounting, conditional
participation, and multiple-start recovery/range accounting before writing any tables.

| Target | Operation |
| --- | --- |
| game-trees | Generate the five structural trees from the small illustrative game. |
| worked-path | Extract the selected saved full-game profile; generate the arranged figure. |
| worked-path-data | Extract JSON only, including all requested adjacent histories. |
| signals | Generate configured information structures in color and grayscale, plus probability-flow JSON and explanations. |
| inverse-signals | Reverse the configured party-signal flows, with the same lower-middle reference signal; destinations are merits or binary truth, as appropriate. |
| party-to-party | Predict the other party's signal from one party's signal, using the full production joint distribution; same middle-signal reference across models. |
| damages-signals | Generate the legacy damages illustration separately; excluded from all. |
| selection-offers | Generate four baseline participation/offer panels from selected action-report rows. |
| dispositions | Generate paired disposition bars from selected All/Only Eq summary rows. |
| publication | Generate selection-offers and dispositions (main Figures 3 and 4). |
| individual-results | Compile every saved individual-result .tex, recursively. |
| multiple-equilibria | Compile every saved multiple-equilibrium .tex, recursively. |
| aggregates | Regenerate sources from the configured full output CSV using existing chart code. |
| results | Individual, multiple-equilibrium and aggregate targets. |
| all | Article targets, excluding endogenous and damages-signals. |
| endogenous | Generate the separate endogenous-disputes beginning diagram. |

The current article configuration expects 804 individual, 216 multiple-equilibrium,
and 338 aggregate diagrams: **1,394** with the five trees, worked-path figure,
sixteen forward, six inverse and six party-to-party signal figures, and two assembled publication figures.
These counts are explicit completeness checks, not revision-plan figure selections.
Adjust them deliberately when the underlying experiment set changes.

Individual/ME sources were produced by the normal simulation reporting code.
This command reuses them; it does not rerun the 134 simulations or load all
equilibria to reconstruct their report sources. Aggregates require the full
production `CS004 output.csv`, not the article's All-cases-only numerical summary.

## Modes and safety

- `--list`: validate paths/counts and print the plan without writes or external
  processes. Aggregates are generated in memory to check their actual count.
  Worked-path profile validation happens during extraction, not during listing.
- `--sources-only`: generate .tex and extraction JSON; no PDF tools required.
- `--compile-only`: compile existing .tex; do not reconstruct games or extract
  data. It does not require the equilibrium, action report, or aggregate CSV.
- `--output-root "C:/review/diagrams"`: put outputs in separate group subfolders,
  preserving result subdirectories. With compile-only, read sources from the
  configured directories and copy them to the output root before compilation.
- `--jobs N`: bound concurrent compilers; defaults to four. Tool timeouts default
  to 180 seconds per process and can be changed in the JSON configuration.

Normal mode generates sources and compiles every selected PDF and a 150-dpi PNG.
Existing generated outputs are replaced. Close PDFs in Acrobat before replacement.
User-authored .txt explanations and README files are preserved, except matching
structural-tree, signal-diagram and publication-figure .txt companions generated by this command.

There is no solver call, results reorganization, production flag change, or
cleanup of an input/results directory. Compilation uses unique short-path temp
directories and hidden processes. Failed compilations retain diagnostic files,
are reported, and cause a nonzero exit; successful temporary files are removed.
No arguments now prints help instead of starting the legacy report workflow.
The existing Runner APIs remain available to production callers.

## Code ownership

- ACESimBase: game-tree traversal, saved-profile loading, information-set
  calculations, path selection/replay, and action-report validation.
- LitigCharts: commands, paths, LaTeX binding/layout, and PDF/PNG compilation.
- Article repository: configuration, requested histories, generated artifacts,
  and accompanying explanatory prose.

Edit `WorkedPathDiagram.cs` for geometry and `ArticleWorkedPathLatexData.cs`
for data binding. The latter checks that the selected histories fit the example.
The emitted worked-path .tex embeds the values directly; no separate values.tex
or hand-maintained template is required. Compilation alone can use the generated
.tex without .NET or the extraction inputs.

For interpretation and legacy API details see
[Game-tree-diagrams.md](../scripts/Game-tree-diagrams.md).

## Signal family

Use `diagrams signals --config <article-diagrams.json>`. The default
`SignalSpecifications` are Baseline, TruthConditionedLatentMerits, and
DirectBinaryStateSignals. Each relationship produces its own matching `-color`
and `-bw` files (truth, party, court; binary has party and court only).
No combined multi-panel PDF is written. Composition is a later manuscript choice.
Color ribbons retain the source hue: orange in the lower half, blue in the upper
half (including a bucket centered exactly at the midpoint). The reference fan
uses a stronger version of that same hue. Monochrome background ribbons all use
the same pale gray, regardless of signal strength. Only the reference fan is darker
and drawn last in both palettes. All ribbons are fill-only, with no white or black
borders that could inflate thin flows or interrupt earlier flows at crossings.
Grayscale distinguishes highlighting, not source strength. Bucket outlines
remain black in both palettes. These rules cover all forward, inverse, party-to-party,
optional specification and damages-extension diagrams generated by this workflow.
All fills are opaque vectors. The JSON sidecars contain the unrounded joint masses.

Optional entries LowNoise, HighNoise, CenterWeightedContinuousMerits and
PolarizedContinuousMerits use their existing production option sets. To generate
these without adding files to the article, use a separate config and --output-root.
Continuous Q is integrated within five display intervals; those are not model
states, and signals need not be independent conditional on a whole display interval.
A highlighted reference source fan supplies a numerical example in both palettes. Conditional destination
probabilities and explanatory sentences appear only in the TXT/JSON companions,
not inside the figure. The graphics retain short column/bucket labels only, with
italic mathematical Q/T and the Greek sigma symbol for noise standard deviation.

`damages-signals` uses `DamagesSignalsDirectory`, outside the article in the
checked-in configuration. It preserves the ten-level, sigma=0.2 illustrative
extension without suggesting that current continuous-merits runs vary damages.
The older SignalsDiagram API remains available for legacy callers.

### Inverse party-signal diagrams

`diagrams inverse-signals --config <article-diagrams.json>` adds standalone
`party inverse` files in color and grayscale for the configured specifications:
Baseline, TruthConditionedLatentMerits, and DirectBinaryStateSignals by default.
The direct-binary diagram has true liability, not merits, on the destination axis.
The inverse target is also included in all.

The joint matrix is transposed, including its prior weights. Continuous merits
keep five display intervals; discrete merits sum pairs of point masses into the
same five intervals before reversal. No smoothing or continuous approximation
of the discrete model is introduced. The lower-middle party signal (0.45 with
ten bins) is the highlighted reference in both palettes; 0.55 is its reflected counterpart. The plots retain
joint-mass widths, while the TXT/JSON files record the normalized posterior
probabilities given that one signal bin. There is no conditioning on procedural
selection or the opponent's signal. The current central-bin posterior is about
34.8% versus 31.4%, so this calibration supports a modest, not dramatic, difference.
In the direct-binary model the posterior remains supported on the two truth
states (about 60.0% not liable / 40.0% liable at signal 0.45). This is uncertainty
about binary truth, not evidence of an intermediate latent merits level. Binary
truth itself is not the distinction: the continuous-merits baseline also has
binary truth, but retains shared case-quality variation within each truth state.

### Party-to-party prediction diagrams

`diagrams party-to-party --config <article-diagrams.json>` generates a separate
`party to party` PDF in each palette for each configured specification; `all`
includes them. The left axis is the plaintiff signal, the right axis the defendant
signal. At equal party noise either party can read the same relationship in reverse.
The reference signal is again 0.45, highlighted in both palettes. This is prediction, not a causal link between
signals and not observation of the opponent's private information.

The joint masses are P(S_P=i) P(S_D=j | S_P=i), taken directly from the production
Bayesian signal methods. Continuous merits are integrated at exact Q inside that
calculation; no conditional-independence approximation within display bins is used.
Tests independently integrate products of the kernels (or sum exact discrete states).
TXT companions include both conditional and unconditional opponent distributions;
JSON retains the full joint matrix and reference conditional distribution; its
Highlight index identifies the emphasized fan and reference calculation in both palettes.
No procedural selection or equilibrium strategy is involved.

For the middle opponent range [0.40,0.60), the current production comparison is:

| Model | Unconditional | Given own signal 0.45 |
| --- | ---: | ---: |
| Continuous merits | 21.5% | 27.8% |
| Truth-conditioned merits | 20.1% | 26.7% |
| Direct binary signals | 16.7% | 16.7% |

In the symmetric, conditionally independent direct-binary specification, any
symmetric signal range has the same probability under either truth state.
One's own signal therefore predicts the opponent's side but not distance from
the middle. This property is structural, not a consequence of the different
calibrated party noise (approximately 0.35 versus 0.20). The numerical levels
do depend on calibration. Shared merits allow a middle own signal to predict
more intermediate opponent signals. This is not a criticism of every binary-truth
model: the baseline also has binary truth, and correlated errors could introduce
shared evidentiary heterogeneity into a binary-state model.

## Publication comparisons

`diagrams publication --config <article-diagrams.json>` generates Figures 3 and 4
in `PublicationFiguresDirectory`. `PublicationFiguresRequest` selects a separate
JSON request; all its data paths resolve relative to that request. The checked-in
article request selects the baseline cost-1 American/British action reports and
ten disposition rows: baseline, doubled costs, moderate risk aversion, lower noise,
and lower noise with moderate risk aversion, each under both fee regimes. Outputs are vector
PDF, 150-dpi PNG, standalone TeX, a caption/interpretation `.txt`, and numerical
`.json` with exact row identifiers, information sets, probabilities and SHA-256
hashes of the request and sources. Long explanations are not put in the figures.

`PublicationFigures.cs` owns reusable two-regime layouts. Change the request to
select runs or label groups, not production switches. Strategy panels preserve
actual mixed offer support (with short probability labels), plot participation
probabilities, and leave off-path offer regions blank without labels or markers.
Lines never bridge those gaps; the JSON still retains off-path status, which uses
the action report's 1e-15 tolerance. Offer coordinates come from the selected
production option set's exact grid, checked against the rounded report labels;
this preserves fifteen-offer precision. No game tree or solver is initialized.
Missing bins, incomplete action menus and multiple reached histories for the
same own signal are rejected rather than imputed or pooled. The current layout
requires all four decisions in a one-round game; additional histories or absent
decisions need an explicit layout extension, not automatic aggregation.

Dispositions read the article's numerical summary, not the larger aggregate
chart input. Every bar uses All/Only Eq rows and all potential disputes as its
denominator. P Abandons and D Defaults already include the 50/50 mutual-give-up
allocation. The separate mutual-give-up audit is retained but never re-added.
The seven categories and the two trial components must reconcile within 1e-5
for source rounding; values are never silently renormalized. The request and
caption must be reviewed when changing the publication selection.

`AdditionalDispositionFigures` in the diagram configuration lists independent
`Request` JSON and `Output` .tex paths, both relative to that configuration. These
comparisons are regenerated automatically by `dispositions`, `publication`, and
`all`; they use the same renderer without changing the main figure's selection.
The article config includes two standalone participation-restriction comparisons,
each with its own baseline reference, in `Supplemental materials/Participation restrictions`.
Optional `DispositionIntroduction` text in each request supplies its specific
caption context outside the diagram. Compile-only requires only existing TeX;
output-root sends these extras to `Supplemental disposition figures`.

Both targets support the existing list, source-only, compile-only and separate
output-root modes. Publication generation validates all selected input data
before writing any outputs. It does not alter the saved reports or equilibria.

## Changed-equilibrium tables

The equilibrium-changes command replaces the former pressure-table generator.
It reuses saved equilibria, calculates all eight opponent-component coalitions,
and displays only changed, commonly reached decisions with direct-first additive
contributions. Ties, selection residuals, and counterfactual reach are explicit.
See [the calculation and reproduction guide](../scripts/Equilibrium-strategy-changes.md).

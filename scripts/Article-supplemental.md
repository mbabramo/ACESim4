# Complete core supplemental analyses

The routine 124-case study remains separate. This workflow covers its three-rule
baseline: American, Trial Fee-Shifting and Complete Fee-Shifting, with every
available risk level. Current coverage is RN and symmetric CARA alpha 2.

From a committed ACESim4 source tree, run `scripts/Rebuild-ArticleSupplemental.ps1`.
It builds the code, runs and aggregates the six ordinary-cost 50-start cases,
then prepares and executes the directed strategy comparisons, mixing checks,
original solution paths and publication tables. `-Processors` defaults to all
processors. `-OutputDirectory` selects the separate output collection; the default
is SupplementalResults, outside routine ReportResults. For an article-side run,
pass its Supplemental materials directory explicitly. `-OriginalSolveLogDirectory`
supplies original single-prior exact logs when routine reports were rebuilt from
cached equilibria. The script never substitutes a cache-validation log for an
original solve log. `-Python` can specify the bundled Python executable.

`-PrepareOnly` writes requests without starting calculations. Use
`-SkipMultipleEquilibria` only when that separate production plan is already
complete or running independently. As with other production plans, changed
source/build manifests require a fresh output directory; do not bypass checks.

## Automatically generated coverage

The planner reads the verified `Comparison Family=baseline` rows from the welfare
CSV and resolves their exact profile/action reports through welfare-exhibits.json.
It requires all three legal rules for every available cost/risk group. All ordered
pairs changing exactly one dimension are generated; reverse comparisons are
calculated separately, not obtained by negating forward contributions.

With the current six cases at each cost there are 18 comparisons: six directed
fee changes under each risk preference, plus both risk directions under each of
three rules. Five costs give 90 comparisons. Additional available risk levels
expand the same pair-generation rule without a new handwritten contrast list.

Each of the 30 profiles has a forward/reverse mixing search and a tighter forward
check, with full equilibrium verification. Each directed contrast is calculated
under the original, selected mixed and tighter mixed representations. The 90
publication tables select the common supported coordinates and offsetting effects.
Their explicit Remaining column preserves selection residuals; mixed offer-action
probabilities and undefined conditional comparisons are represented explicitly.
An empty selected table is a valid result, not proof of identical strategies.

Six independent ordinary-cost paths replay exact seed-zero uniform-prior solves,
matching original pivot counts and every saved action probability. These are
numerical solver paths, not fee-rule transitions. The combined HTML viewer uses
all six verified traces; each also has its own viewer.

## Scheduling and resumption

`rebuild_article_supplemental.py` writes supplemental-plan.json and runs isolated
processes with bounded concurrency. This prevents shared static solver state from
crossing between games. Mixed calculations depend on their two mixing results;
publication depends on all three representations. Logs, process identities,
durations, input/binary hashes and output hashes are in supplemental-state.json;
logs and process identities are in each workflow's Sources/Run records. Repeating the
same command skips only jobs whose recorded inputs, binaries and outputs still
match. Independent jobs continue after a failure; failed dependencies remain
blocked until repaired. Use `--phase` to select original, mixing, mixed,
publication or paths for intervention; prerequisites must already be verified.

Figures/tables share descriptive source folders; editable TeX/JSON and separate
captions are in Sources. Cost and contrast are in filenames, not artwork titles.
Never remove a nonzero residual, fabricate a conditional value at zero reach,
pool distinct offer histories, or infer reverse contributions from forward ones.

Multiple-start recovery frequencies are computational diagnostics, not behavioral
selection probabilities. Risk and fee-rule metadata identify each of the six
range rows independently; Trial and Complete are never pooled as “British.”

The multiple-start production is stored in `Multiple equilibria/Sources/Production`.
After aggregation, `multiple-equilibria-report` validates recovery counts and
truth-weighted outcomes, generates separate welfare-range and disposition-range
tables for Risk Comparison and each risk, and compiles every distinct equilibrium's
individual diagrams in risk/fee folders. The recovery table reports all six cases.
Editable sources and captions accompany the rendered PDF/PNG files. The five
headline welfare measures use the routine study's population-weighted definitions;
legacy truth-specific conditional columns remain explicitly documented diagnostics.
Ranges concern recovered profiles, not confidence intervals, and recovery shares
are not behavioral selection probabilities. The original production manifest is
preserved separately from the reporting inventory and its input/output hashes.

The wrapper finishes with `verify_article_supplemental.py` (requires pypdf).
It checks complete directed coverage, calculation accounting and fingerprints,
mixing verification, every printed table magnitude in reading order, the six
path endpoints and multiple-start exhibit provenance. `--changes-only` audits
the comparison stage while independent expensive calculations are still running.
PDF numeric verification is combined with visual inspection before manuscript use.

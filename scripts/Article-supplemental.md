# Complete core supplemental analyses

The routine 276-case design remains separate. This workflow covers its three-rule
baseline: American, Trial Fee-Shifting and Complete Fee-Shifting, with every
available risk level. Current coverage is RN and symmetric CARA alpha 2.

From a committed ACESim4 source tree, run `scripts/Rebuild-ArticleSupplemental.ps1`.
It builds the code, runs and aggregates the six ordinary-cost 50-start cases,
then prepares and executes the directed strategy comparisons,
original solution paths and publication tables. `-Processors` defaults to all
processors. `-OutputDirectory` selects the separate output collection; the default
is SupplementalResults, outside routine ReportResults. For an article-side run,
pass its Supplemental materials directory explicitly. `-OriginalSolveLogDirectory`
supplies original single-prior exact logs when routine reports were rebuilt from
cached equilibria. The script never substitutes a cache-validation log for an
original solve log. `-Python` can specify the bundled Python executable.

`-PrepareOnly` writes requests without starting calculations. Use
`-SkipMultipleEquilibria` to leave that separate production plan stopped, or when it is
complete or running independently. As with other production plans, changed
source/build manifests require a fresh output directory; do not bypass checks.

Multiple-start solving retains the initial exact equilibrium when an approximate
batch returns no verified profiles and then attempts the exact fallback. Recovery
counts determine the remaining attempts, rather than the number of distinct
profiles. Failed exact attempts remain failures in the recovery report. An
unexpected SequenceForm exception is reported immediately; it is not retried on
a partially initialized game tree. The coordinator waits for already-active
cases to save their outputs before returning a failed status. After every worker
has exited, `recover --failed --include-pending` resets only unfinished tasks on
the same source/build; it preserves completed cases. A changed build still
requires a fresh directory, and old results retain their original provenance.

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

Each directed contrast uses the same saved profiles as the strategy and welfare
exhibits. The 90 tables select strict conditional action-loss and offsetting-effect
coordinates from these calculations, retaining tie and off-path completion checks.
Auxiliary mixing searches and the intersection across alternative mixed profiles
are disabled. The saved equilibria's own mixed strategies remain unchanged.
Their explicit Remaining column preserves selection residuals; mixed offer-action
probabilities and undefined conditional comparisons are represented explicitly.
An empty selected table is a valid result, not proof of identical strategies.

Six independent ordinary-cost paths replay exact seed-zero uniform-prior solves,
matching original pivot counts and every saved action probability. These are
numerical solver paths, not fee-rule transitions. The combined HTML viewer uses
all six verified traces; each also has its own viewer.

Completed path caches require the same core assembly hash as the running code.
A different model/solver build requires a fresh output directory; an old but
internally intact trace is not silently presented as a replay under new code.

## Scheduling and resumption

`rebuild_article_supplemental.py` writes supplemental-plan.json and runs isolated
processes with bounded concurrency. This prevents shared static solver state from
crossing between games. Each table depends on its saved-equilibrium calculation.
With 90 comparisons and six paths, the planner creates 187 jobs: 90 calculations,
90 tables, six replays and one path collection. Logs, process identities,
durations, input/binary hashes and output hashes are in supplemental-state.json;
strategy-change logs and process identities are in Sources/Process Logs; solution-path
logs remain in Sources/Run records. Repeating the
same command skips only jobs whose recorded inputs, binaries and outputs still
match. Independent jobs continue after a failure; failed dependencies remain
blocked until repaired. Use `--phase` to select calculations, tables or paths
for intervention; prerequisites must already be verified.

The strategy-change displays are in Equilibrium strategy changes/Tables, with
C#-generated TeX in Sources/Tex and selected-value JSON in Sources/Json. One shared Methodology
and Explanation.md at the workflow root explains all table columns, selection
criteria and interpretation. The planner copies this document from
scripts/templates/equilibrium-strategy-methodology.md and generates the reader's
README/index. Per-table explanatory TXT files are not generated. Frozen copies
of the equilibrium and action-report inputs stay in Sources/Profiles so later
routine report regeneration cannot silently change the calculation inputs.
Full numerical results, requests and provenance records are in Data/cost-N/<contrast>/.
Cost and contrast are in filenames, not artwork titles.
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
retained sensitivity settings, every printed table magnitude in reading order, the six
path endpoints and multiple-start exhibit provenance. `--changes-only` audits
the comparison stage while independent expensive calculations are still running.
PDF numeric verification is combined with visual inspection before manuscript use.

# Fee liability on unilateral exit

The required no-argument Release suite now runs CS004 (134 cases), CS004ME
(two multiple-start cases), and CS006EF (ten new cases). CS006EF can also run
independently, without touching the original plans, reports, or equilibria:

```powershell
dotnet build ACESimDistributedSaturate -c Release
dotnet run --project ACESimDistributedSaturate -c Release --no-build -- preflight --plan exit-fees
dotnet run --project ACESimDistributedSaturate -c Release --no-build -- run --plan exit-fees --processors 4 --hidden-workers --results-directory "C:/Users/Admin/Documents/GitHub/ACESim4/ReportResults/Exit fee extension"
dotnet run --project ACESimDistributedSaturate -c Release --no-build -- aggregate --plan exit-fees --results-directory "C:/Users/Admin/Documents/GitHub/ACESim4/ReportResults/Exit fee extension"
```

Use a fresh directory for a changed source/build. The manifest rejects mixing
commits or binaries. Existing coordinator state supports resuming this exact
plan/build. Copying the old CS004 equilibria is unnecessary for this separate
extension, and their trial-only payoffs must not be reused as extension solutions.

The ten new cases cross costs 0.25, 0.5, 1, 2, and 4 with risk neutrality and
symmetric CARA alpha 2. Signals, the ten-offer grid, prior, cost timing, and
voluntary filing/answering and later exit match their CS004 controls. The fee
multiplier is one. The American-rule controls and trial-only fee-shifting controls
are existing results, not additional extension solves.

On later abandonment the plaintiff reimburses the defendant's incurred expenses;
on later default the defendant reimburses the plaintiff. Initial nonanswer also
requires reimbursement of the plaintiff's incurred (unsaved) filing expense.
Unfiled disputes incur no fees. Settlement uses the existing inclusive transfer
and each party bears its own expenses. Future trial expenses are not incurred or
reimbursed on exit. The existing 50/50 resolution when both parties choose to
quit determines which terminal transfer applies. This is a model of fee shifting
on trial and unilateral exit, not a complete representation of English procedure.

The independently controlled options are `LoserPaysAfterAbandonment` and
`LoserPaysAfterNonAnswer`; both remain false in all original article cases.
CS006EF enables both. Its numerical and signal reports use the same accounting
validation as CS004 and include the additional exit-fee transfers. Its five
specification comparisons compare risk preferences within the exit-fee regime;
they are not comparisons with the old fee trigger. The extension's comparative
charts must identify their original CS004 source rows separately.

Primary reported disposition probabilities and monetary outcomes use all potential
disputes. Conditional rates remain available in the saved diagnostic reports.

Legal motivation: CPR 38.6 (discontinuance costs) and CPR 44.2 (costs discretion).
Buckhannon, 532 U.S. 598, 603–605 (2001), motivates attention to the form of
termination, but does not establish a blanket exemption for pretrial exits.

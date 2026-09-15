# Auxiliary equilibrium mixing (disabled)

The `equilibrium-mixing` command is disabled and exits without calculations.
The supplemental rebuild no longer schedules auxiliary mixing searches or
comparisons based on their alternative profiles. Earlier implementation and
experimental documentation remain in Git history.

Strategy-change tables now use the saved equilibrium profiles directly. Their
native mixed strategies, tie checks, off-path completion checks and residuals
are retained. See [Equilibrium-strategy-changes.md](Equilibrium-strategy-changes.md)
for reproduction and interpretation. The separate multiple-equilibrium search
is unaffected.

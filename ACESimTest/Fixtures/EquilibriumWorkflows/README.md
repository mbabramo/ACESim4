# Article reporting characterization

Captured on 2026-10-28 before changing the production workflow code at commit
607a19ca99f4f45bd214194eb74bfeaeb21eec68 (tag
predictive-cfr-before-refactor-20261028).

The two fixtures use the correlated-signals article default options and the
endogenous precaution model, with two-point grids to keep execution affordable.
Two explicit nonuniform behavioral profiles are passed through the original
ProcessIdentifiedEquilibria pipeline. They are diagnostic policies, not solved
equilibria. Every CSV cell and report suffix is retained except the Seconds
column. The final restored current policy, average policies, and recorded
historical profiles are also retained.

These fixtures check article reporting compatibility, including matched-profile
correlation versus behavioral averaging. They do not establish unchanged
convergence for every production parameter configuration.

Do not regenerate these files merely to make a refactoring test pass. A deliberate
change to the article's behavior requires an explained baseline update.

The follow-up extraction also captured `generated-correlation.json` and
`serialized-base-fields.json` from commit `0e5d8427`, before moving the larger
workflow blocks. The first records generated expressions for both players and
three correlation/best-response modes on a small coordination game. The second
records every serialized field declared on `StrategiesDeveloperBase`, including
its name, type, and visibility. The workflow extraction tests compare these
unchanged fixtures; fields used only for nonserialized service caches are excluded.

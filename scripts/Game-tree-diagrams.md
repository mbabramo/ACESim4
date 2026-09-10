# Regenerating game-tree diagrams

Run Generate-ArticleGameTrees.ps1 from PowerShell with an explicit output directory.
It requires .NET 9, LuaLaTeX, and Poppler's pdftoppm on PATH.

## Correlated-signals article

    .\scripts\Generate-ArticleGameTrees.ps1 -Model CorrelatedSignals -OutputDirectory "C:\path\to\Game tree diagrams"

This initializes the current focused continuous-merits baseline on a small two-signal,
two-offer grid. It writes the six legacy-named PDFs, matching LaTeX sources and .txt explanations, two PNG
previews, and a README documenting the parameters. It does not solve an equilibrium.
Only chance probabilities are printed; player labels identify information sets.
Explanatory prose belongs in the accompanying .txt files, never inside the diagrams.
The full views expand terminal lotteries; the simplified views integrate them out.
Both integrate continuous merits, so their beginning views are identical.

## Endogenous-disputes article

    .\scripts\Generate-ArticleGameTrees.ps1 -Model EndogenousDisputes -OutputDirectory "C:\path\to\Endogenous game tree"

This selects the existing precaution-negligence generator and its original
BeginningOfGame_Collapsed filter. It writes an endogenous-disputes prefix PDF, LaTeX
source, .txt explanation, and PNG. Use a separate output folder for this other article.
The existing endogenous model, option generators, and production launchers are unchanged.
Small accident probabilities retain scientific notation, and small payoff differences
are shown with up to eight decimal places.

For another endogenous view, pass the public LitigGameDefinition.TreeDiagramExclusions
value to EndogenousGameTreeDiagrams.GenerateAsync: FullDiagram, BeginningOfGame,
BeginningOfGame_Collapsed, MiddleOfGame, or EndOfGame. Large full trees may exceed
LaTeX's page-size limits; the selected prefix is the readable default.

## Existing report switches

EvolutionSettings.PrintGameTree and PrintedGameTreeIncludesInformationSetData still
select TikZ output during normal reporting. That path now saves GameTree.tex with the
normal report/option-set prefix instead of discarding the generated string.
LitigGameDefinition.Exclusions is now public, retaining its existing endogenous default.
The correlated-signals export supplies separate filters; it does not overwrite those
endogenous settings.

The diagram renderer supports repeatable full/subtree views, unique node identifiers,
chance-only versus behavioral probability labels, and configurable payoff precision.
The command-line modes only initialize and traverse the small game. They do not change
production settings, write equilibrium files, or overwrite production reports.

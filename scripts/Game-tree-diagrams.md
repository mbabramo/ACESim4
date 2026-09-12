# Regenerating article diagrams

The workflow is now in the existing C# **LitigCharts** project. See
[LitigCharts/README.md](../LitigCharts/README.md) for commands and configuration.
The former PowerShell wrappers and independently maintained LaTeX template have
been retired. C# owns both numerical binding and the worked-path layout.

## Structural game trees

The `game-trees` target initializes the current focused continuous-merits
baseline on a two-signal, two-offer grid and generates five views. It does not
solve an equilibrium. Only chance probabilities are printed; player labels
identify information sets. Explanations are written to matching .txt files.
The simplified views integrate terminal lotteries; only one beginning view is
generated because simplifying the ending does not change the beginning.

The `endogenous` target selects the existing precaution-negligence generator
and its original BeginningOfGame_Collapsed filter. It is excluded from `all`.
Other views remain available through
`EndogenousGameTreeDiagrams.GenerateAsync(TreeDiagramExclusions)`.
The endogenous model, option generators, and production launchers are unchanged.

## Worked path

The request JSON names an option set, saved equilibrium, matching
InformationSetActions report, one-based equilibrium number, and ordered paths.
Each path lists decision enum names and one-based actions. Input paths resolve
relative to the request file. Invalid, partial, overlong and ambiguous histories
are rejected. Both source files are hashed; every action-report row must match
the reconstructed information sets and numerical results.

Conditional action utilities average over the acting player's information set
with the saved profile thereafter. Replaying a history does not change that
profile. Terminal monetary accounting remains separate from solver-rounded
utility. The JSON also records history and information-set reach probabilities,
off-path and fallback flags, and replayed terminal outcomes.

`LitigCharts/WorkedPathDiagram.cs` owns the deliberately arranged layout for
the selected baseline example. `ArticleWorkedPathLatexData.cs` validates its
assumptions and binds extracted numbers. The output .tex is self-contained;
it is not another project or a hand-maintained input. Revise the request to
change histories; revise these C# classes to change the drawing. Explanatory
prose belongs in the accompanying .txt, not inside the PDF.

The older model-console `--extract-article-paths <request.json> <output.json>`
remains available for extraction without chart generation. New workflows should
use `LitigCharts diagrams worked-path-data`. The old values-only console switch
now reports the replacement command rather than falling through to production.

## Existing reporting APIs

EvolutionSettings.PrintGameTree and PrintedGameTreeIncludesInformationSetData
still select TikZ output during normal reporting. That path saves GameTree.tex
with the normal report/option-set prefix. LitigGameDefinition.Exclusions retains
its endogenous default; the correlated-signals exporter uses separate filters.
The new commands do not require changing these switches.

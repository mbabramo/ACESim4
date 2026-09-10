using ACESim;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.GameSolvingSupport.Settings;
using System.IO;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    public static class EndogenousGameTreeDiagrams
    {
        public const string Description =
            "Endogenous-disputes model: small precaution-negligence illustration. " +
            "Existing endogenous view filters; chance probabilities only, not an equilibrium.";

        public static async Task<string> GenerateAsync(
            LitigGameDefinition.TreeDiagramExclusions view =
                LitigGameDefinition.TreeDiagramExclusions.BeginningOfGame_Collapsed)
        {
            // Use the original endogenous-disputes generator and its existing view filters.
            // This is its small structural illustration, not the production option matrix.
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(
                new PrecautionNegligenceOptionsGeneratorSettings
                {
                    UseSimplifiedPrecautionNegligenceGame = true,
                });
            var settings = new EvolutionSettings
            {
                Algorithm = GameApproximationAlgorithm.GeneralizedVanilla,
                ParallelOptimization = false,
                CreateEFGFile = false,
                CreateEquilibriaFile = false,
                GenerateManualReports = false,
                GenerateReportsByPlaying = false,
            };
            var launcher = new LitigGameEndogenousDisputesLauncher();
            var developer = (StrategiesDeveloperBase)await launcher.GetInitializedDevelper(
                options, "Endogenous disputes illustrative game", settings);
            var definition = (LitigGameDefinition)developer.GameDefinition;
            definition.Exclusions = view;
            var (exclude, include) = definition.GetTreeDiagramExclusions();
            return ArticleGameTreeDiagrams.Collect(developer).GenerateTikzDiagram(
                exclude, include, false, payoffDecimalPlaces: 8);
        }

        public static async Task WriteSourceAsync(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "endogenous disputes beginning.tex"),
                await GenerateAsync());
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "endogenous disputes beginning.txt"),
                Description + "\n");
        }
    }
}

using ACESim;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using System;

namespace LitigCharts
{
    public static class Runner
    {
        public enum DataBeingAnalyzed
        {
            CorrelatedSignalsArticle,
            EndogenousDisputesArticle
        }

        public static void ProcessLitigationGameData(DataBeingAnalyzed article)
        {
            DataProcessingBase.singleEquilibriumOnly = article == DataBeingAnalyzed.EndogenousDisputesArticle;

            bool useVirtualizedFileSystemForIndividualDiagrams = false; // this is for testing purposes -- it doesn't generate any diagrams
            bool buildMainReport = true; // this looks at all of the csv files containing the report outputs (e.g., Report Name.csv where there is only one equilibrium, or "-eq1", "-eq2", "-Avg", etc.), and then aggregates all of the information on the report outputs for each simulation into a CSV file, including both All cases and separate rows for various subsets of cases. Set this to false only if it has already been done. 
            bool printIndividualLatexDiagrams = true; // this is the time consuming one -- it applies to the heat map and offers diagrams for each individual equilibrium
            bool doDeletion = printIndividualLatexDiagrams; // don't delete if we haven't done the diagrams yet
            bool organizeIntoFolders = true;
            bool printAggregatedDiagrams = true;
            bool printAggregatedCostBreakdown = true;

            if (OneTimeDiagrams())
                return; // if we did the one-time diagrams, we won't do any of the rest of the processing

            LitigGameLauncherBase launcher = article switch
            {
                DataBeingAnalyzed.CorrelatedSignalsArticle => new LitigGameCorrelatedSignalsArticleLauncher(),
                DataBeingAnalyzed.EndogenousDisputesArticle => new LitigGameEndogenousDisputesLauncher(),
                _ => throw new NotImplementedException()
            };

            DataProcessingBase.VirtualizableFileSystem = new ACESimBase.Util.Serialization.VirtualizableFileSystem(launcher.GetReportFolder(), !useVirtualizedFileSystemForIndividualDiagrams);

            if (buildMainReport)
            {
                FeeShiftingDataProcessing.BuildMainReport(launcher, article);
                FeeShiftingDataProcessing.BuildCombinedCostBreakdownReport(launcher);
            }
            if (printIndividualLatexDiagrams)
                FeeShiftingDataProcessing.ProduceLatexDiagramsFromTexFiles(launcher, article); // this code assumes that all data (including the .tex files) are in the ReportResults folder, so must do before organization
            if (organizeIntoFolders)
                FeeShiftingDataProcessing.OrganizeIndividualSimulationsIntoFolders(launcher, doDeletion, FeeShiftingDataProcessing.GetFilePlacementRules(article)); // now we organize, including the diagrams just made
            if (printAggregatedDiagrams)
            {
                if (useVirtualizedFileSystemForIndividualDiagrams)
                    DataProcessingBase.VirtualizableFileSystem = new VirtualizableFileSystem(launcher.GetReportFolder(), true);
                FeeShiftingDataProcessing.ProduceLatexDiagramsAggregatingReports(launcher, article); // now we produce diagrams that aggregate info from multiple reports
            }
            if (printAggregatedCostBreakdown)
            {
                FeeShiftingDataProcessing.ProduceRepeatedCostBreakdownReports(launcher, article);
            }

            ////FeeShiftingDataProcessing.BuildOffersReport(); // we're no longer generating the offers data in csv, since we're directly generating a Latex file with the heatmap

            //FeeShiftingDataProcessing.ExampleLatexDiagramsAggregatingReports();
        }

        private static bool OneTimeDiagrams()
        {
            bool doCoefficientOfVariationCalculations = false; // if so, only this will be run. Note that this will generally be useful when there are many equilibria for a single set of parameters, not when there are many different sets of parameters.
            bool doSignalsDiagram = false;  // if so, only this will be run. This is the figure that shows how truly liable or not truly liable cases convert to litigation quality levels and how litigation quality levels convert to signals for the players.
            bool doProbitLiabilityDiagram = false; // same. This is the figure for endogenous disputes that shows for each signal (different curves), and each relative precaution level (x axis), what the probability of liability is (integrating over all hidden states).

            if (doCoefficientOfVariationCalculations)
            {
                FeeShiftingDataProcessing.CalculateAverageCoefficientOfVariation();
                return true;
            }

            if (doSignalsDiagram)
            {
                SignalsDiagram diagram = new SignalsDiagram();
                string texCode = diagram.CreateDiagram();
                return true;
            }

            if (doProbitLiabilityDiagram)
            {
                string texCode = ProbitLiabilityTikzGenerator.BuildStandaloneBaseline();
                string texCodeDeterministic = ProbitLiabilityTikzGenerator.BuildStandaloneBaseline(decisionRule: CourtDecisionRule.DeterministicThreshold);
                return true;
            }

            return false;
        }



        // This is legacy code for an older article.
        public static void AdditiveEvidenceArticle()
        {
            AdditiveEvidenceDataProcessing.GenerateCSV();

            AdditiveEvidenceDataProcessing.GenerateDiagramsFromCSV();

            FeeShiftingDataProcessing.ExecuteLatexProcessesForExisting(); // executes for existing .tex files rather than generating the .tex files

            AdditiveEvidenceDataProcessing.OrganizeIntoFolders();
        }
    }
}

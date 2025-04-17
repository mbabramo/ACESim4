namespace LitigCharts
{
    public static class Runner
    { 

        public static void AdditiveEvidenceArticle()
        {
            AdditiveEvidenceDataProcessing.GenerateCSV();

            AdditiveEvidenceDataProcessing.GenerateDiagramsFromCSV();
            
            FeeShiftingDataProcessing.ExecuteLatexProcessesForExisting(); // executes for existing .tex files rather than generating the .tex files

            AdditiveEvidenceDataProcessing.OrganizeIntoFolders();
        }

        public static void FeeShiftingArticle()
        {
            if (OneTimeDiagrams())
                return; // if we did the one-time diagrams, we won't do any of the rest of the processing

            bool buildMainFeeShiftingReport = true; // this looks at all of the csv files containing the report outputs (e.g., Report Name.csv where there is only one equilibrium, or "-eq1", "-eq2", "-Avg", etc.), and then aggregates all of the information on the report outputs for each simulation into a CSV file, including both All cases and separate rows for various subsets of cases. Set this to false only if it has already been done. 
            bool printIndividualLatexDiagrams = true; // this is the time consuming one -- it applies to the heat map and offers diagrams for each individual equilibrium
            bool doDeletion = printIndividualLatexDiagrams; // don't delete if we haven't done the diagrams yet
            bool organizeIntoFolders = true;
            bool printAggregatedDiagrams = true;

            if (buildMainFeeShiftingReport)
                FeeShiftingDataProcessing.BuildMainFeeShiftingReport();
            if (printIndividualLatexDiagrams)
                FeeShiftingDataProcessing.ProduceLatexDiagramsFromTexFiles(); // this code assumes that all data (including the .tex files) are in the ReportResults folder, so must do before organization
            if (organizeIntoFolders)
                FeeShiftingDataProcessing.OrganizeIntoFolders(doDeletion); // now we organize, including the diagrams just made
            if (printAggregatedDiagrams)
                FeeShiftingDataProcessing.ProduceLatexDiagramsAggregatingReports(); // now we produce diagrams that aggregate info from multiple reports

            ////FeeShiftingDataProcessing.BuildOffersReport(); // we're no longer generating the offers data in csv, since we're directly generating a Latex file with the heatmap

            //FeeShiftingDataProcessing.ExampleLatexDiagramsAggregatingReports();
        }

        private static bool OneTimeDiagrams()
        {
            bool doCoefficientOfVariationCalculations = false; // if so, only this will be run. Note that this will generally be useful when there are many equilibria for a single set of parameters, not when there are many different sets of parameters.
            bool doSignalsDiagram = false; // if so, only this will be run. This is the figure that shows how truly liable or not truly liable cases convert to litigation quality levels and how litigation quality levels convert to signals for the players.

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

            return false;
        }
    }
}

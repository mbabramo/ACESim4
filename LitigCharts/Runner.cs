namespace LitigCharts
{
    public static class Runner
    { 

        public static void AdditiveEvidenceArticle()
        {
            AdditiveEvidenceDataProcessing.BuildReport();

            AdditiveEvidenceDataProcessing.GenerateDiagramsFromCSV();
            
            FeeShiftingDataProcessing.ExecuteLatexProcessesForExisting(); // executes for existing .tex files rather than generating the .tex files
        }

        private static void FeeShiftingArticle()
        {
            bool doSignalsDiagram = false; // if so, only this will be run
            bool printIndividualLatexDiagrams = true; // this is the time consuming one
            bool doDeletion = printIndividualLatexDiagrams; // don't delete if we haven't done the diagrams yet
            bool organizeIntoFolders = true;
            bool printAggregatedDiagrams = true;

            if (doSignalsDiagram)
            {
                SignalsDiagram diagram = new SignalsDiagram();
                string texCode = diagram.CreateDiagram();
                return;
            }

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
    }
}

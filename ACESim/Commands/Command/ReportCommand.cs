using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Path = System.IO.Path;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace ACESim
{
    [Serializable]
    public class ReportCommand : Command
    {

        public string reportName;
        public string storeIterationsResultsFile;
        public string storeReportResultsFile;
        public List<RowOrColumnGroup> rowsGroups;
        public List<RowOrColumnGroup> colsGroups;
        public List<PointChartReport> pointChartReports;
        private string baseOutputDirectory;
        public bool parallelReporting;
        public List<GameProgressReportable> theOutputs;
        public bool isInterimReport;
        public bool requireCumulativeDistributions;

        public ReportCommand(
            MultiPartCommand theMultipartCommand,
            string theReportName,
            string theStoreIterationsResultsFile,
            string theStoreReportResultsFile,
            List<RowOrColumnGroup> theRowsGroups,
            List<RowOrColumnGroup> theColsGroups,
            List<PointChartReport> thePointChartReports,
            string theBaseOutputDirectory,
            bool theRequireCumulativeDistributions,
            bool theParallelReporting)
            : base(theMultipartCommand)
        {
            CommandDifficulty = 10;
            reportName = theReportName;
            storeIterationsResultsFile = theStoreIterationsResultsFile;
            storeReportResultsFile = theStoreReportResultsFile;
            rowsGroups = theRowsGroups;
            colsGroups = theColsGroups;
            pointChartReports = thePointChartReports;
            baseOutputDirectory = theBaseOutputDirectory;
            parallelReporting = theParallelReporting;
            requireCumulativeDistributions = theRequireCumulativeDistributions;
        }

        public override void Execute(SimulationInteraction simulationInteraction)
        {
            storeReportResultsFile = storeReportResultsFile.Trim();
            storeIterationsResultsFile = storeIterationsResultsFile.Trim();
            var originalStoredSettingsInfo = storedSettingsInfo;
            storedSettingsInfo = null;
            if (theOutputs == null)
                theOutputs = ((ConcurrentBag<GameProgressReportable>)BinarySerialization.GetSerializedObject(
                    Path.Combine(baseOutputDirectory, SimulationInteraction.iterationsSubdirectory, storeIterationsResultsFile))).ToList();
            DateTime currentTime = DateTime.Now;
            string currentTimeString = currentTime.ToShortDateString() + " " + currentTime.ToShortTimeString();
            Report theReport = new Report(storeReportResultsFile, simulationInteraction.namesOfVariableSetsChosen, currentTimeString, rowsGroups, colsGroups, pointChartReports, theOutputs, parallelReporting, simulationInteraction);
            string reportString = theReport.ToString();
            TextFileCreate.CreateTextFile(
                Path.Combine(baseOutputDirectory, SimulationInteraction.reportsSubdirectory, storeReportResultsFile),
                reportString);
            bool skipReportingForInterimReport = true;
            if (!skipReportingForInterimReport || !isInterimReport)
            {
                bool addToDatabase = true; 
                bool addToMetareport = false; 
                if (addToDatabase)
                    theReport.AddToDatabase(simulationInteraction.namesOfVariableSets, simulationInteraction.namesOfVariableSetsChosen, CommandSetStartTime);
                if (addToMetareport)
                    theReport.AddToMetareport(simulationInteraction.metaReport, simulationInteraction.namesOfVariableSets);
                try
                {
                    DateTime startTime = simulationInteraction.StartTime;
                    TextFileCreate.CreateTextFile(
                        Path.Combine(baseOutputDirectory, SimulationInteraction.reportsSubdirectory, "MetaReport " + startTime.ToString("yyyy-MM-dd hh-mm-ss") + ".csv"),
                        simulationInteraction.metaReport.ToString()
                        );
                }
                catch
                {
                }
            }
            Debug.WriteLine("-------------------------------------------");
            Debug.WriteLine("Report: " + storeReportResultsFile);
            Debug.WriteLine("-------------------------------------------");
            Debug.WriteLine(reportString);

            if (!isInterimReport)
            {
                simulationInteraction.ExportAll2DCharts();
                simulationInteraction.CloseAllCharts();
            }
            else
                storedSettingsInfo = originalStoredSettingsInfo;
            // XMLSerialization.SerializeObject(storeReportResultsFile, theReport);
        }
    }
}

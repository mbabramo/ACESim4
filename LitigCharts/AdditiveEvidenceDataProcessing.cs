using ACESim;
using ACESimBase.Games.AdditiveEvidenceGame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LitigCharts.DataProcessingUtils;

namespace LitigCharts
{
    public static class AdditiveEvidenceDataProcessing
    {

        static string filePrefix => new AdditiveEvidenceGameLauncher().MasterReportNameForDistributedProcessing + "-";
        
        public static void BuildReport()
        {
            List<string> rowsToGet = new List<string>() { "All", "Settles", "Trial", "Shifting",   };
            List<string> replacementRowNames = rowsToGet.ToList();
            List<string> columnsToGet = new List<string>() { "All", "PQuits", "DQuits", "Settles", "Trial", "Shifting", "ShiftingOccursIfTrial", "ShiftingValueIfTrial", "POffer", "DOffer", "AccSq", "Accuracy", "Accuracy_ForPlaintiff", "Accuracy_ForDefendant", "SettlementOrJudgment", "TrialValuePreShiftingIfOccurs", "TrialValueWithShiftingIfOccurs", "ResolutionValueIncludingShiftedAmount", "SettlementValue", "PWelfare", "DWelfare", "PBestGuess", "DBestGuess", "TrialCost", "FeeShifting", "FeeShiftingThreshold", "Alpha_Plaintiff_Quality", "Alpha_Plaintiff_Bias" };
            List<string> replacementColumnNames = columnsToGet.ToList();
            string endOfFileName = "";

            bool onlyAllFilter = false;
            if (onlyAllFilter)
            {
                rowsToGet = rowsToGet.Take(1).ToList();
                replacementRowNames = replacementRowNames.Take(1).ToList();
            }

            AdditiveEvidenceGameLauncher launcher = new AdditiveEvidenceGameLauncher();
            var gameOptionsSets = launcher.GetOptionsSets();
            var map = launcher.GetAdditiveEvidenceNameMap();
            string path = Launcher.ReportFolder();
            string outputFileFullPath = Path.Combine(path, filePrefix + $"-{endOfFileName}.csv");
            var distinctOptionSets = gameOptionsSets.DistinctBy(x => map[x.Name]).ToList();

            bool includeHeader = true;
            List<List<string>> outputLines = GetCSVLines(distinctOptionSets, map, rowsToGet, replacementRowNames, filePrefix, ".csv", "", path, includeHeader, columnsToGet, replacementColumnNames);

            string result = MakeString(outputLines);
            TextFileManage.CreateTextFile(outputFileFullPath, result);
        }
    }
}

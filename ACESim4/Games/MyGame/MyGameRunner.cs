using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class MyGameRunner
    {
        public static MyGameProgress PlayMyGameOnce(MyGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
            MyGameProgress gameProgress = (MyGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }

        // DEBUG TODO: if we are using the tree method, then we shouldn't need to reset to do multiple games.

        public static void EvolveMyGame()
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            var options = MyGameOptionsGenerator.Standard(); 
            //var options = MyGameOptionsGenerator.UsingRawSignals_10Points_1Round();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);
            EvolutionSettings evolutionSettings = new EvolutionSettings()
            {
                MaxParallelDepth = 1, // we're parallelizing on the iteration level, so there is no need for further parallelization
                ParallelOptimization = false,

                InitialRandomSeed = 100,

                Algorithm = GameApproximationAlgorithm.AbramowiczProbing,

                ReportEveryNIterations = 10_000,
                NumRandomIterationsForSummaryTable = 5000,
                PrintSummaryTable = true,
                PrintInformationSets = false,
                RestrictToTheseInformationSets = null, // new List<int>() {16},
                PrintGameTree = false,
                AlwaysUseAverageStrategyInReporting = false,
                BestResponseEveryMIterations = EvolutionSettings.EffectivelyNever, // should probably set above to TRUE for calculating best response, and only do this for relatively simple games

                TotalProbingCFRIterations = 10_000,
                EpsilonForMainPlayer = 0.5,
                EpsilonForOpponentWhenExploring = 0.05,
                MinBackupRegretsTrigger = 3,
                TriggerIncreaseOverTime = 0,

                TotalAvgStrategySamplingCFRIterations = 10000000,
                TotalVanillaCFRIterations = 100_000_000,
            };
            NWayTreeStorageRoot<IGameState>.EnableUseDictionary = false; // DEBUG evolutionSettings.ParallelOptimization == false; // this is based on some limited performance testing; with parallelism, this seems to slow us down. Maybe it's not worth using. It might just be because of the lock.
            NWayTreeStorageRoot<IGameState>.ParallelEnabled = evolutionSettings.ParallelOptimization;
            const int numRepetitions = 2;
            string cumulativeReport = "";
            int numMainLines = 0;
            for (int i = 0; i < numRepetitions; i++)
            {
                string reportName = "Report";
                string reportIteration = i.ToString();
                CounterfactualRegretMaximization developer =
                    new CounterfactualRegretMaximization(starterStrategies, evolutionSettings, gameDefinition);
                string report = developer.DevelopStrategies();
                string differentiatedReport = DifferentiatedReport(report, reportName, reportIteration, i == 0, out numMainLines);
                cumulativeReport += differentiatedReport;
            }
            Debug.WriteLine(cumulativeReport);
            var aggregatedLines = GetAggregatedLines(cumulativeReport, numMainLines, numRepetitions);
            
        }

        private static string DifferentiatedReport(string report, string reportName, string reportIteration, bool includeFirst, out int numMainLines)
        {
            StringBuilder sb = new StringBuilder();
            using (StringReader reader = new StringReader(report))
            {
                string line;
                bool isFirst = true;
                numMainLines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirst && !includeFirst)
                    {
                        isFirst = false;
                        continue;
                    }
                    string lineWithPreface;
                    if (isFirst)
                        lineWithPreface = $"\"Report\",\"Iteration\",\"LineNum\",{line}";
                    else
                        lineWithPreface = $"\"{reportName}\",{reportIteration},{numMainLines},{line}";
                    sb.Append(lineWithPreface);
                    sb.AppendLine();
                    if (!isFirst)
                        numMainLines++;
                    isFirst = false;
                }
            }
            string differentiatedReport = sb.ToString();
            return differentiatedReport;
        }



        private static Dictionary<string, List<(string theString, double? theValue)>> GetValuesForEachVariable(string cumulativeReport)
        {
            (string headerLine, List<string> otherLines) = GetHeaderLineAndOtherLines(cumulativeReport);
            List<string> variableNames = headerLine.Split(',').ToList();
            var listOfDictionaries = GetLinesAsDictionaries(variableNames, otherLines);
            problem; // Now we need to get each header item and narrow down from that
            var dictionaryOfLists = ConvertToDictionaryOfLists(listOfDictionaries);
            foreach (string variableName in variableNames)
            {
                var itemsForVariable = dictionaryOfLists[variableName];
                if (itemsForVariable.All(x => x.theString == "" || x.theValue != null))
                {
                    var numericItems = itemsForVariable.Where(x => x.theValue != null).Select(x => (double) x.theValue).ToList();
                    var aggregation = numericItems.Average();
                }
            }
            return dictionaryOfLists;
        }

        private static (string, List<string>) GetHeaderLineAndOtherLines(string csvString)
        {
            List<string> lines = GetLinesAsStrings(csvString);
            return (lines.First(), lines.Skip(1).ToList());
        }

        private static List<string> GetLinesAsStrings(string csvString)
        {
            List<string> lines = new List<string>();
            using (StringReader reader = new StringReader(csvString))
            {
                string line = string.Empty;
                do
                {
                    line = reader.ReadLine();
                    if (line != null)
                    {
                        lines.Add(line);
                    }

                } while (line != null);
                return lines;
            }
        }

        private static Dictionary<T, List<U>> ConvertToDictionaryOfLists<T,U>(List<Dictionary<T, U>> listOfDictionaries)
        {
            Dictionary<T, List<U>> outerDict = new Dictionary<T, List<U>>();
            foreach (var key in listOfDictionaries.SelectMany(x => x.Keys).Distinct())
            {
                List<U> innerList = new List<U>();
                foreach (Dictionary<T, U> innerDict in listOfDictionaries)
                {
                    foreach (var keyValuePair in innerDict)
                    {
                        if (keyValuePair.Key.Equals(key))
                            innerList.Add(keyValuePair.Value)
                    }
                }
                outerDict[key] = innerList;
            }
            return outerDict;
        }

        private static List<Dictionary<string, (string theString, double? theValue)>> GetLinesAsDictionaries(List<string> variableNames, List<string> linesToParse)
        {
            List<Dictionary<string, (string theString, double? theValue)>> list = new List<Dictionary<string, (string theString, double? theValue)>>();
            foreach (string line in linesToParse)
            {
                string[] parsed = line.Split(',');
                Dictionary<string, (string theString, double? theValue)> dict = new Dictionary<string, (string theString, double? theValue)>();
                for (int i = 0; i < parsed.Length; i++)
                {
                    string variableName = variableNames[i];
                    string value = parsed[i];
                    bool parsable = double.TryParse(value, out double parseResult);
                    dict[variableName] = parsable ? (value, (double?) parseResult) : (value, null);
                }
                list.Add(dict);
            }
            return list;
        }

        private static List<List<string>> GetAggregatedLines(string cumulativeReport, int numLines, int numReports)
        {
            List<List<string>> list = new List<List<string>>();
            for (int i = 0; i < numLines; i++)
                list.Add(GetLinesByNumber(cumulativeReport, i, numLines, numReports));
            return list;
        }

        private static List<string> GetLinesByNumber(string cumulativeReport, int lineNumber, int numLines, int numReports)
        {
            List<string> stringList = new List<string>();
            string headerLine = null;
            using (StringReader reader = new StringReader(cumulativeReport))
            {
                headerLine = reader.ReadLine();
                for (int r = 0; r < numReports; r++)
                for (int l = 0; l < numLines; l++)
                {
                    string theLine = reader.ReadLine();
                    if (l == lineNumber)
                        stringList.Add(theLine);
                }
            }
            return stringList;
        }
    }
}

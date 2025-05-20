using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LitigCharts.DataProcessingUtils;

namespace LitigCharts
{
    public class DataProcessingBase
    {
        public static string filePrefix(PermutationalLauncher launcher) => launcher.ReportPrefix + "-";


        public const string correlatedEquilibriumFileSuffix = "-Corr";
        public const string averageEquilibriumFileSuffix = "-Avg";
        public const string firstEquilibriumFileSuffix = "-Eq1";
        public const string onlyEquilibriumFileSuffix = "";
        public static string firstOrOnlyEquilibriumFileSuffix => firstEqOnly ? onlyEquilibriumFileSuffix : firstEquilibriumFileSuffix;
        public static string firstOrOnlyEquilibriumTypeWord => firstEqOnly ? "Only Eq" : "First Eq";

        public static string[] equilibriumTypeSuffixes_Major = new string[] { correlatedEquilibriumFileSuffix, averageEquilibriumFileSuffix, firstEquilibriumFileSuffix };
        public static string[] equilibriumTypeSuffixes_One = new string[] { firstOrOnlyEquilibriumFileSuffix };
        public static string[] equilibriumTypeSuffixes_AllIndividual = Enumerable.Range(1, 100).Select(x => $"-Eq{x}").ToArray();
        public static string[] equilibriumTypeWords_Major = new string[] { "Correlated", "Average", "First Eq" };
        public static string[] equilibriumTypeWords_One = new string[] { firstOrOnlyEquilibriumTypeWord };
        public static string[] equilibriumTypeWords_AllIndividual = Enumerable.Range(1, 100).Select(x => $"Eq{x}").ToArray();

        public static bool firstEqOnly => false; // if true, then only the first equilibrium is used. If false, then all equilibria are used (e.g., -Corr, etc.).
        public static bool allIndividual = false; // create a diagram for each individual equilibrium (e.g., 1 to 100).
        public static string[] eqToRun => allIndividual ? equilibriumTypeWords_AllIndividual : (firstEqOnly ? equilibriumTypeWords_One : equilibriumTypeWords_Major);
        public static string[] equilibriumTypeSuffixes => allIndividual ? equilibriumTypeSuffixes_AllIndividual : (firstEqOnly ? equilibriumTypeSuffixes_One : equilibriumTypeSuffixes_Major);

        public static List<Process> ProcessesList = new List<Process>();
        public static bool UseParallel = true;
        public static int maxProcesses = UseParallel ? Environment.ProcessorCount : 1;
        public static bool avoidProcessingIfPDFExists = true;

        #region Process Management


        static void CleanupCompletedProcesses(bool killStaleProcesses)
        {
            ProcessesList = ProcessesList.Where(x => !x.HasExited).ToList();

            TimeSpan maxTimeAllowed = TimeSpan.FromMinutes(2);
            var expiredList = ProcessesList.Where(x => x.StartTime + maxTimeAllowed < DateTime.Now).ToList();
            foreach (var expired in expiredList)
            {
                // terminate the process
                expired.Kill();
                ProcessesList.Remove(expired);
                TabbedText.WriteLine($"Terminated process {expired.StartInfo.Arguments}");
            }

            var remaining = ProcessesList.Select(x => x.StartInfo.Arguments.ToString()).ToList();
            string remainingList = String.Join("\n", remaining);
            Task.Delay(1000);
        }

        public static void WaitForProcessesToFinish()
        {
            while (ProcessesList.Any())
            {
                CleanupCompletedProcesses(true);
            }
        }

        public static void WaitUntilFewerThanMaxProcessesAreRunning()
        {
            while (ProcessesList.Count() >= maxProcesses)
            {
                Task.Delay(100);
                CleanupCompletedProcesses(true);
            }
        }

        #endregion

        #region Latex
        internal static void ExecuteLatexProcessesForExisting(Func<string, bool> includeResultFunc = null)
        {
            string path = Launcher.ReportFolder();
            string[] results = Directory.GetFiles(path);
            foreach (var result in results)
            {
                if (result.EndsWith(".tex") && (includeResultFunc == null || includeResultFunc(result)))
                {
                    TabbedText.WriteLine($"Launching {result}");
                    ExecuteLatexProcess(path, result);
                }
            }
            WaitForProcessesToFinish();
            results = Directory.GetFiles(path);
            string[] toDelete = new string[] { ".aux", ".log", ".synctex.gz" };
            foreach (var result in results)
            {
                if (toDelete.Any(d => result.EndsWith(d)))
                {
                    File.Delete(result);
                }
            }
        }


        internal static List<(string path, string combinedPath, string optionSetName, string fileSuffix)> GetLatexProcessPlans(PermutationalLauncher launcher, IEnumerable<string> fileSuffixes)
        {
            // combine lists from GetLatexProcessPaths for each fileSuffix
            List<(string path, string combinedPath, string optionSetName, string fileSuffix)> result = new();
            foreach (var fileSuffix in fileSuffixes)
            {
                var processPlans = GetLatexProcessPlans(launcher, fileSuffix);
                foreach (var processPlan in processPlans)
                {
                    result.Add(processPlan);
                }
            }
            return result;
        }

        internal static List<(string path, string combinedPath, string optionSetName, string fileSuffix)> GetLatexProcessPlans(PermutationalLauncher launcher, string fileSuffix)
        {
            List<(string path, string combinedPath, string optionSetName, string fileSuffix)> result = new();
            List<GameOptions> optionSets = launcher.GetOptionsSets();
            string path = Launcher.ReportFolder();

            var map = launcher.NameMap; // name to find (avoids redundancies)

            foreach (var gameOptionsSet in optionSets)
            {
                string filenameCore, combinedPath;
                bool processingNeeded = true;
                if (avoidProcessingIfPDFExists)
                {
                    string fileSuffixCopy = fileSuffix;
                    GetFileInfo(map, filePrefix(launcher), ".pdf", firstEquilibriumFileSuffix, ref fileSuffixCopy, path, gameOptionsSet, out filenameCore, out combinedPath, out bool exists);
                    if (File.Exists(combinedPath))
                        processingNeeded = false;
                }
                if (processingNeeded)
                {
                    GetFileInfo(map, filePrefix(launcher), ".tex", firstEquilibriumFileSuffix, ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath, out bool exists);
                    if (!File.Exists(combinedPath))
                    {
                        if (File.Exists(combinedPath.Replace("-Eq1", "")))
                            combinedPath = combinedPath.Replace("-Eq1", "");
                        else
                            throw new Exception("File not found: " + combinedPath);
                    }
                    result.Add((path, combinedPath, gameOptionsSet.Name, fileSuffix));
                }
            }
            return result;
        }

        internal static void ProduceLatexDiagrams(List<(string path, string combinedPath, string optionSetName, string fileSuffix)> processPlans)
        {
            WaitForProcessesToFinish();

            int numToDo = processPlans.Count;
            int numLaunched = 0;

            foreach (var processPlan in processPlans)
            {
                string path = processPlan.path;
                string combinedPath = processPlan.combinedPath;
                string optionSetName = processPlan.optionSetName;
                string fileSuffix = processPlan.fileSuffix;
                TabbedText.WriteLine($"Launching {numLaunched + 1} of {numToDo}");
                TabbedText.WriteLine($"{optionSetName}{fileSuffix}"); // separate line for easy sorting afterwards
                ExecuteLatexProcess(path, combinedPath);
                numLaunched++;
            }

            WaitForProcessesToFinish();

            foreach (var processPlan in processPlans)
            {
                int failures = 0;
            retry:
                try
                {
                    foreach (string suffix in new string[] { $"{processPlan.fileSuffix}.aux", $"{processPlan.fileSuffix}.log", ".synctex.gz" })
                    {
                        string fileToDelete = Path.Combine(processPlan.path, processPlan.optionSetName + suffix);
                        if (File.Exists(fileToDelete))
                            File.Delete(fileToDelete);
                    }
                    failures = 0;
                }
                catch
                {
                    Task.Delay(1000);
                    failures++;
                    if (failures < 5)
                        goto retry;
                }
            }
        }

        public static void ExecuteLatexProcess(string path, string combinedPath)
        {
            WaitUntilFewerThanMaxProcessesAreRunning();

            string texFileInQuotes = $"\"{combinedPath}\"";
            string outputDirectoryInQuotes = $"\"{path}\"";
            bool backupComputer = false;
            string programName = "lualatex"; // "pdflatex";
            string extraHyphen = programName == "lualatex" ? "-" : "";
            string directory = backupComputer ? @$"C:\Program Files\MiKTeX 2.9\miktex\bin\x64" : @$"C:\Users\Admin\AppData\Local\Programs\MiKTeX\miktex\bin\x64";
            string pdflatexProgram = @$"{directory}\{programName}.exe"; // NOTE: pdflatex was original
            string arguments = @$"{texFileInQuotes} {extraHyphen}-output-directory={outputDirectoryInQuotes}";

            ProcessStartInfo processStartInfo = new ProcessStartInfo(pdflatexProgram)
            {
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process theProcess = new Process();
            theProcess.StartInfo = processStartInfo;

            StringBuilder output = new StringBuilder();
            StringBuilder errorOutput = new StringBuilder();

            //processOutput[theProcess] = output;

            theProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    //processOutput[(Process)sender].AppendLine(e.Data);
                }
            };

            theProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data); // Optionally capture errors
                }
            };


            var handle = Process.GetCurrentProcess().MainWindowHandle;
            theProcess.Start();
            theProcess.BeginOutputReadLine();
            theProcess.BeginErrorReadLine(); // Ensure error output is also captured
            ProcessesList.Add(theProcess);
        }

        #endregion

        #region File organization



        public static void OrganizeIntoFolders(PermutationalLauncher launcher, bool doDeletion, List<(string folderName, string[] extensions)> placementRules)
        {
            string reportFolder, individualResultsRoot;
            PrepareFolders(out reportFolder, out individualResultsRoot);

            var map = launcher.NameMap;
            string masterReportName = launcher.ReportPrefix;
            var allOptionSets = launcher.GetOptionsSets();

            Dictionary<string, List<string>> grouped = launcher.GroupOptionSetsByClassification();
            var optionNameToOptionSet = allOptionSets.ToDictionary(opt => opt.Name);

            foreach (var (groupName, optionSetNames) in grouped)
            {
                string simulationFolder = Path.Combine(individualResultsRoot, groupName);
                Directory.CreateDirectory(simulationFolder);

                foreach (var (folderName, _) in placementRules)
                    Directory.CreateDirectory(Path.Combine(simulationFolder, folderName));

                var deletedSources = new HashSet<string>();

                foreach (var optionSetName in optionSetNames)
                {
                    if (!optionNameToOptionSet.TryGetValue(optionSetName, out var optionSet))
                        continue;
                    string mappedName = map[optionSetName];

                    foreach (var (folderName, extensions) in placementRules)
                    {
                        string targetDir = Path.Combine(simulationFolder, folderName);

                        foreach (var ext in extensions)
                        {
                            string sourcePath = Path.Combine(reportFolder, $"{masterReportName}-{mappedName}{ext}");
                            if (!File.Exists(sourcePath)) continue;

                            string targetFileName = optionSetName.Replace("FSA ", "").Replace("  ", " ") + ext;
                            string destPath = Path.Combine(targetDir, targetFileName);
                            File.Copy(sourcePath, destPath, true);

                            if (doDeletion && deletedSources.Add(sourcePath))
                            {
                                File.Delete(sourcePath);
                                TabbedText.WriteLine($"Deleting {sourcePath}");
                            }
                        }
                    }
                }
            }

            CleanupAfterFolderOrganization(reportFolder, allOptionSets, grouped);
        }

        private static void PrepareFolders(out string reportFolder, out string individualResultsRoot)
        {
            reportFolder = Launcher.ReportFolder();
            individualResultsRoot = Path.Combine(reportFolder, "Individual Simulation Results");
            Directory.CreateDirectory(individualResultsRoot);
            DeleteAuxiliaryFiles(reportFolder);
        }

        private static void CleanupAfterFolderOrganization(string reportFolder, List<GameOptions> allOptionSets, Dictionary<string, List<string>> grouped)
        {
            // Move process logs
            string processLogsFolder = Path.Combine(reportFolder, "Process Logs");
            Directory.CreateDirectory(processLogsFolder);

            foreach (var logFile in Directory.GetFiles(reportFolder).Where(f => Path.GetFileName(f).Contains("log-p")))
            {
                string dest = Path.Combine(processLogsFolder, Path.GetFileName(logFile));
                if (File.Exists(dest)) File.Delete(logFile);
                else File.Move(logFile, dest);
            }

            // Report unassigned, if any
            var assignedNames = grouped.SelectMany(g => g.Value).ToHashSet();
            var unassigned = allOptionSets.Where(o => !assignedNames.Contains(o.Name)).ToList();
            if (unassigned.Count > 0)
            {
                TabbedText.WriteLine($"WARNING: {unassigned.Count} simulations were unassigned.");
                foreach (var opt in unassigned.Take(10))
                {
                    TabbedText.WriteLine($"Unassigned: {opt.Name}");
                    foreach (var kvp in opt.VariableSettings)
                        TabbedText.WriteLine($"  {kvp.Key} = {kvp.Value}");
                }
            }
        }

        public static string[] DeleteAuxiliaryFiles(string reportFolder)
        {
            string[] filesInFolder = Directory.GetFiles(reportFolder);
            string[] fileExtensionsTriggeringDeletion = new string[] { ".aux", ".log", ".gz" };
            foreach (string file in filesInFolder)
            {
                foreach (string deletionTrigger in fileExtensionsTriggeringDeletion)
                    if (file.EndsWith(deletionTrigger))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }

                    }
            }

            return filesInFolder;
        }

        #endregion
    }
}

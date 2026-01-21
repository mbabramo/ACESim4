using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static LitigCharts.DataProcessingUtils;

namespace LitigCharts
{
    public class DataProcessingBase
    {
        public static VirtualizableFileSystem VirtualizableFileSystem { get; set; }

        public static string filePrefix(PermutationalLauncher launcher) => launcher.ReportPrefix;

        public const string correlatedEquilibriumFileSuffix = "-Corr";
        public const string averageEquilibriumFileSuffix = "-Avg";
        public const string firstEquilibriumFileSuffix = "-Eq1";
        public const string onlyEquilibriumFileSuffix = "";
        public static string firstOrOnlyEquilibriumFileSuffix => singleEquilibriumOnly ? onlyEquilibriumFileSuffix : firstEquilibriumFileSuffix;
        public static string firstOrOnlyEquilibriumTypeWord => singleEquilibriumOnly ? "Only Eq" : "First Eq";

        public static string[] equilibriumTypeSuffixes_Major => new string[] { correlatedEquilibriumFileSuffix, averageEquilibriumFileSuffix, firstEquilibriumFileSuffix };
        public static string[] equilibriumTypeSuffixes_One => new string[] { firstOrOnlyEquilibriumFileSuffix };
        public static string[] equilibriumTypeSuffixes_AllIndividual => Enumerable.Range(1, 100).Select(x => $"-Eq{x}").ToArray();
        public static string[] equilibriumTypeWords_Major = new string[] { "Correlated", "Average", "First Eq" };
        public static string[] equilibriumTypeWords_One => new string[] { firstOrOnlyEquilibriumTypeWord };
        public static string[] equilibriumTypeWords_AllIndividual = Enumerable.Range(1, 100).Select(x => $"Eq{x}").ToArray();

        public static bool singleEquilibriumOnly = true; // if true, then only the first equilibrium is used. If false, then all equilibria are used (e.g., -Corr, -Avg, -Eq1, -Eq2, etc.).
        public static bool allIndividual = false; // create a diagram for each individual equilibrium (e.g., 1 to 100).
        public static string[] eqToRun => allIndividual ? equilibriumTypeWords_AllIndividual : (singleEquilibriumOnly ? equilibriumTypeWords_One : equilibriumTypeWords_Major);
        public static string[] equilibriumTypeSuffixes => allIndividual ? equilibriumTypeSuffixes_AllIndividual : (singleEquilibriumOnly ? equilibriumTypeSuffixes_One : equilibriumTypeSuffixes_Major);

        public static List<Process> ProcessesList = new List<Process>();
        public static bool UseParallel = true;
        public static int maxProcesses = UseParallel ? Environment.ProcessorCount : 1;
        public static bool avoidProcessingIfPDFExists = true; // should usually be true because when it's false, we only have one shot at getting everything launched properly
        public static bool forceBlackAndWhiteForNonDarkLatexFiles = false;
        static readonly List<(string sourcePdf, string destPdf, string sourceLog, string destLog)> PendingPostCompileMoves
            = new List<(string sourcePdf, string destPdf, string sourceLog, string destLog)>();

        public static void QueuePostCompileMove(string sourcePdf, string destPdf, string sourceLog, string destLog)
        {
            lock (PendingPostCompileMoves)
                PendingPostCompileMoves.Add((sourcePdf, destPdf, sourceLog, destLog));
        }

        #region Process Management
        static void CleanupCompletedProcesses(bool killStaleProcesses)
        {
            // Prune completed processes and enforce the existing watchdog window.
            ProcessesList = ProcessesList.Where(x => !x.HasExited).ToList();

            TimeSpan maxTimeAllowed = TimeSpan.FromMinutes(3);
            var expiredList = ProcessesList.Where(x => x.StartTime + maxTimeAllowed < DateTime.Now).ToList();
            foreach (var expired in expiredList)
            {
                try { expired.Kill(); } catch { }
                finally
                {
                    ProcessesList.Remove(expired);
                    TabbedText.WriteLine($"Terminated process {expired.StartInfo.Arguments}");
                }
            }

            // Move short-name outputs into place when they are definitely unlocked.
            if (PendingPostCompileMoves.Count > 0)
            {
                (string sourcePdf, string destPdf, string sourceLog, string destLog)[] movesSnapshot;
                lock (PendingPostCompileMoves)
                    movesSnapshot = PendingPostCompileMoves.ToArray();

                foreach (var move in movesSnapshot)
                {
                    try
                    {
                        // Skip until the source PDF actually exists and is not locked.
                        if (!VirtualizableFileSystem.File.Exists(move.sourcePdf) || !IsFileUnlocked(move.sourcePdf))
                            continue;

                        // Ensure destination directory exists.
                        string destDir = Path.GetDirectoryName(move.destPdf);
                        if (!string.IsNullOrEmpty(destDir))
                            VirtualizableFileSystem.Directory.CreateDirectory(destDir);

                        // Copy into place (overwrite to keep idempotency).
                        VirtualizableFileSystem.File.Copy(move.sourcePdf, move.destPdf, true);

                        // Try to clean up the temp PDF; if it’s still locked, we’ll retry next tick.
                        if (IsFileUnlocked(move.sourcePdf))
                        {
                            try { VirtualizableFileSystem.File.Delete(move.sourcePdf); } catch { /* retry later */ }
                        }

                        // Handle log similarly, but it’s optional.
                        if (!string.IsNullOrEmpty(move.sourceLog) &&
                            VirtualizableFileSystem.File.Exists(move.sourceLog) &&
                            IsFileUnlocked(move.sourceLog))
                        {
                            string destLogDir = Path.GetDirectoryName(move.destLog);
                            if (!string.IsNullOrEmpty(destLogDir))
                                VirtualizableFileSystem.Directory.CreateDirectory(destLogDir);

                            VirtualizableFileSystem.File.Copy(move.sourceLog, move.destLog, true);

                            try { VirtualizableFileSystem.File.Delete(move.sourceLog); } catch { /* retry later */ }
                        }

                        // Removal from the queue only after we have successfully copied the PDF into place.
                        lock (PendingPostCompileMoves)
                            PendingPostCompileMoves.Remove(move);

                        TabbedText.WriteLine($"Moved {Path.GetFileName(move.destPdf)} into place.");
                    }
                    catch
                    {
                        // Swallow and retry on a future cleanup tick.
                        // We intentionally never throw from cleanup.
                    }
                }
            }

            System.Threading.Thread.Sleep(100);

            // Local helper: true only if we can get an exclusive read handle (i.e., not in use).
            static bool IsFileUnlocked(string path)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        // If we got here, nobody else holds the file open.
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
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
            string[] results = VirtualizableFileSystem.Directory.GetFiles(path);
            foreach (var result in results)
            {
                if (result.EndsWith(".tex") && (includeResultFunc == null || includeResultFunc(result)))
                {
                    TabbedText.WriteLine($"Launching {result}");
                    ExecuteLatexProcess(path, result);
                }
            }
            WaitForProcessesToFinish();
            results = VirtualizableFileSystem.Directory.GetFiles(path);
            string[] toDelete = new string[] { ".aux", ".log", ".synctex.gz" };
            foreach (var result in results)
            {
                if (toDelete.Any(d => result.EndsWith(d)))
                {
                    VirtualizableFileSystem.File.Delete(result);
                }
            }
        }


        internal static List<(string path, string combinedPath, string optionSetName, string fileSuffix)> GetLatexProcessPlans(PermutationalLauncher launcher, IEnumerable<string> fileSuffixes, bool skipIfPDFAlreadyExists)
        {
            // combine lists from GetLatexProcessPaths for each fileSuffix
            List<(string path, string combinedPath, string optionSetName, string fileSuffix)> result = new();
            foreach (var fileSuffix in fileSuffixes)
            {
                var processPlans = GetLatexProcessPlans(launcher, fileSuffix, skipIfPDFAlreadyExists);
                foreach (var processPlan in processPlans)
                {
                    result.Add(processPlan);
                }
            }
            return result;
        }

        internal static List<(string path, string combinedPath, string optionSetName, string fileSuffix)> GetLatexProcessPlans(PermutationalLauncher launcher, string fileSuffix, bool skipIfPDFAlreadyExists)
        {
            List<(string path, string combinedPath, string optionSetName, string fileSuffix)> result = new();
            List<GameOptions> optionSets = launcher.GetOptionsSets();
            string path = Launcher.ReportFolder();

            foreach (var gameOptionsSet in optionSets)
            {
                string filenameCore, combinedPath;
                bool processingNeeded = true;
                if (skipIfPDFAlreadyExists)
                {
                    string fileSuffixCopy = fileSuffix;
                    GetFileInfo(filePrefix(launcher), ".pdf", ref fileSuffixCopy, path, gameOptionsSet, out filenameCore, out combinedPath, out bool exists);
                    if (VirtualizableFileSystem.File.Exists(combinedPath))
                        processingNeeded = false;
                }
                if (processingNeeded)
                {
                    GetFileInfo(filePrefix(launcher), ".tex", ref fileSuffix, path, gameOptionsSet, out filenameCore, out combinedPath, out bool exists);
                    if (!VirtualizableFileSystem.File.Exists(combinedPath))
                    {
                        if (VirtualizableFileSystem.File.Exists(combinedPath.Replace("-Eq1", "")))
                            combinedPath = combinedPath.Replace("-Eq1", "");
                        else
                        {
                            bool throwIfNotFound = true;
                            if (throwIfNotFound)
                                throw new Exception("File not found: " + combinedPath + ". Check whether a failure occurred running some simulations by looking for a Failure file in the report directory.");
                            else
                                return result;
                        }
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
                if (VirtualizableFileSystem.IsReal)
                    ExecuteLatexProcess(path, combinedPath);
                else
                {
                    // Take combinedPath, make sure that it ends with .tex, then remove the end.
                    string combinedPathExcludingTex = combinedPath.Replace(".tex", "");
                    foreach (string ext in new[] { ".aux", ".pdf", ".log", ".synctex.gz" } )
                    {
                        VirtualizableFileSystem.File.Add(combinedPathExcludingTex + ext);
                    }
                    TabbedText.WriteLine("Virtually processed " + combinedPath);
                }
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
                        if (VirtualizableFileSystem.File.Exists(fileToDelete))
                            VirtualizableFileSystem.File.Delete(fileToDelete);
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

        public static void OrganizeIndividualSimulationsIntoFolders(PermutationalLauncher launcher, bool doDeletion, List<(string folderName, string[] extensions)> placementRules)
        {
            string reportFolder, individualResultsRoot;
            PrepareFolders(out reportFolder, out individualResultsRoot);

            string masterReportName = launcher.ReportPrefix;
            var allOptionSets = launcher.GetOptionsSets();

            Dictionary<string, List<string>> grouped = launcher.GroupOptionSetsByClassification();
            var optionNameToOptionSet = allOptionSets.ToDictionary(opt => opt.Name);

            var originalFileNames = VirtualizableFileSystem.Directory.EnumerateFiles(reportFolder).Select(Path.GetFileName).ToHashSet();
            var deletedFileNames = new HashSet<string>();

            foreach (var (groupName, optionSetNames) in grouped)
            {
                string simulationFolder = Path.Combine(individualResultsRoot, groupName);
                VirtualizableFileSystem.Directory.CreateDirectory(simulationFolder);

                foreach (var (folderName, _) in placementRules)
                    VirtualizableFileSystem.Directory.CreateDirectory(Path.Combine(simulationFolder, folderName));

                foreach (var optionSetName in optionSetNames)
                {
                    if (!optionNameToOptionSet.TryGetValue(optionSetName, out var optionSet))
                        continue;

                    foreach (var (folderName, extensions) in placementRules)
                    {
                        string targetDir = Path.Combine(simulationFolder, folderName);

                        foreach (var ext in extensions)
                        {
                            string sourceFileName = Launcher.ReportFilename(masterReportName, optionSetName, ext);

                            if (!originalFileNames.Contains(sourceFileName)) 
                                continue;

                            string sourcePath = Launcher.ReportFullPath(masterReportName, optionSetName, ext);
                            string targetFileName = sourceFileName;
                            string destPath = Path.Combine(targetDir, targetFileName);
                            VirtualizableFileSystem.File.Copy(sourcePath, destPath, true);

                            if (doDeletion && deletedFileNames.Add(sourceFileName))
                            {
                                VirtualizableFileSystem.File.Delete(sourcePath);
                                //TabbedText.WriteLine($"Deleting {sourcePath}");
                            }
                        }
                    }
                }


            }

            CleanupAfterFolderOrganization(reportFolder, allOptionSets, grouped);

            var undeleted = originalFileNames.Except(deletedFileNames).ToList();
        }

        private static void PrepareFolders(out string reportFolder, out string individualResultsRoot)
        {
            reportFolder = Launcher.ReportFolder();
            individualResultsRoot = Path.Combine(reportFolder, "Individual Simulation Results");
            VirtualizableFileSystem.Directory.CreateDirectory(individualResultsRoot);
            DeleteAuxiliaryFiles(reportFolder);
        }

        private static void CleanupAfterFolderOrganization(string reportFolder, List<GameOptions> allOptionSets, Dictionary<string, List<string>> grouped)
        {
            // Move process logs
            string processLogsFolder = Path.Combine(reportFolder, "Process Logs");
            VirtualizableFileSystem.Directory.CreateDirectory(processLogsFolder);

            foreach (var logFile in VirtualizableFileSystem.Directory.GetFiles(reportFolder).Where(f => Path.GetFileName(f).Contains("log-p")))
            {
                string dest = Path.Combine(processLogsFolder, Path.GetFileName(logFile));
                if (VirtualizableFileSystem.File.Exists(dest)) VirtualizableFileSystem.File.Delete(logFile);
                else VirtualizableFileSystem.File.Move(logFile, dest);
            }
        }

        public static string[] DeleteAuxiliaryFiles(string reportFolder)
        {
            string[] filesInFolder = VirtualizableFileSystem.Directory.GetFiles(reportFolder);
            string[] fileExtensionsTriggeringDeletion = new string[] { ".aux", ".log", ".gz" };
            foreach (string file in filesInFolder)
            {
                foreach (string deletionTrigger in fileExtensionsTriggeringDeletion)
                    if (file.EndsWith(deletionTrigger))
                    {
                        try
                        {
                            VirtualizableFileSystem.File.Delete(file);
                        }
                        catch
                        {
                        }

                    }
            }

            return filesInFolder;
        }

        #endregion

        #region Black and white changes

        

        private static readonly System.Text.RegularExpressions.Regex LatexNamedColorsToReplaceWithBlackRegex =
            new System.Text.RegularExpressions.Regex(
                @"\b(?:green|orange|yellow|blue|red)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex LatexNamedColorDefinitionCommandRegex =
            new System.Text.RegularExpressions.Regex(
                @"(\\(?:definecolor|providecolor|colorlet)\s*\{\s*)(green|orange|yellow|blue|red)(\s*\})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string ApplyBlackAndWhiteOptionToLatex(string latexContents, string latexOutputFileNameOrPath)
        {
            if (!forceBlackAndWhiteForNonDarkLatexFiles)
                return latexContents;

            if (string.IsNullOrEmpty(latexContents))
                return latexContents;

            if (LatexFileNameIndicatesDarkMode(latexOutputFileNameOrPath))
                return latexContents;

            return ReplaceNamedColorsWithBlack(latexContents);
        }

        private static bool LatexFileNameIndicatesDarkMode(string latexOutputFileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(latexOutputFileNameOrPath))
                return false;

            string fileName = Path.GetFileName(latexOutputFileNameOrPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = latexOutputFileNameOrPath;

            return fileName.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReplaceNamedColorsWithBlack(string latexContents)
        {
            Dictionary<string, string> placeholdersToOriginalTokens = new Dictionary<string, string>(StringComparer.Ordinal);

            string protectedColorDefinitions = LatexNamedColorDefinitionCommandRegex.Replace(
                latexContents,
                match =>
                {
                    string placeholder = $"__LITIGCHARTS_COLORNAME_PROTECT_{placeholdersToOriginalTokens.Count}__";
                    placeholdersToOriginalTokens[placeholder] = match.Groups[2].Value;
                    return match.Groups[1].Value + placeholder + match.Groups[3].Value;
                });

            string replaced = LatexNamedColorsToReplaceWithBlackRegex.Replace(protectedColorDefinitions, "black");

            if (placeholdersToOriginalTokens.Count == 0)
                return replaced;

            foreach (var kvp in placeholdersToOriginalTokens)
                replaced = replaced.Replace(kvp.Key, kvp.Value);

            return replaced;
        }

        #endregion
    }
}

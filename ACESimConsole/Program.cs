using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.IO;
using System.Runtime.Serialization;
using System.Diagnostics;
using ACESim.Util;
using ACESimBase.Games.AdditiveEvidenceGame;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.DMSReplicationGame;
using ACESimBase.Util.Debugging;
using ACESimBase.GameSolvingSupport.Settings;

namespace ACESim
{
    class Program
    {
        public static bool LaunchSingleOptionsSetOnly = true;

        [STAThread]
        public static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--enumerated-pure")
            {
                GameProgressLogger.LoggingOn = false;
                GameProgressLogger.DetailedLogging = false;
                await ACESimBase.Games.LitigGame.LitigGameEnumeratedPureLauncher.RunCommandAsync(args.Skip(1).ToArray());
                return;
            }
            if (args.Length > 0 && args[0] == "--article-worked-path-values")
            {
                throw new ArgumentException("Worked-path LaTeX generation has moved to LitigCharts: diagrams worked-path --config <article-diagrams.json>.");
            }
            if (args.Length > 0 && args[0] == "--extract-article-paths")
            {
                if (args.Length != 3)
                    throw new ArgumentException("Usage: --extract-article-paths <request.json> <output.json>");
                GameProgressLogger.LoggingOn = false;
                GameProgressLogger.DetailedLogging = false;
                await ACESimBase.Games.LitigGame.ManualReports.ArticleWorkedPathExtraction.WriteJsonAsync(args[1], args[2]);
                return;
            }
            if (args.Length > 0 && (args[0] == "--article-game-trees" || args[0] == "--endogenous-game-tree"))
            {
                if (args.Length != 2)
                    throw new ArgumentException("Usage: " + args[0] + " <output-directory>");
                if (args[0] == "--article-game-trees")
                    await ACESimBase.Games.LitigGame.ManualReports.ArticleGameTreeDiagrams.WriteSourcesAsync(
                        Path.GetFullPath(args[1]));
                else
                    await ACESimBase.Games.LitigGame.ManualReports.EndogenousGameTreeDiagrams.WriteSourceAsync(
                        Path.GetFullPath(args[1]));
                return;
            }
            GameProgressLogger.LoggingOn = false;
            GameProgressLogger.DetailedLogging = false;
            await Execute();
            //// the following is supposed to create a large stack, but it either doesn't work (or isn't large enough for our purposes, which seems unlikely)
            //Thread t = new Thread(new ThreadStart(), delegate ()
            //{
            //    Execute();
            //}, 1024 * 1024 * 1024);
            //t.Start();
            //while (t.IsAlive)
            //    Thread.Sleep(500);
        }

        private static async Task Execute()
        {
            try
            {
                await ExecuteContent();
            }
            catch (Exception e)
            {
                TabbedText.WriteLine(e.Message);
                TabbedText.WriteLine(e.StackTrace);
            }
        }

        private static async Task ExecuteContent()
        {
            Stopwatch s = new Stopwatch();
            s.Start();

            var launcher = Launcher.GetLauncher();
            launcher.LaunchSingleOptionsSetOnly = LaunchSingleOptionsSetOnly;
            ReportCollection launchResult = await launcher.Launch();
            TextCopy.ClipboardService.SetText(launchResult?.standardReport ?? "");
            s.Stop();
            TabbedText.WriteLineEvenIfDisabled($"Total runtime {s.Elapsed} ");
            TabbedText.WriteLineEvenIfDisabled("");
            TabbedText.WriteLineEvenIfDisabled("Press a to copy above text (including scrolled out) to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press s to copy standard report to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press c to copy comma-separated report to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press Enter to end.");
            ConsoleKey key;
            string report = null;
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Keep waiting
                    await Task.Delay(100);
                }
                key = Console.ReadKey(true).Key;
                report = key switch
                {
                    ConsoleKey.A => TabbedText.AccumulatedText.ToString(),
                    ConsoleKey.S => launchResult.standardReport,
                    ConsoleKey.C => String.Join("\r", launchResult.csvReports.SingleOrDefault() ?? ""),
                    ConsoleKey.Enter => report ?? launchResult.standardReport, // copy standard report if no other report has been copied.
                    _ => report
                };
                if (report != null)
                    TextCopy.ClipboardService.SetText(report);
            } while (key != ConsoleKey.Enter);
        }
    }
}

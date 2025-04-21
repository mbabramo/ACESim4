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

namespace ACESim
{
    class Program
    {
        public enum AvailableGames
        {
            SelectEFGFileGame,
            SelectLeducGame,
            SelectMultiRoundCooperationGame,
            SelectLitigGame,
            SelectAdditiveEvidenceGame,
            SelectDMSReplicationGame
        }

        public static AvailableGames GameToPlay = AvailableGames.SelectLitigGame; 
        public static bool LaunchSingleOptionsSetOnly = true;

        [STAThread]
        public static async Task Main(string[] args)
        {
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

            string baseOutputDirectory;
            string strategiesPath;
            Launcher launcher = null;
            switch (GameToPlay)
            {
                case AvailableGames.SelectEFGFileGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\EFGFileGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new EFGFileGameLauncher();
                    break;
                case AvailableGames.SelectLeducGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\LeducGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new LeducGameLauncher();
                    break;
                case AvailableGames.SelectMultiRoundCooperationGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MultiRoundCooperationGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new MultiRoundCooperationGameLauncher();
                    break;
                case AvailableGames.SelectLitigGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\LitigGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new LitigGameLauncher();
                    break;
                case AvailableGames.SelectAdditiveEvidenceGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\AdditiveEvidenceGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new AdditiveEvidenceGameLauncher();
                    break;

                case AvailableGames.SelectDMSReplicationGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\AdditiveEvidenceGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new DMSReplicationGameLauncher();
                    break;

            }
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
                TextCopy.ClipboardService.SetText(report);
            } while (key != ConsoleKey.Enter);
        }
    }
}
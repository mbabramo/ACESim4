﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim;
using System.Threading;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.IO;
using System.Runtime.Serialization;
using System.Diagnostics;
using ACESim.Util;

namespace ACESim
{
    class Program
    {
        public enum AvailableGames
        {
            Leduc,
            MultiRoundCooperation,
            MyGame
        }

        public static AvailableGames GameToPlay = AvailableGames.MyGame;

        [STAThread]
        public static async Task Main(string[] args)
        {
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
            Console.SetBufferSize(1000, 32766);

            string baseOutputDirectory;
            string strategiesPath;
            Launcher launcher = null;
            switch (GameToPlay)
            {
                case AvailableGames.Leduc:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\LeducGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new LeducGameLauncher();
                    break;
                case AvailableGames.MultiRoundCooperation:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MultiRoundCooperationGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new MultiRoundCooperationGameLauncher();
                    break;
                case AvailableGames.MyGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    launcher = new MyGameLauncher();
                    break;
            }
            launcher.LaunchSingleOptionsSetOnly = true;
            ReportCollection launchResult = await launcher.Launch();
            TextCopy.Clipboard.SetText(launchResult.standardReport);
            s.Stop();
            TabbedText.WriteLineEvenIfDisabled($"Total runtime {s.Elapsed} ");
            TabbedText.WriteLineEvenIfDisabled("");
            TabbedText.WriteLineEvenIfDisabled("Press a to copy above text (including scrolled out) to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press s to copy standard report to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press c to copy comma-separated report to clipboard.");
            TabbedText.WriteLineEvenIfDisabled("Press Enter to end.");
            ConsoleKey key = Console.ReadKey(true).Key;
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Keep waiting
                }
                TextCopy.Clipboard.SetText(key switch
                {
                    ConsoleKey.A => TabbedText.AccumulatedText.ToString(),
                    ConsoleKey.S => launchResult.standardReport,
                    ConsoleKey.Enter => launchResult.standardReport,
                    ConsoleKey.C => String.Join("\r", launchResult.csvReports.SingleOrDefault() ?? ""),
                    _ => ""
                });
            } while (key != ConsoleKey.Enter);
        }
    }
}
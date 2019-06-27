using System;
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
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
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
            string launchResult = await launcher.Launch();
            TextCopy.Clipboard.SetText(launchResult);
            s.Stop();
            Console.WriteLine($"Total runtime {s.Elapsed} ");
            Console.WriteLine();
            Console.WriteLine("Press Enter to end.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Enter);
            TextCopy.Clipboard.SetText(launchResult);
        }
    }
}
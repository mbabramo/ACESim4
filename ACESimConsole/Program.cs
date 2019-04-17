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

        public static AvailableGames GameToPlay = AvailableGames.MultiRoundCooperation;

        [STAThread]
        static void Main(string[] args)
        {
            string baseOutputDirectory;
            string strategiesPath;
            string gameResult = null;
            switch (GameToPlay)
            {
                case AvailableGames.Leduc:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\LeducGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    gameResult = LeducGameRunner.EvolveLeducGame().GetAwaiter().GetResult();
                    break;
                case AvailableGames.MultiRoundCooperation:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MultiRoundCooperationGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    gameResult = MultiRoundCooperationGameRunner.EvolveGame();
                    break;
                case AvailableGames.MyGame:
                    baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
                    strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
                    gameResult = MyGameRunner.EvolveMyGame().GetAwaiter().GetResult();
                    break;
            }
            TextCopy.Clipboard.SetText(gameResult);
            Console.WriteLine();
            Console.WriteLine("Press Enter to end.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Enter);
            TextCopy.Clipboard.SetText(gameResult);
        }
    }
}
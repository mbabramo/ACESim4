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

        [STAThread]
        static void Main(string[] args)
        {
            // DEBUG -- turn into class library

            // DEBUG
            string apiURL = "https://acesimfuncs.azurewebsites.net/api/MyTestFn?code=6a5YRX3LZqL3aIjJ8goCIXJC1P0astc5b5fxvBBMv5QwCjZcOS6/cw==&clientId=default";
            string apiURL2 = "https://acesimfuncs.azurewebsites.net/api/GetReport?code=GbM1qaVgKmlBFvbzMGzInPjMTuGmdsfzoMfV6K//wJVv811t4sFbnQ==&clientId=default";
            Debug; // use above API in MyGameRunner

            var task = Util.RunAzureFunction.RunFunction(apiURL, new { first = "Michael", last = "Abramowicz"});
            var resultX = task.GetAwaiter().GetResult();
            var task2 = Util.RunAzureFunction.RunFunction(apiURL, new { first = "BadlyFormed" });
            var resultY = task2.GetAwaiter().GetResult();

            string baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
            string strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
            string result = MyGameRunner.EvolveMyGame();
            System.Windows.Clipboard.SetText(result);
            Console.WriteLine();
            Console.WriteLine("Press Enter to end.");
            do
            {
                while (!Console.KeyAvailable)
                {
                    // Do something
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Enter);
            System.Windows.Clipboard.SetText(result);
        }
    }
}
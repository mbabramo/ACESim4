using System;
using System.Diagnostics;
using System.IO;

namespace ACESimDistributedSaturate
{
    class Program
    {
        static void Main(string[] args)
        {
            // NOTE: Make sure to build before running and confirm that ACESimDistributed is built recently.
            string currentPath = Process.GetCurrentProcess().MainModule.FileName;
            if (currentPath.Contains("Debug"))
                throw new Exception("Set to release mode before saturating.");
            string targetPath = currentPath.Replace("ACESimDistributedSaturate", "ACESimDistributed");
            Console.WriteLine(targetPath);
            const int maxNumProcessors = 5; // DEBUG
            int numProcessors = Environment.ProcessorCount;
            if (numProcessors > maxNumProcessors)
                numProcessors = maxNumProcessors;
            Console.WriteLine($"Launching on {numProcessors} processors");
            for (int i = 0; i < numProcessors; i++)
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(targetPath, i.ToString());
                processStartInfo.UseShellExecute = true;
                Process.Start(processStartInfo);
            }
        }
    }
}

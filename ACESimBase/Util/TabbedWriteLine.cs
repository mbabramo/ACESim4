using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;

namespace ACESim
{
    public static class TabbedText
    {
        public static StringBuilder AccumulatedText = new StringBuilder();

        public static int Tabs = 0;

        public static bool EnableOutput = true;

        public static bool WriteToConsole = true;

        public static void WriteLine()
        {
            WriteLine("");
        }

        public static void WriteLine(string format, params object[] args)
        {
            Write(format, args);
            WriteWithoutTabs(Environment.NewLine, args);
            //StringBuilder local = new StringBuilder();
            //local.Append(Environment.NewLine);
            //OutputAndAccumulate(local);
        }

        public static void Write(string format, params object[] args)
        {
            StringBuilder local = new StringBuilder();
            for (int i = 0; i < Tabs * 5; i++)
                local.Append(" ");
            OutputAndAccumulate(local);
            WriteWithoutTabs(format, args);
        }

        public static void WriteWithoutTabs(string format, object[] args)
        {
            StringBuilder local = new StringBuilder();
            local.Append(String.Format(format, args));
            OutputAndAccumulate(local);
        }

        private static void OutputAndAccumulate(StringBuilder builder)
        {
            string localString = builder.ToString();
            if (EnableOutput)
            {
                if (WriteToConsole)
                    Console.Write(localString);
                else
                    Debug.Write(localString);
            }
            AccumulatedText.Append(localString);
        }

        public static void ResetAccumulated()
        {
            AccumulatedText = new StringBuilder();
        }

        public static void WriteToFile(string baseDirectory, string subDirectory, string fileName, bool reset = true)
        {
            TextFileCreate.CreateTextFile(Path.Combine(baseDirectory, subDirectory, fileName), AccumulatedText.ToString());
            if (reset)
                ResetAccumulated();
        }
    }
}

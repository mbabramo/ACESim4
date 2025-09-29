using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ACESimBase.Util.Serialization;

namespace ACESimBase.Util.Debugging
{
    public static class TabbedText
    {
        public static StringBuilder AccumulatedText = new StringBuilder();

        private static int Tabs = 0;

        public static void TabIndent()
        {
            Interlocked.Increment(ref Tabs);
        }

        public static void TabUnindent()
        {
            Interlocked.Decrement(ref Tabs);
        }

        private static bool OutputEnabled = true;

        public static void EnableOutput()
        {
            lock (AccumulatedText)
                OutputEnabled = true;
        }

        public static void DisableOutput()
        {
            HideConsoleProgressString();
            ConsoleProgressString = null;
            lock (AccumulatedText)
                OutputEnabled = false;
        }

        public static bool WriteToConsole = true;

        public static void WriteLine()
        {
            WriteLine("");
        }

        public static void WriteLineEvenIfDisabled(string format, params object[] args)
        {
            lock (AccumulatedText)
            {
                bool original = OutputEnabled;
                OutputEnabled = true;
                WriteLine(format, args);
                OutputEnabled = original;
            }
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
            if (args == null || args.Length == 0)
                local.Append(format);
            else
                local.Append(string.Format(format, args));
            OutputAndAccumulate(local);
        }

        private static void OutputAndAccumulate(StringBuilder builder)
        {
            lock (AccumulatedText)
            {
                string localString = builder.ToString();
                if (OutputEnabled)
                {
                    if (WriteToConsole)
                    {
                        bool isHiddenInitially = !ConsoleProgressStringVisible;
                        if (!isHiddenInitially)
                            HideConsoleProgressString();

                        Console.Write(localString);
                        if (localString.EndsWith("\r\n"))
                            Console.Out.Flush();
                        if (!isHiddenInitially)
                            ShowConsoleProgressString();
                    }
                    else
                        Debug.Write(localString);
                }
                AccumulatedText.Append(localString);
            }
        }

        public static void ResetAccumulated()
        {
            lock (AccumulatedText)
            {
                AccumulatedText = new StringBuilder();
            }
        }

        public static void WriteToFile(string baseDirectory, string subDirectory, string fileName, bool reset = true)
        {
            TextFileManage.CreateTextFile(Path.Combine(baseDirectory, subDirectory, fileName), AccumulatedText.ToString());
            if (reset)
                ResetAccumulated();
        }



        private static string ConsoleProgressString = null;
        private static bool ConsoleProgressStringVisible;

        public static void HideConsoleProgressString()
        {
            if (OutputEnabled && WriteToConsole)
                if (ConsoleProgressStringVisible)
                    if (ConsoleProgressString != null)
                    {
                        for (int i = 0; i < ConsoleProgressString.Length; i++)
                        {
                            Console.Write("\b \b");
                        }
                        ConsoleProgressStringVisible = false;
                    }
        }

        public static void ShowConsoleProgressString()
        {
            if (OutputEnabled && WriteToConsole)
                if (!ConsoleProgressStringVisible)
                    if (ConsoleProgressString != null)
                    {
                        Console.Write(ConsoleProgressString);
                        ConsoleProgressStringVisible = true;
                    }
        }

        public static void SetConsoleProgressString(string consoleProgressString)
        {
            HideConsoleProgressString();
            ConsoleProgressString = consoleProgressString;
            ShowConsoleProgressString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class GameProgressStep
    {
        public GameProgress GameProgress;
        public string StepPoint;
    }

    public class GameProgressHistory
    {
        public List<GameProgressStep> Steps = new List<GameProgressStep>();
        private object lockObj = new object();
        public void Add(GameProgressStep step)
        {
            lock (lockObj)
            {
                Steps.Add(step);
            }
        }
    }

    public static class GameProgressLogger
    {
        public static bool LoggingOn = false; // change this to enable logging -- but this slows things down a lot
        public static bool RecordLogMessages = true;
        public static bool OutputLogMessages = false;
        public static bool PartialLoggingOn = false; // change this to allow partial logging -- might do this in code in specific places
        public static int MaxIncomplete = 1000;
        public static int Tabs = 0;
        public static StringBuilder MessagesLog = new StringBuilder();

        private static object lockObj = new object();

        public static Queue<GameProgressHistory> IncompleteGames = new Queue<GameProgressHistory>();
        public static Queue<GameProgressHistory> CompleteGames = new Queue<GameProgressHistory>();

        /// <summary>
        /// Uses a function to produce the message to log. The advantage of this is that if logging is not on, the function need not be called, and so the string is never constructed.
        /// </summary>
        /// <param name="messageProducer"></param>
        public static void Log(Func<string> messageProducer)
        {
            if (LoggingOn)
                Log(messageProducer());
        }

        public static void Log(string message)
        {
            if (LoggingOn)
            {
                for (int tab = 0; tab < Tabs; tab++)
                    message = "    " + message;
                if (OutputLogMessages)
                    Debug.WriteLine(message);
                if (RecordLogMessages)
                    MessagesLog.Append("\n" + message);
            }
        }

        public static void Reset()
        {
            IncompleteGames = new Queue<GameProgressHistory>();
            CompleteGames = new Queue<GameProgressHistory>();
            MessagesLog = new StringBuilder();
        }

        public static void AddGameProgressStep(GameProgress currentProgress, string stepPoint)
        {
            if (LoggingOn)
            {
                lock (lockObj)
                {
                    GameProgressStep step = new GameProgressStep() { GameProgress = currentProgress.DeepCopy(), StepPoint = stepPoint };
                    GameProgressHistory inProgress = IncompleteGames.SingleOrDefault(x => x.Steps != null && x.Steps.Any(y => (y.GameProgress.IterationID == null && currentProgress.IterationID == null) || y.GameProgress.IterationID.IterationNumber == currentProgress.IterationID.IterationNumber));
                    Queue<GameProgressHistory> queueToAddTo = currentProgress.GameComplete ? CompleteGames : IncompleteGames;
                    if (inProgress == null)
                        queueToAddTo.Enqueue(new GameProgressHistory() { Steps = { step } });
                    else
                    {
                        inProgress.Add(step);
                        if (queueToAddTo == CompleteGames)
                            queueToAddTo.Enqueue(inProgress);
                    }
                }
            }
        }

        public static void LocateIterationID(IterationID iterationID)
        {
            GameProgressHistory inProgress = IncompleteGames.SingleOrDefault(x => x.Steps != null && x.Steps.Any(y => y.GameProgress.IterationID.IterationNumber == iterationID.IterationNumber));
            GameProgressHistory complete = CompleteGames.SingleOrDefault(x => x.Steps != null && x.Steps.Any(y => y.GameProgress.IterationID.IterationNumber == iterationID.IterationNumber));
            
        }
    }
}

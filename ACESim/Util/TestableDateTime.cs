using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ACESim
{
    public static class TestableDateTime
    {
        static bool useFakeTimes = false;
        static TimeSpan timeToAdd = new TimeSpan(0, 0, 0);

        public static DateTime Now { get { return GetCurrentTimeAdjustingForAddedTime(); } }

        public static void UseFakeTimes()
        {
            useFakeTimes = true; // once done, cannot be undone except by restarting; for testing only
        }

        internal static DateTime GetCurrentTimeAdjustingForAddedTime()
        {
            if (useFakeTimes)
                return DateTime.Now + timeToAdd;
            else
                return DateTime.Now;
        }

        public static void SleepOrSkipTime(long milliseconds)
        {
            if (useFakeTimes)
                timeToAdd += TimeSpan.FromMilliseconds(milliseconds); // new TimeSpan(0, 0, 0, 0, milliseconds);
            else
                Thread.Sleep((int) milliseconds);
        }
    }
}

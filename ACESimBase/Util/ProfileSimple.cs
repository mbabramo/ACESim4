using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Diagnostics;

/// <summary>
/// Summary description for ProfileSimple
/// </summary>
public static class ProfileSimple
{
    public static Dictionary<string, Stopwatch> activeProfiles = new Dictionary<string, Stopwatch>();
    public static Dictionary<string, int> invocations = new Dictionary<string, int>();
    public static Dictionary<string, TimeSpan> cumulative = new Dictionary<string, TimeSpan>();
    public static Dictionary<string, int> recursionDepth = new Dictionary<string, int>();

    public static void Start(string key, bool allowRecursion = false)
    {
        if (!activeProfiles.ContainsKey(key))
            activeProfiles.Add(key, null);
        if (activeProfiles[key] != null)
        {
            if (!allowRecursion)
                Debug.WriteLine("Profiling " + key + " was already started.");
            else
                recursionDepth[key]++;
        }
        else
        {
            if (allowRecursion)
            {
                if (recursionDepth.ContainsKey(key))
                    recursionDepth[key]++;
                else
                    recursionDepth.Add(key, 1);
            }
            Stopwatch theStopwatch = new Stopwatch();
            activeProfiles[key] = theStopwatch;
            theStopwatch.Reset();
            theStopwatch.Start();
        }
    }

    public static void End(string key, bool suppressOutput = false, bool allowRecursion = false)
    {
        if (!activeProfiles.ContainsKey(key))
        {
            Debug.WriteLine("Profiling " + key + " not currently active.");
            return;
        }
        if (recursionDepth.ContainsKey(key))
        {
            int depth = recursionDepth[key];
            depth--;
            recursionDepth[key] = depth;
            if (depth > 0)
                return; // don't end until we get back to recursion depth of 0
        }
        Stopwatch theStopWatch = activeProfiles[key];
        if (theStopWatch != null) // occasionally null even after ContainsKey check
        {
            theStopWatch.Stop();
            TimeSpan elapsedTime = theStopWatch.Elapsed;
            if (!suppressOutput)
                Debug.WriteLine("Time elapsed for " + key + ": " + elapsedTime);
            if (!cumulative.ContainsKey(key))
            {
                cumulative.Add(key, elapsedTime);
                invocations.Add(key, 1);
            }
            else
            {
                cumulative[key] += elapsedTime;
                invocations[key] += 1;
            }
        }
        activeProfiles[key] = null;
    }

    static long overheadTimePerCallInTicks = -1;
    public static long CalculateOverheadTimePerCallInTicks()
    {
        if (overheadTimePerCallInTicks == -1)
        {
            for (int i = 0; i < 10000; i++)
            {
                ProfileSimple.Start("OverheadTest");
                ProfileSimple.End("OverheadTest", true);
            }
            overheadTimePerCallInTicks = (long) ((double)cumulative["OverheadTest"].Ticks / (double)10000);
        }
        return overheadTimePerCallInTicks;
    }

    public static void ReportCumulative(string key)
    {
        if (!cumulative.ContainsKey(key))
            Debug.WriteLine("No invocations yet of " + key);
        else
        {
            TimeSpan totalTime = TimeSpan.FromTicks(cumulative[key].Ticks - CalculateOverheadTimePerCallInTicks() * invocations[key]);
            TimeSpan averageTime = TimeSpan.FromTicks((long) ((double) cumulative[key].Ticks / (double)invocations[key]));
            Debug.WriteLine("Cumulative time elapsed for " + key + ": " + totalTime + " average: " + averageTime + " # calls: " + invocations[key]);
        }
    }

}

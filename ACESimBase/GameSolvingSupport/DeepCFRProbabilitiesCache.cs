using System;
using System.Collections.Concurrent;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRProbabilitiesCache
    {
        ConcurrentDictionary<string, double[]> Cache = new ConcurrentDictionary<string, double[]>();

        public double[] GetValue(DirectGamePlayer directGamePlayer, Func<double[]> probabilitiesLoader)
        {
            string s = directGamePlayer.GetInformationSetString(true);
            bool success = Cache.TryGetValue(s, out double[] value);
            if (success == false)
            {
                value = probabilitiesLoader();
                Cache[s] = value;
            }
            return value;
        }
    }
}

using ACESimBase.Util;
using System;
using System.Collections.Concurrent;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRProbabilitiesCache
    {
        bool active = true; // testing seems to indicate that this does speed things up
        ConcurrentDictionary<Bytes50, double[]> Cache = new ConcurrentDictionary<Bytes50, double[]>();

        public double[] GetValue(DirectGamePlayer directGamePlayer, Func<double[]> probabilitiesLoader)
        {
            if (!active)
                return probabilitiesLoader();
            Bytes50 s = directGamePlayer.GetInformationSetAsBytes50(true);
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

using ACESimBase.Util.NWayTreeStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ACESimBase.GameSolvingSupport.DeepCFR
{
    public class DeepCFRProbabilitiesCache
    {
        bool active = true; // testing seems to indicate that this does speed things up
        NWayTreeStorageInternal<double[]> Cache = new NWayTreeStorageInternal<double[]>(null);

        public double[] GetValue(DirectGamePlayer directGamePlayer, Func<double[]> probabilitiesLoader)
        {
            if (!active)
                return probabilitiesLoader();
            IEnumerable<byte> path = directGamePlayer.GetInformationSet_PlayerAndInfo();
            double[] result = Cache.GetOrSetValueAtPath(path, probabilitiesLoader);
            return result;
        }
    }
}

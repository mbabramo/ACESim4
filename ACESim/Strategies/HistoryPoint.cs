using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct HistoryPoint
    {
        InformationSetLookupApproach LookupApproach;

        NWayTreeStorage<object> TreePoint;
        List<Strategy> Strategies;

        public HistoryPoint(InformationSetLookupApproach lookupApproach, NWayTreeStorage<object> treePoint, List<Strategy> strategies)
        {
            LookupApproach = lookupApproach;
            TreePoint = treePoint;
            Strategies = strategies;
        }
    }
}

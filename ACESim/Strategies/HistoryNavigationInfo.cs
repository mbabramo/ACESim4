﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public struct HistoryNavigationInfo
    {
        public InformationSetLookupApproach LookupApproach;
        public List<Strategy> Strategies;
        public GameDefinition GameDefinition;

        public HistoryNavigationInfo(InformationSetLookupApproach lookupApproach, List<Strategy> strategies, GameDefinition gameDefinition)
        {
            LookupApproach = lookupApproach;
            Strategies = strategies;
            GameDefinition = gameDefinition;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{

    /// <summary>
    /// </summary>
    [Serializable]
    public class StrategyBounds
    {
        public double LowerBound;
        public double UpperBound;
        [OptionalSetting]
        public bool AllowBoundsToExpandIfNecessary = false;

        public StrategyBounds()
            : this( new Tuple<double, double>(0, 0))
        {
            // Nothing to do.
        }

        public StrategyBounds(Tuple<double, double> bounds, bool allowBoundsToExpandIfNecessary = false)
        {
            LowerBound = bounds.Item1;
            UpperBound = bounds.Item2;
            AllowBoundsToExpandIfNecessary = allowBoundsToExpandIfNecessary;
        }

    }
}

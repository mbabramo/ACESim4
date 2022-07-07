using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    public class DMSReplicationGameOptions : GameOptions
    {
        public double T, C, Q;
        public static double[] PiecewiseLinearBidsSlopeOptions = new double[] { 1.0 / 3.0, 1.0 / 2.0, 2.0 / 3.0, 1.0 }; 
        public static byte NumMinValues => (byte)25;
        public static byte NumSlopes => (byte) PiecewiseLinearBidsSlopeOptions.Length;
        public static byte NumTruncationPortions = 10;
    }
}

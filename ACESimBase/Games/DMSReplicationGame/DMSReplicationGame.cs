using ACESim;
using ACESim.Util;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.DMSReplicationGame
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class DMSReplicationGame : Game
    {
        public DMSReplicationGameDefinition DMSReplicationDefinition => (DMSReplicationGameDefinition)GameDefinition;
        public DMSReplicationGameProgress DMSReplicationProgress => (DMSReplicationGameProgress)Progress;

        public DMSReplicationGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired
            ) : base(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame, fullHistoryRequired)
        {

        }

        public override void Initialize()
        {
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)DMSReplicationGameDecisions.C_Dummy:
                    break;
                case (byte)DMSReplicationGameDecisions.P_Slope:
                    DMSReplicationProgress.PSlope = DMSReplicationGameOptions.PiecewiseLinearBidsSlopeOptions[action - 1];
                    break;
                case (byte)DMSReplicationGameDecisions.P_MinValue:
                    double minForPiecewiseLinearSegment = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, DMSReplicationGameOptions.NumMinValues, false);
                    DMSReplicationProgress.PMinValue = minForPiecewiseLinearSegment;
                    break;
                case (byte)DMSReplicationGameDecisions.P_TruncationPortion:
                    DMSReplicationProgress.PTruncationPortion = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1, DMSReplicationGameOptions.NumTruncationPortions, true);
                    break;
                case (byte)DMSReplicationGameDecisions.D_Slope:
                    DMSReplicationProgress.DSlope = DMSReplicationGameOptions.PiecewiseLinearBidsSlopeOptions[action - 1];

                    break;
                case (byte)DMSReplicationGameDecisions.D_MinValue:
                    double minForPiecewiseLinearSegment2 = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, DMSReplicationGameOptions.NumMinValues, false);
                    DMSReplicationProgress.DMinValue = minForPiecewiseLinearSegment2;
                    break;
                case (byte)DMSReplicationGameDecisions.D_TruncationPortion:
                    DMSReplicationProgress.DTruncationPortion = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1, DMSReplicationGameOptions.NumTruncationPortions, true);
                    DMSReplicationProgress.GameComplete = true; // with piecewise linear offers, we always end after defendant's announcement of line
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public override void FinalProcessing()
        {
            DMSReplicationProgress.CalculateGameOutcomes();

            base.FinalProcessing();
        }
    }
}

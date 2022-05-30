using ACESim;
using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class AdditiveEvidenceGame : Game
    {
        public AdditiveEvidenceGameDefinition AdditiveEvidenceDefinition => (AdditiveEvidenceGameDefinition)GameDefinition;
        public AdditiveEvidenceGameProgress AdditiveEvidenceProgress => (AdditiveEvidenceGameProgress)Progress;

        public AdditiveEvidenceGame(List<Strategy> strategies,
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
                // Note: THe linear bids decisions will be executed if and only if the POffer and DOffer decisions are not


                case (byte)AdditiveEvidenceGameDecisions.P_Slope:
                    AdditiveEvidenceProgress.PSlope = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, AdditiveEvidenceDefinition.Options.PiecewiseLinearBidsSlopeOptions.Length, false);
                    break;
                case (byte)AdditiveEvidenceGameDecisions.P_MinValueForRange: 
                    double minForPiecewiseLinearSegment = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, AdditiveEvidenceDefinition.Options.NumOffers, false);
                    AdditiveEvidenceProgress.PMinValueForRange = minForPiecewiseLinearSegment;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.D_Slope:
                    AdditiveEvidenceProgress.DSlope = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, AdditiveEvidenceDefinition.Options.PiecewiseLinearBidsSlopeOptions.Length, false);
                    break;
                case (byte)AdditiveEvidenceGameDecisions.D_MinValueForRange:
                    // DEBUG -- delete following
                    //double minForPiecewiseLinearSegment2 = EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */, AdditiveEvidenceDefinition.Options.NumOffers, false);
                    //AdditiveEvidenceProgress.DMinValueForRange = minForPiecewiseLinearSegment2;
                    //bool settled = AdditiveEvidenceProgress.PiecewiseLinearPOffer <= AdditiveEvidenceProgress.PiecewiseLinearDOffer;
                    //if (settled)
                    AdditiveEvidenceProgress.GameComplete = true; // with piecewise linear offers, we always end after defendant's announcement of line
                    break;

                case (byte)AdditiveEvidenceGameDecisions.PQuit:
                    AdditiveEvidenceProgress.PQuits = action == 1;
                    if (action == 1)
                        AdditiveEvidenceProgress.GameComplete = true;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.DQuit:
                    AdditiveEvidenceProgress.DQuits = action == 1;
                    if (action == 1)
                        AdditiveEvidenceProgress.GameComplete = true;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Quality:
                    AdditiveEvidenceProgress.Chance_Plaintiff_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality:
                    AdditiveEvidenceProgress.Chance_Defendant_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias:
                    AdditiveEvidenceProgress.Chance_Plaintiff_Bias = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                    AdditiveEvidenceProgress.Chance_Defendant_Bias = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.POffer:
                    AdditiveEvidenceProgress.POffer = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.DOffer:
                    AdditiveEvidenceProgress.DOffer = action;
                    if (action >= AdditiveEvidenceProgress.POffer)
                        AdditiveEvidenceProgress.GameComplete = true;
                    break;

                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality:
                    AdditiveEvidenceProgress.Chance_Neither_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias:
                    AdditiveEvidenceProgress.Chance_Neither_Bias = action;
                    AdditiveEvidenceProgress.GameComplete = true;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public class AdditiveEvidenceGameOutcome
        {
            public double PWelfare;
            public double DWelfare;
            public bool TrialOccurs;
            public double DamagesAwarded;
        }

        public override void FinalProcessing()
        {
            AdditiveEvidenceProgress.CalculateGameOutcome();

            CalculateSocialWelfareOutcomes();

            base.FinalProcessing();
        }

        private void CalculateSocialWelfareOutcomes()
        {
            
        }
    }
}

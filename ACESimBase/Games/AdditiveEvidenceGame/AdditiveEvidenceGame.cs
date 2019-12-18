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

        public AdditiveEvidenceGame()
        {

        }

        public override void Initialize()
        {
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)AdditiveEvidenceGameDecisions.P_LinearBid_Min:
                    AdditiveEvidenceProgress.P_LinearBid_Min = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.P_LinearBid_Max:
                    AdditiveEvidenceProgress.P_LinearBid_Max = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.D_LinearBid_Min:
                    AdditiveEvidenceProgress.D_LinearBid_Min = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.D_LinearBid_Max:
                    AdditiveEvidenceProgress.D_LinearBid_Max = action;
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
                    if (AdditiveEvidenceDefinition.Options.LinearBids)
                    {
                        if (!(AdditiveEvidenceDefinition.Options.Alpha_Bias > 0))
                            AdditiveEvidenceProgress.GameComplete = true; // b/c using linear bids, there are no more offers, and there are also no more chance decisions
                        if (AdditiveEvidenceProgress.D_LinearBid_Continuous >= AdditiveEvidenceProgress.P_LinearBid_Continuous)
                            AdditiveEvidenceProgress.GameComplete = true;
                    }
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias:
                    AdditiveEvidenceProgress.Chance_Plaintiff_Bias = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                    AdditiveEvidenceProgress.Chance_Defendant_Bias = action;
                    if (AdditiveEvidenceDefinition.Options.LinearBids)
                    {
                        if (!(AdditiveEvidenceDefinition.Options.Alpha_Bias > 0 && AdditiveEvidenceDefinition.Options.Alpha_Neither_Bias > 0))
                            AdditiveEvidenceProgress.GameComplete = true; // b/c using linear bids, there are no more offers, and there are also no more chance decisions
                        if (AdditiveEvidenceProgress.D_LinearBid_Continuous >= AdditiveEvidenceProgress.P_LinearBid_Continuous)
                            AdditiveEvidenceProgress.GameComplete = true;
                    }
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

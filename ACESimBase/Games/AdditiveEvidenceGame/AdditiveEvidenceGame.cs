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
        public AdditiveEvidenceGameDefinition MyDefinition => (AdditiveEvidenceGameDefinition)GameDefinition;
        public AdditiveEvidenceGameProgress MyProgress => (AdditiveEvidenceGameProgress)Progress;

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
                case (byte)AdditiveEvidenceGameDecisions.PQuit:
                    MyProgress.PQuits = action == 1;
                    if (action == 1)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.DQuit:
                    MyProgress.DQuits = action == 1;
                    if (action == 1)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Quality:
                    MyProgress.Chance_Plaintiff_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Quality:
                    MyProgress.Chance_Defendant_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Plaintiff_Bias:
                    MyProgress.Chance_Plaintiff_Bias = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Defendant_Bias:
                    MyProgress.Chance_Defendant_Bias = action;
                    break;


                case (byte)AdditiveEvidenceGameDecisions.POffer:
                    if (action == 1)
                    {
                        var DEBUG = 0;
                    }
                    MyProgress.POffer = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.DOffer:
                    MyProgress.DOffer = action;
                    if (action >= MyProgress.POffer)
                        MyProgress.GameComplete = true;
                    break;

                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Quality:
                    MyProgress.Chance_Neither_Quality = action;
                    break;
                case (byte)AdditiveEvidenceGameDecisions.Chance_Neither_Bias:
                    MyProgress.Chance_Neither_Bias = action;
                    MyProgress.GameComplete = true;
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
            MyProgress.CalculateGameOutcome();

            CalculateSocialWelfareOutcomes();

            base.FinalProcessing();
        }

        private void CalculateSocialWelfareOutcomes()
        {
            
        }
    }
}

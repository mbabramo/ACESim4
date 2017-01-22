using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGame : Game
    {
        int CurveNumber; // allows information to be passed from Play() to makeDecision()

        /// <summary>
        /// <para>
        /// This method implements gameplay for the PatentDamages game.
        /// </para>
        /// <para>
        /// The game uses a set of a set of Curves.  
        /// As an example the set might be [ ![0,1,4], ![2,3,5], ![4,5,8] ].
        /// The goal of the first decision in the game is to evolve a formula for predicting the last number
        /// in terms of the others.
        /// Thus in this example, there would be two input variables.  The game can evaluate a strategy simply by
        /// plugging in the input variables for each of the lists, calculating for each the absolute value of
        /// the difference between the actual last number and the strategy's output value, and summing these differences.
        /// </para>
        /// </summary>
        public override void PrepareForOrMakeCurrentDecision()
        {
            if (Progress.GameComplete)
                return;

            PatentDamagesGameInputs CurveGameInputs = (PatentDamagesGameInputs) GameInputs;
            PatentDamagesGameProgressInfo CurveGameProgress = (PatentDamagesGameProgressInfo)Progress;

            switch (CurrentDecisionIndex)
            {
                case 0:
                    break;
                case 1:
                    break;
                default:
                    // Do Nothing; there are only two decisions
                    Progress.GameComplete = true;
                    break;
            }
        }


        /// <summary>
        /// This method returns the strategy inputs for the current decision being calculated.
        /// </summary>
        protected override List<double> GetDecisionInputs()
        {
            PatentDamagesGameInputs CurveGameInputs = (PatentDamagesGameInputs)GameInputs;
            PatentDamagesGameProgressInfo CurveGameProgress = (PatentDamagesGameProgressInfo)Progress;

            double[] inputs = null;

            switch (CurrentDecisionIndex)
            {
                case 0:
                    inputs = new double[] { }; 
                    break;
                case 1:
                default:
                    inputs = new double[] { };
                    break;

            }
            
            RecordInputsIfNecessary(inputs.ToList());

            return inputs.ToList();
        }
    }
}

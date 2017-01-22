using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class CurveFittingGame : Game
    {
        int CurveNumber; // allows information to be passed from Play() to makeDecision()

        /// <summary>
        /// <para>
        /// This method implements gameplay for the CurveFitting game.
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

            CurveFittingGameInputs CurveGameInputs = (CurveFittingGameInputs) GameInputs;
            CurveFittingGameProgressInfo CurveGameProgress = (CurveFittingGameProgressInfo)Progress;

            switch (CurrentDecisionIndex)
            {
                case 0:
                    double totalScore = 0; // Lower is better
                    double obfuscation = CurveGameInputs.Obfuscation;
                    double guess = MakeDecision();
                    // NOTE: When switching among number of inputs, must change CurveFittingGameInputs.cs and getDecisionInputs.
                    //double target = -17 + 0.1 * CurveGameInputs.x + 10.0 * CurveGameInputs.y - 0.1 * CurveGameInputs.z + 1 * CurveGameInputs.v + 4 * CurveGameInputs.w;
                    //double target = (CurveGameInputs.x < 3) ? 5 * CurveGameInputs.y - CurveGameInputs.x : 50;
                
                //double target = CurveGameInputs.x * 4 / (CurveGameInputs.y * CurveGameInputs.y + 0.1 * CurveGameInputs.x);
                   // double target = 25 * (CurveGameInputs.x - 1.4) * (CurveGameInputs.y + 1.2) * (CurveGameInputs.z + 4.2) / (CurveGameInputs.v * CurveGameInputs.w * (double)Math.Sqrt((double)CurveGameInputs.w) + 8);

                    // double target = -17 + 0.1 * CurveGameInputs.x + 10.0 * CurveGameInputs.y - 0.1 * CurveGameInputs.z + 1 * CurveGameInputs.v + 4 * CurveGameInputs.w + 0.001 * CurveGameInputs.x * CurveGameInputs.x + 3 * CurveGameInputs.y * CurveGameInputs.z + 50.5 / (3.7 + 0.2 * (double)(Math.Pow(Math.Abs(CurveGameInputs.x - 3), 2.1)) + 0.4 * (double)(Math.Pow(Math.Abs(CurveGameInputs.y - 1.5), 1.6)));
                    //double target = 100 + (double) 3 * (CurveGameInputs.x) + (double) (CurveGameInputs.x) * (CurveGameInputs.x);
                    // double target = 20;
                // double target = (CurveGameInputs.x > 2.5) ? 20 : 10;
                    //double target = 10 * CurveGameInputs.x + 0.33 * CurveGameInputs.y + 7 + 50.5 / (3.7 + 0.2 * (double) (Math.Pow(Math.Abs(CurveGameInputs.x - 3), 2.1)) + 0.4 * (double) (Math.Pow(Math.Abs(CurveGameInputs.y - 1.5), 1.6)) );
                    //double target = 5 * CurveGameInputs.x + 4 * CurveGameInputs.y ; // 9 / (1 + CurveGameInputs.x * (double) Math.Sqrt(CurveGameInputs.y));
                    //double target = (double) 6.0 * CurveGameInputs.x + (double) 7.0 * CurveGameInputs.y + (double) (50 * Math.Sin(CurveGameInputs.x) * Math.Sin(CurveGameInputs.x) * Math.Cos(CurveGameInputs.y));
                    
                    // These are some tests of multiplicative warps.
                    //double target = CurveGameInputs.x + CurveGameInputs.y;
                    //double target = CurveGameInputs.x * CurveGameInputs.x + CurveGameInputs.y * CurveGameInputs.y;
                    //if (CurveGameInputs.x > 3)
                    //    target *= 4;
                    //double target = (5 / (1 + 3 * (CurveGameInputs.x - 3) * (CurveGameInputs.x - 3)))*(1 / (1 + 2 * Math.Min((CurveGameInputs.y - 3),0)*Math.Min((CurveGameInputs.y - 3),0))) + 3 / ( 2 + Math.Pow(Math.Abs(CurveGameInputs.x - 1.5),1.2) + Math.Pow(Math.Abs(CurveGameInputs.y - 0.5),1.6));
                    
                    //double target = (5 / (1 + 3 * (CurveGameInputs.x - 2) * (CurveGameInputs.x - 2)));
                    //////double ytrans = (CurveGameInputs.y > 3) ? CurveGameInputs.y : 1000;
                    //double ytrans = Math.Max((CurveGameInputs.y - 3), 0);
                    //target *= 1 / (1 + 2 * (double) Math.Pow((double)ytrans, (double) 0.2));
                    //target += (5 / (1 + 3 * (CurveGameInputs.x - 4) * (CurveGameInputs.x - 4))); // this is now bimodal, but there will be no decrease based on y for this

                    double target = Math.Sin(CurveGameInputs.x);

                    //double item1 = CurveGameInputs.x;
                    //double item2 = 2.5;
                    //double weightItem1 = 1.0 - CurveGameInputs.y / 5; // start weight at 1.0 and then go to 0 as CurveGameInputs.y goes up to 5
                    //double target = item1 * weightItem1 + item2 * (1.0 - weightItem1); // should give a 45 degree line that gradually rotates clockwise down to a horizontal line

                    // this is a test of a shape that should be like the brute force occupation
                    //double likeObfNum = CurveGameInputs.x / 5.0;
                    //double likeStdDev = CurveGameInputs.y / 10.0;
                    //ObfuscationGame obfGame = new ObfuscationGame();
                    //double target = ObfuscationGame.ObfuscationBruteForceCalculation.GetValue(likeObfNum, likeStdDev);

                    //double target = 3.0123456789;

                    //target -= obfuscation; // uncomment this to eliminate effect of obfuscation term. this is useful if we want to see how good a job we can do evolving a shape with no obfuscation, and can help clarify the extent to which any difficulty in evolution is the result of obfuscation.
                    target += obfuscation;
                    double difference = target - guess;
                    totalScore += difference * difference;

                    CurveGameProgress.TotalScoreDecision1 = totalScore;
                    Strategies[CurrentDecisionIndex.Value].AddScore(totalScore, WeightOfScoreInWeightedAverage);
                    break;
                case 1:
                    double guessScoreOfPreviousDecision = MakeDecision();
                    double difference2 = guessScoreOfPreviousDecision - CurveGameProgress.TotalScoreDecision1;
                    CurveGameProgress.TotalScoreDecision2 = difference2 * difference2;
                    Strategies[CurrentDecisionIndex.Value].AddScore(CurveGameProgress.TotalScoreDecision2, WeightOfScoreInWeightedAverage);
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
            CurveFittingGameInputs CurveGameInputs = (CurveFittingGameInputs)GameInputs;
            CurveFittingGameProgressInfo CurveGameProgress = (CurveFittingGameProgressInfo)Progress;

            // Currently the inputs do not depend upon the decision number (we only have one decision.
            //int decisionNumber = (int)progress.CurrentDecisionNumber;

            double[] inputs = null;

            switch (CurrentDecisionIndex)
            {
                case 0:
                    // CurveFittingGame's Curves are in the format of a list of double inputs, followed by one double, the target output.
                    // So for the current Curve, return all but the last double.
                    inputs = new double[] { CurveGameInputs.x}; //, CurveGameInputs.y}; //, CurveGameInputs.z, CurveGameInputs.v, CurveGameInputs.w };
                    break;
                case 1:
                default:
                    inputs = new double[] { CurveGameInputs.x }; //, CurveGameInputs.y };
                    break;

            }
            
            RecordInputsIfNecessary(inputs.ToList());

            return inputs.ToList();
        }
    }
}

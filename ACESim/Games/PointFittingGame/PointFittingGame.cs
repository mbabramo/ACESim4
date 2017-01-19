using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    public class PointFittingGame : Game
    {
        int pointNumber; // allows information to be passed from Play() to makeDecision()

        /// <summary>
        /// <para>
        /// This method implements gameplay for the PointFitting game.
        /// </para>
        /// <para>
        /// The game uses a set of a set of points.  
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

            PointFittingGameInputs pointGameInputs = (PointFittingGameInputs) GameInputs;
            PointFittingGameProgressInfo pointGameProgress = (PointFittingGameProgressInfo)Progress;

            // Since the 0th decision in this PointFitting game is not a single call to score the strategy,
            // but one call for each point set, I need to handle its calculation and scoring differently
            switch (CurrentDecisionIndex)
            {
                case 0:
                    double totalScore = 0; // Lower is better
                    for (
                        pointNumber = 0;
                        pointNumber < pointGameInputs.Points.Count;
                        pointNumber++)
                    {
                        double obfuscation = pointGameInputs.Obfuscations[pointNumber];
                        double guess = MakeDecision() ;
                        List<double> point = pointGameInputs.Points[pointNumber];
                        double target = point.Last() + obfuscation;
                        totalScore += Math.Abs(target - guess);
                    }

                    pointGameProgress.TotalScoreDecision1 = totalScore;
                    Strategies[CurrentDecisionIndex.Value].AddScore(totalScore, WeightOfScoreInWeightedAverage);
                    break;
                case 1:
                    Progress.GameComplete = true; // TEMPORARY -- remove this line after getting second decision to work
                    //double guessScoreOfPreviousDecision = makeDecision();
                    //pointGameProgress.TotalScoreDecision2 = Math.Abs(guessScoreOfPreviousDecision - pointGameProgress.TotalScoreDecision1);
                    //strategies[CurrentDecisionNumber.Value].AddScore(pointGameProgress.TotalScoreDecision2);
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
            PointFittingGameInputs pointGameInputs = (PointFittingGameInputs)GameInputs;
            PointFittingGameProgressInfo pointGameProgress = (PointFittingGameProgressInfo)Progress;

            // Currently the inputs do not depend upon the decision number (we only have one decision.
            //int decisionNumber = (int)progress.CurrentDecisionNumber;

            List<double> inputs = null;

            switch (CurrentDecisionIndex)
            {
                case 0:
                    // PointFittingGame's points are in the format of a list of double inputs, followed by one double, the target output.
                    // So for the current point, return all but the last double.
                    List<double> point = pointGameInputs.Points[pointNumber];
                    int inputDimensions = point.Count - 1;
                    inputs = point.Take(inputDimensions).ToList();
                    break;
                case 1:
                default:
                    inputs = null;
                    break;

            }
            if (CurrentDecisionIndex == RecordInputsForDecisionNumber && !PreparationPhase)
                RecordedInputs.Add(inputs.ToArray());
            return inputs;
        }
    }
}

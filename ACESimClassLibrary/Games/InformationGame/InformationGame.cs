using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class InformationGame : Game
    {
        double[] previousDecisions;

        public InformationGame()
        {
            previousDecisions = new double[2];
        }

        public override void Play(int? recordInputsForDecisionNum)
        {
            if (Progress.Complete)
                return;

            InformationGameInputs infoGameInputs = (InformationGameInputs)gameInputs;
            InformationGameProgressInfo infoGameProgress = (InformationGameProgressInfo)Progress;

            double decision;
            switch (Progress.CurrentDecisionNumber)
            {
                case 0:
                case 1:
                    decision = Forecast.Score(genomes[0], genomes[1], Progress.CurrentDecisionNumber == 0, 
                        getDecisionInputs(Progress.CurrentDecisionNumber == recordInputsForDecisionNum), infoGameInputs.Target);
                    previousDecisions[Progress.CurrentDecisionNumber.Value] = decision;

                    genomes[Progress.CurrentDecisionNumber.Value].AddScore(decision);

                    Progress.SetCurrentDecisionCompleted();
                    break;

                case 2:
                case 3:
                    decision = Forecast.Score(genomes[2], genomes[3], Progress.CurrentDecisionNumber == 2,
                        getDecisionInputs(Progress.CurrentDecisionNumber == recordInputsForDecisionNum), infoGameInputs.Target);

                    genomes[Progress.CurrentDecisionNumber.Value].AddScore(decision);

                    Progress.SetCurrentDecisionCompleted();
                    break;

                default:
                    // Do Nothing; there are only four decisions
                    Progress.Complete = true;
                    break;
            }
        }

        public override bool DoPreplay(int decisionNumber)
        {
            return true;
        }

        /// <summary>
        /// This method returns the genome inputs for the current decision being calculated.
        /// </summary>
        protected override double[] getDecisionInputs()
        {
            InformationGameInputs infoGameInputs = (InformationGameInputs)gameInputs;

            switch (Progress.CurrentDecisionNumber)
            {
                case 0:
                case 1:
                    double targetObfuscation = infoGameInputs.TargetObfuscationMean + 
                        infoGameInputs.TargetObfuscationStdDev * (double)alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
                    double obfuscatedTarget = infoGameInputs.Target + targetObfuscation;
                    return new double[] { obfuscatedTarget, infoGameInputs.TargetObfuscationStdDev };

                case 2:
                case 3:
                    double anotherObfuscation = infoGameInputs.AnotherObfuscationMean +
                        infoGameInputs.AnotherObfuscationStdDev * (double)alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
                    double anotherObfuscatedTarget = infoGameInputs.Target + anotherObfuscation;
                    return new double[] { previousDecisions[0], previousDecisions[1], anotherObfuscatedTarget, 
                            infoGameInputs.AnotherObfuscationStdDev };

                default:
                    return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class CurveFitting5DGameProgressInfo : GameProgress
    {
        public double Target;
        public double Guess;
        public double TotalScoreDecision1; // must be defined as fields rather than as properties to include in automatic report generation.
        public double TotalScoreDecision2;

        public override GameProgress DeepCopy()
        {
            CurveFitting5DGameProgressInfo copy = new CurveFitting5DGameProgressInfo();
            copy.TotalScoreDecision1 = this.TotalScoreDecision1;
            copy.TotalScoreDecision2 = this.TotalScoreDecision2;
            copy.Target = this.Target;
            copy.Guess = this.Guess;

            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);
            return copy;
        }
    }
}

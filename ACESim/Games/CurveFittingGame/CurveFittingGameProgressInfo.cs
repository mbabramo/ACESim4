﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class CurveFittingGameProgressInfo : GameProgress
    {
        public double TotalScoreDecision1; // must be defined as fields rather than as properties to include in automatic report generation.
        public double TotalScoreDecision2;

        public override GameProgress DeepCopy()
        {
            CurveFittingGameProgressInfo copy = new CurveFittingGameProgressInfo();
            copy.TotalScoreDecision1 = this.TotalScoreDecision1;
            copy.TotalScoreDecision2 = this.TotalScoreDecision2;

            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }
    }
}

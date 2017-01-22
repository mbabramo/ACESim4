﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameProgressInfo : GameProgress
    {
        public double TotalScoreDecision1; // must be defined as fields rather than as properties to include in automatic report generation.
        public double TotalScoreDecision2;

        public override GameProgress DeepCopy()
        {
            PatentDamagesGameProgressInfo copy = new PatentDamagesGameProgressInfo();
            copy.TotalScoreDecision1 = this.TotalScoreDecision1;
            copy.TotalScoreDecision2 = this.TotalScoreDecision2;

            copy.GameComplete = this.GameComplete;

            base.CopyFieldInfo(copy);

            return copy;
        }
    }
}

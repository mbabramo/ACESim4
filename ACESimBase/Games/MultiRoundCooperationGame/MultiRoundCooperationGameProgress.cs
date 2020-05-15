using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class MultiRoundCooperationGameProgress : GameProgress
    {
        public MultiRoundCooperationGameProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }
        public List<byte> P1Decisions = new List<byte>(), P2Decisions = new List<byte>(), ChanceDecision = new List<byte>();

        public override GameProgress DeepCopy()
        {
            MultiRoundCooperationGameProgress copy = new MultiRoundCooperationGameProgress(FullHistoryRequired);

            copy.GameComplete = this.GameComplete;
            CopyFieldInfo(copy);

            return copy;
        }

        internal override void CopyFieldInfo(GameProgress copy)
        {
            base.CopyFieldInfo(copy);
            MultiRoundCooperationGameProgress LeducGameProgress = (MultiRoundCooperationGameProgress)copy;
            LeducGameProgress.P1Decisions = P1Decisions.ToList();
            LeducGameProgress.P2Decisions = P2Decisions.ToList();
            LeducGameProgress.ChanceDecision = ChanceDecision.ToList();
        }

        public override double[] GetNonChancePlayerUtilities()
        {
            double p1ScoreSoFar = 0, p2ScoreSoFar = 0;
            bool nonCooperationHasOccurred = false;
            int numRounds = P1Decisions.Count;
            for (int i = 0; i < numRounds; i++)
            {
                bool p1Cooperates = P1Decisions[i] == 1;
                bool p2Cooperates = P2Decisions[i] == 1;
                if (p1Cooperates && p2Cooperates)
                {
                    p1ScoreSoFar += -1;
                    p2ScoreSoFar += -1;
                }
                else if (p1Cooperates && !p2Cooperates)
                {
                    p1ScoreSoFar += -3;
                    p2ScoreSoFar += 0;
                    nonCooperationHasOccurred = true;
                }
                else if (!p1Cooperates && p2Cooperates)
                {
                    p1ScoreSoFar += 0;
                    p2ScoreSoFar += -3;
                    nonCooperationHasOccurred = true;
                }
                else if (!p1Cooperates && !p2Cooperates)
                {
                    p1ScoreSoFar += -2;
                    p2ScoreSoFar += -2;
                    nonCooperationHasOccurred = true;
                }
                if (MultiRoundCooperationGameDefinition.AllRoundCooperationBonus &&  i == numRounds - 1 && !nonCooperationHasOccurred)
                {
                    p1ScoreSoFar += numRounds + 1;
                    p2ScoreSoFar += numRounds + 1;
                }
            }
            return new double[] {p1ScoreSoFar, p2ScoreSoFar};
        }
    }
}

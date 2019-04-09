using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class LeducGameProgress : GameProgress
    {
        public LeducGameState GameState;

        public override GameProgress DeepCopy()
        {
            LeducGameProgress copy = new LeducGameProgress();

            // copy.GameComplete = this.GameComplete;
            CopyFieldInfo(copy);

            return copy;
        }

        internal override void CopyFieldInfo(GameProgress copy)
        {
            base.CopyFieldInfo(copy);
            LeducGameProgress LeducGameProgress = (LeducGameProgress)copy;
            LeducGameProgress.GameState = GameState?.DeepCopy();
        }

        public override double[] GetNonChancePlayerUtilities()
        {
            return new double[] { GameState.P1Gain, GameState.P2Gain };
        }
    }
}

using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class LeducGame : Game
    {
        public LeducGameDefinition MyDefinition => (LeducGameDefinition)GameDefinition;
        public LeducGameProgress MyProgress => (LeducGameProgress)Progress;

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            if (currentDecisionByteCode == (byte)LeducGameDecisions.P1Decision)
                MyProgress.P1Decision = action;
            else if (currentDecisionByteCode == (byte)LeducGameDecisions.P2Decision)
                MyProgress.P2Decision = action;
            else if (currentDecisionByteCode == (byte)LeducGameDecisions.Chance)
                MyProgress.ChanceDecision = action;
        }


        public override void FinalProcessing()
        {
            base.FinalProcessing();
        }
    }
}

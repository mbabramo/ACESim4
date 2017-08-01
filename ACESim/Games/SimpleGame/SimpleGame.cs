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
    public class SimpleGame : Game
    {
        public SimpleGameDefinition MyDefinition => (SimpleGameDefinition)GameDefinition;
        public SimpleGameProgress MyProgress => (SimpleGameProgress)Progress;

        public override bool DecisionIsNeeded(Decision currentDecision)
        {
            return true;
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            if (currentDecisionByteCode == (byte)SimpleGameDecisions.P1Decision)
                MyProgress.P1Decision = action;
            else if (currentDecisionByteCode == (byte)SimpleGameDecisions.P2Decision)
                MyProgress.P2Decision = action;
            else if (currentDecisionByteCode == (byte)SimpleGameDecisions.Chance)
                MyProgress.ChanceDecision = action;
        }


        public override void FinalProcessing()
        {
            base.FinalProcessing();
        }
    }
}

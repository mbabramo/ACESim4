using System;
using System.Collections.Generic;

namespace ACESim
{
    public class DefineActions
    {
        public static Func<Decision, GameProgress, byte> ForReportOverride(
            List<(byte decision, byte action)> decisionsAndActions,
            List<(byte decision, byte customInfo, byte action)> actionsDifferentiatedForSameDecision = null)
        {
            return ForGamePlay(decisionsAndActions, actionsDifferentiatedForSameDecision, true);
        }

        public static Func<Decision, GameProgress, byte> ForTest(
            List<(byte decision, byte action)> decisionsAndActions,
            List<(byte decision, byte customInfo, byte action)> actionsDifferentiatedForSameDecision = null)
        {
            return ForGamePlay(decisionsAndActions, actionsDifferentiatedForSameDecision, false);
        }

        private static Func<Decision, GameProgress, byte> ForGamePlay(
            List<(byte decision, byte action)> decisionsAndActions, List<(byte decision, byte customInfo, byte action)> actionsDifferentiatedForSameDecision = null, bool useDefaultIfNotListed = false)
        {
            return (decision, gameProgress) =>
            {
                foreach (var decisionAndAction in decisionsAndActions)
                    if (decisionAndAction.decision == decision.DecisionByteCode)
                        return decisionAndAction.action;
                if (actionsDifferentiatedForSameDecision != null)
                    foreach (var decisionCustomByteAndAction in actionsDifferentiatedForSameDecision)
                        if (decisionCustomByteAndAction.decision == decision.DecisionByteCode && decisionCustomByteAndAction.customInfo == decision.CustomByte)
                            return decisionCustomByteAndAction.action;
                if (useDefaultIfNotListed)
                    return 0; // this is appropriate when using an action generator as an override during game play --> returning 0 means that we'll play the default action
                else
                    throw new NotImplementedException($"Must define action for decision {decision.DecisionByteCode} (custom byte {decision.CustomByte})"); // this is appropriate during a test, where we should specify each decision
            };
        }
    }
}
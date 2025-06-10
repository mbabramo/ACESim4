using ACESimBase.GameSolvingSupport.GameTree;
using System;
using System.Collections.Generic;
using System.Linq;
using Tensorflow;

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
                {
                    if (decisionAndAction.decision == decision.DecisionByteCode)
                        return decisionAndAction.action;
                }
                if (actionsDifferentiatedForSameDecision != null)
                    foreach (var decisionCustomByteAndAction in actionsDifferentiatedForSameDecision)
                    {
                        if (decisionCustomByteAndAction.decision == decision.DecisionByteCode && decisionCustomByteAndAction.customInfo == decision.CustomByte)
                            return decisionCustomByteAndAction.action;
                    }
                if (useDefaultIfNotListed)
                    return 0; // this is appropriate when using an action generator as an override during game play --> returning 0 means that we'll play the default action
                else
                    throw new NotImplementedException($"Must define action for decision {decision.DecisionByteCode} (custom byte {decision.CustomByte})"); // this is appropriate during a test, where we should specify each decision
            };
        }

        public static Func<Decision, GameProgress, byte> GamePathToActionFunction(
            RecordGamePathsProcessor.GamePath gamePath)
        {
            var primary = new List<(byte decision, byte action)>();
            var differentiated = new List<(byte decision, byte customInfo, byte action)>();

            var seenDecisionCodes = new HashSet<byte>();
            var seenCustomPairs = new HashSet<(byte decision, byte customInfo)>();

            foreach (var step in gamePath.Steps)
            {
                var decision = step.FromNode switch
                {
                    ChanceNode c => c.Decision,
                    InformationSetNode i => i.Decision,
                    _ => null
                };

                if (decision is null)
                    continue; // step came from a non-decision node

                var key = decision.DecisionByteCode;
                var custom = decision.CustomByte;
                var action = step.ActionIndex;

                if (!seenDecisionCodes.Contains(key))
                {
                    primary.Add((key, action));
                    seenDecisionCodes.Add(key);
                    seenCustomPairs.Add((key, custom));
                }
                else if (!seenCustomPairs.Contains((key, custom)))
                {
                    differentiated.Add((key, custom, action));
                    seenCustomPairs.Add((key, custom));
                }
            }

            return DefineActions.ForTest(primary, differentiated.Count > 0 ? differentiated : null);
        }

    }
}
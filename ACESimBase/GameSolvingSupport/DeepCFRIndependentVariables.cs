using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRIndependentVariables
    {
        /// <summary>
        /// A code identifying the player. It is possible to include multiple players within the same regression. A binary variable will be used to represent all players but one. 
        /// </summary>
        public byte Player;
        /// <summary>
        /// A code identifying the decision type. A single regression may include observations with different byte codes, for example representing different stages in a game. Binary variables will be used to represent the different possible decision byte codes (except for one).
        /// </summary>
        public byte DecisionIndex;
        /// <summary>
        /// The action chosen by the player at the information set.
        /// </summary>
        public byte ActionChosen;
        /// <summary>
        /// The information set will consist of actions of players that the player knows about, including but not limited to all of the player's own actions. Each of these will be an independent variable.
        /// If some decision indices exist for some players and not others, then there will also be an independent variable indicating whether the information is available.
        /// </summary>
        public List<(byte decisionIndex, byte information)> InformationSet;
        /// <summary>
        /// If we change game parameters in each observation, then the game parameters for this observation can be specified here. If different game parameters are used for early iterations than for late iterations, then both sets can be included here. Game parameters may also include some indication of the extent to which each player takes other players' utilities into account.
        /// </summary>
        public List<float> GameParameters;

        public DeepCFRIndependentVariables()
        {

        }

        public DeepCFRIndependentVariables(byte player, byte decisionIndex, List<(byte decisionIndex, byte information)> informationSet, byte actionChosen, List<float> gameParameters)
        {
            Player = player;
            DecisionIndex = decisionIndex;
            InformationSet = informationSet;
            if (informationSet.Count() != informationSet.Select(x => x.decisionIndex).Distinct().Count())
                throw new Exception("DEBUG");
            ActionChosen = actionChosen;
            GameParameters = gameParameters;
        }

        public IEnumerable<(byte decisionIndex, byte information)> InformationSetPlusActionChosen()
        {
            foreach (var item in InformationSet)
                yield return item;
            yield return (DecisionIndex, ActionChosen);
        }

        public static List<byte> GetIncludedDecisionIndices(IEnumerable<DeepCFRIndependentVariables> independentVariablesSets)
        {
            HashSet<byte> includedDecisionIndices = new HashSet<byte>();
            foreach (var independentVariables in independentVariablesSets)
                foreach (var decisionIndex in independentVariables.InformationSetPlusActionChosen().Select(x => x.decisionIndex))
                    includedDecisionIndices.Add(decisionIndex);
            var result = includedDecisionIndices.OrderBy(x => x).ToList();
            return result;
        }

        public float[] AsArray(List<byte> includedDecisionIndices)
        {
            int informationSetSize = InformationSetPlusActionChosen()?.Count() ?? 0;
            int numGameParameters = GameParameters?.Count() ?? 0;
            int arraySize = 2 + includedDecisionIndices.Count() * 2 + numGameParameters;
            float[] result = new float[arraySize];
            int resultIndex = 0;
            result[resultIndex++] = Player;
            result[resultIndex++] = DecisionIndex;
            int informationSetPlusActionIndex = 0;
            var informationSetPlusAction = InformationSetPlusActionChosen().ToArray();
            foreach (byte decisionIndex in includedDecisionIndices)
            {
                int? nextInformationSetDecisionIndex = informationSetPlusActionIndex >= informationSetPlusAction.Count() ? null : (int?)informationSetPlusAction[informationSetPlusActionIndex].decisionIndex;
                if (nextInformationSetDecisionIndex == null || nextInformationSetDecisionIndex > decisionIndex)
                { 
                    // the decisionIndex is not included in the information set.
                    result[resultIndex++] = 0; // i.e., this information is unavailable
                    result[resultIndex++] = 0; // this is where information would have gone if available
                }
                else
                {// the decisionIndex is included in the information set. Note this if necessary. Then, record the information itself in the result.
                    result[resultIndex++] = 1.0f; // i.e., information is available
                    if (resultIndex == result.Length)
                        throw new Exception("DEBUG");
                    result[resultIndex++] = informationSetPlusAction[informationSetPlusActionIndex++].information;
                }
            }
            // Finally, add the game parameters
            for (int i = 0; i < numGameParameters; i++)
                result[resultIndex + i] = GameParameters[i];
            return result;
        }
    }
}

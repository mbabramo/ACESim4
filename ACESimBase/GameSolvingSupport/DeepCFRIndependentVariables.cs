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
        /// The information set will consist of actions of players that the player knows about, including but not limited to all of the player's own actions. Each of these will be an independent variable.
        /// If some decision indices exist for some players and not others, then there will also be an independent variable indicating whether the information is available.
        /// </summary>
        public List<(byte decisionIndex, byte information)> InformationSet;
        /// <summary>
        /// The action chosen by the player at the information set.
        /// </summary>
        public byte ActionChosen;
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

        public static List<(byte decisionIndex, bool includedForAll)> GetIncludedDecisionIndices(IEnumerable<DeepCFRIndependentVariables> independentVariablesSets)
        {
            HashSet<byte> includedDecisionIndices = new HashSet<byte>();
            foreach (var independentVariables in independentVariablesSets)
                foreach (var decisionIndex in independentVariables.InformationSet.Select(x => x.decisionIndex))
                    includedDecisionIndices.Add(decisionIndex);
            List<(byte decisionIndex, bool includedForAll)> result = 
                includedDecisionIndices
                .Select(decisionIndex => (decisionIndex, independentVariablesSets.All(
                    iv => iv.InformationSet.Any(
                        item => item.decisionIndex == decisionIndex)
                    )
                )).ToList();
            return result;
        }

        public float[] AsArray(bool includePlayer, bool includeDecision, List<(byte decisionIndex, bool includedForAll)> includedDecisionIndices)
        {
            int informationSetSize = InformationSet?.Count() ?? 0;
            int numGameParameters = GameParameters?.Count() ?? 0;
            int arraySize = (includePlayer ? 1 : 0) + (includeDecision ? 1 : 0) + includedDecisionIndices.Count() + includedDecisionIndices.Where(x => x.includedForAll == false).Count() + numGameParameters;
            float[] result = new float[arraySize];
            int resultIndex = 0;
            if (includePlayer)
                result[resultIndex++] = Player;
            if (includeDecision)
                result[resultIndex++] = DecisionIndex;
            result[resultIndex++] = ActionChosen;
            int informationSetIndex = 0;
            foreach ((byte decisionIndex, bool includedForAll) in includedDecisionIndices)
            {
                int? nextInformationSetDecisionIndex = informationSetIndex >= InformationSet.Count() ? null : (int?) InformationSet[informationSetIndex].decisionIndex;
                if (nextInformationSetDecisionIndex == null || nextInformationSetDecisionIndex > decisionIndex)
                { // the decisionIndex is not included in the information set. Note this if necessary.
                    if (!includedForAll)
                    {
                        result[resultIndex++] = 0;
                    }
                }
                else
                {// the decisionIndex is included in the information set. Note this if necessary. Then, record the information itself in the result.
                    if (!includedForAll)
                    {
                        result[resultIndex++] = 1.0f;
                    }
                    if (resultIndex == result.Length)
                        throw new Exception("DEBUG");
                    result[resultIndex++] = InformationSet[informationSetIndex++].information;
                }
            }
            // Finally, add the game parameters
            for (int i = 0; i < numGameParameters; i++)
                result[resultIndex + i] = GameParameters[i];
            return result;
        }
    }
}

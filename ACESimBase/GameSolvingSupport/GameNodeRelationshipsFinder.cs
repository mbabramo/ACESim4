using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{

    public class GameNodeRelationshipsFinder : ITreeNodeProcessor<GameNodeRelationshipsFinder.ForwardInfo, bool /* ignored */>
    {

        public record ForwardInfo(int gameNodeRelationshipID, bool[] nextIsZero);

        public List<GameNodeRelationship> Relationships;
        public bool SequenceFormCutOffProbabilityZeroNodes;
        public int MaxIntegralUtility;

        public GameNodeRelationshipsFinder(IGameState root, bool sequenceFormCutOffProbabilityZeroNodes, int maxIntegralUtility)
        {
            Relationships =  new List<GameNodeRelationship>() { new GameNodeRelationship(0, root, null, null) };
            SequenceFormCutOffProbabilityZeroNodes = sequenceFormCutOffProbabilityZeroNodes;
            MaxIntegralUtility = maxIntegralUtility;
        }

        public bool ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<bool> fromSuccessors, int distributorChanceInputs)
        {
            return true;
        }

        public ForwardInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor, int distributorChanceInputs)
        {
            int id = 0;
            bool isZero = false;
            if (predecessorAction != 0) // i.e., this is not the root, which we already added with nulls for parent and predecessor action
            {
                isZero = fromPredecessor.nextIsZero[predecessorAction];
                if (!isZero || !SequenceFormCutOffProbabilityZeroNodes)
                {
                    id = Relationships.Count();
                    Relationships.Add(new GameNodeRelationship(id, chanceNode, fromPredecessor.gameNodeRelationshipID, predecessorAction));
                }
            }
            var probabilitiesAsRationals = chanceNode.GetProbabilitiesAsRationals(!SequenceFormCutOffProbabilityZeroNodes, MaxIntegralUtility);
            bool[] nextIsZero = probabilitiesAsRationals.Select(x => isZero || x.IsZero).ToArray();
            return new ForwardInfo(id, nextIsZero);
        }

        public bool FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor)
        {
            bool isZero = fromPredecessor.nextIsZero[predecessorAction];
            if (!isZero || !SequenceFormCutOffProbabilityZeroNodes)
            {
                int id = Relationships.Count();
                Relationships.Add(new GameNodeRelationship(id, finalUtilities, fromPredecessor.gameNodeRelationshipID, predecessorAction));
            }

            return true;
        }

        public bool InformationSet_Backward(InformationSetNode informationSet, IEnumerable<bool> fromSuccessors)
        {
            return true;
        }

        public ForwardInfo InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor)
        {
            int id = 0;
            bool isZero = false;
            if (predecessorAction != 0)
            {
                isZero = fromPredecessor.nextIsZero[predecessorAction];
                if (!isZero || !SequenceFormCutOffProbabilityZeroNodes)
                {
                    id = Relationships.Count();
                    Relationships.Add(new GameNodeRelationship(id, informationSet, fromPredecessor.gameNodeRelationshipID, predecessorAction));
                }
            }
            bool[] nextIsZero = Enumerable.Range(0, informationSet.GetNumPossibleActions()).Select(x => isZero).ToArray();
            return new ForwardInfo(id, nextIsZero);
        }
    }
}

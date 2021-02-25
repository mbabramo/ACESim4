using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class GameNodeRelationshipsFinder : ITreeNodeProcessor<int, bool /* ignored */>
    {
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

        public int ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, int fromPredecessor, int distributorChanceInputs)
        {
            int id = 0;
            if (predecessorAction != 0) // i.e., this is not the root, which we already added with nulls for parent and predecessor action
            {
                var probabilitiesAsRationals = chanceNode.GetProbabilitiesAsRationals(!SequenceFormCutOffProbabilityZeroNodes, MaxIntegralUtility);
                id = Relationships.Count();
                Relationships.Add(new GameNodeRelationship(id, chanceNode, fromPredecessor, predecessorAction));
            }
            return id;
        }

        public bool FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, int fromPredecessor)
        {
            int id = Relationships.Count();
            Relationships.Add(new GameNodeRelationship(id, finalUtilities, fromPredecessor, predecessorAction));
            return true;
        }

        public bool InformationSet_Backward(InformationSetNode informationSet, IEnumerable<bool> fromSuccessors)
        {
            return true;
        }

        public int InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, int fromPredecessor)
        {
            int id = 0;
            if (predecessorAction != 0)
            {
                id = Relationships.Count();
                Relationships.Add(new GameNodeRelationship(id, informationSet, fromPredecessor, predecessorAction));
            }
            return id;
        }
    }
}

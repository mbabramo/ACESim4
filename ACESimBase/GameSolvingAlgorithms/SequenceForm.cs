using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class SequenceForm : StrategiesDeveloperBase
    {
        double[,] E, F, A, B;

        public SequenceForm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }
        public override async Task Initialize()
        {
            await base.Initialize();
            InitializeInformationSets();
            if (!EvolutionSettings.CreateInformationSetCharts) // otherwise this will already have been run
                InformationSetNode.IdentifyNodeRelationships(InformationSets);
        }

        public override Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            CreateConstraintMatrices();
            CreatePayoffMatrices();

            ReportCollection reportCollection = new ReportCollection();

            return Task.FromResult(reportCollection);
        }

        private void CreatePayoffMatrices()
        {
            SequenceFormPayoffs c = new SequenceFormPayoffs(E.GetLength(1), F.GetLength(1));
            TreeWalk_Tree(c);
            c.FinalizeMatrices();
            A = c.A;
            B = c.B;
        }

        private void CreateConstraintMatrices()
        {
            int[] cumulativeChoiceNumber = new int[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                var informationSets = InformationSets.Where(x => x.PlayerIndex == p).OrderBy(x => x.InformationSetContents.Length).ThenBy(x => x.InformationSetContents, new ArrayComparer<byte>()).ToList();
                for (int perPlayerInformationSetNumber = 1; perPlayerInformationSetNumber <= informationSets.Count(); perPlayerInformationSetNumber++)
                {
                    var informationSet = informationSets[perPlayerInformationSetNumber - 1];
                    informationSet.PerPlayerNodeNumber = perPlayerInformationSetNumber;
                    informationSet.CumulativeChoiceNumber = cumulativeChoiceNumber[p];
                    cumulativeChoiceNumber[p] += informationSet.NumPossibleActions;
                }
                double[,] constraintMatrix = new double[informationSets.Count() + 1, cumulativeChoiceNumber[p] + 1];
                constraintMatrix[0, 0] = 1; // empty sequence
                for (int perPlayerInformationSetNumber = 1; perPlayerInformationSetNumber <= informationSets.Count(); perPlayerInformationSetNumber++)
                {
                    var informationSet = informationSets[perPlayerInformationSetNumber - 1];
                    int columnOfParent = 0;
                    if (informationSet.ParentInformationSet is InformationSetNode parent)
                    {
                        byte actionAtParent = informationSet.InformationSetContentsSinceParent[0]; // one-based action
                        columnOfParent = parent.CumulativeChoiceNumber + actionAtParent;
                    }
                    constraintMatrix[perPlayerInformationSetNumber, columnOfParent] = -1;
                    for (int a = 1; a <= informationSet.NumPossibleActions; a++)
                    {
                        constraintMatrix[perPlayerInformationSetNumber, informationSet.CumulativeChoiceNumber + a] = 1;
                    }
                }
                if (p == 0)
                    E = constraintMatrix;
                else
                    F = constraintMatrix;
            }
        }
    }
}

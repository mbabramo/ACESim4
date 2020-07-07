using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NumSharp;
using System.IO;

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
            ExportMatrices();

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

        private void ExportMatrices()
        {
            string path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
            bool convertToSparse = false;
            double[,] maybeConvert(double[,] x) => convertToSparse ? x.ConvertToSparse() : x;
            SaveMultidimensionalMatrix(path, "A.npy", maybeConvert(A));
            SaveMultidimensionalMatrix(path, "B.npy", maybeConvert(B));
            SaveMultidimensionalMatrix(path, "E.npy", maybeConvert(E));
            SaveMultidimensionalMatrix(path, "F.npy", maybeConvert(F));
        }

        private static void SaveMultidimensionalMatrix(string path, string filename, double[,] original)
        {
            bool verify = false;
            if (verify)
                VerifyNotAllZero(original);
            np.Save(original, Path.Combine(path, filename));
            if (verify)
                VerifyMatrixSerialization(path, filename, original);
        }

        private static void VerifyNotAllZero(double[,] original)
        {
            int dim0 = original.GetLength(0);
            int dim1 = original.GetLength(1);
            bool allZero = true;
            for (int counter0 = 0; counter0 < dim0; counter0++)
            {
                for (int counter1 = 0; counter1 < dim1; counter1++)
                {
                    if (original[counter0, counter1] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (!allZero)
                    break;
            }
            if (allZero)
                throw new Exception("Matrix is entirely zeros.");
        }

        private static void VerifyMatrixSerialization(string path, string filename, double[,] original)
        {
            var reloaded = np.Load<double[,]>(Path.Combine(path, filename));
            bool isEqual = true;
            int dim0 = original.GetLength(0);
            int dim1 = original.GetLength(1);
            if (dim0 != reloaded.GetLength(0) || dim1 != reloaded.GetLength(1))
                throw new Exception("Serialization failed -- inconsistent array sizes");
            for (int counter0 = 0; counter0 < dim0; counter0++)
            {
                for (int counter1 = 0; counter1 < dim1; counter1++)
                {
                    if (original[counter0, counter1] != reloaded[counter0, counter1])
                    {
                        isEqual = false;
                    }
                }
            }
            if (!isEqual)
                throw new Exception("Serialization failed");
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

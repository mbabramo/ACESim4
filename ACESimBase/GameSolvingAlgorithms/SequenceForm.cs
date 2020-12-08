using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NumSharp;
using System.IO;
using ACESimBase.GameSolvingSupport;
using System.Diagnostics;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class SequenceForm : StrategiesDeveloperBase
    {
        double[,] E, F, A, B;
        bool UseGambit = true;

        public SequenceForm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            if (EvolutionSettings.DistributeChanceDecisions)
                throw new Exception("Distributing chance decisions is not supported.");
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

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {

            ReportCollection reportCollection = new ReportCollection();

            if (UseGambit)
            {
                await UseGambitToCalculateEquilibrium(reportCollection);
            }
            else
            {
                CreateConstraintMatrices();
                CreatePayoffMatrices();
                ExportMatrices();
            }

            return reportCollection;
        }

        private async Task UseGambitToCalculateEquilibrium(ReportCollection reportCollection)
        {
            string filename = CreateGambitFile();
            string output = RunGambit(filename);
            var results = ProcessGambitResults(reportCollection, output);
            await SetEquilibria(results, reportCollection);
        }

        private List<List<double>> ProcessGambitResults(ReportCollection reportCollection, string output)
        {
            string[] result = output.Split("\n\r".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            List<List<double>> resultsAsDoubles = new List<List<double>>();
            if (result.Any())
            {
                int numEquilibria = result.Length;
                for (int eqNum = 0; eqNum < result.Length; eqNum++)
                {
                    string anEquilibriumString = (string)result[eqNum];
                    if (anEquilibriumString.StartsWith("NE,"))
                    {
                        string numbersOnly = anEquilibriumString[3..];
                        string[] rationalNumbers = numbersOnly.Split(',');
                        List<double> numbers = new List<double>();
                        foreach (string rationalNumberString in rationalNumbers)
                        {
                            if (rationalNumberString.Contains("/"))
                            {
                                string[] fractionComponents = rationalNumberString.Split('/');
                                double rationalConverted = double.Parse(fractionComponents[0]) / double.Parse(fractionComponents[1]);
                                numbers.Add(rationalConverted);
                            }
                            else
                            {
                                numbers.Add(double.Parse(rationalNumberString));
                            }
                        }
                        resultsAsDoubles.Add(numbers);
                    }
                }
            }
            return resultsAsDoubles;
        }

        private async Task SetEquilibria(List<List<double>> equilibria, ReportCollection reportCollection)
        {
            int numEquilibria = equilibria.Count();
            for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
            {
                var numbers = equilibria[eqNum];
                var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
                if (infoSets.Sum(x => x.Decision.NumPossibleActions) != numbers.Count())
                    throw new Exception();
                int totalNumbersProcessed = 0;
                for (int i = 0; i < infoSets.Count(); i++)
                    for (byte a = 1; a <= infoSets[i].Decision.NumPossibleActions; a++)
                        infoSets[i].SetActionToProbabilityValue(a, numbers[totalNumbersProcessed++], true);

                var reportResult = await GenerateReports(EvolutionSettings.ReportEveryNIterations ?? 0,
                    () =>
                        $"{GameDefinition.OptionSetName}{(numEquilibria > 1 ? $"Eq{eqNum + 1}" : "")}");
                reportCollection.Add(reportResult);
            }
        }

        private string RunGambit(string filename)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            string output = RunGambitLCP(filename);
            TabbedText.WriteLine($"Gambit output for {filename} ({s.ElapsedMilliseconds} ms): {output}");
            return output;
        }

        private string CreateGambitFile()
        {
            EFGFileCreator efgCreator = new EFGFileCreator(GameDefinition.OptionSetName, GameDefinition.NonChancePlayerNames);
            TreeWalk_Tree(efgCreator);
            string efgResult = efgCreator.FileText.ToString();
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, GameDefinition.OptionSetName + ".efg");
            TextFileCreate.CreateTextFile(filename, efgResult);
            return filename;
        }

        private string RunGambitLCP(string filename)
        {
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-lcp.exe";
            // -D gives more detailed info.
            // -q suppresses the banner
            // Note that -d is supposed to use decimals instead of rationals, but it doesn't work.
            // -P limits to subgame perfect
            p.StartInfo.Arguments = filename + " -q -P"; 
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
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

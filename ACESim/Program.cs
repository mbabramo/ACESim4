using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim;
using System.Threading;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.IO;
using System.Runtime.Serialization;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Queue;
using ACESim.Util;

namespace ACESim
{
    public enum RunStatus
    {
        Uninitialized,
        Stopped,
        Running,
        StopSoon,
        Paused
    }


    public static class StartRunning
    {
        public static Thread Go(string baseOutputDirectory, string settingsPath, IUiInteraction ui, ProgressResumptionOptions progressResumptionOption)
        {
            //if (progressResumptionOption == ProgressResumptionOptions.ProceedNormallySavingPastProgress || progressResumptionOption == ProgressResumptionOptions.ProceedNormallyWithoutSavingProgress)
            //    AzureReset.Go(); // will only reset if resetting is on in AzureSetup

            string strategiesPath = Path.Combine(baseOutputDirectory, "Strategies"); 

            ProgressResumptionManager prm = new ProgressResumptionManager(progressResumptionOption, Path.Combine(strategiesPath, "ResumeInfo"));

            string rerunFileStart = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame\\Strategies\\strsta";  // "C:\\ACESim\\ACESim\\Games\\LitigationGame\\Strategies\\strsta
            string strategyStepToReplay = "23"; // "326"; // 97

            // Use this to load a specific strategy and then save it again but without prior version history
            //string filenameToLoad = "LitigationGameStrategies-14.stg";
            //Strategy loadStrategy = (Strategy)BinarySerialization.GetSerializedObject(Path.Combine(rerunPath, filenameToLoad), undoPreserialize: false);
            //loadStrategy.ClearPriorVersionHistory();
            //BinarySerialization.SerializeObject(Path.Combine(rerunPath, "TEMPOut2"), loadStrategy, true, true);

            // use the following code to convert an evolved strategy to RPROP (to save time and space)
            //string filenameToLoad = "RangeUncertaintyDecision"; // "Hash" + theInfo.HashCodes[14].ToString() + ".stg2";
            //Strategy loadStrategy = (Strategy)BinarySerialization.GetSerializedObject(Path.Combine(strategiesPath, filenameToLoad));
            //// these next four lines can be eliminated later
            //loadStrategy._previousVersionOfThisStrategy = null;
            //loadStrategy._versionOfStrategyBeforeThat = null;
            //BinarySerialization.SerializeObject(Path.Combine(strategiesPath, "RPROPIn"), loadStrategy, true);
            //loadStrategy = (Strategy)BinarySerialization.GetSerializedObject(Path.Combine(strategiesPath, "RPROPIn"));
            //// now convert and save
            //loadStrategy.ConvertStrategyComponentsToRPROP();
            //BinarySerialization.SerializeObject(Path.Combine(strategiesPath, "RPROPOut"), loadStrategy, true);


            //StrategyStateSerialization.RerunStrategyStep(rerunPath, "strsta" + strategyStepToReplay, true);
            //StrategyStateSerialization.RerunStrategyStep(rerunPath, "strsta" + strategyStepToReplay, true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta230", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta266", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta302", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta338", true);
            //return;
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta336", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta337", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta338", true);
            //StrategyStateSerialization.RerunStrategyStep(path, "strsta339", true);
            //return;
            //StrategyStateSerialization.RerunStrategyStep(strategyStepToReplay, rerandomize: true, 
            //    replacementStrategies: new List<Tuple<int,string>>() // note: decision 7 seems to be the main culprit on run 97. replacing that decision (esp. if 4, 5, and 6 are replaced) makes a large difference
            //    {  // main projections from a run producing a different result
            //        //new Tuple<int,string>(4,filestart+"555") ,
            //        //new Tuple<int,string>(5,filestart+"555") ,
            //        //new Tuple<int,string>(6,filestart+"555") ,
            //        new Tuple<int,string>(7,filestart+"555") ,
            //        //new Tuple<int,string>(16,filestart+"555") ,
            //        //new Tuple<int,string>(17,filestart+"555") ,
            //        //new Tuple<int,string>(18,filestart+"555") ,
            //        //new Tuple<int,string>(19,filestart+"555") ,
            //        //new Tuple<int,string>(28,filestart+"555") ,
            //        //new Tuple<int,string>(29,filestart+"555") ,
            //        //new Tuple<int,string>(30,filestart+"555") ,
            //        //new Tuple<int,string>(31,filestart+"555") ,
            //        // end drops
            //        //new Tuple<int,string>(40,filestart+"555") ,
            //        //new Tuple<int,string>(41,filestart+"555") ,
            //        // other

            //        //new Tuple<int,string>(1,filestart+"555") ,
            //        //new Tuple<int,string>(3,filestart+"555") ,
            //        //new Tuple<int,string>(2,filestart+"555") , // the decision itself!
            //        //new Tuple<int,string>(0,filestart+"555") ,
            //        //// threat points
            //        //new Tuple<int,string>(12,filestart+"555") ,
            //        //new Tuple<int,string>(13,filestart+"555") ,
            //        //new Tuple<int,string>(14,filestart+"555") ,
            //        //new Tuple<int,string>(15,filestart+"555") ,
            //        //new Tuple<int,string>(24,filestart+"555") ,
            //        //new Tuple<int,string>(25,filestart+"555") ,
            //        //new Tuple<int,string>(26,filestart+"555") ,
            //        //new Tuple<int,string>(27,filestart+"555") ,
            //        //new Tuple<int,string>(36,filestart+"555") ,
            //        //new Tuple<int,string>(37,filestart+"555") ,
            //        //new Tuple<int,string>(38,filestart+"555") ,
            //        //new Tuple<int,string>(39,filestart+"555") ,
            //    });
            // why are the following two different?
            //StrategyStateSerialization.RerunStrategyStep(filestart + "531", rerandomize: true);
            // This one is too high for low numbers. Let's try to do it repeatedly borrowing from the higher one (up to decision 41), to see if a single decision changes it much
            //StrategyStateSerialization.RerunStrategyStep(filestart + "73", rerandomize: true);
            //for (int test = 0; test <= 41; test++)
            //{
            //    Debug.WriteLine("Replacing strategy " + test + " to see if that generates lower numbers for the strategy around 0.18.");
            //    StrategyStateSerialization.RerunStrategyStep(filestart + "73", rerandomize: true, replacementStrategies: new List<Tuple<int,string>>() { new Tuple<int,string>(test, filestart + "531") });
            //}
            // maybe 17 and 19 have some effect
            var replacements = new List<Tuple<int, string>>();
            //for (int test = 16; test <= 19; test++) // round 2 projections
            //{
            //    if (test != 2)
            //        replacements.Add(new Tuple<int, string>(test, filestart + "531"));
            //}
            //for (int catchProblem = 1; catchProblem <= 25; catchProblem++)
            //    StrategyStateSerialization.RerunStrategyStep(filestart + "115", rerandomize: true, replacementStrategies: replacements);

            //// 229 difference start at 5
            const int serializationsBetweenRuns = 229;

            //for (int ser = 2; ser <= 73; ser++)
            //{
            //    TabbedText.WriteLine("Comparing strategies as of serialization # " + ser + " and same in subsequent runs");
            //    TabbedText.Tabs++;
            //    //StrategyStateSerialization.CompareStrategySets(filestart, new List<int>() { ser + serializationsBetweenRuns * 1, ser, ser + serializationsBetweenRuns * 2, ser + serializationsBetweenRuns * 3, ser + serializationsBetweenRuns * 4, ser + serializationsBetweenRuns * 5 }); // 73, 302, 531, 760, 989, 1218 });

            //    StrategyStateSerialization.CompareStrategySets(filestart, new List<int>() { ser, ser + serializationsBetweenRuns * 1, ser + serializationsBetweenRuns * 2, ser + serializationsBetweenRuns * 3, ser + serializationsBetweenRuns * 4, ser + serializationsBetweenRuns * 5 }); // 73, 302, 531, 760, 989, 1218 });
            //    TabbedText.Tabs--;
            //}

            //new RemoteCutoffTester().DoTest();

            //new OversamplingPlanTester().DoTest();


            //uncomment this to clear the online database -- may take a while
            new SaveExecutionResults().DeleteAllExistingExecutionResultSets(); 

            //LitigationGameProbMagnitudeSimpleModel lgpm = new LitigationGameProbMagnitudeSimpleModel();
            //lgpm.RunSimulation();

            //LitigationGameTwoClaimToyModel m = new LitigationGameTwoClaimToyModel();
            //m.FindSymmetricSurplusesWithDifferentStructures();

            //ProcessInSeparateAppDomainSetupExample.DoIt();
            //ProcessInSeparateAppDomainSetupExample.DoIt();

            //List<double[]> simpleClustering = new List<double[]>();
            //for (int i = 0; i < 10000; i++)
            //    simpleClustering.Add(new double[] { RandomGenerator.NextDouble() });
            //var output2 = ClusteringByFirstItem.GetClusters(simpleClustering, 100).OrderBy(x => x[0]).ToList();

            //List<double[]> kMeansTest = new List<double[]>();
            //for (int i = 0; i < 10000; i++)
            //    kMeansTest.Add(new double[] { RandomGenerator.NextDouble() });
            //var output = KMeansClustering.GetClusters(kMeansTest, 100).OrderBy(x => x[0]).ToList();

            //var toyModel = new LitigationGameToyModel();
            //toyModel.RepeatedlyFindDropCutoff();

            //HeisenbugTester.TryToFindFakeBug();
            //Parallelizer.TestConsistentParallelism();

            // BruteForceProbabilityProxyGreaterThan05AssessAccuracy.DoTest();

            //ConstrainedOrderTest.DoTest();

            //PriorityQueue<double, double> pq = new PriorityQueue<double, double>(100, true);
            //ProfileSimple.Start("pq");
            //for (int i = 0; i < 10000000; i++)
            //{
            //    bool full;
            //    pq.Enqueue(RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), out full);
            //}
            //ProfileSimple.End("pq");
            //ProfileSimple.Start("pqparallel");
            //Parallel.For(0, 10000000, i =>
            //{
            //    bool full;
            //    pq.Enqueue(RandomGenerator.NextDouble(), RandomGenerator.NextDouble(), out full);
            //}
            //);
            //ProfileSimple.End("pqparallel");


            //WikiDocumentation.TranslateXmlToWikiDocumentation();

            //UtilityCalculationTest.Check();

            //GoldenSectionOptimizerTester.DoTest();

            //OptimizeSmoothedNoisyFunctionTester.DoTest();

            //ThreadLocal<double?[]> myTest = new ThreadLocal<double?[]>();
            //while (true)
            //{
            //    Parallel.For(0, 100, y => { myTest.Value = new double?[100000]; });
            //}

            //foreach (var item in new System.Management.ManagementObjectSearcher("Select * from Win32_ComputerSystem").Get())
            //{
            //    Console.WriteLine("Number Of Physical Processors: {0} ", item["NumberOfProcessors"]);
            //}

            //int coreCount = 0;
            //foreach (var item in new System.Management.ManagementObjectSearcher("Select * from Win32_Processor").Get())
            //{
            //    coreCount += int.Parse(item["NumberOfCores"].ToString());
            //}
            //Console.WriteLine("Number Of Cores: {0}", coreCount);


            //UtilityCalculationTest.Check();

            //ValueFromSignalExample.DoExample();

            //StatCollector sc = new StatCollector();
            //for (int j = 0; j < 1000; j++)
            //{
            //    int numX = 0;
            //    for (int i = 0; i < 20000; i++)
            //    {
            //        if (RandomGenerator.NextDouble() < 0.5)
            //            numX++;
            //    }
            //    sc.Add(numX);
            //}

            //StatCollector diffFromExp = new StatCollector();
            //for (int j = 0; j < 1000; j++)
            //{
            //    int countLessThan01 = 0;
            //    const int totalRuns = 10000;
            //    for (int i = 0; i < totalRuns; i++)
            //        if (RandomGenerator.NextDouble() < 0.1)
            //            countLessThan01++;
            //    Debug.WriteLine("Count: " + countLessThan01);
            //    diffFromExp.Add(Math.Abs(countLessThan01 - 0.1 * totalRuns));
            //}
            //Debug.WriteLine("Avg difference: " + diffFromExp.Average());

            //NeuralNetworkTesting nt = new NeuralNetworkTesting();
            //nt.TestSin2(TrainingTechnique.ResilientPropagation);

            //SimpleBackprop.SimpleBackpropTest.DoTest2();

            ExecutorMultiple theExecutor = new ExecutorMultiple(baseOutputDirectory, settingsPath, ui, prm);
            Thread aceSimThread = new Thread(new ThreadStart(theExecutor.RunAll));
            aceSimThread.Name = "ACESim Multiple Execution Thread";
            aceSimThread.Start();
            return aceSimThread;
        }
    }

    class Program
    {
        public static AppDomain CreateSandbox()
        { // does not work, and probably can't be used anyway since we use ThreadLocal -- this was an attempt to help isolate accessviolationexceptions (which might be caused by some error in ThreadLocal)
            var platform = Assembly.GetExecutingAssembly();
            var name = platform.FullName + ": Sandbox " + Guid.NewGuid();
            AppDomainSetup setup = null; //  new AppDomainSetup { ApplicationBase = platform.Location };
            var permissions = new PermissionSet(PermissionState.None); 
            permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery | FileIOPermissionAccess.AllAccess, platform.Location));
            var sandbox = AppDomain.CreateDomain(name, null, setup, permissions);

            return sandbox;
        }

        public class RunWithoutUI
        {
            public RunWithoutUI()
            {
                Console.WriteLine("Running without user interface. Change startup project to run with user interface. Output is in debug window.");
                //string baseOutputDirectory = "C:\\ACESim\\ACESim\\Games\\LitigationGame";
                //string settingsPath = "C:\\ACESim\\ACESim\\Games\\LitigationGame\\Settings\\LitigationGameMultipleRunSettingsTemp2.xml";
                string baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
                string settingsPath = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame\\Settings\\MyGameOverallSettings.xml";
                //string settingsPath = "C:\\GitHub\\ACESim\\ACESim\\Games\\PatentDamagesGame\\Settings\\PatentDamagesChangeVariableSettings.xml";
                //string settingsPath = "C:\\GitHub\\ACESim\\ACESim\\Games\\LitigationGame\\Settings\\LitigationGameSingleRunSettings.xml";
                ProgressResumptionOptions progressResumptionOption = ProgressResumptionOptions.ProceedNormallySavingPastProgress; // change this line if some other option is appropriate
                var consoleWindow = new Interactionless();
                var currentExecutionInformation = new CurrentExecutionInformation(null, null, null, null, null, null, null);
                consoleWindow.CurrentExecutionInformation = currentExecutionInformation;
                StartRunning.Go(baseOutputDirectory, settingsPath, consoleWindow, progressResumptionOption);
            }
        }


        static void Main(string[] args)
        {
            //new RunWithoutUI();
            bool useQuadraticUtilityWithInitialWealthAsMax = true;
            const double initialWealth = 2000000;
            const double damagesSought = 10000;
            const double N = 1000;
            const double probAdjudication = 1.0 / N;
            const double probNoAdjudication = 1.0 - probAdjudication;
            const int numCoverageRanges = 6; // first coverage range is 0 coverage
            double dollarsPerCoverageLevel = damagesSought / (double)(numCoverageRanges - 1.0);
            const int numLiabilityProbabilityLevels = 10;
            const int numNoiseLevels = 100;
            const int numParties = numLiabilityProbabilityLevels * numNoiseLevels;
            const double noiseStdev = 0.20;
            const int numDiscreteSignals = 5;
            int numPartiesPerDiscreteSignal = numParties / numDiscreteSignals;
            double[] liabilityProbabilities = new double[numParties];
            double[] continuousSignals = new double[numParties];
            double[] liabilityProbabilityLevels = EquallySpaced.GetEquallySpacedPoints(numLiabilityProbabilityLevels);
            double[] noiseLevels = EquallySpaced.GetEquallySpacedPoints(numNoiseLevels).Select(x => noiseStdev * InvNormal.Calculate(x)).ToArray();
            for (int liabilityProbabilityLevel = 0; liabilityProbabilityLevel < numLiabilityProbabilityLevels; liabilityProbabilityLevel++)
                for (int noiseLevel = 0; noiseLevel < numNoiseLevels; noiseLevel++)
                {
                    int index = liabilityProbabilityLevel * numNoiseLevels + noiseLevel;
                    liabilityProbabilities[index] = liabilityProbabilityLevels[liabilityProbabilityLevel];
                    continuousSignals[index] = liabilityProbabilityLevels[liabilityProbabilityLevel] + noiseLevels[noiseLevel];
                }

            double[] continuousSignalsOrdered = continuousSignals.OrderBy(x => x).ToArray();
            int[] discreteSignals = new int[numParties];
            for (int s = 0; s < numDiscreteSignals; s++)
            {
                int lowerBoundIndex = s * numPartiesPerDiscreteSignal;
                double lowerBound = continuousSignalsOrdered[lowerBoundIndex];
                for (int p = 0; p < numParties; p++)
                    if (continuousSignals[p] >= lowerBound)
                        discreteSignals[p] = s;
            }
            double[,] prices = new double[numDiscreteSignals, numCoverageRanges];
            double[,] insurerUtility = new double[numDiscreteSignals, numCoverageRanges];
            int[,] totalPurchases = new int[numDiscreteSignals, numCoverageRanges];
            double[] coveragePurchased = new double[numParties];

            for (int s = 0; s < numDiscreteSignals; s++)
            {
                // initialize price to lowest value for each coverage range
                for (int c = 0; c < numCoverageRanges; c++)
                    if (c == 0)
                        prices[s, c] = 0;
                    else
                        prices[s, c] = dollarsPerCoverageLevel;
                foreach (double priceDelta in new double[] { dollarsPerCoverageLevel * 1.0, dollarsPerCoverageLevel * 0.5, dollarsPerCoverageLevel * 0.2, dollarsPerCoverageLevel * 0.1, dollarsPerCoverageLevel * 0.05, dollarsPerCoverageLevel * 0.02, dollarsPerCoverageLevel * 0.01, dollarsPerCoverageLevel * 0.001 })
                { 
                    HashSet<string> hashes = new HashSet<string>();
                    bool equilibriumReached = false;
                    int cycle = 0;
                    while (!equilibriumReached)
                    {
                        cycle++;
                        for (int c = 0; c < numCoverageRanges; c++)
                        {
                            insurerUtility[s, c] = 0;
                            totalPurchases[s, c] = 0;
                        }
                        for (int p = 0; p < numParties; p++)
                        {
                            if (discreteSignals[p] == s)
                            // find all parties with the matching signal
                            {
                                int bestC;
                                double insurerUtilityInBestScenariForInsured;
                                DeterminePartysCoverageDecision(useQuadraticUtilityWithInitialWealthAsMax, initialWealth, damagesSought, N, probAdjudication, numCoverageRanges, dollarsPerCoverageLevel, liabilityProbabilities[p], prices, s, out bestC, out insurerUtilityInBestScenariForInsured);
                                coveragePurchased[p] = bestC;
                                insurerUtility[s, bestC] += insurerUtilityInBestScenariForInsured;
                                totalPurchases[s, bestC]++;
                            }
                        }
                        // uncomment here if you want to see the evolution of prices for a particular signal
                        //if (s == numDiscreteSignals - 1)
                        //    PrintCoverageTable(numCoverageRanges, numDiscreteSignals, prices, totalPurchases, insurerUtility, numDiscreteSignals, true);
                        equilibriumReached = true;
                        bool[] priceAffected = new bool[numCoverageRanges];
                        for (int c = 1; c < numCoverageRanges; c++)
                        {
                            if (priceAffected[c])
                                continue;
                            if (insurerUtility[s, c] < 0)
                            {
                                prices[s, c] += priceDelta;
                                priceAffected[c] = true;
                                // any product that is better and cheaper should also be increased in price
                                for (int c2 = c + 1; c2 < numCoverageRanges; c2++)
                                    if (prices[s, c2] < prices[s, c])
                                    {
                                        prices[s, c2] = prices[s, c];
                                        priceAffected[c2] = true;
                                    }
                                equilibriumReached = false;
                            }
                            else if (insurerUtility[s, c] > 0)
                            {
                                prices[s, c] -= priceDelta;
                                priceAffected[c] = true;
                                // any product that is worse and more expensive should also be decreased in price
                                for (int c2 = c - 1; c2 >= 0; c2--)
                                    if (prices[s, c2] > prices[s, c])
                                    {
                                        prices[s, c2] = prices[s, c];
                                        priceAffected[c2] = true;
                                    }
                                equilibriumReached = false;
                            }
                        }
                        if (!equilibriumReached)
                        {
                            string hashKey = MD5HashGenerator.GenerateKey(prices);
                            if (hashes.Contains(hashKey))
                                equilibriumReached = true; // no change since some previous iteration
                            else
                                hashes.Add(hashKey);
                        }
                    }
                }
            }
            PrintCoverageTable(numCoverageRanges, numDiscreteSignals, prices, totalPurchases, insurerUtility);

        }

        private static void DeterminePartysCoverageDecision(bool useQuadraticUtilityWithInitialWealthAsMax, double initialWealth, double damagesSought, double N, double probAdjudication, int numCoverageRanges, double dollarsPerCoverageLevel, double liabilityProbability, double[,] prices, int s, out int bestC, out double insurerUtilityInBestScenarioForInsured)
        {
            double[] utilityAtCoverageRange = new double[numCoverageRanges];
            bestC = 0;
            double bestUtility = 0;
            insurerUtilityInBestScenarioForInsured = 0;
            // calculate utility at each price and find optimal price
            for (int c = 0; c < numCoverageRanges; c++)
            {
                double dollarsCovered = dollarsPerCoverageLevel * c;
                double dollarsRemaining = damagesSought - dollarsCovered;
                double damagesIfAdjudicationAndLiability = dollarsRemaining * N;
                double probabilityAdjudicationAndLiability = probAdjudication * liabilityProbability;
                double wealthIfAllGoesWell = initialWealth - prices[s, c];
                double wealthIfFoundLiableAtTrial = initialWealth - prices[s, c] - damagesIfAdjudicationAndLiability;
                double utilityIfAllGoesWell, utilityIfFoundLiableAtTrial;
                if (useQuadraticUtilityWithInitialWealthAsMax)
                {
                    utilityIfAllGoesWell = 0 - Math.Pow(initialWealth - wealthIfAllGoesWell, 2);
                    utilityIfFoundLiableAtTrial = 0 - Math.Pow(initialWealth - wealthIfFoundLiableAtTrial, 2);
                }
                else
                {
                    utilityIfAllGoesWell = Math.Log(wealthIfAllGoesWell);
                    utilityIfFoundLiableAtTrial = Math.Log(wealthIfFoundLiableAtTrial);
                }
                utilityAtCoverageRange[c] = (1.0 - probabilityAdjudicationAndLiability) * utilityIfAllGoesWell + probabilityAdjudicationAndLiability * utilityIfFoundLiableAtTrial;
                double insurerUtilityIfAllGoesWell = prices[s, c];
                double insurerUtilityIfLiabilityFoundAtTrial = prices[s, c] - dollarsCovered * N;
                double insurerUtilityForThisContract = (1.0 - probabilityAdjudicationAndLiability) * insurerUtilityIfAllGoesWell + probabilityAdjudicationAndLiability * insurerUtilityIfLiabilityFoundAtTrial;
                if (c == 0)
                    bestUtility = utilityAtCoverageRange[c];
                else if (utilityAtCoverageRange[c] > bestUtility)
                {
                    bestC = c;
                    bestUtility = utilityAtCoverageRange[c];
                    insurerUtilityInBestScenarioForInsured = insurerUtilityForThisContract;
                }
            }
        }

        private static void PrintCoverageTable(int numCoverageRanges, int numDiscreteSignals, double[,] prices, int[,] totalPurchases, double[,] insurerUtility, int? limitToSignal = null, bool reportInsurerProfit = false)
        {
            int grandTotalPurchases = 0;

            if (limitToSignal == null)
            {
                string headingRowIntro = "Coverage ";
                Debug.Write(String.Format("{0,-23}", headingRowIntro));
                for (int c = 0; c < numCoverageRanges; c++)
                {
                    string columnHeading = c.ToString() + "/" + (numCoverageRanges - 1).ToString();
                    if (c == 0)
                        columnHeading = "No coverage";
                    else if (c == numCoverageRanges - 1)
                        columnHeading = "Full coverage";
                    Debug.Write(String.Format("{0,-23}", columnHeading));
                }
                Debug.Write(Environment.NewLine);
            }
            for (int s = 0; s < numDiscreteSignals; s++)
            {
                if (limitToSignal == null || limitToSignal == s + 1)
                {
                    string rowHeading = "Signal range " + (s + 1).ToString();
                    Debug.Write(String.Format("{0,-23}", rowHeading));
                    for (int c = 0; c < numCoverageRanges; c++)
                    {
                        double insurerUtilityPerPolicy = (insurerUtility[s, c] / totalPurchases[s, c]);
                        string insurerUtilityPerPolicyString = " => " + String.Format("{0:0.0}", insurerUtilityPerPolicy);
                        if (!reportInsurerProfit)
                            insurerUtilityPerPolicyString = "";
                        string info = $"${prices[s, c]} ({totalPurchases[s, c]}{insurerUtilityPerPolicyString}) ";
                        grandTotalPurchases += totalPurchases[s, c];
                        Debug.Write(String.Format("{0,-23}", info));
                    }
                    Debug.Write(Environment.NewLine);
                }
            }
        }
    }
}
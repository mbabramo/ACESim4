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

        public class TwoType
        {

            bool useQuadraticUtilityWithInitialWealthAsMax = true;
            const double initialWealth = 1000001;
            const double DamagesSought = 10000;
            double N = 0;
            public double probAdjudication => 1.0 / N;
            public double probNoAdjudication => 1.0 - probAdjudication;
            const double goodTypeProportionOfPopulation = 0.20;
            double badTypeProportionOfPopulation = 1.0 - goodTypeProportionOfPopulation;
            double goodTypeLiabilityProbability = 0.25;
            double badTypeLiabilityProbability = 0.75;
            public double maxExposure => DamagesSought / N;
            static Func<double, double> UtilityFn = (double wealth) => Math.Log10(wealth);
            //static Func<double, double> UtilityFn = (double wealth) => 0 - (initialWealth - wealth) * (initialWealth - wealth);

            // 1. Bad type buys full coverage.
            // 2. For a particular (PricePerUnit, CoverageLevel), we must satisfy the following conditions:
            // a. The good type would not prefer to buy more or less at this price.
            // b. Neither the good type nor the bad type would prefer the other's contract.
            // c. The insurer must at least break even.
            // 3. Among the contracts meeting these criteria, if there is a contract that is better for the insured, that will be preferred. Meanwhile, if there is a contract that is worse for the insurer, that will be preferred. (In other words, we seek zero economic profits but allow for profits if needed to maintain the separating equilibrium.)

            // ==> Algorithm. 
            // The price must be at least break even for the insurer for a unit of coverage for the good type. 
            // See if a unit will be bought at that price (without destroying separation). If not, no coverge. If so, see how much coverage will be purchased. Keep increasing the coverage so long as it will not destroy adverse selection and is better for the insured than the other policy.
            // Once we have a policy at one price, keep looking at higher prices. See if there are any policies that are at least as good for the insured.

            const double unitsBundle = 10;

            const double costOfMarketAdjudication = 1000;
            const double costOfRealAdjudication = 1000;
            double expectedCostOfRealAdjudication => costOfRealAdjudication / N;
            double expectedCostOfMarketAdjudication => costOfMarketAdjudication + expectedCostOfRealAdjudication;
            double extraCostOfMarketAdjudication => expectedCostOfMarketAdjudication - expectedCostOfRealAdjudication;

            public TwoType(double n)
            {
                N = n;
                //Contract result = FindGoodTypeContract(); // this allows for separating equilibrium
                //Debug.WriteLine($"N: {N} --> {result}");
                (Contract result, bool willInsure) = CheckWhetherGoodTypeWillInsure();  // this ensures full insurance is required
                if (willInsure)
                    Debug.WriteLine($"N: {N} --> {result}");
                else
                    Debug.WriteLine($"N: {N} --> No insurance (utility {result.ExpectedUtility_NotInsured}");
            }

            public (Contract pooled, bool willInsure) CheckWhetherGoodTypeWillInsure()
            {
                // In this version, we assume that one can obtain either a full
                // insurance contract or no insurance at all, for example as a result
                // of a governmental rule preventing partial sales of claims.
                // (We could also imagine less extreme variants to encourage continued participation by defendants.)
                // Thus, we figure out what the pooling equilibrium would be and determine whether the good type
                // is better off simply not buying the contract.
                double pooledProbabilityOfPayout = (goodTypeProportionOfPopulation * goodTypeLiabilityProbability + badTypeProportionOfPopulation * badTypeLiabilityProbability) / N;
                double numUnits = DamagesSought * N;
                double totalPrice = numUnits * pooledProbabilityOfPayout;
                double pricePerUnit = totalPrice / numUnits; // i.e., == pooledProbabilityOfPayout
                Contract pooled = new Contract(pricePerUnit, numUnits, goodTypeLiabilityProbability, N);

                bool willInsure = (pooled.IsBetterForInsuredThanNothing());
                return (pooled, willInsure);
            }

            public Contract FindGoodTypeContract()
            {
                Contract badTypeContract = new Contract(badTypeLiabilityProbability / N, DamagesSought * N, badTypeLiabilityProbability, N);
                Contract minimumContractForInsurer = FindMinimumContractGoodForInsurer();
                if (minimumContractForInsurer == null)
                    return null;
                Contract bestContractYet = null;
                double priceToCheck = 0.0001; // DEBUG minimumContractForInsurer.PricePerUnit;
                Contract separatingContract;
                do
                {
                    separatingContract = FindGoodTypeCoverageForPrice(priceToCheck, badTypeContract, bestContractYet);
                    if (separatingContract != null)
                        bestContractYet = separatingContract;
                    priceToCheck += 0.0001;
                }
                while (priceToCheck <= badTypeContract.PricePerUnit);
                return bestContractYet;
            }

            public Contract FindMinimumContractGoodForInsurer()
            {
                double initialPrice = 0.000001;
                Contract proposedContract = new Contract(initialPrice, unitsBundle, goodTypeLiabilityProbability, N);
                if (proposedContract.IsBetterForInsurerThanNothing())
                    return proposedContract;
                return new Contract(initialPrice * proposedContract.MultiplyPremiumForInsurerToBreakEven(), unitsBundle, goodTypeLiabilityProbability, N);
            }

            public Contract FindGoodTypeCoverageForPrice(double pricePerUnit, Contract badTypeContract, Contract alternativeGoodTypeContract)
            {
                double units = unitsBundle;
                if (alternativeGoodTypeContract != null)
                    units = alternativeGoodTypeContract.Units; // once we have found a contract that is good for the good type, there will never be a contract with a higher price and fewer units that is good for the good type
                bool coverageIsBetter = false;
                bool badTypeWillNotDefect = true;
                Contract bestContractYet = null;
                do
                {
                    Contract goodTypeContractToConsider = new Contract(pricePerUnit, units, goodTypeLiabilityProbability, N);
                    
                    (coverageIsBetter, badTypeWillNotDefect) = BetterThanAlternativesSoFar(goodTypeContractToConsider, badTypeContract, bestContractYet ?? alternativeGoodTypeContract);
                    if (coverageIsBetter)
                        bestContractYet = goodTypeContractToConsider;
                    units += unitsBundle;
                }
                while ((coverageIsBetter || badTypeWillNotDefect) && units <= DamagesSought * N); // keep looking for acceptable contracts so long as the bad type will not defect
                return bestContractYet;
            }

            bool TraceReasoning = false;

            private (bool betterThanAlternative, bool badTypeWillNotDefect) BetterThanAlternativesSoFar(Contract goodTypeContractToConsider, Contract badTypeContract, Contract alternativeGoodTypeContract)
            {
                bool isOKForInsurer = goodTypeContractToConsider.IsBetterForInsurerThanNothing();
                bool isBetterForGoodType;
                if (alternativeGoodTypeContract == null)
                    isBetterForGoodType = goodTypeContractToConsider.IsBetterForInsuredThanNothing();
                else
                    isBetterForGoodType = goodTypeContractToConsider.IsBetterForInsuredThanAlternative(alternativeGoodTypeContract);
                bool badTypeWillNotDefect = badTypeContract.IsBetterForInsuredThan_OtherPartysContract(goodTypeContractToConsider);
                bool betterThanAlternative = isOKForInsurer && isBetterForGoodType && badTypeWillNotDefect;
                if (TraceReasoning)
                    Debug.WriteLine($"{(betterThanAlternative ? "Adopting" : "Rejecting")} {goodTypeContractToConsider} OKForInsurer: {isOKForInsurer} BetterForGoodType: {isBetterForGoodType} BadTypeWillNotDefect: {badTypeWillNotDefect}");
                return (betterThanAlternative, badTypeWillNotDefect);
            }

            public class Contract
            {
                public double PricePerUnit;
                public double Units;
                public double ProbabilityLiability;
                public double N;

                public double Premium, ProbabilityAdjudicatedAndLiable, ProbabilityNotAdjudicatedOrNotLiable, DamagesIfAdjudicatedAndLiable, PayoutIfLiability, WealthWithInsurance_NoLiability, WealthWithInsurance_Liability, WealthNoInsurance_NoLiability, WealthNoInsurance_Liability, UtilityWithInsurance_NoLiability, UtilityWithInsurance_Liability, UtilityNoInsurance_NoLiability, UtilityNoInsurance_Liability, ExpectedUtility_WithInsurance, ExpectedUtility_NotInsured, ExpectedPayout, ExpectedInsurerProfit;

                public override string ToString()
                {
                    return $"Price {PricePerUnit} * Units {Units} = {Premium} of max {DamagesIfAdjudicatedAndLiable} ({Premium / DamagesIfAdjudicatedAndLiable}). Utility {ExpectedUtility_WithInsurance} (without: {ExpectedUtility_NotInsured})";
                }

                public Contract(double pricePerUnit, double units, double probabilityLiability, double n)
                {
                    PricePerUnit = pricePerUnit;
                    Units = units;
                    ProbabilityLiability = probabilityLiability;
                    N = n;

                    Premium = PricePerUnit * Units;
                    ProbabilityAdjudicatedAndLiable = ProbabilityLiability / N;
                    ProbabilityNotAdjudicatedOrNotLiable = 1.0 - ProbabilityAdjudicatedAndLiable;
                    DamagesIfAdjudicatedAndLiable = DamagesSought * N;
                    PayoutIfLiability = Math.Min(DamagesIfAdjudicatedAndLiable, Units);
                    WealthWithInsurance_NoLiability = initialWealth - Premium;
                    WealthWithInsurance_Liability = WealthWithInsurance_NoLiability - DamagesIfAdjudicatedAndLiable + PayoutIfLiability;
                    WealthNoInsurance_NoLiability = initialWealth;
                    WealthNoInsurance_Liability = initialWealth - DamagesIfAdjudicatedAndLiable;
                    UtilityWithInsurance_NoLiability = UtilityFn(WealthWithInsurance_NoLiability);
                    UtilityWithInsurance_Liability = UtilityFn(WealthWithInsurance_Liability);
                    UtilityNoInsurance_NoLiability = UtilityFn(WealthNoInsurance_NoLiability);
                    UtilityNoInsurance_Liability = UtilityFn(WealthNoInsurance_Liability);
                    ExpectedUtility_WithInsurance = ProbabilityAdjudicatedAndLiable * UtilityWithInsurance_Liability + ProbabilityNotAdjudicatedOrNotLiable * UtilityWithInsurance_NoLiability;
                    ExpectedUtility_NotInsured = ProbabilityAdjudicatedAndLiable * UtilityNoInsurance_Liability + ProbabilityNotAdjudicatedOrNotLiable * UtilityNoInsurance_NoLiability;
                    ExpectedInsurerProfit = Premium - ProbabilityAdjudicatedAndLiable * PayoutIfLiability;
                }

                public double MultiplyPremiumForInsurerToBreakEven()
                {
                    return ProbabilityAdjudicatedAndLiable * PayoutIfLiability / Premium;
                }

                public bool IsBetterForInsurerThan(Contract other)
                {
                    return ExpectedInsurerProfit > other.ExpectedInsurerProfit;
                }

                public bool IsBetterForInsurerThanNothing()
                {
                    return ExpectedInsurerProfit >= -0.00000001; // this compensates for contracts being rejected because of rounding errors
                }

                public bool NeitherPartyWillDefect(Contract otherPartysContract)
                {
                    return IsBetterForInsuredThan_OtherPartysContract(otherPartysContract) && otherPartysContract.IsBetterForInsuredThan_OtherPartysContract(this);
                }

                public bool IsBetterForInsuredThanNothing()
                {
                    return ExpectedUtility_WithInsurance > ExpectedUtility_NotInsured;
                }

                public bool IsBetterForInsuredThan_OtherPartysContract(Contract otherPartysContract)
                {
                    Contract thisPartysAlternative = new Contract(otherPartysContract.PricePerUnit, otherPartysContract.Units, ProbabilityLiability, N);
                    return IsBetterForInsuredThanAlternative(thisPartysAlternative);
                }

                public bool IsBetterForInsuredThanAlternative(Contract thisPartysAlternative)
                {
                    return ExpectedUtility_WithInsurance > thisPartysAlternative.ExpectedUtility_WithInsurance;
                }

                public bool IsRightCoverageLevel(double precision)
                {
                    Contract lowerAlternative = new Contract(PricePerUnit, Units - precision, ProbabilityLiability, N);
                    Contract higherAlternative = new Contract(PricePerUnit, Units + precision, ProbabilityLiability, N);
                    return ExpectedUtility_WithInsurance >= lowerAlternative.ExpectedUtility_WithInsurance && ExpectedUtility_WithInsurance >= higherAlternative.ExpectedUtility_WithInsurance;
                }
            }
        }


        static void Main(string[] args)
        {
            for (int i = 1; i <= 100; i += 1)
                new TwoType(i);
        }
        
    }
}
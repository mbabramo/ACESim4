using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using ACESim.Util;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PriorityQueue;
using SeparateAppDomain;
using System.Xml.Serialization;
using Microsoft.WindowsAzure.Storage.Table;

namespace ACESim
{
    [Serializable]
    [XmlInclude(typeof(GRNNSmoothing)), XmlInclude(typeof(RPROPSmoothing))]
    public class OptimizePointsAndSmooth : IStrategyComponent
    {
        public Strategy OverallStrategy { get; set; }
        public int Dimensions { get; set; } // may be different from the number of dimensions of the OverallStrategy
        public EvolutionSettings EvolutionSettings { get { return OverallStrategy.EvolutionSettings; } set { OverallStrategy.EvolutionSettings = value; } }
        public Decision Decision { get; set; }
        internal SimulationInteraction SimulationInteraction { get { return OverallStrategy.SimulationInteraction; } }
        public string Name { get; set; }
        public bool InitialDevelopmentCompleted { get; set; }

        internal List<SmoothingSetPointInfo> SmoothingSetPointInfos;
        internal List<SmoothingSetPointInfo> SmoothingSetPointInfosBeforeNarrowing;
        internal KDTree KDTreeForInputs;
        internal List<double> InputAveragesInSmoothingSet, InputStdevsInSmoothingSet;

        internal bool InValidationMode = false;

        internal bool InitialDevelopmentCompletedValidation;
        internal List<SmoothingSetPointInfo> SmoothingSetPointInfosValidationSet;
        internal KDTree StorageForSmoothingSetValidation;

        internal bool InitialDevelopmentCompletedMainSet;
        internal List<SmoothingSetPointInfo> SmoothingSetPointInfosMainSet;
        internal KDTree StorageForSmoothingSetMainSet;

        internal List<SmoothingSetPointInfo> PreviousSmoothingSetPointInfosMainSet;
        internal List<SmoothingSetPointInfo> PreviousSmoothingSetPointInfosValidationSet;

        int? SmoothingPointsMainSetWithPreviousPointsAdded = null;
        int? SmoothingPointsValidationSetWithPreviousPointsAdded = null;

        internal double? OptimalValueForZeroDimensions = null;

        internal bool _IsCurrentlyBeingDeveloped = false;
        public bool IsCurrentlyBeingDeveloped { get { return _IsCurrentlyBeingDeveloped; } set { _IsCurrentlyBeingDeveloped = value; } }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal double[] savedOriginalValues;

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public OptimizePointsAndSmooth OriginalStateToBeAveragedIn;


        public virtual IStrategyComponent DeepCopy()
        {
            OptimizePointsAndSmooth copy = new OptimizePointsAndSmooth()
            {
            };
            SetCopyFields(copy);
            return copy;
        }

        public virtual void SetCopyFields(IStrategyComponent copy)
        {
            OptimizePointsAndSmooth copyCast = (OptimizePointsAndSmooth)copy;

            copyCast.OverallStrategy = OverallStrategy;
            copyCast.Dimensions = Dimensions;
            copyCast.EvolutionSettings = EvolutionSettings;
            copyCast.Decision = Decision;
            copyCast.SmoothingSetPointInfos = SmoothingSetPointInfos == null ? null : SmoothingSetPointInfos.Select(x => x == null ? null : x.DeepCopy()).ToList();
            copyCast.KDTreeForInputs = KDTreeForInputs == null ? null : KDTreeForInputs.DeepCopy(null);
            copyCast.InputAveragesInSmoothingSet = InputAveragesInSmoothingSet == null ? null : InputAveragesInSmoothingSet.ToList();
            copyCast.InputStdevsInSmoothingSet = InputStdevsInSmoothingSet == null ? null : InputStdevsInSmoothingSet.ToList();
            copyCast.SmoothingSetPointInfosMainSet = SmoothingSetPointInfosMainSet == null ? null : SmoothingSetPointInfosMainSet.Select(x => x == null ? null : x.DeepCopy()).ToList();
            copyCast.StorageForSmoothingSetMainSet = StorageForSmoothingSetMainSet == null ? null : StorageForSmoothingSetMainSet.DeepCopy(null);
            copyCast.SmoothingSetPointInfosValidationSet = SmoothingSetPointInfosValidationSet == null ? null : SmoothingSetPointInfosValidationSet.Select(x => x == null ? null : x.DeepCopy()).ToList();
            copyCast.StorageForSmoothingSetValidation = StorageForSmoothingSetValidation == null ? null : StorageForSmoothingSetValidation.DeepCopy(null);
            copyCast.InitialDevelopmentCompleted = InitialDevelopmentCompleted;
            copyCast.IsCurrentlyBeingDeveloped = IsCurrentlyBeingDeveloped;
            copyCast.OptimalValueForZeroDimensions = OptimalValueForZeroDimensions;
            copyCast.savedOriginalValues = savedOriginalValues == null ? null : savedOriginalValues.ToArray();
        }

        // parameterless constructor -- needed by DeepCopy
        public OptimizePointsAndSmooth()
        {
        }

        public OptimizePointsAndSmooth(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            OverallStrategy = overallStrategy;
            Dimensions = dimensions;
            EvolutionSettings = evolutionSettings;
            Decision = decision;
            InitialDevelopmentCompleted = false;
            Name = name;
        }

        public virtual void SetCopyFieldsBaseOnly(IStrategyComponent copy)
        {
            SetCopyFields(copy);
        }

        // The following is useful for converting to RPROP Smoothing AFTER evolution is complete.
        public virtual RPROPSmoothing ConvertToRPROPSmoothing()
        {
            RPROPSmoothing rprop = new RPROPSmoothing();
            SetCopyFieldsBaseOnly(rprop);
            rprop.EvolutionSettings = rprop.EvolutionSettings.DeepCopy();
            rprop.EvolutionSettings.SmoothingOptions = new RPROPSmoothingOptions() 
            { // many of these don't matter -- the key ones are on the top
                FirstHiddenLayerNeurons = 10, 
                SecondHiddenLayerNeurons = 25, 
                Epochs = 10000, 
                TestValidationSetEveryNEpochs = 9999999, 
                PreliminaryOptimizationPrecision = 0.0001, 
                MemoryLimitForIterations = 1000000, 
                AutomaticallyGeneratePlots = false,
                SwitchToDirectEstimationWhenScoreRepresentsCorrectAnswer = false, 
                SizeOfOversamplingSample = 1000,
                SizeOfSuccessReplicationSample = 1000,
                StartSuccessReplicationFromScratch = true,
                SkipIfSuccessAttemptRateFallsBelow = 0.0001,
                MaxFailuresToRemember = 1000
            };
            rprop.CreateNeuralNetworkApproximation(false);
            return rprop;
        }

        public void DevelopStrategyComponent()
        {
            Stopwatch s = new Stopwatch();
            s.Start();

            bool stop = false;
            IsCurrentlyBeingDeveloped = true;

            if (Dimensions == 0)
            {
                DetermineOptimalValueForZeroDimensions();
                return;
            }

            SetUpProgressBar();

            PreSmoothingStepsForMainSetAndValidationSet(out stop);

            SimulationInteraction.CheckStopOrPause(out stop);
            if (!stop)
                SmoothingSteps();
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;
            if (!stop)
                PostSmoothingSteps();
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;

            IsCurrentlyBeingDeveloped = false;

            s.Stop();
            TabbedText.WriteLine("Elapsed seconds: " + s.ElapsedMilliseconds / 1000.0 + (EvolutionSettings.ParallelOptimization ? "" : " (with parallel execution disabled) ") + " current time: " + DateTime.UtcNow.ToLongTimeString());
        }

        private void PostSmoothingSteps()
        {
            if (EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet)
            {
                double newScore = ScoreStrategyBasedOnValidationSet();
                TabbedText.WriteLine("Score based on validation set: " + newScore);
            }
            bool printOutSmoothingPoints = false;
            int maxSmoothingPointsToPrint = 5;
            if (printOutSmoothingPoints)
            {
                var sp = SmoothingSetPointInfos.OrderBy(x => x.decisionInputs[0]).ToList();
                if (maxSmoothingPointsToPrint < sp.Count())
                {
                    var sp_orig = sp;
                    sp = new List<SmoothingSetPointInfo>();
                    for (int stp = 0; stp < maxSmoothingPointsToPrint; stp++)
                    {
                        int pointToPrint = (int)((stp + 1) * (double)sp_orig.Count() / (double) (maxSmoothingPointsToPrint + 1));
                        if (pointToPrint < 0)
                            pointToPrint = 0;
                        sp.Add(sp_orig[pointToPrint]);
                    }
                }
                foreach (SmoothingSetPointInfo pi in sp)
                    TabbedText.WriteLine(String.Join(",", pi.decisionInputs) + " ==> " + pi.preSmoothingValue + " ==> " + pi.postSmoothingValue);
            }
            bool automaticallyGeneratePlotsDefaultSetting = ((SmoothingOptionsWithPresmoothing)(EvolutionSettings.SmoothingOptions)).AutomaticallyGeneratePlots;
            bool automaticallyGeneratePlots = (automaticallyGeneratePlotsDefaultSetting && !OverallStrategy.Decision.DontAutomaticallyGeneratePlotRegardlessOfGeneralSetting) || (!automaticallyGeneratePlotsDefaultSetting && OverallStrategy.Decision.AutomaticallyGeneratePlotRegardlessOfGeneralSetting);
            if (automaticallyGeneratePlots)
            {
                if (SmoothingSetPointInfos[0].decisionInputs.Count == 1)
                    Create2dPlot(p => SmoothingSetPointInfos[p].postSmoothingValue, p => SmoothingSetPointInfosValidationSet[p].preSmoothingValue, "Smoothed values " + Name, "Smoothed" /* OverallStrategy.RepetitionWithinSimpleEquilibriumStrategy == null ? "" : "Repetition " + OverallStrategy.RepetitionWithinSimpleEquilibriumStrategy */);
                else if (SmoothingSetPointInfos[0].decisionInputs.Count == 2)
                    Create3dPlot(p => SmoothingSetPointInfos[p].postSmoothingValue, p => SmoothingSetPointInfosValidationSet[p].preSmoothingValue, "Smoothed values " + Name);
            }
            Func<int, bool> nextDecisionAfterNWillNeedInfo = n => n >= 0 && n < OverallStrategy.AllStrategies.Count - 1 && (OverallStrategy.AllStrategies[n + 1].Decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision || (OverallStrategy.AllStrategies[n].Decision.UseSimpleMethodForDeterminingEquilibrium && OverallStrategy.AllStrategies[n].Decision.UseFastConvergenceWithSimpleEquilibrium));
            bool nextDecisionWillNeedInformation = nextDecisionAfterNWillNeedInfo(OverallStrategy.DecisionNumber);
            if (!nextDecisionWillNeedInformation)
            {
                FreeUnnecessaryStorageAfterSmoothingComplete();
                int previous = OverallStrategy.DecisionNumber - 1;
                while (nextDecisionAfterNWillNeedInfo(previous))
                {
                    ((OptimizePointsAndSmooth)OverallStrategy.AllStrategies[previous + 1].GetCorrespondingStrategyComponentFromPreviousDecision()).FreeUnnecessaryStorageAfterSmoothingComplete();
                    previous--;
                }
                    
            }
            // if the next decision will need the information, then it will call FreeUnnecessaryStorage.

        }

        internal virtual void FreeUnnecessaryStorageAfterSmoothingComplete()
        {
            SmoothingSetPointInfosBeforeNarrowing = null;
            foreach (SmoothingSetPointInfo s in SmoothingSetPointInfos)
            {
                s.pointsInRunningSetClosestToThisPoint = null; // clear this space now
            }
        }

        internal virtual void SmoothingSteps()
        {
            // Note that this is likely to be overriden
            ReportSmoothingInfo();
            CopyPreSmoothedValuesToPostSmoothed();
        }

        internal virtual void ReportSmoothingInfo()
        {
            string smoothingInfo = String.Format("Smoothing using {0} points and {1} iterations ({2} max per point)", SmoothingPoints(), SmoothingGamePlayIterations(), MaxSmoothingGamePlayIterationsPerSmoothingPoint());
            TabbedText.WriteLine(smoothingInfo);
        }

        internal virtual void PreSmoothingStepsForMainSetAndValidationSet(out bool stop)
        {
            stop = false;

            SmoothingPointsMainSetWithPreviousPointsAdded = null;
            SmoothingPointsValidationSetWithPreviousPointsAdded = null;

            SwitchBackFromValidationSet();
            TabbedText.WriteLine("Main set");
            TabbedText.Tabs++;
            PreSmoothingSteps(out stop);

            TabbedText.Tabs--;

            if (EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet)
            {
                SwitchToValidationSet();
                TabbedText.WriteLine("Validation set");
                TabbedText.Tabs++;
                PreSmoothingSteps(out stop);
                TabbedText.Tabs--;
                if (!stop)
                    SwitchBackFromValidationSet();
            }

            if (!stop)
            {
                bool automaticallyGeneratePlotsDefaultSetting = ((SmoothingOptionsWithPresmoothing)(EvolutionSettings.SmoothingOptions)).AutomaticallyGeneratePlots;
                bool automaticallyGeneratePlots = (automaticallyGeneratePlotsDefaultSetting && !OverallStrategy.Decision.DontAutomaticallyGeneratePlotRegardlessOfGeneralSetting) || (!automaticallyGeneratePlotsDefaultSetting && OverallStrategy.Decision.AutomaticallyGeneratePlotRegardlessOfGeneralSetting);
                if (automaticallyGeneratePlots)
                {
                    if (SmoothingSetPointInfos[0].decisionInputs.Count == 1)
                        Create2dPlot(p => SmoothingSetPointInfos[p].preSmoothingValue, p => SmoothingSetPointInfosValidationSet[p].preSmoothingValue, "Unsmoothed values " + Name, "Unsmoothed" /* OverallStrategy.RepetitionWithinSimpleEquilibriumStrategy == null ? "" : "Repetition " + OverallStrategy.RepetitionWithinSimpleEquilibriumStrategy */);
                    else if (SmoothingSetPointInfos[0].decisionInputs.Count == 2)
                        Create3dPlot(p => SmoothingSetPointInfos[p].preSmoothingValue, p => SmoothingSetPointInfosValidationSet[p].preSmoothingValue, "Unsmoothed values " + Name);
                }
            }


        }

        private void PreSmoothingSteps(out bool stop)
        {
            stop = false;

            bool averageInPreviousVersions = !InValidationMode && EvolutionSettings.AverageInPreviousVersionsOfStrategy && OverallStrategy.CyclesStrategyDevelopment >= EvolutionSettings.StartAveragingInPreviousVersionsOfStrategyOnStepN;
            if (averageInPreviousVersions)
                OriginalStateToBeAveragedIn = (OptimizePointsAndSmooth)this.DeepCopy();

            ConsiderSkippingInitialDevelopment();

            if (!InitialDevelopmentCompleted)
            {
                if (Decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision)
                    PreSmoothingWhereInputsAndOccurrencesSameAsPreviousDecision();
                else
                {
                    // Initialize the smoothing set by calculating the decision inputs for each point in it, and adding these to a Hypercube for fast nearest neighbors searching.
                    TabbedText.WriteLine("Initializing...");

                    InitializeSmoothingSet();
                    SimulationInteraction.CheckStopOrPause(out stop);
                    if (stop)
                        return;

                    IdentifyClosestPointsAndOptimize(Decision.NeverEliminateIneligiblePoints); // the heart of the algorithm
                }
            }

            if (averageInPreviousVersions)
                AverageInPreviousVersionsOfSmoothingPoints();

            RememberSmoothingPointsForPotentialInsertionIntoNextOptimization();
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;

            HackForObfuscationGame();
        }

        int TotalAveragings = 0;
        private void AverageInPreviousVersionsOfSmoothingPoints()
        {
            if (OverallStrategy.CyclesStrategyDevelopment < EvolutionSettings.StartAveragingInPreviousVersionsOfStrategyOnStepN)
                return;
            TotalAveragings++;
            double weightOnNewOptimization;
            double weightOnOldOptimization;
            if (EvolutionSettings.UseAlternatingAveraging)
            {
                // On Step N, we average with previous step (Step N - 1)
                // On Step N + 1, we don't do any averaging.
                // On Step N + 2, we average with previous step (step N + 1)
                // Generally: If the evenness status of CyclesStrategyDevelopment is equal to the evenness status of N (i.e., both are even or both are odd),
                // then we average with the previous step. Otherwise, we don't do any averaging.
                if (OverallStrategy.CyclesStrategyDevelopment % 2 == EvolutionSettings.StartAveragingInPreviousVersionsOfStrategyOnStepN % 2)
                {
                    TabbedText.WriteLine("Averaging results with previous strategy.");
                    weightOnNewOptimization = 0.5;
                    weightOnOldOptimization = 0.5;
                }
                else
                {
                    TabbedText.WriteLine("Not averaging results with previous strategy.");
                    weightOnNewOptimization = 1.0;
                    weightOnOldOptimization = 0.0;
                }
            }
            else
            {
                TabbedText.WriteLine("Averaging results with previous strategies.");
                weightOnNewOptimization = 1.0 / (TotalAveragings + 1);
                weightOnOldOptimization = 1.0 - weightOnNewOptimization;
            }
            for (int s = 0; s < SmoothingSetPointInfos.Count(); s++)
            {
                double newOptimization = SmoothingSetPointInfos[s].preSmoothingValue;
                double oldOptimization = OriginalStateToBeAveragedIn.CalculateOutputForInputs(SmoothingSetPointInfos[s].decisionInputs);
                SmoothingSetPointInfos[s].preSmoothingValue = weightOnNewOptimization * newOptimization + weightOnOldOptimization * oldOptimization;
            }
            OriginalStateToBeAveragedIn = null;
        }

        private void PreSmoothingWhereInputsAndOccurrencesSameAsPreviousDecision()
        {
            TabbedText.WriteLine("Using closest points from previous decision...");
            OptimizePointsAndSmooth previousDecisionOptimizer = (OptimizePointsAndSmooth)OverallStrategy.GetCorrespondingStrategyComponentFromPreviousDecision();
            KDTreeForInputs = previousDecisionOptimizer.KDTreeForInputs.DeepCopy(null);
            // note that this will be called for both the main set and the validation set, and that the nnumber of points in each set will be recorded in EliminateIneligiblePoints.
            SmoothingSetPointInfos = previousDecisionOptimizer.SmoothingSetPointInfosBeforeNarrowing.Where(x => !x.isFromPreviousOptimization).Select(x => x.DeepCopy()).ToList();
            if (OverallStrategy.PresmoothingValuesComputedEarlier)
            {
                int additionalScoreIndex = (int)OverallStrategy.Decision.ScoresRecordedByDecisionNPrevious;
                foreach (SmoothingSetPointInfo s in SmoothingSetPointInfos)
                {
                    if (s.preSmoothingValuesForSubsequentDecisions != null)
                        s.preSmoothingValue = s.preSmoothingValuesForSubsequentDecisions[additionalScoreIndex - 1];
                }
            }
            InputAveragesInSmoothingSet = previousDecisionOptimizer.InputAveragesInSmoothingSet.ToList();
            InputStdevsInSmoothingSet = previousDecisionOptimizer.InputStdevsInSmoothingSet.ToList();
            previousDecisionOptimizer.FreeUnnecessaryStorageAfterSmoothingComplete(); // information may have been saved so that it could be copied here
            EliminateIneligiblePointsAndAddFormerPointsOutsideTrainingDataRange();

            InitialDevelopmentCompleted = true;
            // For each point in the smoothing set, take the corresponding points in the running set, and repeatedly play the game for each of them, to determine our preliminary estimate of the optimal value for the point in the smoothing set.
            TabbedText.WriteLine("Determining unsmoothed optimal value...");
            DetermineUnsmoothedOptimalValueForEachPointInSmoothingSet(true);
            if (!OverallStrategy.PresmoothingValuesComputedEarlier)
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
        }

        [NonSerialized]
        Strategy.StrategyState strategyContext = null;
        private void IdentifyClosestPointsAndOptimize(bool neverEliminateIneligiblePoints)
        {
            
            long smoothingGamePlayIterations = SmoothingGamePlayIterations();
            
            Stopwatch s = new Stopwatch();
            s.Start();
            if (ChunkIterationsForRemoting())
                strategyContext = OverallStrategy.RememberStrategyState();
            if (ChunkIterationsForRemoting() && !RemotingShouldSeparateFindingAndOptimizing())
            { 
                // We are going to do finding and smoothing as one integrated remote process, returning OptimalValueResults. This is faster, but relies on an assumption
                // that the optimal value for a set of smoothing points is the average of the optimal values for subsets of smoothing points. 
                bool useWorkerRolesForRemoting = UseWorkerRolesForRemoting();
                int chunkSizeForRemoting = ChunkSizeForRemoting();
                int minNumChunks = (int)Math.Round((double)smoothingGamePlayIterations / (double)chunkSizeForRemoting);
                OptimalValueResults[] cumulative = null;
                if (useWorkerRolesForRemoting)
                    cumulative = (OptimalValueResults[]) ChunkIterationsWithRemotingToFindAndOrOptimize(chunkSizeForRemoting, strategyContext, true, true);
                else
                    cumulative = ChunkIterationsWithoutRemotingToFindAndOptimize(chunkSizeForRemoting, strategyContext);
                ApplyAggregatedOptimalValueResults(cumulative);
                CompleteInitializationOfSmoothingSet(true);
                InitialDevelopmentCompleted = true;
                for (int ps = 0; ps < 2; ps++)
                    SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
            }
            else
                IdentifyClosestPointsAndOptimizeChunk(0, smoothingGamePlayIterations, true, new CancellationToken(), neverEliminateIneligiblePoints); // can ignore result, since no need to aggregate

            s.Stop();
            TabbedText.WriteLine("Total time elapsed: " + s.ElapsedMilliseconds);
            strategyContext = null;
        }

        long completedChunkSuccessesSoFar = 0; 
        OptimalValueResults[] cumulativeChunkResults = null;
        private bool ProcessCompletedChunkFindingAndOptimizing(object output, int index)
        {
            OptimalValueResults[] chunkResult = (OptimalValueResults[])output;
            completedChunkSuccessesSoFar += chunkResult.Sum(x => x == null ? 0 : x.numberOfObservations);
            if (cumulativeChunkResults == null)
                cumulativeChunkResults = chunkResult;
            else
                cumulativeChunkResults = AggregateOptimalValueResults(new List<OptimalValueResults[]> { cumulativeChunkResults, chunkResult });
            return completedChunkSuccessesSoFar >= SmoothingGamePlayIterations();
        }

        private bool ProcessCompletedChunkFindingOnly(object output, int index)
        {
            int numberFound = (int)output;
            completedChunkSuccessesSoFar += numberFound;
            return completedChunkSuccessesSoFar >= SmoothingGamePlayIterations();
        }

        private bool ProcessCompletedChunkOptimizingOnly(object output, int index)
        {
            return ProcessCompletedChunkFindingAndOptimizing(output, index); // use same approach
        }

        [NonSerialized]
        OptimizePointsAndSmoothRemotelyInfo optimizePointsAndSmoothRemotelyInfo = null;
        private object ChunkIterationsWithRemotingToFindAndOrOptimize(int chunkSizeForRemoting, Strategy.StrategyState strategyContext, bool find = true, bool optimize = true)
        {
            int minNumChunks = (int)Math.Round((double)SmoothingGamePlayIterations() / (double)chunkSizeForRemoting);
            if (optimizePointsAndSmoothRemotelyInfo == null || find)
                optimizePointsAndSmoothRemotelyInfo = new OptimizePointsAndSmoothRemotelyInfo() 
                { 
                    ChunkSize = chunkSizeForRemoting, 
                    Optimizer = this, 
                    StrategyState = strategyContext,
                    DecisionNumber = OverallStrategy.DecisionNumber,
                    Find = find,
                    Optimize = optimize
                };
            else
            {
                optimizePointsAndSmoothRemotelyInfo.Find = find; /* false */
                optimizePointsAndSmoothRemotelyInfo.Optimize = optimize; /* true */
                if (!(find == false && optimize == true))
                    throw new Exception("Internal error. Should not reuse remoting object except for optimization of each smoothing point phase.");
            }

            bool useAzureWorkerRoleForRemoting = UseWorkerRolesForRemoting();
            cumulativeChunkResults = null;
            completedChunkSuccessesSoFar = 0;
            if (useAzureWorkerRoleForRemoting)
            {
                if (find)
                    if (AzureSetup.useBlobsForInterRoleCommunication)
                        optimizePointsAndSmoothRemotelyInfo.SerializeStrategyContextToAzure();

                Func<object, int, bool> completionProcessor = null;
                if (find && optimize)
                    completionProcessor = ProcessCompletedChunkFindingAndOptimizing;
                else if (find)
                    completionProcessor = ProcessCompletedChunkFindingOnly;
                else if (optimize)
                    completionProcessor = ProcessCompletedChunkOptimizingOnly;

                ProcessInAzureWorkerRole p = new ProcessInAzureWorkerRole();
                if (!find && optimize)
                {
                    // we need to count the number of tasks that we will need to do the optimization for all the smoothing points (which may be grouped to minimize number of uploads)
                    int numPartitionsToSpreadOver = 10;
                    List<AzureTableGrouping.EntityGroupInfo> egis = AzureTableGrouping.GetEntityGroupingInfos(SmoothingSetPointInfos.Count(), numPartitionsToSpreadOver, "N/A");
                    List<Tuple<int, int>> itemRangeList = AzureTableGrouping.GetItemRangeList(SmoothingSetPointInfos.Count(), numPartitionsToSpreadOver, egis);
                    p.ExecuteTask(optimizePointsAndSmoothRemotelyInfo, "OptimizePointsAndSmooth", itemRangeList.Count(), false, completionProcessor);
                }
                else
                    p.ExecuteTask(optimizePointsAndSmoothRemotelyInfo, "OptimizePointsAndSmooth", minNumChunks, true, completionProcessor);
            }
            else
            {
                ProcessInSeparateAppDomain p = new ProcessInSeparateAppDomain();
                p.ExecuteTask(optimizePointsAndSmoothRemotelyInfo, "ACESim.OptimizePointsAndSmoothInSeparateAppDomain", minNumChunks, Int32.MaxValue, ProcessCompletedChunkFindingAndOptimizing, true);
            }
            if (optimize)
                optimizePointsAndSmoothRemotelyInfo = null; // we reset this to null now that we know we don't need to save it for the smoothing process
            if (optimize)
                return cumulativeChunkResults;
            else if (find)
                return completedChunkSuccessesSoFar;
            else throw new NotImplementedException("Internal error: Either find or smooth must be true.");
        }


        private OptimalValueResults[] ChunkIterationsWithoutRemotingToFindAndOptimize(int chunkSizeForRemoting, Strategy.StrategyState strategyContext)
        {

            bool originalTabbedTextStatus = TabbedText.EnableOutput;
            bool originalParallelizerStatus = Parallelizer.DisableParallel;
            TabbedText.EnableOutput = false;
            Parallelizer.DisableParallel = true;
            OptimalValueResults[] cumulative = null;
            byte[] byteArray = BinarySerialization.GetByteArray(this);
            OptimizePointsAndSmooth copyOfThis = (OptimizePointsAndSmooth)BinarySerialization.GetObjectFromByteArray(byteArray);
            copyOfThis.OverallStrategy.RecallStrategyState(strategyContext);
            long successesSoFar = 0;
            long iterationsNeeded = SmoothingGamePlayIterations();
            for (int c = 0; successesSoFar < iterationsNeeded; c++)
            {
                OptimalValueResults[] chunkResult = copyOfThis.IdentifyClosestPointsAndOptimizeChunk(((long)c) * ((long)chunkSizeForRemoting), (long)chunkSizeForRemoting, false, new CancellationToken(), false);
                successesSoFar += chunkResult.Sum(x => x.numberOfObservations);
                if (cumulative == null)
                    cumulative = chunkResult;
                else
                    cumulative = AggregateOptimalValueResults(new List<OptimalValueResults[]> { cumulative, chunkResult });
            }

            TabbedText.EnableOutput = originalTabbedTextStatus;
            Parallelizer.DisableParallel = originalParallelizerStatus;
            return cumulative;
        }

        private void ApplyAggregatedOptimalValueResults(OptimalValueResults[] aggregated)
        {
            for (int i = 0; i < SmoothingSetPointInfos.Count(); i++)
            {
                SmoothingSetPointInfos[i].preSmoothingValue = aggregated[i].preSmoothingValue;
                SmoothingSetPointInfos[i].preSmoothingValuesForSubsequentDecisions = aggregated[i].preSmoothingValuesForSubsequentDecisions;
                SmoothingSetPointInfos[i].pointsInRunningSetCount = aggregated[i].numberOfObservations;
            }

            //var orderedPoints = SmoothingSetPointInfos.OrderBy(x => x.decisionInputs[0]).ToList();
            //foreach (var pt in orderedPoints)
            //    Debug.WriteLine(pt.decisionInputs[0] + ": " + pt.preSmoothingValue + " " + pt.pointsInRunningSetCount);
        }

        private long CountSuccessfullyCompletedIterations(List<OptimalValueResults[]> listOfResultsForEachSmoothingPoint)
        {
            long total = 0;
            bool stopAsSoonAsNullValueEncountered = Parallelizer.EnsureConsistentIterationNumbers;
            bool keepGoing = true;
            int index = 0;
            while (keepGoing)
            {
                OptimalValueResults[] o = listOfResultsForEachSmoothingPoint[index];
                if (o == null && stopAsSoonAsNullValueEncountered)
                    keepGoing = false;
                if (keepGoing && o != null)
                {
                    foreach (OptimalValueResults ovr in o)
                        total += ovr.numberOfObservations;
                }
                index++;
                if (index == listOfResultsForEachSmoothingPoint.Count())
                    keepGoing = false;
            }
            return total;
        }

        private OptimalValueResults[] AggregateOptimalValueResults(List<OptimalValueResults[]> listOfResultsForEachSmoothingPoint)
        {
            if (listOfResultsForEachSmoothingPoint == null || !listOfResultsForEachSmoothingPoint.Any())
                return null;
            OptimalValueResults[] aggregated = new OptimalValueResults[SmoothingSetPointInfos.Count()];
            for (int i = 0; i < SmoothingSetPointInfos.Count(); i++)
            {
                StatCollector main = new StatCollector();
                StatCollectorArray sub = new StatCollectorArray();
                foreach (OptimalValueResults[] oList in listOfResultsForEachSmoothingPoint)
                {
                    OptimalValueResults o = oList[i];
                    if (o == null)
                        continue;
                    if (o.numberOfObservations > 0)
                    {
                        main.Add(o.preSmoothingValue, o.numberOfObservations);
                        if (o.preSmoothingValuesForSubsequentDecisions != null)
                            sub.Add(o.preSmoothingValuesForSubsequentDecisions, o.numberOfObservations);
                    }
                }
                int obs = (int) Math.Round(main.sumOfWeights);
                aggregated[i] = new OptimalValueResults { numberOfObservations = obs, preSmoothingValue = obs == 0 ? 0 : main.Average(), preSmoothingValuesForSubsequentDecisions = obs == 0 || !(sub.Initialized) ? null : sub.Average().ToArray() };
            }
            return aggregated;
        }

        public async Task<int> IdentifyClosestPointsAndSaveToAzure(int taskSetNum, int taskIndex, long skipIterations, long fixedNumberIterations, CancellationToken ct)
        {
            IdentifyClosestPoints(skipIterations, fixedNumberIterations, false, ct);
            CountPointsInRunningSet();
            int totalPointsFound = SmoothingSetPointInfos.Sum(x => x.pointsInRunningSetCount);
            List<List<IterationID>> pointsFound = SmoothingSetPointInfos.Select(x => x.pointsInRunningSetClosestToThisPoint.GetValues()).ToList();

            CloudTable table = AzureTableV2.GetCloudTable("SmoothingPoints");
            const int numPartitionsToSpreadOver = 10;
            List<Task<IList<TableResult>>> tasks = AzureTableGrouping.AddEntitiesGroupedAndBatched(table, pointsFound, numPartitionsToSpreadOver, "SPI" + taskSetNum.ToString(), taskIndex.ToString());
            await Task.WhenAll(tasks.ToArray());

            return totalPointsFound;
        }

        public async Task<OptimalValueResults[]> OptimizeSomeSmoothingPointsBasedOnIterationsSavedPreviouslyInAzure(int taskSetNum, int taskIndex, CancellationToken ct)
        {
            OptimalValueResults[] optimalValueResults = new OptimalValueResults[SmoothingSetPointInfos.Count()];

            taskSetNum--; // because it's the previous task set that produced the numbers
            CloudTable table = AzureTableV2.GetCloudTable("SmoothingPoints");
            const int numPartitionsToSpreadOver = 10;
            List<AzureTableGrouping.EntityGroupInfo> egis = AzureTableGrouping.GetEntityGroupingInfos(SmoothingSetPointInfos.Count(), numPartitionsToSpreadOver, "SPI" + taskSetNum.ToString());
            List<Tuple<int, int>> itemRangeList = AzureTableGrouping.GetItemRangeList(SmoothingSetPointInfos.Count(), numPartitionsToSpreadOver, egis);
            Tuple<int, int> itemRange = itemRangeList[taskIndex];

            List<List<IterationID>> allLists = await AzureTableGrouping.DownloadItemsAndMerge<List<IterationID>>(egis, itemRange.Item1, itemRange.Item2, table, AzureTableGrouping.MergeListOfLists<IterationID>);
            for (int p = itemRange.Item1; p <= itemRange.Item2; p++)
            {
                int indexInAllLists = p - itemRange.Item1;
                SmoothingSetPointInfos[p].pointsInRunningSetClosestToThisPoint = new PriorityQueue<double, IterationID>(allLists[indexInAllLists].Count(), Int32.MaxValue);
                foreach (IterationID iter in allLists[indexInAllLists])
                    SmoothingSetPointInfos[p].pointsInRunningSetClosestToThisPoint.Add(new KeyValuePair<double, IterationID>(1.0 /* ignored */, iter));
                optimalValueResults[p] = GetOptimalValueResultForSinglePointInSmoothingSet(p);
                SmoothingSetPointInfos[p].pointsInRunningSetClosestToThisPoint = null;
            }

            return optimalValueResults;
        }

        public OptimalValueResults[] IdentifyClosestPointsAndOptimizeChunk(long skipIterations, long? fixedNumberIterations, bool executingLocallyNotRemotely, CancellationToken ct, bool neverEliminateIneligiblePoints)
        {
            IdentifyClosestPoints(skipIterations, fixedNumberIterations, executingLocallyNotRemotely, ct);

            //DEBUG; // make eliminateIneligiblePoints false for particular decisions. 

            // Complete the initialization of the smoothing set, by eliminating points with too few corresponding points in the running set and calculating nearest neighbors within the smoothing set. 
            // We shouldn't do this when this is a remote execution, or we are about to use remote execution with separate finding and optimizing (since there would then be no points to eliminate).
            CompleteInitializationOfSmoothingSet(eliminateIneligiblePoints: !neverEliminateIneligiblePoints && executingLocallyNotRemotely && !(UseWorkerRolesForRemoting() && RemotingShouldSeparateFindingAndOptimizing()));

            InitialDevelopmentCompleted = true;

            return OptimizeAllSmoothingPoints(skipIterations, executingLocallyNotRemotely);
        }

        private OptimalValueResults[] OptimizeAllSmoothingPoints(long skipIterations, bool executingLocallyNotRemotely)
        {
            // For each point in the smoothing set, take the corresponding points in the running set, and repeatedly play the game for each of them, to determine our preliminary estimate of the optimal value for the point in the smoothing set.
            TabbedText.WriteLine("Determining unsmoothed optimal value...");
            OptimalValueResults[] results = null;
            if (executingLocallyNotRemotely && UseWorkerRolesForRemoting() && RemotingShouldSeparateFindingAndOptimizing())
            {
                results = (OptimalValueResults[])ChunkIterationsWithRemotingToFindAndOrOptimize(ChunkSizeForRemoting(), strategyContext, false, true);
                ApplyAggregatedOptimalValueResults(results);
                CompleteInitializationOfSmoothingSet(true);
            }
            else
                results = DetermineOptimalValueWrapper(updateProgressStep: executingLocallyNotRemotely);
            if (executingLocallyNotRemotely)
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);

            return results;
        }

        private void ResetStrategiesAfterIdentifyClosestPoint(long skipIterations)
        {
            foreach (Strategy s in OverallStrategy.AllStrategies)
                if (s.CacheFromPreviousOptimization != null && s.CacheFromPreviousOptimization.Any())
                    s.CacheFromPreviousOptimization = new ConcurrentDictionary<IterationID, CachedInputsAndScores>();

            OverallStrategy.NextIterationDuringSuccessReplication -= skipIterations;
        }

        private void IdentifyClosestPoints(long skipIterations, long? fixedNumberIterations, bool executingLocallyNotRemotely, CancellationToken ct)
        {
            // reset the values
            foreach (SmoothingSetPointInfo s in SmoothingSetPointInfos)
            {
                s.preSmoothingValue = 0;
                s.preSmoothingValuesForSubsequentDecisions = null;
                s.pointsInRunningSetClosestToThisPoint = new PriorityQueue<double, IterationID>();
                s.pointsInRunningSetCount = 0;
            }

            if (!executingLocallyNotRemotely)
                OverallStrategy.NextIterationDuringSuccessReplication += skipIterations;

            // Calculate the decision inputs for each point in the larger running set, and find the closest point in the smoothing set. Keep track of all such entries in the running set for each point in the smoothing set. 
            TabbedText.WriteLine("Identifying closest point for each iteration to run...");
            IdentifyClosestPointInSmoothingSetForEachIterationToRun(ct, skipIterations, fixedNumberIterations, executingLocallyNotRemotely);
            if (executingLocallyNotRemotely)
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);

            if (!executingLocallyNotRemotely)
                ResetStrategiesAfterIdentifyClosestPoint(skipIterations);
        }

        private void ConsiderSkippingInitialDevelopment()
        {
            if (Decision.UseFastConvergenceWithSimpleEquilibrium)
            {
                OptimizePointsAndSmooth previous = (OptimizePointsAndSmooth)OverallStrategy.GetCorrespondingStrategyComponentFromPreviousVersionOfThisStrategy();
                if (previous != null)
                {
                    SmoothingSetPointInfos = previous.SmoothingSetPointInfos.Select(x => x.DeepCopy()).ToList();
                    InitialDevelopmentCompleted = true;
                }
            }
            else if (SmoothingSetPointInfos == null || SmoothingSetPointInfos.Any(x => x.eligible && x.pointsInRunningSetClosestToThisPoint == null) || (!Decision.SkipIdentifyClosestPointOnSubsequentRepetitions))
            {
                SmoothingSetPointInfos = null;
                InitialDevelopmentCompleted = false; // must redo development, because some information was not serialized
            }
            else
            {
            }
        }

        private void HackForObfuscationGame()
        {
            // The following (somewhat hacky) code allows for the correct values to override the validation set values for a particular game, the obfuscation game.
            // In the future, we could generalize this to allow the game to indicate that there is some way to set the correct value.
            const bool alsoSetMainSetToCorrectValues = false; // useful if we want to determine what percentage of error comes from the neural network itself, rather than from our noisy estimates
            if ((InValidationMode || alsoSetMainSetToCorrectValues) && Decision.Name == "ObfuscationDecision")
            {
                //StatCollector err = new StatCollector();
                Parallelizer.Go(true, 0, SmoothingSetPointInfos.Count, i =>
                {
                    double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(SmoothingSetPointInfos[i].decisionInputs[1], SmoothingSetPointInfos[i].decisionInputs[0]);
                    // Debug.WriteLine("Estimate: " + smoothingSetPointInfos[i].preSmoothingValue + " correct answer: " + correctAnswer);
                    //err.Add(Math.Abs(correctAnswer - smoothingSetPointInfos[i].preSmoothingValue));
                    SmoothingSetPointInfos[i].preSmoothingValue = correctAnswer;
                });
                //Debug.WriteLine("Average error " + err.Average());
            }
        }

        private void SwitchToValidationSet()
        {
            if (!InValidationMode)
            {
                InitialDevelopmentCompletedMainSet = InitialDevelopmentCompleted;
                SmoothingSetPointInfosMainSet = SmoothingSetPointInfos;
                StorageForSmoothingSetMainSet = KDTreeForInputs;
                InitialDevelopmentCompleted = InitialDevelopmentCompletedValidation;
                SmoothingSetPointInfos = SmoothingSetPointInfosValidationSet;
                KDTreeForInputs = StorageForSmoothingSetValidation;
                InValidationMode = true;
            }
        }

        private void SwitchBackFromValidationSet()
        {
            if (InValidationMode)
            {
                InitialDevelopmentCompletedValidation = InitialDevelopmentCompleted;
                SmoothingSetPointInfosValidationSet = SmoothingSetPointInfos;
                StorageForSmoothingSetValidation = KDTreeForInputs;
                InitialDevelopmentCompleted = InitialDevelopmentCompletedMainSet;
                SmoothingSetPointInfos = SmoothingSetPointInfosMainSet;
                KDTreeForInputs = StorageForSmoothingSetMainSet;
                InValidationMode = false;
            }
        }

        internal int SmoothingPoints()
        {
            if (InValidationMode)
                return SmoothingPointsValidationSet();
            else
                return SmoothingPointsMainSet();
        }

        private int SmoothingPointsMainSet()
        {
            return 
                SmoothingPointsMainSetWithPreviousPointsAdded == null ? 
                    (int) (OverallStrategy.Decision.SmoothingPointsOverride ?? EvolutionSettings.SmoothingPointsMainSet.GetNumSmoothingPoints(Dimensions))
                    : 
                    (int)SmoothingPointsMainSetWithPreviousPointsAdded;
        }

        private int SmoothingPointsValidationSet()
        {
            return SmoothingPointsValidationSetWithPreviousPointsAdded == null ? 
                (int) (OverallStrategy.Decision.SmoothingPointsOverride ?? EvolutionSettings.SmoothingPointsValidationSet.GetNumSmoothingPoints(Dimensions))
                : 
                (int)SmoothingPointsValidationSetWithPreviousPointsAdded;
        }

        internal bool ChunkIterationsForRemoting()
        {
            if (InValidationMode)
                return (EvolutionSettings.SmoothingPointsValidationSet.ChunkIterationsForRemoting);
            else
                return (EvolutionSettings.SmoothingPointsMainSet.ChunkIterationsForRemoting);
        }

        internal bool RemotingShouldSeparateFindingAndOptimizing()
        {
            if (OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
                return false; // There is no reason to use the slower approach here.
            if (InValidationMode)
                return (EvolutionSettings.SmoothingPointsValidationSet.RemotingCanSeparateFindingAndSmoothing);
            else
                return (EvolutionSettings.SmoothingPointsMainSet.RemotingCanSeparateFindingAndSmoothing);
        }

        internal bool UseWorkerRolesForRemoting()
        {
            if (AzureSetup.runCompleteSettingsInAzure)
                return false;
            if (InValidationMode)
                return (EvolutionSettings.SmoothingPointsValidationSet.UseWorkerRolesForRemoting);
            else
                return (EvolutionSettings.SmoothingPointsMainSet.UseWorkerRolesForRemoting);
        }

        internal int ChunkSizeForRemoting()
        {
            if (InValidationMode)
                return (EvolutionSettings.SmoothingPointsValidationSet.ChunkSizeForRemoting);
            else
                return (EvolutionSettings.SmoothingPointsMainSet.ChunkSizeForRemoting);
        }

        internal long SmoothingGamePlayIterations()
        {
            if (OverallStrategy.Decision.IterationsOverride != null)
                return (long) OverallStrategy.Decision.IterationsOverride;
            double multiplier = OverallStrategy.Decision.IterationsMultiplier;
            if (multiplier == 0)
                multiplier = 1.0;
            if (InValidationMode)
                return (long) (EvolutionSettings.SmoothingPointsValidationSet.GetNumIterations(Dimensions) * multiplier);
            else
                return (long) (EvolutionSettings.SmoothingPointsMainSet.GetNumIterations(Dimensions) * multiplier);
        }

        internal int MaxSmoothingGamePlayIterationsPerSmoothingPoint()
        {
            if (InValidationMode)
                return EvolutionSettings.SmoothingPointsValidationSet.MaxIterationsPerSmoothingPoint;
            else
                return EvolutionSettings.SmoothingPointsMainSet.MaxIterationsPerSmoothingPoint;
        }

        internal virtual int CountSubstepsFromSmoothingItself()
        {
            return 0;
        }

        private void SetUpProgressBar()
        {
            int numSubsteps = 1; // for determining optimal value
            bool initialDevelopmentReallyCompleted = InitialDevelopmentCompleted;
            if (!Decision.UseFastConvergenceWithSimpleEquilibrium && (SmoothingSetPointInfos == null || SmoothingSetPointInfos.Any(x => x.eligible && x.pointsInRunningSetClosestToThisPoint == null) || !Decision.SkipIdentifyClosestPointOnSubsequentRepetitions))
                initialDevelopmentReallyCompleted = false;
            if (!initialDevelopmentReallyCompleted && !Decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision)
                numSubsteps += 2;
            if (OverallStrategy.PresmoothingValuesComputedEarlier)
                numSubsteps--;
            if (EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet)
                numSubsteps *= 2;
            numSubsteps += CountSubstepsFromSmoothingItself();
            SimulationInteraction.GetCurrentProgressStep().AddChildSteps(numSubsteps, "Develop strategy component substep " + Name);
        }


        private void InitializeSmoothingSet(bool reinitialization = false)
        {
            int approxMemoryLimit = ((SmoothingOptionsWithPresmoothing)(EvolutionSettings.SmoothingOptions)).MemoryLimitForIterations;
            int smoothingPoints = SmoothingPoints();
            if (!reinitialization)
            {
                SmoothingSetPointInfos = new List<SmoothingSetPointInfo>(smoothingPoints);
                for (int i = 0; i < smoothingPoints; i++)
                    SmoothingSetPointInfos.Add(new SmoothingSetPointInfo(MaxSmoothingGamePlayIterationsPerSmoothingPoint(), i, EvolutionSettings.SmoothingPointsMainSet.MaxIterationsPerSmoothingPoint, InValidationMode));
                SetSmoothingPointDecisionInputs();
            }
            double[] lowerBounds = new double[Dimensions];
            double[] upperBounds = new double[Dimensions];
            for (int d = 0; d < Dimensions; d++)
            {
                lowerBounds[d] = -4.0; // we'll start with +- 4 standard deviations, but this can be automatically expanded later
                upperBounds[d] = 4.0;
            }
            List<Point> listOfPoints = new List<Point>();
            if (!InValidationMode)
            {
                StatCollectorArray statCollectorForSmoothingSet = new StatCollectorArray();
                foreach (SmoothingSetPointInfo s in SmoothingSetPointInfos)
                    statCollectorForSmoothingSet.Add(s.decisionInputs.ToArray());
                InputAveragesInSmoothingSet = statCollectorForSmoothingSet.Average();
                InputStdevsInSmoothingSet = statCollectorForSmoothingSet.StandardDeviation();
                if (InputStdevsInSmoothingSet.Any(x => x == 0 || double.IsNaN(x)))
                    throw new Exception("Smoothing failed. One of the decision inputs was always the same value. Decision inputs must have some variance, unless there is a dynamic decision, in which case a constant input should have been filtered out before this point.");
            }
            int numberPerHypercube = 10;   /* 10 seems optimal for 2 dimensions and greater dimensions based on a preliminary study of efficiency */
            KDTreeForInputs = new KDTree(Dimensions, new List<Point>(), null, lowerBounds, upperBounds, -1, numberPerHypercube);
            KDTreeForInputs.SuspendSplit = true; // we suspend for now so that we'll end up with a balanced tree when we split after adding all the points
            int index = 0;
            for (int i = 0; i < smoothingPoints; i++)
            {
                List<double> decisionInputsSet = SmoothingSetPointInfos[i].decisionInputs;
                Point normalizedPoint = (Point)new NormalizedPoint(decisionInputsSet, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, index);

                KDTreeForInputs.AddPoint(normalizedPoint);
                index++;
            }
            KDTreeForInputs.CompleteInitializationAfterAddingAllPoints();
            if (!reinitialization)
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Develop strategy component substep " + Name);
        }

        object lockObj = new object();
        private void SetSmoothingPointDecisionInputs()
        {
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            if (!InValidationMode && OverallStrategy.Decision.ProduceClusteringPointsEvenlyFrom0To1)
            {
                List<double[]> clusters = ClusteringByFirstItem.GetEvenlySpacedClustersFrom0To1(SmoothingSetPointInfos.Count);
                for (int i = 0; i < SmoothingSetPointInfos.Count; i++)
                    SmoothingSetPointInfos[i].decisionInputs = clusters[i].ToList();
            }
            else if (!InValidationMode && EvolutionSettings.SmoothingPointsMainSet.ChooseSmoothingPointsByClusteringLargerNumberOfPoints)
                SetSmoothingPointDecisionInputsByClustering();
            else
            {
                // just get decision inputs for the specified number of points
                    // TabbedText.WriteLine("Number of unique sources: " + OverallStrategy.IterationsWhereDecisionIsReached.Select(x => x is IterationIDComposite ? ((IterationIDComposite)x).Source : x).Distinct().Count().ToString());
                var oldMode = System.Runtime.GCSettings.LatencyMode;
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                SetSmoothingPointsDecisionInputsWithoutClustering();
                if (Parallelizer.VerifyConsistentResults)
                    VerifyParallelism_IdenticalDecisionPoints();
                System.Runtime.GCSettings.LatencyMode = oldMode;
            }
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;

        }

        private void VerifyParallelism_IdenticalDecisionPoints()
        {
            List<SmoothingSetPointInfo> prevVersion = SmoothingSetPointInfos.Select(x => x.DeepCopy()).ToList();
            int smoothingPoints = SmoothingPoints();
            for (int s = 0; s < smoothingPoints; s++)
                SmoothingSetPointInfos[s].decisionInputs = null;
            SetSmoothingPointsDecisionInputsWithoutClustering();
            for (int s = 0; s < smoothingPoints; s++)
            {
                int numberInNewSet = SmoothingSetPointInfos.Count(x => x.decisionInputs.SequenceEqual(SmoothingSetPointInfos[s].decisionInputs));
                int numberInPreviousSet = SmoothingSetPointInfos.Count(x => x.decisionInputs.SequenceEqual(SmoothingSetPointInfos[s].decisionInputs));
                if (numberInNewSet != numberInPreviousSet)
                    throw new Exception("Internal error: The game is violating the assumptions of parallelism. Possibility 1) The code is dependent on the order in which it is run. Possibility 2) Earlier runs are making changes to data structures that are then relied on by later runs.");
            }
            double numberToVerifyAcrossRuns = SmoothingSetPointInfos.Select(x => x.decisionInputs[0]).OrderBy(x => x).Skip(14).First();
            Debug.WriteLine("Verify parallelism across runs number: " + numberToVerifyAcrossRuns);
        }

        private void SetSmoothingPointsDecisionInputsWithoutClustering()
        {
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            int smoothingPoints = SmoothingPoints();

            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, smoothingPoints, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(OverallStrategy.GenerateIterationID(iterationNumber), smoothingPoints, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                    SmoothingSetPointInfos[successNumber].decisionInputs = inputs;
                return decisionReached;
            });
            bool decisionTrueUniquenessRequired = true; // Should not be necessary, but can change that if we change our approach
            double uniquenessMultiplicationFactor = 0.9999;
            double uniquenessMultiplicationFactor2 = 0.9999999;
            if (decisionTrueUniquenessRequired)
            {
                bool decisionUnique;
                do
                {
                    decisionUnique = true;
                    for (int s = 0; s < smoothingPoints; s++)
                    {
                        for (int s1 = 0; s1 < smoothingPoints; s1++)
                        {
                            if (s != s1)
                            {
                                if (SmoothingSetPointInfos[s1].decisionInputs.SequenceEqual(SmoothingSetPointInfos[s].decisionInputs))
                                {
                                    decisionUnique = false;
                                    for (int index = 0; index < SmoothingSetPointInfos[s].decisionInputs.Count; index++)
                                    {
                                        SmoothingSetPointInfos[s].decisionInputs[index] *= uniquenessMultiplicationFactor; // make it slightly different to avoid errors
                                        uniquenessMultiplicationFactor *= uniquenessMultiplicationFactor2;
                                        if (SmoothingSetPointInfos[s].decisionInputs[index] == 0)
                                            SmoothingSetPointInfos[s].decisionInputs[index] = 0.000001 * uniquenessMultiplicationFactor;
                                    }
                                    //break;
                                }
                            }
                        }
                        //if (!decisionUnique)
                        //    break;

                    }
                }
                while (!decisionUnique);
            }
        }

        private void PrintFirstInClustersStats(IEnumerable<double[]> clusters, string text)
        {
            TabbedText.WriteLine(text);
            List<double> firstInClusters = clusters.Select(x => x.ToList().First()).OrderBy(x => x).ToList();
            StatCollector sc = new StatCollector();
            for (int i = 0; i < firstInClusters.Count() - 1; i++)
            {
                sc.Add(firstInClusters[i + 1] - firstInClusters[i]);
            }
            TabbedText.WriteLine("Difference avg: " + sc.Average() + " std: " + sc.StandardDeviation());
        }

        private void SetSmoothingPointDecisionInputsByClustering()
        {
            // we get decision inputs for a large number of points, and then we cluster them into SmoothingPoints() points
            // but first let's create initial cluster centers based on SmoothingPoints() points
            int smoothingPoints = SmoothingPoints();
            double[][] initialClusters = new double[smoothingPoints][];
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, smoothingPoints, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(OverallStrategy.GenerateIterationID(iterationNumber), smoothingPoints, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    initialClusters[successNumber] = inputs.ToArray();
                }
                return decisionReached;
            });

            PrintFirstInClustersStats(initialClusters, "Preclustering");
            double[][] trainingDataForClustering = new double[EvolutionSettings.SmoothingPointsMainSet.LargerNumberOfPointsFromWhichToClusterSmoothingPoints][];
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, EvolutionSettings.SmoothingPointsMainSet.LargerNumberOfPointsFromWhichToClusterSmoothingPoints, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(OverallStrategy.GenerateIterationID(iterationNumber), EvolutionSettings.SmoothingPointsMainSet.LargerNumberOfPointsFromWhichToClusterSmoothingPoints, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                    trainingDataForClustering[successNumber] = inputs.ToArray();
                return decisionReached;
            }
            );

            //FuzzyCMeansClustering clusterer = new FuzzyCMeansClustering();
            List<double[]> clusterCenters;
            //List<double> clusterOutputs;
            //clusterer.GetClusters(trainingDataForClustering.ToList(), null, initialClusters.ToList(), smoothingPoints, out clusterCenters, out clusterOutputs);

            //ProfileSimple.Start("ClusterAlgorithm");
            bool alwaysUseKMeans = false; // potentially useful for testing
            if (trainingDataForClustering[0].Length == 1 && !alwaysUseKMeans)
                clusterCenters = ClusteringByFirstItem.GetClusters(trainingDataForClustering.ToList(), smoothingPoints);
            else
                clusterCenters = KMeansClustering.GetClusters(trainingDataForClustering.ToList(), smoothingPoints);
            //ProfileSimple.End("ClusterAlgorithm");
            // The following can be uncommented to help see the effects of clustering.
            //var orderedClusterCenters = clusterCenters.OrderBy(x => x[0]).ToList();
            //KDTreeFromClusterCenters kdTreeFromClusterCenters = new KDTreeFromClusterCenters() { ClusterCenters = clusterCenters, Dimensions = 1 };
            //kdTreeFromClusterCenters.PrepareKDTree();
            //var pp = kdTreeFromClusterCenters.GetNumberOfPointsInEachCluster(trainingDataForClustering.ToList());
            //StatCollector statCollector = new StatCollector();
            //foreach (var r in pp)
            //    statCollector.Add(r);


            PrintFirstInClustersStats(clusterCenters, "Post clustering: ");
            for (int i = 0; i < smoothingPoints; i++)
            {
                SmoothingSetPointInfos[i].decisionInputs = clusterCenters[i].ToList();
            }
        }

        private void CalculateNormalizedLocationAndNearestNeighborsForEachPointInSmoothingSet()
        {
            int numNearestNeighbors = EvolutionSettings.SmoothingOptions.NumNearestNeighborsToCalculate();
            int smoothingPoints = SmoothingPoints();
            if (numNearestNeighbors > smoothingPoints - 1)
                numNearestNeighbors = smoothingPoints - 1;
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, smoothingPoints, i =>
            {
                if (SmoothingSetPointInfos[i].eligible)
                {
                    SmoothingSetPointInfo pointInfo = SmoothingSetPointInfos[i];
                    //Debug.WriteLine(String.Join(",",pointInfo.decisionInputs));
                    NormalizedPoint normalizedPoint = new NormalizedPoint(pointInfo.decisionInputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, i);
                    pointInfo.decisionInputsNormalized = normalizedPoint.GetLocation().ToList();
                    List<Point> nearestNeighbors = KDTreeForInputs.GetKNearestNeighbors(normalizedPoint, true, numNearestNeighbors);
                    pointInfo.nearestNeighbors = nearestNeighbors.Select(x => ((NormalizedPoint)x).AssociatedIndex).ToArray();
                }
            });
        }

        internal List<double> GetNormalizedLocation(List<double> unnormalizedLocation)
        {
            NormalizedPoint normalizedPoint = new NormalizedPoint(unnormalizedLocation, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            return normalizedPoint.GetLocation().ToList();
        }

        private void IdentifyClosestPointInSmoothingSetForEachIterationToRun(CancellationToken ct, long skipIterations = 0, long? fixedNumberIterations = null, bool executingLocallyNotRemotely = true)
        {
            if (executingLocallyNotRemotely && UseWorkerRolesForRemoting() && RemotingShouldSeparateFindingAndOptimizing())
                ChunkIterationsWithRemotingToFindAndOrOptimize(ChunkSizeForRemoting(), strategyContext, true, false);
            else
                IdentifyClosestPointInSmoothingSetForEachIterationToRunWithoutRemoting(ct, skipIterations, fixedNumberIterations);
        }

        long maxIterationNumber = -1;
        private void IdentifyClosestPointInSmoothingSetForEachIterationToRunWithoutRemoting(CancellationToken ct, long skipIterations = 0, long? fixedNumberIterations = null)
        {
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            long totalIterations = SmoothingGamePlayIterations();
            long reportEvery = totalIterations / 1000;
            ProgressStep progress = null;
            if (SimulationInteraction != null)
            {
                progress = SimulationInteraction.GetCurrentProgressStep();
                if (progress != null)
                    progress.PrepareToAutomaticallyUpdateProgress();
            }

            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights here
            int numSuccesses = 0;
            int numFailures = 0;
            maxIterationNumber = -1;
            var oldMode = System.Runtime.GCSettings.LatencyMode;
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;


            bool doParallel = EvolutionSettings.ParallelOptimization;

            bool cacheFromPreviousOptimizationContainsValues = OverallStrategy.CacheFromPreviousOptimizationContainsValues;
            int numberItemsInCacheNotYetFound = 0;
            ConcurrentDictionary<IterationID, CachedInputsAndScores> cache = null;
            if (cacheFromPreviousOptimizationContainsValues)
            {
                cache = OverallStrategy.CacheFromPreviousOptimization;
                numberItemsInCacheNotYetFound = cache.Count();
            }

            int? logIterationNumber1 = null;
            int? logDecisionNumber1 = 32;
            int? logIterationNumber2 = null;
            int? logDecisionNumber2 = 6;

            if (logIterationNumber1 != null)
                doParallel = false;
            if (fixedNumberIterations == null && progress != null)
            {
                Parallelizer.GoForSpecifiedNumberOfSuccesses(doParallel, totalIterations, (successNumber, iterationNumber) => progress.PerformActionAndAutomaticallyUpdatePartialProgress("Develop strategy component substep " + Name, successNumber, iterationNumber, totalIterations, reportEvery, (successNumber2, iterationNumber2) =>
                {
                    return IdentifyClosestPointForParticularIteration(skipIterations, totalIterations, oversamplingInfo, ref numSuccesses, ref numFailures, cacheFromPreviousOptimizationContainsValues, ref numberItemsInCacheNotYetFound, cache, logIterationNumber1, logDecisionNumber1, logIterationNumber2, logDecisionNumber2, iterationNumber2);
                }), 0, null, ct);
            }
            else
            {
                for (long iterationNumber2 = 0; iterationNumber2 < (long)fixedNumberIterations; iterationNumber2++)
                {
                    IdentifyClosestPointForParticularIteration(skipIterations, totalIterations, oversamplingInfo, ref numSuccesses, ref numFailures, cacheFromPreviousOptimizationContainsValues, ref numberItemsInCacheNotYetFound, cache, logIterationNumber1, logDecisionNumber1, logIterationNumber2, logDecisionNumber2, iterationNumber2);
                }
            }
            if (logIterationNumber1 != null)
                doParallel = true;
            System.Runtime.GCSettings.LatencyMode = oldMode;
            if (fixedNumberIterations != null)
                TabbedText.WriteLine("Successes: " + numSuccesses + " failures: " + numFailures + " successPerAttemptRatio: " + ((double)numSuccesses) / ((double)numSuccesses + (double)numFailures) + " attempts per 1000 successes: " + (1000.0 * ((double)numSuccesses + (double)numFailures)) / ((double)numSuccesses));


            //if (!EvolutionSettings.ParallelOptimization)
            //{
            //    ProfileSimple.ReportCumulative("GetDecisionInputsForIteration");
            //    ProfileSimple.ReportCumulative("GetNearestNeighbor");
            //    ProfileSimple.ReportCumulative("GetSmallestContainingHypercube");
            //    ProfileSimple.ReportCumulative("GetNearbyKPoints");
            //    ProfileSimple.ReportCumulative("GetHypercubesWithinDistanceOfPoint");
            //    ProfileSimple.ReportCumulative("AddingPointsFromHypercubes");
            //    ProfileSimple.ReportCumulative("AddingPointsFromHypercubes1");
            //    ProfileSimple.ReportCumulative("AddingPointsFromHypercubes2");
            //    ProfileSimple.ReportCumulative("Compiling final");
            //    ProfileSimple.ReportCumulative("AddPointToList");
            //}
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        }

        private bool IdentifyClosestPointForParticularIteration(long skipIterations, long totalIterations, OversamplingInfo oversamplingInfo, ref int numSuccesses, ref int numFailures, bool cacheFromPreviousOptimizationContainsValues, ref int numberItemsInCacheNotYetFound, ConcurrentDictionary<IterationID, CachedInputsAndScores> cache, int? logIterationNumber1, int? logDecisionNumber1, int? logIterationNumber2, int? logDecisionNumber2, long iterationNumber2)
        {
            long iterationNumber3 = iterationNumber2 + skipIterations;
            bool disableCache = false;

            // to log one or two decisions, indicate iteration and decision numbers here
            if ((iterationNumber3 == logIterationNumber1 && OverallStrategy.DecisionNumber == logDecisionNumber1) || (iterationNumber3 == logIterationNumber2 && OverallStrategy.DecisionNumber == logDecisionNumber2))
            {
                GameProgressLogger.LoggingOn = true;
                GameProgressLogger.OutputLogMessages = true;
                disableCache = true;
            }
            if (iterationNumber3 > maxIterationNumber)
                maxIterationNumber = iterationNumber3;
            bool decisionReached = false;
            IterationID iterationID = OverallStrategy.GenerateIterationID(iterationNumber3);
            GameProgress preplayedGameProgressInfo;
            List<double> decisionInputs = null;
            if (!disableCache && cacheFromPreviousOptimizationContainsValues && numberItemsInCacheNotYetFound > 0)
            {
                CachedInputsAndScores cached = null;
                bool itemAvailable = cache.TryGetValue(iterationID, out cached);
                if (itemAvailable)
                {
                    decisionInputs = OverallStrategy.FilterInputsWhenUsingDefaultInputGroup(cached.Inputs); // we'll add the scores later -- but we need to add the inputs so that everything matches
                    decisionReached = true;
                    Interlocked.Decrement(ref numberItemsInCacheNotYetFound);
                }
                else
                    decisionReached = false; // we will go on to look for new iterations only once we have gone through all cached values
            }
            else if (decisionInputs == null)
                decisionInputs = OverallStrategy.GetDecisionInputsForIteration(iterationID, 
totalIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
            if (decisionReached)
            {
                Interlocked.Increment(ref numSuccesses);
                ConsiderAddingToPriorityQueue(iterationID, decisionInputs);
            }
            else
            {
                // decisionInputs = OverallStrategy.GetDecisionInputsForIteration(iterationID, totalIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo); 
                Interlocked.Increment(ref numFailures);
            }

            if ((iterationNumber3 == logIterationNumber1 && OverallStrategy.DecisionNumber == logDecisionNumber1) || (iterationNumber3 == logIterationNumber2 && OverallStrategy.DecisionNumber == logDecisionNumber2))
            {
                GameProgressLogger.LoggingOn = false;
                GameProgressLogger.OutputLogMessages = true;
            }
            return decisionReached;
        }

        private void ConsiderAddingToPriorityQueue(IterationID iterationID, List<double> decisionInputs)
        {
            //if (!EvolutionSettings.ParallelOptimization)
            //    ProfileSimple.End("GetDecisionInputsForIteration", true);
            NormalizedPoint point = new NormalizedPoint(decisionInputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            //if (!EvolutionSettings.ParallelOptimization)
            //    ProfileSimple.Start("GetNearestNeighbor");
            NormalizedPoint nearestNeighbor = (NormalizedPoint)KDTreeForInputs.GetNearestNeighbor(point, false); // Note: This may exclude points where point could not be close enough to be in its priority queue.

            if (nearestNeighbor != null)
            { // nearest neighbor will return null if there is no point X within its own MaximumDistanceForNearestNeighbor threshold of the new point
                double normalizedDistance = point.DistanceTo(nearestNeighbor);
                double normalizedDistanceSquared = normalizedDistance * normalizedDistance;
                //if (!EvolutionSettings.ParallelOptimization)
                //    ProfileSimple.End("GetNearestNeighbor", true);
                //if (!EvolutionSettings.ParallelOptimization)
                //    ProfileSimple.Start("AddPointToList");
                SmoothingSetPointInfo nearestNeighborPointInfo = SmoothingSetPointInfos[nearestNeighbor.AssociatedIndex];
                //lock (nearestNeighborPointInfo.pointsInRunningSetLock)
                //{
                PriorityQueue<double, IterationID> priorityQueue = nearestNeighborPointInfo.pointsInRunningSetClosestToThisPoint;
                //if (nearestNeighbor.AssociatedIndex == 0)
                //    Debug.WriteLine("Trying to add iteration " + i + " normalized dist sq " + normalizedDistanceSquared);
                bool full;
                priorityQueue.Enqueue(normalizedDistanceSquared, iterationID, out full); // will only add if priority is sufficiently good
                double? distanceOfLowestPriorityItemIfFull = priorityQueue.LowestPriorityOrNullIfNotYetFull;
                if (distanceOfLowestPriorityItemIfFull != null) // null means that it's not full
                    nearestNeighbor.MaximumSquaredDistanceForNearestNeighbor = distanceOfLowestPriorityItemIfFull; // Set this distance into the point so that the nearest neighbor algorithm can stop sooner with points that are further away.
                //}
            }
            //if (!EvolutionSettings.ParallelOptimization)
            //    ProfileSimple.End("AddPointToList", true);
        }

        private void CompleteInitializationOfSmoothingSet(bool eliminateIneligiblePoints)
        {
            CountPointsInRunningSet();

            if (eliminateIneligiblePoints)
            {
                MarkPointsAsIneligibleAndRemoveFromTree();
                EliminateIneligiblePointsAndAddFormerPointsOutsideTrainingDataRange();
            }
        }

        private void CountPointsInRunningSet()
        {
            foreach (var point in SmoothingSetPointInfos)
            {
                if (point.pointsInRunningSetClosestToThisPoint != null && point.pointsInRunningSetClosestToThisPoint.Count > 0)
                    point.pointsInRunningSetCount = point.pointsInRunningSetClosestToThisPoint.Count();
            }
        }

        private void MarkPointsAsIneligibleAndRemoveFromTree()
        {
            // Eliminate from smoothing set points with two few matches from the running set.
            int minimumMatchesToKeepInSmoothingSet = 2;
            bool measureMinimumRelativeToOthers = true;
            if (measureMinimumRelativeToOthers)
            {
                StatCollector sc = new StatCollector();
                foreach (var point in SmoothingSetPointInfos)
                {
                    int count = point.pointsInRunningSetCount;
                    if (count > 0)
                        sc.Add(count);
                }
                minimumMatchesToKeepInSmoothingSet = (int)(sc.Average() - 7.0 * sc.StandardDeviation()); // target points that are real outliers
                if (minimumMatchesToKeepInSmoothingSet < 2)
                    minimumMatchesToKeepInSmoothingSet = 2;
            }
            if (SmoothingSetPointInfos.Count(x => x.pointsInRunningSetCount >= minimumMatchesToKeepInSmoothingSet) <= 5)
                minimumMatchesToKeepInSmoothingSet = 1;

            object lockObj = new object();
            KDTreeForInputs.ReadOnly = false;
            int smoothingPoints = SmoothingPoints();
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, smoothingPoints, i =>
            {
                var pointInfo = SmoothingSetPointInfos[i];
                if (pointInfo.pointsInRunningSetCount < minimumMatchesToKeepInSmoothingSet)
                {
                    lock (lockObj)
                    {
                        pointInfo.eligible = false;
                        Point thePoint = KDTreeForInputs.PointsWithin.First(x => ((NormalizedPoint)x).AssociatedIndex == i);
                        KDTreeForInputs.RemovePoint(thePoint);
                    }
                }
            });
            KDTreeForInputs.ReadOnly = true;
        }

        private void EliminateIneligiblePointsAndAddFormerPointsOutsideTrainingDataRange()
        {
            // Create a list of replacementSmoothingSetInfos, consisting of all eligible points. At the point where this method is called, this should consist of all points currently in the kdTree.
            List<SmoothingSetPointInfo> replacementSmoothingSetInfos = SmoothingSetPointInfos.Where(x => x.eligible).ToList();
            if (!replacementSmoothingSetInfos.Any())
                throw new Exception("There are so few observations that we have been left with no smoothing set points.");
            // Now, add points from the previous strategy development IF they are outside the training range of the smoothing points developed so far. This ensures that if data is narrowed, the strategy still remembers what to do when confronted with data outside the narrowed training range, which is critical for training other strategies whose result depends on this strategy's acting sensibly.
            List<SmoothingSetPointInfo> previous = InValidationMode ? PreviousSmoothingSetPointInfosValidationSet : PreviousSmoothingSetPointInfosMainSet;
            if (previous != null && previous.Any())
            {
                // Find the nearest neighbor for each point already in the replacement set and calculate the distance (not squared). Figure out the average nearest distance and standard deviation.
                // Also, calculate relevant statistics for each dimension.
                StatCollector nearestDistanceForPointsAlreadyInSet = new StatCollector();
                StatCollector[] statsForEachDimension = new StatCollector[Dimensions];
                for (int d = 0; d < Dimensions; d++)
                    statsForEachDimension[d] = new StatCollector();
                foreach (var sspi in replacementSmoothingSetInfos)
                {
                    double distance = GetDistanceToNearestInKDTree(sspi);
                    nearestDistanceForPointsAlreadyInSet.Add(distance);
                    for (int d = 0; d < Dimensions; d++)
                        statsForEachDimension[d].Add(sspi.decisionInputs[d]);
                }
                double averageDistance = nearestDistanceForPointsAlreadyInSet.Average();
                double standardDeviation = nearestDistanceForPointsAlreadyInSet.StandardDeviation();
                double farAwayFromNearest = averageDistance + 3.0 * standardDeviation;
                
                //Now, for each smoothing point in the old set, determine whether to add to the replacement smoothing set point infos.
                //Criteria (aside from the point being eligible):
                //(1) The average nearest distance is at least 3 standard deviations from the mean (meaning that it is far from any point in the set).
                //OR (2) The location along any single dimension is outside the range for that dimension or at least 4 standard deviations from the mean
                foreach (var sspi in previous)
                {
                    bool? qualifying = null;
                    if (!sspi.eligible)
                        qualifying = false;
                    else
                    {
                        double distance = GetDistanceToNearestInKDTree(sspi, excludeExactMatch: false);
                        const bool enableAddingFormerPointsOutsideTrainingDataRange = true;
                        if (distance > farAwayFromNearest && enableAddingFormerPointsOutsideTrainingDataRange)
                            qualifying = true;
                        if (qualifying == null)
                        {
                            for (int d = 0; d < Dimensions; d++)
                            {
                                double locationInThisDimension = sspi.decisionInputs[d];
                                if (locationInThisDimension < statsForEachDimension[d].Min || locationInThisDimension > statsForEachDimension[d].Max || (Math.Abs(locationInThisDimension - statsForEachDimension[d].Average()) > 4.0 * statsForEachDimension[d].StandardDeviation()))
                                {
                                    if (enableAddingFormerPointsOutsideTrainingDataRange)
                                        qualifying = true;
                                    break;
                                }
                            }
                        }
                        if (qualifying == true)
                        {
                            sspi.isFromPreviousOptimization = true;
                            replacementSmoothingSetInfos.Add(sspi);
                        }
                    }
                }

            }

            SmoothingSetPointInfosBeforeNarrowing = SmoothingSetPointInfos;
            SmoothingSetPointInfos = replacementSmoothingSetInfos;
            if (InValidationMode)
                SmoothingPointsValidationSetWithPreviousPointsAdded = SmoothingSetPointInfos.Count();
            else
                SmoothingPointsMainSetWithPreviousPointsAdded = SmoothingSetPointInfos.Count();
            replacementSmoothingSetInfos = null;
            ReinitializeSmoothingSetAndRecalculateNearestNeighbors();
        }

        private void ReinitializeSmoothingSetAndRecalculateNearestNeighbors()
        {
            InitializeSmoothingSet(reinitialization: true);
            CalculateNormalizedLocationAndNearestNeighborsForEachPointInSmoothingSet();
        }

        private void RememberSmoothingPointsForPotentialInsertionIntoNextOptimization()
        {
            var copyToRemember = SmoothingSetPointInfos.Select(x => x.DeepCopy(excludeExtrinsicInformation: true)).ToList();
            if (InValidationMode)
                PreviousSmoothingSetPointInfosValidationSet = copyToRemember;
            else
                PreviousSmoothingSetPointInfosMainSet = copyToRemember;
        }

        private double GetDistanceToNearestInKDTree(SmoothingSetPointInfo sspi, bool excludeExactMatch = true)
        {
            NormalizedPoint point = new NormalizedPoint(sspi.decisionInputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            Point nearestNeighborExcludingSelf = KDTreeForInputs.GetNearestNeighbor(point, excludeExactMatch);
            double distance = point.DistanceTo(nearestNeighborExcludingSelf);
            return distance;
        }

        [Serializable]
        public class OptimalValueResults
        {
            public int numberOfObservations;
            public double preSmoothingValue;
            public double[] preSmoothingValuesForSubsequentDecisions;
        }

        private OptimalValueResults GetOptimalValueResultForSinglePointInSmoothingSet(int smoothingPointIndex)
        {
            DetermineUnsmoothedOptimalValueForParticularPointInSmoothingSet(smoothingPointIndex);
            SmoothingSetPointInfo p = SmoothingSetPointInfos[smoothingPointIndex];
            OptimalValueResults ovr = new OptimalValueResults();
            ovr.numberOfObservations = p.pointsInRunningSetClosestToThisPoint == null ? 0 : p.pointsInRunningSetClosestToThisPoint.Count();
            ovr.preSmoothingValue = p.preSmoothingValue;
            ovr.preSmoothingValuesForSubsequentDecisions = p.preSmoothingValuesForSubsequentDecisions;
            return ovr;
        }

        // We create this wrapper so that we can extract the key information that we have saved, so that
        // we can better parallelize this process over multiple AppDomains or worker roles.
        public OptimalValueResults[] DetermineOptimalValueWrapper(bool updateProgressStep = true)
        {
            DetermineUnsmoothedOptimalValueForEachPointInSmoothingSet(updateProgressStep);
            OptimalValueResults[] results = new OptimalValueResults[SmoothingSetPointInfos.Count];
            int i = 0;
            foreach (var p in SmoothingSetPointInfos)
            {
                results[i] = new OptimalValueResults();
                results[i].numberOfObservations = p.pointsInRunningSetClosestToThisPoint == null ? 0 : p.pointsInRunningSetClosestToThisPoint.Count();
                results[i].preSmoothingValue = p.preSmoothingValue;
                results[i].preSmoothingValuesForSubsequentDecisions = p.preSmoothingValuesForSubsequentDecisions;
                i++;
            }
            return results;
        }

        private void DetermineUnsmoothedOptimalValueForEachPointInSmoothingSet(bool updateProgressStep)
        {
            if (OverallStrategy.PresmoothingValuesComputedEarlier)
                return;

            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true; // we need to keep track of scores separately for each thread, since we will be simultaneously calculating scores for different smoothing points

            if (OverallStrategy.UseFastConvergence)
            {
                if (OverallStrategy.PreviousVersionOfThisStrategy != null)
                    for (int i = 0; i < SmoothingSetPointInfos.Count; i++)
                        if (SmoothingSetPointInfos[i].preSmoothingValue == 0) // only if it hasn't been preserved
                            SmoothingSetPointInfos[i].preSmoothingValue = OverallStrategy.PreviousVersionOfThisStrategy.Calculate(SmoothingSetPointInfos[i].decisionInputs);
            }

            if (!InValidationMode && ((Decision.TestInputs != null && Decision.TestInputs.Any()) || (Decision.TestInputsList != null && Decision.TestInputsList.Any())) && Decision.TestOutputs != null && Decision.TestOutputs.Any())
            {
                if (Decision.TestInputsList != null && Decision.TestInputsList.Any())
                {
                    foreach (List<double> specificTestInputs in Decision.TestInputsList)
                    {
                        Debug.WriteLine("Input set to test: " + String.Join(",", specificTestInputs.ToArray()));
                        TestNearParticularPoint(specificTestInputs, Decision.TestOutputs);
                    }
                }
                else
                    TestNearParticularPoint(Decision.TestInputs, Decision.TestOutputs);
            }

            int smoothingPoints = SmoothingSetPointInfos.Count;
            ProgressStep progress = null;
            if (updateProgressStep)
                progress = SimulationInteraction.GetCurrentProgressStep();
            var oldMode = System.Runtime.GCSettings.LatencyMode;
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; // reduce garbage collection

            if (updateProgressStep)
            {
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, smoothingPoints, i0 => progress.PerformActionAndAutomaticallyUpdatePartialProgress("Develop strategy component substep " + Name, i0, smoothingPoints, 20, i2 =>
                {
                    int i = (int)i2;
                    if (SmoothingSetPointInfos[i].eligible) // we only optimize points that have help unless there are very few that need help
                    {
                        DetermineUnsmoothedOptimalValueForParticularPointInSmoothingSet(i);
                    }
                }));
            }
            else
            {
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, smoothingPoints, i2 =>
                {
                    int i = (int)i2;
                    if (SmoothingSetPointInfos[i].eligible) // we only optimize points that have help unless there are very few that need help
                    {
                        DetermineUnsmoothedOptimalValueForParticularPointInSmoothingSet(i);
                    }
                });
            }
            System.Runtime.GCSettings.LatencyMode = oldMode;
            if (Decision.Bipolar)
                ImproveOptimizationOfCloseCasesForBipolarDecisions();
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;

            //var orderedPoints = SmoothingSetPointInfos.OrderBy(x => x.decisionInputs[0]).ToList();
            //foreach (var pt in orderedPoints)
            //    Debug.WriteLine(pt.decisionInputs[0] + ": " + pt.preSmoothingValue + " " + pt.pointsInRunningSetCount);

            if (OverallStrategy.UseFastConvergence)
                OverallStrategy.FastConvergenceCurrentShiftDistance = SmoothingSetPointInfos.Where(x => x.fastConvergenceShiftValue != null).Max(x => x.fastConvergenceShiftValue);

        }

        private OversamplingInfo GetOversamplingInfoWhereWeightsAreNeeded()
        {
            return new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreWeightsForAdjustmentOfScoreAverages = true, ReturnedWeightsToApplyToObservation = new List<double>(), StoreInputSeedsForImprovementOfOversamplingPlan = false };
        }

        private void DetermineUnsmoothedOptimalValueForParticularPointInSmoothingSet(int i)
        {
            if (SmoothingSetPointInfos[i].isFromPreviousOptimization)
                return;

            List<IterationID> specificIterationsFromRunSetToUse = SmoothingSetPointInfos[i].pointsInRunningSetClosestToThisPoint.GetValues();

            long smoothingIterations = SmoothingGamePlayIterations();

            double optimalValue;
            double[] optimalValuesForSubsequentDecisions = null; // will be created only if necessary
            if (Decision.ScoreRepresentsCorrectAnswer)
            {
                double[] scoresForSubsequentDecisions = null; // will be returned if OverallStrategy.PresmoothingValuesComputedEarlier
                // This is much faster than having the score represent the squared error and then using FindOptimalPoint to zoom into the correct answer.

                // Originally, we called PlaySpecificValueForSomeIterations here with the strategy bounds midpoint instead of null.
                // But we shouldn't need to do this. If we don't play a specific value, it will revert to its default behavior if necessary.
                // Moreover, this causes problems if later decisions rely on this number, and this number depends in part on those later decisions.
                // In the example we had, bargaining round 2 depended on bargaining round 1 (where we were estimating relative values), but the correct
                // answer for bargaining round 1 was dependent on bargaining round 2.
                //double average = OverallStrategy.PlaySpecificValueForSomeIterations((Decision.StrategyBounds.LowerBound + Decision.StrategyBounds.UpperBound) / 2.0, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded(), out scoresForSubsequentDecisions);
                
                double average = OverallStrategy.PlaySpecificValueForSomeIterations(null, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded(), out scoresForSubsequentDecisions);
                
                if (Decision.Bipolar)
                    optimalValue = (average > 0) ? 1.0 : -1.0;
                else
                    optimalValue = average;
                if (OverallStrategy.Decision.SubsequentDecisionsToRecordScoresFor > 0)
                {
                    optimalValuesForSubsequentDecisions = new double[OverallStrategy.Decision.SubsequentDecisionsToRecordScoresFor];
                    for (int j = 0; j < OverallStrategy.Decision.SubsequentDecisionsToRecordScoresFor; j++)
                    {
                        if (Decision.Bipolar)
                            optimalValuesForSubsequentDecisions[j] = (scoresForSubsequentDecisions[j] > 0) ? 1.0 : -1.0;
                        else
                            optimalValuesForSubsequentDecisions[j] = scoresForSubsequentDecisions[j];
                    }
                }
            }
            else if (Decision.Bipolar)
            {
                double negativeOneResult = OverallStrategy.PlaySpecificValueForSomeIterations(-1.0, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                double positiveOneResult = OverallStrategy.PlaySpecificValueForSomeIterations(1.0, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                SmoothingSetPointInfos[i].absoluteDifferenceBetweenBipolarStrategies = Math.Abs(negativeOneResult - positiveOneResult);
                bool storeDifferenceBetweenScoresAsOptimalValue = true; // we have switched to this approach, because it provides for smoother data. For points where the negative and positive are very close, this will end up being very close to zero.
                if (storeDifferenceBetweenScoresAsOptimalValue)
                {
                    if (Decision.HighestIsBest)
                        optimalValue = positiveOneResult - negativeOneResult; // if positive is greater, that is better, so return true, else false
                    else
                        optimalValue = negativeOneResult - positiveOneResult;
                }
                else
                {
                    if (Decision.HighestIsBest == negativeOneResult > positiveOneResult)
                        optimalValue = -1.0;
                    else
                        optimalValue = 1.0;
                }
            }
            else
            {
                if (OverallStrategy.UseFastConvergence)
                    optimalValue = GetOptimalValueForFastConvergence(specificIterationsFromRunSetToUse, smoothingIterations, i);
                else
                    optimalValue = FindOptimalPoint.Optimize(
                        Decision.StrategyBounds.LowerBound, Decision.StrategyBounds.UpperBound,
                        ((SmoothingOptionsWithPresmoothing)(EvolutionSettings.SmoothingOptions)).PreliminaryOptimizationPrecision,
                        valueToTest => OverallStrategy.PlaySpecificValueForSomeIterations(valueToTest, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded()),
                        Decision.HighestIsBest,
                        10,
                        4,
                        true,
                        Decision.StrategyBounds.AllowBoundsToExpandIfNecessary);
            }
            
            SmoothingSetPointInfos[i].preSmoothingValue = optimalValue;
            SmoothingSetPointInfos[i].preSmoothingValuesForSubsequentDecisions = optimalValuesForSubsequentDecisions;

            //smoothingSetPointInfos[i].pointsInRunningSetClosestToThisPoint = null; // free memory
        }

        private double GetOptimalValueForFastConvergence(List<IterationID> specificIterationsFromRunSetToUse, long smoothingIterations, int smoothingSetPointIndex)
        {
            double currentValue = SmoothingSetPointInfos[smoothingSetPointIndex].preSmoothingValue;
            double originalValue = currentValue;
            double shiftDistance;
            const double multiplierToDetermineShiftDistanceFloor = 0.25;
            if (SmoothingSetPointInfos[smoothingSetPointIndex].fastConvergenceShiftValue != null)
            {
                shiftDistance = (double)SmoothingSetPointInfos[smoothingSetPointIndex].fastConvergenceShiftValue;
                if (OverallStrategy.FastConvergenceCurrentShiftDistance != null && shiftDistance < OverallStrategy.FastConvergenceCurrentShiftDistance * multiplierToDetermineShiftDistanceFloor)
                    return currentValue; // we're not going to bother doing anything if the shift distance here is much lower than for elsewhere
            }
            else
                shiftDistance = (OverallStrategy.Decision.StrategyBounds.UpperBound - OverallStrategy.Decision.StrategyBounds.LowerBound) / 20.0;
            double currentScore = OverallStrategy.PlaySpecificValueForSomeIterations(currentValue, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
            double precision = (double) OverallStrategy.Decision.PrecisionForFastConvergence * (OverallStrategy.Decision.StrategyBounds.UpperBound - OverallStrategy.Decision.StrategyBounds.LowerBound);

            int maxProgressivelySmallerImprovements = 4; // do we want just a crude improvement or do we want to improve things up to the limits of our precision
            int numImprovements = 0;
            double lastCurrentValue;
            do
            {
                lastCurrentValue = currentValue;
                FindShiftDistanceAsLargeAsPossibleAndThenKeepShiftingInThatDirection(specificIterationsFromRunSetToUse, smoothingIterations, smoothingSetPointIndex, ref currentValue, ref shiftDistance, ref currentScore, precision);
                if (currentValue != lastCurrentValue)
                {
                    numImprovements++;
                    shiftDistance /= 2.0;
                }
            }
            while (maxProgressivelySmallerImprovements > numImprovements && lastCurrentValue != currentValue && shiftDistance >= OverallStrategy.FastConvergenceCurrentShiftDistance * multiplierToDetermineShiftDistanceFloor);
            return currentValue;
        }

        private void FindShiftDistanceAsLargeAsPossibleAndThenKeepShiftingInThatDirection(List<IterationID> specificIterationsFromRunSetToUse, long smoothingIterations, int smoothingSetPointIndex, ref double currentValue, ref double shiftDistance, ref double currentScore, double precision)
        {
            bool shiftDistanceReducedToFindBetterScore = false;
            double scoreForHigherValue = 0, scoreForLowerValue = 0;
            ReduceShiftDistanceUntilBetterScoreFound(specificIterationsFromRunSetToUse, smoothingIterations, ref currentValue, ref shiftDistance, ref currentScore, precision, ref shiftDistanceReducedToFindBetterScore, ref scoreForHigherValue, ref scoreForLowerValue);
            if (shiftDistance < precision)
            { // we couldn't find a better score. keep the current score.
                SmoothingSetPointInfos[smoothingSetPointIndex].fastConvergenceShiftValue = precision;
                return;
            }
            if (shiftDistanceReducedToFindBetterScore)
            { // we had to lower the shift distance to find a better score, so moving further out the same amount will not be successful.
                SmoothingSetPointInfos[smoothingSetPointIndex].fastConvergenceShiftValue = shiftDistance;
                return;
            }
            else
            {
                KeepShiftingSameDistanceAsFarAsPossible(specificIterationsFromRunSetToUse, smoothingIterations, ref currentValue, ref shiftDistance, ref currentScore, ref scoreForHigherValue, ref scoreForLowerValue);
                SmoothingSetPointInfos[smoothingSetPointIndex].fastConvergenceShiftValue = shiftDistance; // this will be larger than before if we shifted multiple times
                return;
            }
        }

        private void KeepShiftingSameDistanceAsFarAsPossible(List<IterationID> specificIterationsFromRunSetToUse, long smoothingIterations, ref double currentValue, ref double shiftDistance, ref double currentScore, ref double scoreForHigherValue, ref double scoreForLowerValue)
        {
            double originalShiftDistance = shiftDistance;
            double deltaShiftDistance = originalShiftDistance;
            bool choosingHigherValue = (Decision.HighestIsBest && scoreForHigherValue > scoreForLowerValue) || (!Decision.HighestIsBest && scoreForHigherValue < scoreForLowerValue);
            if (choosingHigherValue)
            {
                bool anotherImprovementFound = true;
                while (anotherImprovementFound) // if we reduced the shift distance to get here, we know we won't find another improvement by increasing the shift distance
                {
                    currentScore = scoreForHigherValue;
                    scoreForHigherValue = OverallStrategy.PlaySpecificValueForSomeIterations(currentValue + deltaShiftDistance, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                    anotherImprovementFound = (Decision.HighestIsBest && scoreForHigherValue > currentScore) || (!Decision.HighestIsBest && scoreForHigherValue < currentScore);
                    if (anotherImprovementFound)
                    {
                        currentValue += deltaShiftDistance;
                        shiftDistance += deltaShiftDistance;
                        deltaShiftDistance *= 1.5;
                    }
                }
            }
            else
            {
                bool anotherImprovementFound = true;
                while (anotherImprovementFound)
                {
                    currentScore = scoreForLowerValue;
                    scoreForLowerValue = OverallStrategy.PlaySpecificValueForSomeIterations(currentValue - deltaShiftDistance, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                    anotherImprovementFound = (Decision.HighestIsBest && scoreForLowerValue > currentScore) || (!Decision.HighestIsBest && scoreForLowerValue < currentScore);
                    if (anotherImprovementFound)
                    {
                        currentValue -= deltaShiftDistance;
                        shiftDistance += deltaShiftDistance;
                        deltaShiftDistance *= 1.5;
                    }
                }
            }
        }

        private void ReduceShiftDistanceUntilBetterScoreFound(List<IterationID> specificIterationsFromRunSetToUse, long smoothingIterations, ref double currentValue, ref double shiftDistance, ref double currentScore, double precision, ref bool shiftDistanceHasReduced, ref double scoreForHigherValue, ref double scoreForLowerValue)
        {
            bool successfulShiftDistanceFound = false;
            while (!successfulShiftDistanceFound && shiftDistance >= precision)
            {
                scoreForHigherValue = OverallStrategy.PlaySpecificValueForSomeIterations(currentValue + shiftDistance, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                scoreForLowerValue = OverallStrategy.PlaySpecificValueForSomeIterations(currentValue - shiftDistance, specificIterationsFromRunSetToUse, smoothingIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                bool currentScoreIsBetter = ((Decision.HighestIsBest && currentScore > scoreForHigherValue && currentScore > scoreForLowerValue) || (!Decision.HighestIsBest && currentScore < scoreForHigherValue && currentScore < scoreForLowerValue));
                if (!currentScoreIsBetter)
                {
                    if ((Decision.HighestIsBest && scoreForHigherValue >= currentScore) || (!Decision.HighestIsBest && scoreForHigherValue <= currentScore))
                    {
                        currentValue = currentValue + shiftDistance;
                        currentScore = scoreForHigherValue;
                    }
                    else
                    {
                        currentValue = currentValue - shiftDistance;
                        currentScore = scoreForLowerValue;
                    }
                }
                successfulShiftDistanceFound = !currentScoreIsBetter;
                if (!successfulShiftDistanceFound)
                {
                    shiftDistance /= 2.0;
                    shiftDistanceHasReduced = true;
                }
            }
        }

        public void ImproveOptimizationOfCloseCasesForBipolarDecisions()
        {
            if (!OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecision)
                return;
            double proportionToScrutinizeMore = OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize;
            List<int> smoothingSetPointInfoIndicesToFocusOn = SmoothingSetPointInfos.Where(x => x.eligible && x.pointsInRunningSetClosestToThisPoint != null).Select((x, i) => new { I = i, X = x }).OrderBy(x => x.X.absoluteDifferenceBetweenBipolarStrategies).Take((int)(SmoothingSetPointInfos.Count * proportionToScrutinizeMore)).Select(x => x.I).ToList();
            List<double> farthestDistanceToConsider = smoothingSetPointInfoIndicesToFocusOn.Select(i => SmoothingSetPointInfos[i].pointsInRunningSetClosestToThisPoint.LowestPriorityWhetherOrNotFull).ToList();
            List<List<double>> focusedOnUnnormalizedPoints = smoothingSetPointInfoIndicesToFocusOn.Select(x => SmoothingSetPointInfos[x].decisionInputs).ToList();
            List<Point> focusedOnNormalizedPoints = smoothingSetPointInfoIndicesToFocusOn.Select(x => (Point) new NormalizedPoint(SmoothingSetPointInfos[x].decisionInputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1)).ToList();
            long totalIterations = SmoothingGamePlayIterations();
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights here
            long iterationsMax = maxIterationNumber + 1 + totalIterations * OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecisionMultiplier;
            var oldMode = System.Runtime.GCSettings.LatencyMode;
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, maxIterationNumber + 1, iterationsMax, iterationNumber2 =>
            {
                bool decisionReached;
                IterationID iterationID = OverallStrategy.GenerateIterationID(iterationNumber2);
                GameProgress preplayedGameProgressInfo;
                List<double> decisionInputs = OverallStrategy.GetDecisionInputsForIteration(iterationID, SmoothingGamePlayIterations() /* note that we're going over this number but that's ok */, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    NormalizedPoint point = new NormalizedPoint(decisionInputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
                    double distanceSquared;
                    int? bestMatch = null;
                    double? bestDistanceSquared = null;
                    for (int i = 0; i < smoothingSetPointInfoIndicesToFocusOn.Count; i++)
                    {
                        distanceSquared = point.DistanceTo(focusedOnNormalizedPoints[i], true, farthestDistanceToConsider[i]);
                        if (distanceSquared < farthestDistanceToConsider[i])
                        {
                            if (bestMatch == null || distanceSquared > bestDistanceSquared)
                            {
                                bestMatch = i;
                                bestDistanceSquared = distanceSquared;
                            }
                        }
                    }
                    if (bestMatch != null)
                    {
                        PriorityQueue<double, IterationID> priorityQueue = SmoothingSetPointInfos[smoothingSetPointInfoIndicesToFocusOn[(int)bestMatch]].pointsInRunningSetClosestToThisPoint;
                        bool full;
                        priorityQueue.Enqueue((double)bestDistanceSquared, iterationID, out full);
                    }
                 }
            });
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, smoothingSetPointInfoIndicesToFocusOn.Count, i =>
            {
                if (SmoothingSetPointInfos[smoothingSetPointInfoIndicesToFocusOn[i]].eligible)
                    DetermineUnsmoothedOptimalValueForParticularPointInSmoothingSet(smoothingSetPointInfoIndicesToFocusOn[i]);
            });
            System.Runtime.GCSettings.LatencyMode = oldMode;
        }

        public void TestNearParticularPoint(List<double> inputs, List<double> possibleOutputs)
        {
            if (inputs.Count != InputAveragesInSmoothingSet.Count)
            {
                Debug.WriteLine("TestNearParticularPoint aborted because there were " + inputs.Count + " inputs when " + InputAveragesInSmoothingSet.Count + " were needed.");
                return;
            }
            NormalizedPoint normalizedPoint = new NormalizedPoint(inputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            Point nearestNeighbor = KDTreeForInputs.GetNearestNeighbor(normalizedPoint, false);
            if (nearestNeighbor != null)
            {
                
                int i = ((NormalizedPoint)nearestNeighbor).AssociatedIndex;
                string nearestPointString = String.Join(",", SmoothingSetPointInfos[i].decisionInputs.ToArray());
                Debug.WriteLine("Nearest point: " + nearestPointString);
                if (SmoothingSetPointInfos[i].pointsInRunningSetClosestToThisPoint == null)
                {
                    Debug.WriteLine("TestNearParticularPoint aborted because the nearest point was from a previous version of the strategy, and thus specific iterations were unavailable.");
                    return;
                }
                List<IterationID> specificIterationsFromRunSetToUse = SmoothingSetPointInfos[i].pointsInRunningSetClosestToThisPoint.GetValues();

                long smoothingIterations = SmoothingGamePlayIterations();

                int? limitNumberIterations = null;
                if (limitNumberIterations != null)
                    specificIterationsFromRunSetToUse = specificIterationsFromRunSetToUse.Take((int)limitNumberIterations).ToList();
                bool enablePartialLogging = false;

                foreach (var output in possibleOutputs)
                {
                    bool originalDoParallel = OverallStrategy.Player.DoParallel;
                    OverallStrategy.Player.DoParallel = false;
                    GameProgressLogger.PartialLoggingOn = enablePartialLogging;
                    double average = OverallStrategy.PlaySpecificValueForSomeIterations(output, specificIterationsFromRunSetToUse, smoothingIterations, new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false });
                    Debug.WriteLine("Score/result for " + output + " = " + average);
                    GameProgressLogger.PartialLoggingOn = false;
                    OverallStrategy.Player.DoParallel = originalDoParallel;
                }
            }
        }


        private void DetermineOptimalCutoff()
        {
            Stopwatch s = new Stopwatch();
            s.Start();

            double? originalOptimalValueForZeroDimensions = OptimalValueForZeroDimensions;

            double upperBound = OverallStrategy.Decision.StrategyBounds.UpperBound;
            double lowerBound = OverallStrategy.Decision.StrategyBounds.LowerBound;
            long numIterations = SmoothingGamePlayIterations();
            if (numIterations > int.MaxValue)
                throw new Exception();
            bool improveOptimizationOfCloseCases = OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecision;
            int improveOptimizationOfCloseCasesForBipolarDecisionMultiplier = OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecisionMultiplier;
            double improveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize = OverallStrategy.Decision.ImproveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize;
            bool highestIsBest = OverallStrategy.Decision.HighestIsBest;
            bool positiveOneIsPlayedToLeftOfItem = OverallStrategy.Decision.CutoffPositiveOneIsPlayedToLeft;

            GamePlayer play = OverallStrategy.Player;
            List<Strategy> strategiesToPlayWith = play.bestStrategies;
            bool doParallel = play.DoParallel && !strategiesToPlayWith[OverallStrategy.DecisionNumber].UseThreadLocalScores; // if we are using thread local scores, then we do not want to further subdivide into more threads, because then those threads' scores will not aggregate properly
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;

            bool useAzureWorkerRole = !AzureSetup.runCompleteSettingsInAzure && ChunkIterationsForRemoting() && UseWorkerRolesForRemoting();
            bool remotingCanSeparateFindingAndSmoothing = RemotingShouldSeparateFindingAndOptimizing();
            int chunkSizeForRemoting = useAzureWorkerRole ? ChunkSizeForRemoting() : 0;
            OptimizePointsAndSmoothRemoteCutoffExecutor remoteCutoffExecutor = new OptimizePointsAndSmoothRemoteCutoffExecutor(this);
            if (useAzureWorkerRole)
                remoteCutoffExecutor.SerializeStrategyContextToAzure();
            OptimalValueForZeroDimensions = StochasticCutoffFinder.FindCutoff(OverallStrategy.Player.DoParallel, lowerBound, upperBound, numIterations, numIterations, improveOptimizationOfCloseCases, improveOptimizationOfCloseCasesForBipolarDecisionMultiplier, improveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize, highestIsBest, positiveOneIsPlayedToLeftOfItem, null, remoteCutoffExecutor, useAzureWorkerRole, chunkSizeForRemoting, OptimalValueForZeroDimensions);

            strategiesToPlayWith[OverallStrategy.DecisionNumber].ResetScores();
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
            InitialDevelopmentCompleted = true;

            bool enableAveraging = false; // currently disabled because when we have an extreme value resulting, it doesn't make sense to average it
            bool averageInPreviousVersions = !InValidationMode && EvolutionSettings.AverageInPreviousVersionsOfStrategy && OverallStrategy.CyclesStrategyDevelopment >= EvolutionSettings.StartAveragingInPreviousVersionsOfStrategyOnStepN && enableAveraging;
            if (averageInPreviousVersions)
            {
                TotalAveragings++;
                double weightOnNewOptimization = 1.0 / (TotalAveragings + 1);
                double weightOnOldOptimization = 1.0 - weightOnNewOptimization;
                OptimalValueForZeroDimensions = weightOnNewOptimization * OptimalValueForZeroDimensions + weightOnOldOptimization * originalOptimalValueForZeroDimensions;
            }

            TabbedText.WriteLine("Optimal value for cutoff decision: " + OptimalValueForZeroDimensions);
            s.Stop();
            TabbedText.WriteLine("Elapsed seconds: " + s.ElapsedMilliseconds / 1000.0 + (EvolutionSettings.ParallelOptimization ? "" : " (with parallel execution disabled) "));
        }


        public StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfInputs, long iteration)
        {
            StochasticCutoffFinderOutputs scfOutputs = new StochasticCutoffFinderOutputs();
            scfOutputs.Weight = 0; // assume we're not reaching the decision

            Func<GameProgress, bool> shouldFinishGame = x =>
            {
                if (scfInputs.TentativeCutoff == null)
                    return x.CutoffVariable != null;
                return x.CutoffVariable != null && Math.Abs((double)x.CutoffVariable - (double)scfInputs.TentativeCutoff) < (double)scfInputs.MaxRangeFromCutoff;
            };

            Action<GameProgress, List<double>, double> processScores = (gameProgress, scoreList, oversamplingWeight) =>
            {
                double positiveOneScoreMinusNegativeOneScore = scoreList[0] - scoreList[1];
                scfOutputs.InputVariable = (double) gameProgress.CutoffVariable;
                scfOutputs.Score = positiveOneScoreMinusNegativeOneScore;
                scfOutputs.Weight = oversamplingWeight;
            };

            long maxIterations = SmoothingGamePlayIterations();

            OverallStrategy.Player.ProcessScoresForSpecifiedValues_OneIteration(SimulationInteraction, new List<double> { 1.0, -1.0 }, shouldFinishGame, processScores, OverallStrategy.DecisionNumber, OverallStrategy, maxIterations, OverallStrategy.DecisionNumber, OverallStrategy.Player.bestStrategies, (int) iteration);

            if (scfOutputs.Weight == 0)
                return null; // didn't reach decision

            return scfOutputs;
        }

        private void DetermineOptimalValueForZeroDimensions()
        {
            if (Decision.Cutoff)
            {
                DetermineOptimalCutoff();
                return;
            }
            // there are no inputs, so we just do a simple optimization over a lot of iterations
            const int maxNumIterationsToOptimizeOver = 10000;
            long totalIterations = SmoothingGamePlayIterations();
            int iterationsToUse = (int)Math.Min((long)maxNumIterationsToOptimizeOver, totalIterations);
            List<IterationID> allIterations = Enumerable.Range(0, iterationsToUse).Select(x => OverallStrategy.GenerateIterationID((long)x)).ToList();
            List<double> decisionInputsToIgnore;
            bool originalUseThreadLocalScores = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            if (Decision.ScoreRepresentsCorrectAnswer)
            {
                double result = OverallStrategy.PlaySpecificValueForSomeIterations(0 /* IRRELEVANT */, allIterations, totalIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                OptimalValueForZeroDimensions = result;
            }
            else if (Decision.Bipolar)
            {
                double negativeOneResult = OverallStrategy.PlaySpecificValueForSomeIterations(-1.0, allIterations, totalIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                double positiveOneResult = OverallStrategy.PlaySpecificValueForSomeIterations(1.0, allIterations, totalIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                //double negativeOneResult = OverallStrategy.PlaySpecificValueForLargeNumberOfIterations(-1.0, iterationsToUse, totalIterations);
                //double positiveOneResult = OverallStrategy.PlaySpecificValueForLargeNumberOfIterations(1.0, iterationsToUse, totalIterations);
                if (Decision.HighestIsBest == negativeOneResult > positiveOneResult)
                    OptimalValueForZeroDimensions = negativeOneResult;
                else
                    OptimalValueForZeroDimensions = positiveOneResult;
            }
            else
            {
                bool printOutSampleValues = false; // use this to figure out why the optimum is what it is
                if (printOutSampleValues)
                {
                    double distance = Decision.StrategyBounds.UpperBound - Decision.StrategyBounds.LowerBound;
                    for (double val = Decision.StrategyBounds.LowerBound; val < Decision.StrategyBounds.UpperBound; val += distance / 50.0 /* DEBUG */)
                    {
                        double result = OverallStrategy.PlaySpecificValueForSomeIterations(val, allIterations, totalIterations, GetOversamplingInfoWhereWeightsAreNeeded());
                        Debug.WriteLine(val + " --> " + result);
                    }
                }
                OptimalValueForZeroDimensions =
                        FindOptimalPoint.OptimizeByNarrowingRanges(
                            Decision.StrategyBounds.LowerBound, 
                            Decision.StrategyBounds.UpperBound, 
                            ((SmoothingOptionsWithPresmoothing)(EvolutionSettings.SmoothingOptions)).PreliminaryOptimizationPrecision,
                            valueToTest =>
                                //OverallStrategy.PlaySpecificValueForLargeNumberOfIterations(valueToTest, iterationsToUse, totalIterations),
                                OverallStrategy.PlaySpecificValueForSomeIterations(valueToTest, allIterations, totalIterations, GetOversamplingInfoWhereWeightsAreNeeded()),
                            Decision.HighestIsBest, numberRangesToTestFirstCall: 5, numberRangesToTestGenerally: 3, targetValue: Decision.ZeroDimensionalTargetValue);
            }
            OverallStrategy.UseThreadLocalScores = originalUseThreadLocalScores;
            InitialDevelopmentCompleted = true;
            TabbedText.WriteLine("Optimal value for zero-dimensional decision " + OptimalValueForZeroDimensions.ToString());
        }

        internal void SaveOriginalValues()
        {
            savedOriginalValues = new double[SmoothingSetPointInfos.Count];
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, SmoothingSetPointInfos.Count, v =>
            {
                savedOriginalValues[v] = SmoothingSetPointInfos[v].preSmoothingValue;
            });
        }

        internal void CopyPostSmoothedValuesToPreSmoothed()
        { 
            // why might we do this? 
            // In RegressionBasedSmoothing, we have multiple rounds of smoothing, but we don't for GRNNSmoothing and RPROPSmoothing
            // note that grnnsmoothing uses the presmoothed values as the inputs to the grnn, and then the postsmoothing values represent what the smoothed value would
            // be at that point but aren't themselves inputs into anything else. So we shouldn't call this from there.
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, SmoothingSetPointInfos.Count, v =>
            {
                var pointInfo = SmoothingSetPointInfos[v];
                pointInfo.preSmoothingValue = pointInfo.postSmoothingValue;
            });
        }

        internal void CopyPreSmoothedValuesToPostSmoothed()
        {
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, SmoothingSetPointInfos.Count, v =>
            {
                var pointInfo = SmoothingSetPointInfos[v];
                pointInfo.postSmoothingValue = pointInfo.preSmoothingValue;
            });
        }

        internal double ScoreStrategyBasedOnValidationSet()
        {
            SwitchBackFromValidationSet(); // we want to assume that there is a regular set, plus a separate validation set.
            double sum = 0;
            double count = 0;
            //Debug.WriteLine(String.Join("\n",smoothingSetPointInfosMainSet.Select(x => String.Join(",", x.decisionInputs))));
            int smoothingPointsValidationSet = SmoothingPointsValidationSet();
            for (int i = 0; i < smoothingPointsValidationSet; i++)
            {
                if (SmoothingSetPointInfosValidationSet[i].eligible)
                {
                    List<double> validationSetInputs = SmoothingSetPointInfosValidationSet[i].decisionInputs;
                    double mainSetValue = CalculateOutputForInputs(validationSetInputs);
                    double error = Math.Abs(mainSetValue - SmoothingSetPointInfosValidationSet[i].preSmoothingValue);
                    //Debug.WriteLine(String.Join(",", validationSetInputs) + " ==> " + mainSetValue + " validation: " + smoothingSetPointInfosValidation[i].preSmoothingValue + " error: " + error);
                    sum += error;
                    count += 1.0;
                }
            }
            double avg = sum / count;
            return avg;
        }

        public double CalculateOutputForInputs(List<double> inputs)
        {
            double result;
            if (Dimensions == 0 && OptimalValueForZeroDimensions != null)
            {
                result = (double)OptimalValueForZeroDimensions;
            }
            else
                result = CalculateOutputForInputsNotZeroDimensions(inputs);
            if (Decision.Bipolar)
                result = (result < 0) ? -1.0 : 1.0;
            return result;
        }

        internal virtual double CalculateOutputForInputsNotZeroDimensions(List<double> inputs)
        {
            return InterpolateOutputForPointUsingNearestNeighborOnly(inputs);
        }

        internal double InterpolateOutputForPointUsingNearestNeighborOnly(List<double> inputs)
        {
            // This very simple algorithm should be used only before we have started the smoothing process.
            NormalizedPoint normalizedPoint = new NormalizedPoint(inputs, InputAveragesInSmoothingSet, InputStdevsInSmoothingSet, -1);
            List<Point> neighbors = KDTreeForInputs.GetKNearestNeighbors(normalizedPoint, false, 1);
            SmoothingSetPointInfo neighbor = SmoothingSetPointInfos[((NormalizedPoint)neighbors[0]).AssociatedIndex];
            return neighbor.preSmoothingValue;
        }


        internal void Create2dPlot(Func<int, double> valueToPlotFuncMainSet, Func<int, double> valueToPlotFuncValidationSet, string title, string seriesName)
        {
            List<double[]> points = new List<double[]>();
            int maxi = 1;
            const bool disablePlottingOfValidationSet = true;
            if (!EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet || disablePlottingOfValidationSet)
                maxi = 0;
            for (int i = 0; i <= maxi; i++)
            {
                if (i == 0)
                    SwitchBackFromValidationSet();
                else
                    SwitchToValidationSet();
                int smoothingPoints = SmoothingPoints();
                for (int p = 0; p < smoothingPoints; p++)
                {
                    if (SmoothingSetPointInfos[p].eligible)
                    {
                        double[] point = new double[2];
                        List<double> decisionInputs = SmoothingSetPointInfos[p].decisionInputs;
                        point[0] = decisionInputs[0];
                        point[1] = i == 0 ? valueToPlotFuncMainSet(p) : valueToPlotFuncValidationSet(p);
                        points.Add(point);
                    }
                }
            }
            SwitchBackFromValidationSet();

            double? yAxisMin = null, yAxisMax = null;
            if (!OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
            {
                yAxisMin = OverallStrategy.Decision.YAxisMinOverrideForPlot ?? OverallStrategy.Decision.StrategyBounds.LowerBound;
                yAxisMax = OverallStrategy.Decision.YAxisMaxOverrideForPlot ?? OverallStrategy.Decision.StrategyBounds.UpperBound;
            }

            SimulationInteraction.Create2DPlot(points.OrderBy(x => x[0]).ToList(), new Graph2DSettings() { graphName = title, seriesName = seriesName, xMin = OverallStrategy.Decision.XAxisMinOverrideForPlot, xMax = OverallStrategy.Decision.XAxisMaxOverrideForPlot, yMin = yAxisMin, yMax = yAxisMax, xAxisLabel = OverallStrategy.Decision.XAxisLabelForPlot ?? "", yAxisLabel = OverallStrategy.Decision.YAxisLabelForPlot ?? "", fadeSeriesOfSameName = true }, OverallStrategy.ActionGroup.RepetitionTagStringLongForm()); 
        }

        internal void Create3dPlot(Func<int, double> valueToPlotFuncMainSet, Func<int, double> valueToPlotFuncValidationSet, string title)
        {
            List<double[]> points = new List<double[]>();
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>();
            int maxi = 1;
            const bool disablePlottingOfValidationSet = true;
            if (!EvolutionSettings.SmoothingPointsValidationSet.CreateValidationSet || disablePlottingOfValidationSet)
                maxi = 0;
            for (int i = 0; i <= maxi; i++)
            {
                if (i == 0)
                    SwitchBackFromValidationSet();
                else
                    SwitchToValidationSet();
                int smoothingPoints = SmoothingPoints();
                for (int p = 0; p < smoothingPoints; p++)
                {
                    if (SmoothingSetPointInfos[p].eligible)
                    {
                        double[] point = new double[3];
                        List<double> decisionInputs = SmoothingSetPointInfos[p].decisionInputs;
                        point[0] = decisionInputs[0];
                        point[1] = decisionInputs[1];
                        point[2] = i == 0 ? valueToPlotFuncMainSet(p) : valueToPlotFuncValidationSet(p);
                        points.Add(point);
                        if (i == 0)
                            colors.Add(System.Windows.Media.Colors.Beige);
                        else
                            colors.Add(System.Windows.Media.Colors.Cyan);
                    }
                }
            }
            SwitchBackFromValidationSet();

                // The following code can add some specific points we know to be correct for the obfuscation game
            //points.Add(new double[] { 0.02, 0.0, 0.02 });
            //points.Add(new double[] { 0.02, 0.2, 0.1679 });
            //points.Add(new double[] { 0.02, 0.5, 0.3696 });
            //points.Add(new double[] { 0.2, 0.0, 0.2 });
            //points.Add(new double[] { 0.2, 0.2, 0.2506 });
            //points.Add(new double[] { 0.2, 0.5, 0.4111 });
            //points.Add(new double[] { 0.5, 0.0, 0.5 });
            //points.Add(new double[] { 0.5, 0.2, 0.5011 });
            //points.Add(new double[] { 0.5, 0.5, 0.4908 });
            //points.Add(new double[] { 0.8, 0.0, 0.8 });
            //points.Add(new double[] { 0.8, 0.2, 0.7413 });
            //points.Add(new double[] { 0.8, 0.5, 0.5818 });
            //for (int i = 0; i < 4; i++)
            //{
            //    colors.Add(System.Windows.Media.Colors.Cyan);
            //    colors.Add(System.Windows.Media.Colors.DarkGreen);
            //    colors.Add(System.Windows.Media.Colors.Crimson);
            //}

            SimulationInteraction.Create3DPlot(points, colors, title);
        }

        public virtual void PreSerialize()
        {
            if (KDTreeForInputs != null)
                KDTreeForInputs.PreSerialize();
        }

        public virtual void UndoPreSerialize()
        {
            if (KDTreeForInputs != null)
                KDTreeForInputs.UndoPreSerialize();
        }

        [NonSerialized]
        private Strategy OverallStrategyTemp;
        [NonSerialized]
        private OptimizePointsAndSmooth OriginalStateTemp;
        public void PreSerializeTemporarilyLimitingSize()
        {
            // overall strategy will be serialized separately as part of the strategy context
            if (OverallStrategy != null) // thus it will work even if called twice
                OverallStrategyTemp = OverallStrategy;
            if (OriginalStateToBeAveragedIn != null)
                OriginalStateTemp = OriginalStateToBeAveragedIn;
            OverallStrategy = null;
            PreSerialize();
        }

        public void UndoPreSerializeTemporarilyLimitingSize()
        {
            if (OverallStrategy == null)
                OverallStrategy = OverallStrategyTemp; // note: this will recover the object for the coordinator but not for the worker; for that, we use the separately serialized strategy 
            if (OriginalStateToBeAveragedIn == null)
                OriginalStateToBeAveragedIn = OriginalStateTemp;
            UndoPreSerialize();
        }


    }
}

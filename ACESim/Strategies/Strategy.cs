using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Linq.Expressions;
using ACESim.Util;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Runtime;

namespace ACESim
{
    [Serializable]
    public class Strategy : ISerializationPrep
    {
        #region Class fields and properties
        public EvolutionSettings EvolutionSettings;

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal SimulationInteraction _simulationInteraction;
        public SimulationInteraction SimulationInteraction { get { return _simulationInteraction; } set { _simulationInteraction = value; } }

        public Decision Decision;

        public ActionGroup ActionGroup;

        public int DecisionNumber;

        public int? DynamicDimensions;
        public double[] MinObservedInputs;
        public double[] MaxObservedInputs;
        public bool[] DimensionFilteredOut; // if all values are constant across a dimension, then the strategy will filter that dimension out for the strategy component.
        public int? NumFilteredDimensions;
        public int Dimensions
        {
            get
            {
                if (Decision.DynamicNumberOfInputs)
                    return (int) DynamicDimensions - (int) NumFilteredDimensions;
                return Decision.InputNames.Count;
            }
        }
        public int GetFilteredInputIndex(int unfilteredInputIndex)
        {
            if (DimensionFilteredOut == null || DimensionFilteredOut[unfilteredInputIndex])
                return -1;
            return unfilteredInputIndex - DimensionFilteredOut.Select((item, index) => new { Item = item, Index = index }).Count(x => x.Index < unfilteredInputIndex && x.Item == true);
        }

        public StatCollectorArray FilteredInputsStatistics;

        private List<InputGroup> _InputGroups = null;
        public List<InputGroup> InputGroups
        {
            get
            {
                return _InputGroups ?? Decision.InputGroups;
            }
            set
            {
                _InputGroups = value;
            }
        }

        internal StatCollectorArray _Scores;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public bool UseThreadLocalScores = false; // when simultaneously adding scores from different threads, if we want to keep track of the different sets of scores separately, set this to true.

        public CumulativeDistribution[] CumulativeDistributions; // all cumulative distributions that the game is keeping track of as of the time of this decision

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal ThreadLocal<StatCollectorArray> ThreadLocalScores = new ThreadLocal<StatCollectorArray>(() => null);

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal List<Strategy> _allStrategies;
        public List<Strategy> AllStrategies { get { return _allStrategies; } set { _allStrategies = value; } }

        public Strategy _previousVersionOfThisStrategy, _versionOfStrategyBeforeThat;
        public void ClearPriorVersionHistory()
        {
            _previousVersionOfThisStrategy = null;
            _versionOfStrategyBeforeThat = null;
        }

        public double? GeneralOverrideValue = null; // if non-null, a constant value will always be returned from calculate. Takes precedence over threadlocal override value.

        public bool UseBuiltInStrategy = false; // This is set to true for dummy decisions requiring no optimization, and signals the game module to use a built in strategy for the specific decision instead of calling the strategy.

        [InternallyDefinedSetting]
        public bool UseFastConvergence = false;
        [InternallyDefinedSetting]
        public double? FastConvergenceCurrentShiftDistance = null;
        [InternallyDefinedSetting]
        public bool AbortFastConvergenceIfPreciseEnough = true;
        [InternallyDefinedSetting]
        public List<double> ConvergencePrecision;
        [InternallyDefinedSetting]
        public List<double[]> ConvergenceSampleDecisionInputs;

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public ThreadLocal<double?> _ThreadLocalOverrideValue;

        public bool PresmoothingValuesComputedEarlier { get { return Decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision && Decision.OversamplingWillAlwaysBeSameAsPreviousDecision && Decision.ScoreRepresentsCorrectAnswer && !Decision.DisablePrescoringForThisDecision; } }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public GamePlayer Player;

        public double ResultIncrement = 0;
        public double ResultMultiplier = 1.0; // result will always be multiplied by this; this is set to 1 by DevelopStrategy but can be changed elsewhere

        List<InputGroupPlus> InputGroupTree;
        public InputGroupPlus ActiveInputGroupPlus; // when developing the strategy, we traverse the InputGroupTree, and the active input group determines which inputs are passed to the strategy component.
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        ThreadLocal<double?> _overrideValueForActiveInputGroupPlus;
        

        public OversamplingPlan OversamplingPlanDuringOptimization;
        public List<IterationID> IterationsWhereDecisionIsReached;
        public List<IterationID> IterationsWhereDecisionIsNotReached;
        public bool DecisionReachedEnoughTimes;
        public bool SuccessReplicationTriggered;
        public long NextIterationDuringSuccessReplication;
        public int NumGameInputSeedIndices;
        public bool[] KeepSourceGameInputSeedIndexDuringSuccessReplication;

        public bool CurrentlyDevelopingStrategy = false;
        public bool StrategyDevelopmentInitiated = false;
        public int CyclesStrategyDevelopment = 0;
        public int CyclesStrategyDevelopmentThisEvolveStep = 0;
        bool UsingDefaultInputGroup = false;

        public bool StrategyStillToEvolveThisEvolveStep = false;
        object CacheLock = new object();
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore] // note: as a result of this, we cannot simply replay back the development of a strategy that relies on cached scores. also, if we were to try to resume evolution at a particular point, we would need to find a point where there would not have been cached scores.
        private ConcurrentDictionary<IterationID, CachedInputsAndScores> _CacheFromPreviousOptimization = null;
        [SkipInWalkToIdentifyMemoryPathAttribute]
        [System.Xml.Serialization.XmlIgnore]
        public ConcurrentDictionary<IterationID, CachedInputsAndScores> CacheFromPreviousOptimization
        {
            get
            {
                if (_CacheFromPreviousOptimization == null)
                    lock (CacheLock)
                        if (_CacheFromPreviousOptimization == null)
                            _CacheFromPreviousOptimization = new ConcurrentDictionary<IterationID, CachedInputsAndScores>();
                return _CacheFromPreviousOptimization;
            }
            set
            {
                _CacheFromPreviousOptimization = value;
            }
        }
        public bool CacheFromPreviousOptimizationContainsValues { get { return _CacheFromPreviousOptimization != null && _CacheFromPreviousOptimization.Any(); } }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public bool StrategyDeserializedFromDisk = false;

        #endregion

        #region Initialization, duplication, and previous versions

        public Strategy()
        {
        }

        public Strategy DeepCopy()
        {
            var inputGroupTreeCopy = ListInputGroupPlusCopier.DeepCopy(InputGroupTree, null);
            var activeInputGroupPlus = ListInputGroupPlusCopier.FindCorrespondingInputGroupInCopy(InputGroupTree, inputGroupTreeCopy, ActiveInputGroupPlus);
            Strategy theStrategy = new Strategy()
            {
                EvolutionSettings = EvolutionSettings,
                SimulationInteraction = SimulationInteraction,
                Decision = Decision,
                DecisionNumber = DecisionNumber,
                CumulativeDistributions = CumulativeDistributions == null ? null : CumulativeDistributions.Select(x => x == null ? null : x.DeepCopy()).ToArray(),
                Scores = null, /* do not copy scores or ThreadLocalScores as part of deep copy -- we want the copy to have its own scores*/
                UseThreadLocalScores = UseThreadLocalScores,
                ThreadLocalScores = null,
                AllStrategies = AllStrategies.ToList(),
                ThreadLocalOverrideValue = new ThreadLocal<double?>(() => null), // do not copy thread local values when copying strategy
                Player = null, /* do not copy -- we want the copy to create its own GamePlayer */
                InputGroupTree = inputGroupTreeCopy,
                ActiveInputGroupPlus = activeInputGroupPlus, 
                OverrideValueForActiveInputGroupPlus = OverrideValueForActiveInputGroupPlus,
                StrategyDevelopmentInitiated = StrategyDevelopmentInitiated,
                UsingDefaultInputGroup = UsingDefaultInputGroup,
                CyclesStrategyDevelopment = CyclesStrategyDevelopment,
                CyclesStrategyDevelopmentThisEvolveStep = CyclesStrategyDevelopmentThisEvolveStep,
                DynamicDimensions = DynamicDimensions,
                NumFilteredDimensions = NumFilteredDimensions,
                DimensionFilteredOut = DimensionFilteredOut == null ? null : DimensionFilteredOut.ToArray(),
                FilteredInputsStatistics = FilteredInputsStatistics == null ? null : FilteredInputsStatistics.DeepCopy(),
                OversamplingPlanDuringOptimization = OversamplingPlanDuringOptimization == null ? null : OversamplingPlanDuringOptimization.DeepCopy(null),
                IterationsWhereDecisionIsReached = IterationsWhereDecisionIsReached == null ? null : IterationsWhereDecisionIsReached.Select(x => x.DeepCopy()).ToList(),
                IterationsWhereDecisionIsNotReached = IterationsWhereDecisionIsNotReached == null ? null : IterationsWhereDecisionIsNotReached.Select(x => x.DeepCopy()).ToList(),
                DecisionReachedEnoughTimes = DecisionReachedEnoughTimes,
                SuccessReplicationTriggered = SuccessReplicationTriggered,
                ResultIncrement = ResultIncrement,
                ResultMultiplier = ResultMultiplier,
                IStrategyComponentsInDevelopmentOrder = IStrategyComponentsInDevelopmentOrder == null ? null : GetIStrategyComponentsInDevelopmentOrder(), // can't just DeepCopy b/c this must match the IStrategyComponents in the input tree
            };
            ListInputGroupPlusCopier.SetOverallStrategyForIStrategyComponents(theStrategy.InputGroupTree, theStrategy);
            return theStrategy;
        }

        public Strategy PreviousVersionOfThisStrategy
        {
            get
            {
                //if (!Decision.PreservePreviousVersionWhenOptimizing && !Decision.AverageInPreviousVersion)
                //    throw new Exception("PreservePreviousVersionWhenOptimizing must be true to access previous version of strategy.");
                return _previousVersionOfThisStrategy;
            }
            set
            {
                _versionOfStrategyBeforeThat = _previousVersionOfThisStrategy;
                if (_versionOfStrategyBeforeThat == null)
                    _versionOfStrategyBeforeThat = value; // set to same as PreviousVersionOfThisStrategy
                _previousVersionOfThisStrategy = value;
                // we only keep the references to the previous strategies in this strategy.
                if (_previousVersionOfThisStrategy != null)
                {
                    _previousVersionOfThisStrategy._previousVersionOfThisStrategy = null;
                    _previousVersionOfThisStrategy._versionOfStrategyBeforeThat = null;
                }
                if (_versionOfStrategyBeforeThat != null)
                {
                    _versionOfStrategyBeforeThat._previousVersionOfThisStrategy = null;
                    _versionOfStrategyBeforeThat._versionOfStrategyBeforeThat = null; 
                }
            }
        }

        public bool VersionOfStrategyBeforePreviousMayExist { get { return Decision.PreservePreviousVersionWhenOptimizing || Decision.AverageInPreviousVersion; } }

        public Strategy VersionOfStrategyBeforePrevious
        {
            get
            {
                if (!VersionOfStrategyBeforePreviousMayExist)
                    return null; //  throw new Exception("VersionOfStrategyBeforeThat must be true to access previous version of strategy.");
                if (_versionOfStrategyBeforeThat == null)
                    return this; // occurs when we have not yet developed the strategy
                return _versionOfStrategyBeforeThat;
            }
        }

        [Serializable]
        public class StrategyState
        {
            public List<Byte[]> SerializedStrategies;
            public List<Strategy> UnserializedStrategies; // this is a short cut to use when we don't need to deserialize
            public Byte[] SerializedGameFactory;
            public Byte[] SerializedGameDefinition;
            public Byte[] SerializedSimulationInteraction;
            public Byte[] SerializedFastPseudoRandom;

            public Dictionary<string, Strategy> GetAlreadyDeserializedStrategies(StrategySerializationInfo ssi)
            {
                Dictionary<string, Strategy> d = new Dictionary<string,Strategy>();
                for (int i = 0; i < UnserializedStrategies.Count; i++)
                    if (ssi.HashCodes[i] != null)
                        d.Add(ssi.HashCodes[i], UnserializedStrategies[i]);
                return d;
            }
        }

        public StrategyState RememberStrategyState()
        {
            bool serializeStrategyItself = true;
            StrategyState s = new StrategyState();
            s.SerializedStrategies = new List<byte[]>();
            s.UnserializedStrategies = new List<Strategy>(); // we'll just populate this with nulls for now
            for (int i = 0; i < AllStrategies.Count; i++)
            {
                if (DecisionNumber == i && !serializeStrategyItself)
                    s.SerializedStrategies.Add(null);
                else
                {
                    Strategy duplicatedStrategy = null;
                    try
                    {
                        duplicatedStrategy = AllStrategies[i].DeepCopy();
                    }
                    catch (OutOfMemoryException ome)
                    {
                        // ignore it -- we'll use the original strategy (will take longer because of undo preserialize)
                        duplicatedStrategy = null;
                    }
                    byte[] bytes = BinarySerialization.GetByteArray(duplicatedStrategy ?? AllStrategies[i]); // will preserialize
                    if (duplicatedStrategy == null)
                        AllStrategies[i].UndoPreSerialize();
                    s.SerializedStrategies.Add(bytes);
                    // doesn't work TextFileCreate.CreateTextFile(@"C:\Temp\" + BinarySerialization.GetHashCodeFromByteArray(bytes), XMLSerialization.SerializeToString<Strategy>(AllStrategies[i])); 
                }
                s.UnserializedStrategies.Add(null);
            }
            s.SerializedGameFactory = BinarySerialization.GetByteArray(SimulationInteraction.CurrentExecutionInformation.GameFactory);
            s.SerializedGameDefinition = BinarySerialization.GetByteArray(SimulationInteraction.PreviouslyLoadedGameDefinition);
            s.SerializedSimulationInteraction = BinarySerialization.GetByteArray(SimulationInteraction);
            s.SerializedFastPseudoRandom = FastPseudoRandom.GetSerializedState();
            return s;
        }

        public void RecallStrategyState(StrategyState s)
        {
            AllStrategies = new List<Strategy>();
            if (s.UnserializedStrategies == null)
                s.UnserializedStrategies = (new Strategy[s.SerializedStrategies.Count()]).ToList();
            for (int i = 0; i < s.SerializedStrategies.Count; i++)
            {
                Strategy toAdd = null;
                if (DecisionNumber == i)
                    toAdd = this;
                else
                {
                    if (s.UnserializedStrategies != null && s.UnserializedStrategies[i] != null)
                        toAdd = null;
                    else if (s.SerializedStrategies[i] == null)
                        toAdd = null;
                    else
                    {
                        toAdd = (Strategy)BinarySerialization.GetObjectFromByteArray(s.SerializedStrategies[i]);
                    }

                }
                if (toAdd == null)
                    AllStrategies.Add(s.UnserializedStrategies[i]);
                else
                {
                    toAdd.UndoPreSerialize();
                    AllStrategies.Add(toAdd);
                    s.UnserializedStrategies[i] = toAdd; // we'll save this for later
                }
            }

            IGameFactory gameFactory = (IGameFactory)BinarySerialization.GetObjectFromByteArray(s.SerializedGameFactory);
            gameFactory.InitializeStrategyDevelopment(this);
            Player = new GamePlayer(
                AllStrategies,
                gameFactory,
                EvolutionSettings.ParallelOptimization,
                (GameDefinition)BinarySerialization.GetObjectFromByteArray(s.SerializedGameDefinition));
            SimulationInteraction = (SimulationInteraction)BinarySerialization.GetObjectFromByteArray(s.SerializedSimulationInteraction);
            foreach (var s2 in AllStrategies)
            {
                s2.SimulationInteraction = SimulationInteraction;
                s2.AllStrategies = AllStrategies;
            }
            FastPseudoRandom.SetState(s.SerializedFastPseudoRandom);
        }


        public void RecallStrategyState(Strategy strategyWithStateAlreadyRecalled)
        {
            AllStrategies = strategyWithStateAlreadyRecalled.AllStrategies.ToList();
            ;

            Player = new GamePlayer(
                AllStrategies,
                strategyWithStateAlreadyRecalled.Player.gameFactory,
                EvolutionSettings.ParallelOptimization,
                strategyWithStateAlreadyRecalled.Player.gameDefinition);
            SimulationInteraction = strategyWithStateAlreadyRecalled.SimulationInteraction;
            foreach (var s2 in AllStrategies)
                s2.SimulationInteraction = SimulationInteraction;
        }

        #endregion

        #region Develop strategy and components

        public void DevelopStrategy(bool calledRecursivelyForSimpleEquilibriumStrategyDevelopment, ProgressResumptionManager prm, out bool stop, bool rerandomize=false)
        {
            //if (Decision.Name.StartsWith("InsertNameOfDecisionToSkipHere"))
            //{
            //    stop = false;
            //    return; 
            //}


            if (Decision.DummyDecisionSkipAltogether || (StrategyDeserializedFromDisk && Decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved))
            {
                StrategyDevelopmentInitiated = true;
                stop = false;
                return;
            }

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

            if (rerandomize) // this is useful if we want to run the exact same step again but with different random numbers (so we serialize it first)
            {
                RandomGeneratorInstanceManager.Reset(true, false);
                FastPseudoRandom.Reinitialize();
            }

            SaveProgressIfNecessary(prm);

            TabbedText.WriteLine("Presmoothing already computed: " + PresmoothingValuesComputedEarlier + " Cache has values: " + CacheFromPreviousOptimizationContainsValues);

            ResultIncrement = 0.0;
            ResultMultiplier = 1.0;

            IGameFactory gameFactory = SimulationInteraction.CurrentExecutionInformation.GameFactory;
            gameFactory.InitializeStrategyDevelopment(this);
            Player = new GamePlayer(
                AllStrategies,
                gameFactory,
                EvolutionSettings.ParallelOptimization,
                SimulationInteraction.PreviouslyLoadedGameDefinition);
            CurrentlyDevelopingStrategy = true;
            StrategyDevelopmentInitiated = true;
            if (!calledRecursivelyForSimpleEquilibriumStrategyDevelopment)
                RepetitionWithinSimpleEquilibriumStrategy = null;


            if (calledRecursivelyForSimpleEquilibriumStrategyDevelopment || (!Decision.MustBeInEquilibriumWithNextDecision && !Decision.MustBeInEquilibriumWithPreviousDecision && !Decision.MustBeInEquilibriumWithPreviousAndNextDecision))
                DevelopStrategyStandard(out stop);
            else
                DevelopEquilibriumStrategies(out stop);

            CyclesStrategyDevelopment++;
            CyclesStrategyDevelopmentThisEvolveStep++;
            gameFactory.ConcludeStrategyDevelopment();
            CurrentlyDevelopingStrategy = false;

            if (Decision.ActionToTakeFollowingStrategyDevelopment != null)
                Decision.ActionToTakeFollowingStrategyDevelopment(this);
        }

        private void SaveProgressIfNecessary(ProgressResumptionManager prm)
        {
            if (prm != null && prm.ProgressResumptionOption == ProgressResumptionOptions.ProceedNormallySavingPastProgress && !CacheFromPreviousOptimizationContainsValues && !Decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision) // we don't serialize when the cache has values because then we're effectively still in the middle of a serialization
            {
                string path;
                string filenameBase;
                prm.Info.NumStrategyStatesSerialized++;
                GetSerializedStrategiesPathAndFilenameBase(prm.Info.NumStrategyStatesSerialized, out path, out filenameBase);
                StrategyStateSerialization.SerializeStrategyStateToFiles(this, path, filenameBase);
                TabbedText.WriteLine("Strategy state serialized as " + filenameBase);
                prm.SaveProgressIfSaving();
            }
        }

        public void GetSerializedStrategiesPathAndFilenameBase(int numStrategyStatesSerialized, out string path, out string filenameBase)
        {
            path = Path.Combine(SimulationInteraction.BaseOutputDirectory, SimulationInteraction.storedStrategiesSubdirectory);
            filenameBase = "strsta" + numStrategyStatesSerialized.ToString();
        }

        private void DevelopStrategyStandard(out bool stop)
        {
            stop = false;

            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;

            if (Decision.PreservePreviousVersionWhenOptimizing || Decision.AverageInPreviousVersion) // average in previous version will trigger copying regarless of preservepreviousversion setting.
                PreviousVersionOfThisStrategy = this.DeepCopy();
            GeneralOverrideValue = null;
            DevelopOversamplingPlan(out DecisionReachedEnoughTimes);
            SetCumulativeDistributions();

            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;
            if (Decision.DummyDecisionRequiringNoOptimization)
            {
                UseBuiltInStrategy = true;
                StrategyDevelopmentInitiated = true;
                return; // we've done the oversampling analysis and cumulative distributions, but there's nothing else to do
            }
            if (DecisionReachedEnoughTimes || DisableOversampling)
            { // else the development of this decision will be skipped, because it isn't being reached enough

                if (CyclesStrategyDevelopment == 0 || InputGroupTree == null)
                {
                    if (Decision.DynamicNumberOfInputs)
                        ProcessDynamicInputs();
                    if (Decision.DynamicNumberOfInputs || InputGroups == null || !InputGroups.Any())
                        CreateDefaultInputGroup();
                    CreateInputGroupTree();
                }
                DevelopIStrategyComponents();
                if (Decision.ConvertOneDimensionalDataToLookupTable && Dimensions == 1)
                    CreateLookupTable();
                else
                    ClearLookupTable();
                if (Decision.TestInputs != null || Decision.TestInputsList != null)
                {
                    var testInputsList = Decision.TestInputsList;
                    if (testInputsList == null)
                        testInputsList = new List<List<double>>() { Decision.TestInputs };
                    foreach (var testInputSet in testInputsList)
                    {
                        double calcResult = Calculate(testInputSet);
                        Debug.WriteLine(String.Join(",", testInputSet.ToArray()) + " ==> " + calcResult);
                    }
                }

                CheckProgressIntegrity();
            }
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;
        }


        int DevelopmentOrder = 0;
        public List<IStrategyComponent> IStrategyComponentsInDevelopmentOrder;

        /// <summary>
        /// This can be called by the strategy component to get the corresponding strategy component for the previous decision.
        /// </summary>
        /// <returns></returns>
        public IStrategyComponent GetCorrespondingStrategyComponentFromPreviousDecision()
        {
            Strategy previousStrategy = AllStrategies[GetPreviousNonDummyStrategyDecisionNumber()];
            if (previousStrategy.IStrategyComponentsInDevelopmentOrder == null)
                return null;
            return previousStrategy.IStrategyComponentsInDevelopmentOrder[DevelopmentOrder];
        }

        public IStrategyComponent GetCorrespondingStrategyComponentFromDecisionNumber(int decisionIndex)
        {
            if (AllStrategies[decisionIndex].IStrategyComponentsInDevelopmentOrder == null)
                return null;
            return AllStrategies[decisionIndex].IStrategyComponentsInDevelopmentOrder[DevelopmentOrder];
        }

        public IStrategyComponent GetCorrespondingStrategyComponentFromPreviousVersionOfThisStrategy()
        {
            if (PreviousVersionOfThisStrategy == null || PreviousVersionOfThisStrategy.IStrategyComponentsInDevelopmentOrder == null)
                return null;
            return PreviousVersionOfThisStrategy.IStrategyComponentsInDevelopmentOrder[DevelopmentOrder];
        }

        public List<IStrategyComponent> GetIStrategyComponentsInDevelopmentOrder()
        {
            if (InputGroupTree == null)
                return null;
            List<IStrategyComponent> iStrategyComponentsInDevelopmentOrder = new List<IStrategyComponent>(); 
            foreach (InputGroupPlus inputGroupPlus in InputGroupTree)
            {
                GetIStrategyComponentsInDevelopmentOrder_Helper(ref iStrategyComponentsInDevelopmentOrder, inputGroupPlus);
            }
            return iStrategyComponentsInDevelopmentOrder;
        }

        private void GetIStrategyComponentsInDevelopmentOrder_Helper(ref List<IStrategyComponent> iStrategyComponentsInDevelopmentOrder, InputGroupPlus inputGroupPlus)
        {
            iStrategyComponentsInDevelopmentOrder.Add(inputGroupPlus.IStrategyComponent);
            foreach (InputInfoPlus inputInfoPlus in inputGroupPlus.Inputs)
            {
                if (inputInfoPlus.SubGroups != null && inputInfoPlus.SubGroups.Count != 0)
                {
                    foreach (InputGroupPlus inputGroupPlus2 in inputInfoPlus.SubGroups)
                        GetIStrategyComponentsInDevelopmentOrder_Helper(ref iStrategyComponentsInDevelopmentOrder, inputGroupPlus2);
                }
            }
        }

        private void DevelopIStrategyComponents()
        {
            DevelopmentOrder = -1;
            IStrategyComponentsInDevelopmentOrder = new List<IStrategyComponent>();
            int numInputGroupPluses = InputGroupTree.Sum(x => x.CountContainedInputGroupPlus());
            SimulationInteraction.GetCurrentProgressStep().AddChildSteps(numInputGroupPluses, "Developing strategy components");
            foreach (InputGroupPlus inputGroupPlus in InputGroupTree)
            {
                DevelopIStrategyComponentsForListInputGroupPlus(inputGroupPlus);
                bool stop;
                SimulationInteraction.CheckStopOrPause(out stop);
                if (stop)
                    return;
            }
            CacheFromPreviousOptimization = null;
        }

        private void DevelopIStrategyComponentsForListInputGroupPlus(InputGroupPlus inputGroupPlus)
        {
            ActiveInputGroupPlus = inputGroupPlus;
            DevelopmentOrder++;
            inputGroupPlus.DevelopmentOrder = DevelopmentOrder;

            inputGroupPlus.IStrategyComponent.DevelopStrategyComponent(); // develop strategy for this one before developing strategy for any children
            IStrategyComponentsInDevelopmentOrder.Add(inputGroupPlus.IStrategyComponent);
            SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Developing strategy components");
            foreach (InputInfoPlus inputInfoPlus in inputGroupPlus.Inputs)
            {
                if (inputInfoPlus.SubGroups != null && inputInfoPlus.SubGroups.Count != 0)
                {
                    foreach (InputGroupPlus inputGroupPlus2 in inputInfoPlus.SubGroups)
                        DevelopIStrategyComponentsForListInputGroupPlus(inputGroupPlus2);
                }
            }
        }

        public void ConvertStrategyComponentsToRPROP()
        {
            IStrategyComponentsInDevelopmentOrder = new List<IStrategyComponent>();
            foreach (InputGroupPlus inputGroupPlus in InputGroupTree)
            {
                ConvertStrategyComponentsToRPROP_Helper(inputGroupPlus);
            }
        }

        public void ConvertStrategyComponentsToRPROP_Helper(InputGroupPlus inputGroupPlus)
        {
            if (inputGroupPlus.IStrategyComponent is OptimizePointsAndSmooth)
                inputGroupPlus.IStrategyComponent = ((OptimizePointsAndSmooth)inputGroupPlus.IStrategyComponent).ConvertToRPROPSmoothing();
            IStrategyComponentsInDevelopmentOrder.Add(inputGroupPlus.IStrategyComponent);
            foreach (InputInfoPlus inputInfoPlus in inputGroupPlus.Inputs)
            {
                if (inputInfoPlus.SubGroups != null && inputInfoPlus.SubGroups.Count != 0)
                {
                    foreach (InputGroupPlus inputGroupPlus2 in inputInfoPlus.SubGroups)
                    {
                        ConvertStrategyComponentsToRPROP_Helper(inputGroupPlus2);
                    }
                }
            }
        }

        #endregion

        #region Equilibrium strategies

        public int? RepetitionWithinSimpleEquilibriumStrategy;
        int FirstEquilibriumStrategyDecisionNumber;
        int LastEquilibriumStrategyDecisionNumber;
        int EquilibriumStrategiesCount;
        double[] LowerBoundEquilibriumStrategies;
        double[] UpperBoundEquilibriumStrategies;
        const int NumRandomPointsForEquilibriumStrategies = 1000;
        const int NumRandomPointsForEquilibriumStrategiesValidation = 100;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        double[][] RandomPointsForEquilibriumStrategies, RandomPointsForEquilibriumStrategiesValidation;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        double[] ScoreForRandomPoints, ScoreForRandomPointsValidation;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        double[] SumDerivativesRandomPoints, SumDerivativesRandomPointsValidation;
        int NumPlaysForEachRandomPointForEquilibriumStrategies = 1000;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        NeuralNetworkWrapper NeuralNetworkForThisEquilibriumStrategy;

        private void DevelopEquilibriumStrategies(out bool stop)
        {
            stop = false;
            PrepareInfoForEquilibriumStrategiesDevelopment(out stop);
            SimulationInteraction.CheckStopOrPause(out stop);
            if (stop)
                return;
            if (DecisionNumber == LastEquilibriumStrategyDecisionNumber)
                CompleteEquilibriumStrategyDevelopment();
        }

        /// <summary>
        /// This calculates the score for this strategy as a function of random combinations of strategies for each score that much be kept in equilibrium. It does this by first randomly picking strategies and then using a neural network to generalize. It finally outputs the relevant points.
        /// </summary>
        /// <param name="stop"></param>
        private void PrepareInfoForEquilibriumStrategiesDevelopment(out bool stop)
        {
            stop = false;
            int decisionNumber = DecisionNumber;
            while (!AllStrategies[decisionNumber].Decision.MustBeInEquilibriumWithNextDecision)
                decisionNumber--;
            FirstEquilibriumStrategyDecisionNumber = decisionNumber;
            while (!AllStrategies[decisionNumber].Decision.MustBeInEquilibriumWithPreviousDecision)
                decisionNumber++;
            LastEquilibriumStrategyDecisionNumber = decisionNumber;
            EquilibriumStrategiesCount = LastEquilibriumStrategyDecisionNumber - FirstEquilibriumStrategyDecisionNumber + 1;
            LowerBoundEquilibriumStrategies = new double[EquilibriumStrategiesCount];
            UpperBoundEquilibriumStrategies = new double[EquilibriumStrategiesCount];
            for (int d = FirstEquilibriumStrategyDecisionNumber; d <= LastEquilibriumStrategyDecisionNumber; d++)
            {
                LowerBoundEquilibriumStrategies[d - FirstEquilibriumStrategyDecisionNumber] = AllStrategies[d].Decision.StrategyBounds.LowerBound;
                UpperBoundEquilibriumStrategies[d - FirstEquilibriumStrategyDecisionNumber] = AllStrategies[d].Decision.StrategyBounds.UpperBound;
                AllStrategies[d].StrategyDevelopmentInitiated = true;
            }

            if (!Decision.UseSimpleMethodForDeterminingEquilibrium)
                stop = PrepareInfoForComplexEquilibriumMethod(stop);
        }

        private bool PrepareInfoForComplexEquilibriumMethod(bool stop)
        {
            RandomPointsForEquilibriumStrategies = new double[NumRandomPointsForEquilibriumStrategies][];
            ScoreForRandomPoints = new double[NumRandomPointsForEquilibriumStrategies];
            ScoreRandomPointsForEquilibriumStrategies(ref stop, RandomPointsForEquilibriumStrategies, ScoreForRandomPoints, NumRandomPointsForEquilibriumStrategies);
            RandomPointsForEquilibriumStrategiesValidation = new double[NumRandomPointsForEquilibriumStrategiesValidation][];
            ScoreForRandomPointsValidation = new double[NumRandomPointsForEquilibriumStrategiesValidation];
            ScoreRandomPointsForEquilibriumStrategies(ref stop, RandomPointsForEquilibriumStrategiesValidation, ScoreForRandomPointsValidation, NumRandomPointsForEquilibriumStrategiesValidation);
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(EquilibriumStrategiesCount, RandomPointsForEquilibriumStrategies, ScoreForRandomPoints.Select(x => new double[] { x }).ToArray());
            NeuralNetworkTrainingData validationData = new NeuralNetworkTrainingData(EquilibriumStrategiesCount, RandomPointsForEquilibriumStrategiesValidation, ScoreForRandomPointsValidation.Select(x => new double[] { x }).ToArray());
            TrainingInfo trainingInfo = new TrainingInfo() { Technique = TrainingTechnique.ResilientPropagation, Epochs = 500, ValidateEveryNEpochs = 50 };
            NeuralNetworkForThisEquilibriumStrategy = new NeuralNetworkWrapper(trainingData, validationData, 20, 0, trainingInfo);
            PlotScoreAsFunctionOfDifferentStrategies();
            return stop;
        }

        private void PlotScoreAsFunctionOfDifferentStrategies()
        {
            List<double[]> plotPoints = new List<double[]>();
            List<System.Windows.Media.Color> colorList = new List<System.Windows.Media.Color>();
            const int numPlotPoints = 10000;
            for (int p = 0; p < numPlotPoints; p++)
            {
                double[] plotPoint = new double[EquilibriumStrategiesCount + 1];

                for (int s = 0; s < EquilibriumStrategiesCount; s++)
                    plotPoint[s] = RandomGenerator.NextDouble(LowerBoundEquilibriumStrategies[s], UpperBoundEquilibriumStrategies[s]); // may need to change this to FastPseudoRandom if we use it again
                plotPoint[EquilibriumStrategiesCount] = NeuralNetworkForThisEquilibriumStrategy.CalculateResult(plotPoint.Take(EquilibriumStrategiesCount).ToList());
                plotPoints.Add(plotPoint);
                colorList.Add(System.Windows.Media.Colors.Cyan);
            }
            if (plotPoints[0].Count() == 2)
                SimulationInteraction.Create2DPlot(plotPoints, new Graph2DSettings() { graphName = "Score for decision " + DecisionNumber.ToString(), seriesName = "", spline = true, yMin = Decision.StrategyBounds.LowerBound, yMax = Decision.StrategyBounds.UpperBound }, "");
            else if (plotPoints[0].Count() == 3)
                SimulationInteraction.Create3DPlot(plotPoints, colorList, "Score for decision " + DecisionNumber.ToString());
        }

        /// <summary>
        /// For each of numRandomPoints, a random strategy is selected for each strategy, and a score is calculated for this strategy.
        /// </summary>
        /// <param name="stop"></param>
        /// <param name="randomPoints"></param>
        /// <param name="scores"></param>
        /// <param name="numRandomPoints"></param>
        private void ScoreRandomPointsForEquilibriumStrategies(ref bool stop, double[][] randomPoints, double[] scores, int numRandomPoints)
        {
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = new OversamplingPlan(), StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
            bool doStop = stop; // can't use out parameter in lambda expression
            bool doStop2 = stop;
            //for (int i = 0; i < NumRandomPointsForEquilibriumStrategies; i++)
            bool originalUseThreadLocalScore = UseThreadLocalScores;
            UseThreadLocalScores = true;
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numRandomPoints, i =>
            {
                if (!doStop2)
                {
                    doStop = PlayIterationOfEquilibriumStrategies(randomPoints, scores, oversamplingInfo, i);
                    if (doStop)
                        doStop2 = true;
                }
            }
            );
            stop = doStop2;
            UseThreadLocalScores = originalUseThreadLocalScore;
        }

        /// <summary>
        /// For the specified iteration, a random point is specified for each strategy, and a score is then calculated for the current strategy.
        /// </summary>
        /// <param name="randomPoints"></param>
        /// <param name="scores"></param>
        /// <param name="oversamplingInfo"></param>
        /// <param name="i"></param>
        /// <returns></returns>
        private bool PlayIterationOfEquilibriumStrategies(double[][] randomPoints, double[] scores, OversamplingInfo oversamplingInfo, int i)
        {
            bool doStop = false;
            randomPoints[i] = new double[EquilibriumStrategiesCount];
            for (int s = 0; s < EquilibriumStrategiesCount; s++)
            {
                double rand = RandomGenerator.NextDouble(LowerBoundEquilibriumStrategies[s], UpperBoundEquilibriumStrategies[s]);
                randomPoints[i][s] = rand;
                AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].GeneralOverrideValue = rand;
            }
            List<IterationID> iterationsToPlay = Enumerable.Range(i * NumPlaysForEachRandomPointForEquilibriumStrategies, NumPlaysForEachRandomPointForEquilibriumStrategies).Select(x => GenerateIterationID((long)x)).ToList();
            scores[i] = PlaySpecificValueForSomeIterations(-1.0, iterationsToPlay, NumRandomPointsForEquilibriumStrategies * NumPlaysForEachRandomPointForEquilibriumStrategies, oversamplingInfo);
            SimulationInteraction.CheckStopOrPause(out doStop);
            return doStop;
        }

        private bool PlayIterationOfEquilibriumStrategiesForPrespecifiedPoints(double[][] nonRandomPoints, double[] scores, OversamplingInfo oversamplingInfo, int i)
        {
            bool doStop = false;
            for (int s = 0; s < EquilibriumStrategiesCount; s++)
                AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].GeneralOverrideValue = nonRandomPoints[i][s];
            List<IterationID> iterationsToPlay = Enumerable.Range(i * NumPlaysForEachRandomPointForEquilibriumStrategies, NumPlaysForEachRandomPointForEquilibriumStrategies).Select(x => GenerateIterationID((long)x)).ToList();
            scores[i] = PlaySpecificValueForSomeIterations(-1.0, iterationsToPlay, NumRandomPointsForEquilibriumStrategies * NumPlaysForEachRandomPointForEquilibriumStrategies, oversamplingInfo);
            SimulationInteraction.CheckStopOrPause(out doStop);
            return doStop;
        }


        private void CompleteEquilibriumStrategyDevelopment()
        {
            if (Decision.UseSimpleMethodForDeterminingEquilibrium)
                CompleteEquilibriumStrategyDevelopment_SimpleRepetition();
            else
            {
                if (LastEquilibriumStrategyDecisionNumber == FirstEquilibriumStrategyDecisionNumber + 1)
                    CompleteEquilibriumStrategyDevelopment_ForTwoStrategies();
                else
                    CompleteEquilibriumStrategyDevelopment_ForThreeOrMoreStrategies();
            }
        }

        int SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining;

        private void CompleteEquilibriumStrategyDevelopment_SimpleRepetition()
        {
            CurrentlyDevelopingStrategy = false;

            int maxRepetitions = Decision.RepetitionsForSimpleMethodForDeterminingEquilibrium;
            SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining = maxRepetitions * (LastEquilibriumStrategyDecisionNumber - FirstEquilibriumStrategyDecisionNumber + 1);
            SimulationInteraction.GetCurrentProgressStep().AddChildSteps(SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining, "Simple repetition for equilibrium strategy development");
            if (Decision.UseFastConvergenceWithSimpleEquilibrium)
            {
                for (int d = FirstEquilibriumStrategyDecisionNumber; d <= LastEquilibriumStrategyDecisionNumber; d++)
                {
                    AllStrategies[d].UseFastConvergence = true;
                    AllStrategies[d].AbortFastConvergenceIfPreciseEnough = Decision.AbortFastConvergenceIfPreciseEnough;
                    AllStrategies[d].ConvergencePrecision = new List<double>();
                    AllStrategies[d].ConvergenceSampleDecisionInputs = null;
                }
                CompleteEquilibriumStrategyDevelopment_SingleRepetition();
                bool precisionReached = false;
                int numRepetitions = 1;
                Debug.WriteLine("Number fast convergence repetitions: " + numRepetitions);
                do
                {
                    precisionReached = AllStrategies
                                            .Skip(FirstEquilibriumStrategyDecisionNumber)
                                            .Take(LastEquilibriumStrategyDecisionNumber - FirstEquilibriumStrategyDecisionNumber + 1)
                                            .All(s => s.FastConvergencePrecisionReached());
                                         //.All(s => s.FastConvergenceCurrentShiftDistance < s.Decision.PrecisionForFastConvergence * (s.Decision.StrategyBounds.UpperBound - s.Decision.StrategyBounds.LowerBound));
                    if (!precisionReached)
                    {
                        numRepetitions++;
                        CompleteEquilibriumStrategyDevelopment_SingleRepetition();
                    }
                }
                while (!precisionReached && SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining >= (LastEquilibriumStrategyDecisionNumber - FirstEquilibriumStrategyDecisionNumber + 1));
                for (int d = FirstEquilibriumStrategyDecisionNumber; d <= LastEquilibriumStrategyDecisionNumber; d++)
                    AllStrategies[d].UseFastConvergence = false;
            }
            else
            {
                for (RepetitionWithinSimpleEquilibriumStrategy = 1; RepetitionWithinSimpleEquilibriumStrategy <= Decision.RepetitionsForSimpleMethodForDeterminingEquilibrium; RepetitionWithinSimpleEquilibriumStrategy++)
                {
                    CompleteEquilibriumStrategyDevelopment_SingleRepetition();
                }
            }

            
            while (SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining > 0)
            {
                SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Simple repetition for equilibrium strategy development");
                SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining--;
            }
        }

        private List<double> GetListDelta(List<double> theList, int numberBack)
        {
            var delta = theList.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Index > numberBack - 1).Select(x => new { ThisItem = x.Item, PreviousItem = ConvergencePrecision[x.Index - numberBack] }).Select(x => x.ThisItem - x.PreviousItem).ToList();
            return delta;
        }

        private bool FastConvergencePrecisionReached()
        {
            if (!AbortFastConvergenceIfPreciseEnough)
                return false;
            if (ConvergencePrecision.Any())
                if (ConvergencePrecision.Last() < Decision.PrecisionForFastConvergence * (Decision.StrategyBounds.UpperBound - Decision.StrategyBounds.LowerBound))
                    return true;
            if (ConvergencePrecision.Count > 7)
            {
                // If the improvement over the last four iterations is not much better than our improvement over the last two iterations, and the improvement over the last three iterations is not much better than our improvement over the last iteration, then we are done.
                double improvementFourVersusTwo = ConvergencePrecision[ConvergencePrecision.Count - 4] - ConvergencePrecision[ConvergencePrecision.Count - 2]; 
                double improvementThreeVersusOne = ConvergencePrecision[ConvergencePrecision.Count - 3] - ConvergencePrecision[ConvergencePrecision.Count - 1];
                if (improvementFourVersusTwo < 0.1 * ConvergencePrecision[ConvergencePrecision.Count - 4] && improvementThreeVersusOne < 0.1 * ConvergencePrecision[ConvergencePrecision.Count - 3])
                    return true;
                else
                    return false;
            }
            else
                return false;
            // to do: figure out if convergence precision is leveling off
        }

        private void MeasureConvergencePrecision()
        {
            if (PreviousVersionOfThisStrategy == null)
                return;
            if (ConvergenceSampleDecisionInputs == null)
                ConvergenceSampleDecisionInputs = GetSampleDecisionInputs(500); // get just a small number of sample decision inputs
            double sumAbsDifferences = 0;
            foreach (double[] decisionInputs in ConvergenceSampleDecisionInputs)
            {
                double currentValue = Calculate(decisionInputs.ToList());
                double previousValue = PreviousVersionOfThisStrategy.Calculate(decisionInputs.ToList());
                double absDifference = Math.Abs(currentValue - previousValue);
                sumAbsDifferences += absDifference;
            }
            double quotient = sumAbsDifferences / (double)ConvergenceSampleDecisionInputs.Count;
            ConvergencePrecision.Add(quotient);
        }

        private void CompleteEquilibriumStrategyDevelopment_SingleRepetition()
        {
            for (int d = FirstEquilibriumStrategyDecisionNumber; d <= LastEquilibriumStrategyDecisionNumber; d++)
            {
                bool stop;
                Debug.Write("Decision " + AllStrategies[d].Decision.Name + ": ");
                AllStrategies[d].RepetitionWithinSimpleEquilibriumStrategy = RepetitionWithinSimpleEquilibriumStrategy;
                AllStrategies[d].DevelopStrategy(true, null, out stop);
                AllStrategies[d].Decision.AddToStrategyGraphs(SimulationInteraction.BaseOutputDirectory, true, false, AllStrategies[d], AllStrategies[d].ActionGroup);
                if (Decision.UseFastConvergenceWithSimpleEquilibrium)
                    AllStrategies[d].MeasureConvergencePrecision();
                if (SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining > 0)
                    SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "Simple repetition for equilibrium strategy development");
                SimpleEquilibriumStrategyDevelopmentTotalChildStepsRemaining--;
            }
        }

        /// <summary>
        /// We try to find as precisely as possible the point(s) where the first strategy will not change if given the chance when the second strategy is changed based on it.
        /// </summary>
        private void CompleteEquilibriumStrategyDevelopment_ForTwoStrategies()
        {
            const int numRanges = 100;
            double[] derivativeAtEachPoint = new double[numRanges];
            double[] secondStrategyAtEachPoint = new double[numRanges];
            double rangeSizeFirstStrategy = (UpperBoundEquilibriumStrategies[0] - LowerBoundEquilibriumStrategies[0])/numRanges;
            double rangeSizeSecondStrategy = (UpperBoundEquilibriumStrategies[1] - LowerBoundEquilibriumStrategies[1])/numRanges;
            for (int rangeFirst = 0; rangeFirst < numRanges; rangeFirst++)
            {
                // Consider a first strategy
                double initialFirstStrategy = LowerBoundEquilibriumStrategies[0] + rangeSizeFirstStrategy * (rangeFirst + 0.5);
                // Figure out the optimal second strategy based on this first strategy
                double bestSecondStrategy = 0; 
                double bestSecondStrategyScore = 0;
                for (int rangeSecond = 0; rangeSecond < numRanges; rangeSecond++)
                {
                    double testSecondStrategy = LowerBoundEquilibriumStrategies[1] + rangeSizeSecondStrategy * (rangeSecond + 0.5);
                    double scoreSecondStrategy = AllStrategies[LastEquilibriumStrategyDecisionNumber].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(new List<double>() { initialFirstStrategy, testSecondStrategy });
                    if (rangeSecond == 0 || scoreSecondStrategy > bestSecondStrategyScore)
                    {
                        bestSecondStrategy = testSecondStrategy;
                        bestSecondStrategyScore = scoreSecondStrategy;
                        secondStrategyAtEachPoint[rangeFirst] = testSecondStrategy;
                    }
                }
                // Figure out the derivative of the first strategy based on this second strategy at this point.
                const double differenceForDerivative = 0.0001;
                double scoreFirstStrategy1 = AllStrategies[FirstEquilibriumStrategyDecisionNumber].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(new List<double>() { initialFirstStrategy, bestSecondStrategy });
                double scoreFirstStrategy2 = AllStrategies[FirstEquilibriumStrategyDecisionNumber].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(new List<double>() { initialFirstStrategy + differenceForDerivative, bestSecondStrategy });
                double derivative = (scoreFirstStrategy2 - scoreFirstStrategy1) / differenceForDerivative;
                derivativeAtEachPoint[rangeFirst] = derivative;
            }
            // Now, find spots where we go from positive to negative or vice versa, and interpolate exact point, then calculate optimal second strategy there.
            List<double[]> equilibriumPoints = new List<double[]>();
            List<double> correspondingSumScores = new List<double>();
            for (int rangeFirst = 0; rangeFirst < numRanges - 1; rangeFirst++)
            {
                if ((derivativeAtEachPoint[rangeFirst]) > 0 != (derivativeAtEachPoint[rangeFirst + 1]) > 0)
                {
                    double r0 = LowerBoundEquilibriumStrategies[0] + rangeSizeFirstStrategy * (rangeFirst + 0.5);
                    double r1 = LowerBoundEquilibriumStrategies[0] + rangeSizeFirstStrategy * (rangeFirst + 1.5);
                    double approximateLocationOfZeroDerivativeFirstStrategy = r0 + (r1 - r0) * (derivativeAtEachPoint[rangeFirst] / (derivativeAtEachPoint[rangeFirst] - derivativeAtEachPoint[rangeFirst + 1]));
                    double s0 = secondStrategyAtEachPoint[rangeFirst];
                    double s1 = secondStrategyAtEachPoint[rangeFirst + 1];
                    double approximateLocationOfCorrespondingSecondStrategy = s0 + (s1 - s0) * (derivativeAtEachPoint[rangeFirst] / (derivativeAtEachPoint[rangeFirst] - derivativeAtEachPoint[rangeFirst + 1]));
                    double[] equilibriumPoint = new double[] { approximateLocationOfZeroDerivativeFirstStrategy, approximateLocationOfCorrespondingSecondStrategy };
                    equilibriumPoints.Add(equilibriumPoint);

                    double scoreFirstStrategy = AllStrategies[FirstEquilibriumStrategyDecisionNumber].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(equilibriumPoint.ToList());
                    double scoreSecondStrategy = AllStrategies[LastEquilibriumStrategyDecisionNumber].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(equilibriumPoint.ToList());
                    correspondingSumScores.Add(scoreFirstStrategy + scoreSecondStrategy);
                }
            }
            Debug.WriteLine("Equilibrium points:");
            for (int p = 0; p < equilibriumPoints.Count(); p++)
            {
                var point = equilibriumPoints[p];
                Debug.WriteLine(String.Join(",", point) + " --> " + correspondingSumScores[p]);
            }
            if (!equilibriumPoints.Any())
            {
                for (int s = 0; s < EquilibriumStrategiesCount; s++)
                {
                    AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].GeneralOverrideValue = (LowerBoundEquilibriumStrategies[s] + UpperBoundEquilibriumStrategies[s]) / 2.0;
                    AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].IterationsWhereDecisionIsNotReached = new List<IterationID>();
                }
            }
            else
            {
                int winningIndex = IndexOfMax(correspondingSumScores);
                Debug.WriteLine("Using point with index " + winningIndex);
                AllStrategies[FirstEquilibriumStrategyDecisionNumber].GeneralOverrideValue = equilibriumPoints[winningIndex][0];
                AllStrategies[LastEquilibriumStrategyDecisionNumber].GeneralOverrideValue = equilibriumPoints[winningIndex][1];
                for (int s = 0; s < EquilibriumStrategiesCount; s++)
                    AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].IterationsWhereDecisionIsNotReached = new List<IterationID>();
            }
        }

        private static int IndexOfMax<T>(IEnumerable<T> sequence)
    where T : IComparable<T>
        {
            int maxIndex = -1;
            T maxValue = default(T); // Immediately overwritten anyway

            int index = 0;
            foreach (T value in sequence)
            {
                if (value.CompareTo(maxValue) > 0 || maxIndex == -1)
                {
                    maxIndex = index;
                    maxValue = value;
                }
                index++;
            }
            return maxIndex;
        }

        /// <summary>
        /// This method seeks to find points where the strategies are relatively stable by plotting the sum of the absolute derivatives of each strategy at each point. It then sets the strategy to the point that is the most stable. (The code can also be changed to set it the point that maximizes joint welfare.)
        /// </summary>
        private void CompleteEquilibriumStrategyDevelopment_ForThreeOrMoreStrategies()
        {
            SumDerivativesRandomPoints = new double[NumRandomPointsForEquilibriumStrategies];
            SumDerivativesRandomPointsValidation = new double[NumRandomPointsForEquilibriumStrategiesValidation];
            CalculateSumAbsoluteDerivativesAtSpecifiedPoints(RandomPointsForEquilibriumStrategies, SumDerivativesRandomPoints);
            CalculateSumAbsoluteDerivativesAtSpecifiedPoints(RandomPointsForEquilibriumStrategiesValidation, SumDerivativesRandomPointsValidation);
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(EquilibriumStrategiesCount, RandomPointsForEquilibriumStrategies, SumDerivativesRandomPoints.Select(x => new double[] { x }).ToArray());
            NeuralNetworkTrainingData validationData = new NeuralNetworkTrainingData(EquilibriumStrategiesCount, RandomPointsForEquilibriumStrategiesValidation, SumDerivativesRandomPointsValidation.Select(x => new double[] { x }).ToArray());
            TrainingInfo trainingInfo = new TrainingInfo() { Technique = TrainingTechnique.ResilientPropagation, Epochs = 500, ValidateEveryNEpochs = 50 };
            NeuralNetworkWrapper wrapper = new NeuralNetworkWrapper(trainingData, validationData, 20, 0, trainingInfo);
            List<double[]> apparentLocalMinimaInSumOfDerivatives = FindApparentLocalMinima(wrapper);
            if (!apparentLocalMinimaInSumOfDerivatives.Any())
                throw new Exception("No equilibrium points found.");
            int bestIndexSoFar = -1;
            double bestScoreSoFar = 0;
            for (int lm = 0; lm < apparentLocalMinimaInSumOfDerivatives.Count; lm++)
            {
                double[] apparentLocalMinimum = apparentLocalMinimaInSumOfDerivatives[lm];
                double overallStabilityMeasureWhereLowerIsMoreStable = wrapper.CalculateResult(apparentLocalMinimum.ToList());
                Debug.WriteLine("The derivatives are minimized (i.e., strategy is relatively stable) at the following points: " + String.Join(",", apparentLocalMinimum.Select(x => x.ToSignificantFigures(3)).ToArray()) + " --> " + overallStabilityMeasureWhereLowerIsMoreStable.ToString());
                bool takePointBasedOnHighestScore = false; // If false, then we take the point that is the most stable.
                if (takePointBasedOnHighestScore)
                {
                    double[] scoresForEachStrategy = new double[EquilibriumStrategiesCount];
                    for (int d = FirstEquilibriumStrategyDecisionNumber; d <= LastEquilibriumStrategyDecisionNumber; d++)
                    {
                        if (takePointBasedOnHighestScore)
                            scoresForEachStrategy[d] = AllStrategies[d].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(apparentLocalMinimum.ToList()); // remember, the apparentLocalMinimum represents the strategies being played by all of the strategies
                    }
                    if (scoresForEachStrategy.Sum() > bestScoreSoFar || lm == 0)
                    {
                        bestIndexSoFar = lm;
                        bestScoreSoFar = scoresForEachStrategy.Sum();
                    }
                }
                else
                { // we're taking the most stable point

                    if (overallStabilityMeasureWhereLowerIsMoreStable < bestScoreSoFar || lm == 0)
                    {
                        bestIndexSoFar = lm;
                        bestScoreSoFar = overallStabilityMeasureWhereLowerIsMoreStable;
                    }
                }

            }
            for (int s = 0; s < EquilibriumStrategiesCount; s++)
            {
                AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].GeneralOverrideValue = apparentLocalMinimaInSumOfDerivatives[bestIndexSoFar][s];
                AllStrategies[s + FirstEquilibriumStrategyDecisionNumber].IterationsWhereDecisionIsNotReached = new List<IterationID>();
            }
        }

        private List<double[]> FindApparentLocalMinima(NeuralNetworkWrapper wrapper)
        {
            List<double[]> pointsPlotList = new List<double[]>();
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>();
            List<double[]> apparentLocalMinima = new List<double[]>();
            const int numGridPointsEachDimension = 501;
            int[] localMinCandidateIndices = new int[EquilibriumStrategiesCount];
            double[] localMinCandidate = new double[EquilibriumStrategiesCount];
            double[] gridStepSize = new double[EquilibriumStrategiesCount];
            for (int s = 0; s < EquilibriumStrategiesCount; s++)
            {
                localMinCandidateIndices[s] = 0;
                gridStepSize[s] = (UpperBoundEquilibriumStrategies[s] - LowerBoundEquilibriumStrategies[s]) / (numGridPointsEachDimension - 1.0);
            }
            bool allCandidatesExplored = false;
            while (!allCandidatesExplored)
            {
                double[] pointToPlot = new double[EquilibriumStrategiesCount + 1];
                for (int s = 0; s < EquilibriumStrategiesCount; s++)
                {
                    localMinCandidate[s] = LowerBoundEquilibriumStrategies[s] + gridStepSize[s] * localMinCandidateIndices[s];
                    pointToPlot[s] = localMinCandidate[s];
                }
                pointToPlot[EquilibriumStrategiesCount] = wrapper.CalculateResult(localMinCandidate.ToList());
                if (pointToPlot[EquilibriumStrategiesCount] < 10.0)
                {
                    pointsPlotList.Add(pointToPlot.ToArray());
                    colors.Add(System.Windows.Media.Colors.Beige);
                }
                pointToPlot[EquilibriumStrategiesCount] = 0; // for zero plane
                pointsPlotList.Add(pointToPlot.ToArray());
                colors.Add(System.Windows.Media.Colors.Cyan);
                if (IsApproximateLocalMinimum(localMinCandidate, x => wrapper.CalculateResult(x.ToList()), LowerBoundEquilibriumStrategies, UpperBoundEquilibriumStrategies, gridStepSize))
                    apparentLocalMinima.Add(localMinCandidate.ToArray());
                int dimensionToChange = EquilibriumStrategiesCount - 1;
                bool keepChanging = true;
                while (keepChanging)
                {
                    localMinCandidateIndices[dimensionToChange]++;
                    if (localMinCandidateIndices[dimensionToChange] == numGridPointsEachDimension)
                    {
                        localMinCandidateIndices[dimensionToChange] = 0;
                        dimensionToChange--;
                        if (dimensionToChange < 0)
                        {
                            allCandidatesExplored = true;
                            keepChanging = false;
                        }
                    }
                    else
                        keepChanging = false;
                }
            }
            if (pointsPlotList[0].Count() == 2)
                SimulationInteraction.Create2DPlot(pointsPlotList, new Graph2DSettings() { graphName = "Sum of derivatives of strategies in equilibrium", seriesName = "", spline = true, yMin = Decision.StrategyBounds.LowerBound, yMax = Decision.StrategyBounds.UpperBound }, "");
            else if (pointsPlotList[0].Count() == 3)
                SimulationInteraction.Create3DPlot(pointsPlotList, colors, "Sum of derivatives of strategies in equilibrium");
            return apparentLocalMinima;
        }

        /// <summary>
        /// The sum of the absolute derivative shows us whether the strategies are stable at a particular point. If the strategy is truly stable, the sum should be zero, indicating that none of the strategies is changing at that point. This method calculates the sum of the absolute value of each strategy's derivative for each of the specified points.
        /// </summary>
        /// <param name="specifiedPoints"></param>
        /// <param name="derivatives"></param>
        private void CalculateSumAbsoluteDerivativesAtSpecifiedPoints(double[][] specifiedPoints, double[] derivatives)
        {
            for (int i = 0; i < derivatives.Length; i++)
            {
                double sumDeltaDerivatives = 0;
                for (int dec = FirstEquilibriumStrategyDecisionNumber; dec <= LastEquilibriumStrategyDecisionNumber; dec++)
                {
                    List<double> point = specifiedPoints[i].ToList();
                    double originalValue = AllStrategies[dec].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(point);
                    point[dec - FirstEquilibriumStrategyDecisionNumber] += 0.001;
                    double newValue = AllStrategies[dec].NeuralNetworkForThisEquilibriumStrategy.CalculateResult(point);
                    sumDeltaDerivatives += Math.Abs(newValue - originalValue) / 0.001; 
                }
                derivatives[i] = sumDeltaDerivatives;
            }
        }

        private bool IsApproximateLocalMinimum(double[] point, Func<double[], double> function, double[] lowerBounds, double[] upperBounds, double[] fixedDistanceToSearchInEachDimension)
        {
            bool isLocalMinimum = true;
            for (int s = 0; s < EquilibriumStrategiesCount; s++)
            {
                double[] pointHigher = point.ToArray();
                pointHigher[s] = point[s] + fixedDistanceToSearchInEachDimension[s];
                if (pointHigher[s] > upperBounds[s])
                    pointHigher[s] = upperBounds[s];
                double[] pointLower = point.ToArray();
                pointLower[s] = point[s] - fixedDistanceToSearchInEachDimension[s];
                if (pointLower[s] < lowerBounds[s])
                    pointLower[s] = lowerBounds[s];
                double pointResult = function(point);
                double pointHigherResult = function(pointHigher);
                double pointLowerResult = function(pointLower);
                isLocalMinimum = pointResult <= pointHigherResult && pointResult <= pointLowerResult;
                if (!isLocalMinimum)
                    break;
            }
            return isLocalMinimum;
        }

        private bool IsLocalMinimumInArray(double[][] points, int index1, int index2)
        {
            bool isLocalMinimum = true;
            if (index1 > 0)
                isLocalMinimum = points[index1 - 1][index2] > points[index1][index2];
            if (isLocalMinimum && index1 < points.Length - 1)
                isLocalMinimum = points[index1 + 1][index2] > points[index1][index2];
            if (isLocalMinimum && index2 > 0)
                isLocalMinimum = points[index1][index2 - 1] > points[index1][index2];
            if (isLocalMinimum && index2 < points[index1].Length - 1)
                isLocalMinimum = points[index1][index2 + 1] > points[index1][index2];
            return isLocalMinimum;
        }

        #endregion

        #region Lookup tables

        double[] LookupTable;
        bool CurrentlyCreatingLookupTable;
        double LookupTableStepSize;

        public void CreateLookupTable()
        {
            CurrentlyCreatingLookupTable = true;
            LookupTable = new double[Decision.NumberPointsInLookupTable];
            LookupTableStepSize = ((double)(Decision.StrategyBounds.UpperBound - Decision.StrategyBounds.LowerBound)) / (double)(Decision.NumberPointsInLookupTable - 1);
            for (int p = 0; p < Decision.NumberPointsInLookupTable; p++)
            {
                double inputValue = Decision.StrategyBounds.LowerBound + ((double)p) * LookupTableStepSize;
                double outputValue = Calculate(new List<double> { inputValue });
                LookupTable[p] = outputValue;
            }
            CurrentlyCreatingLookupTable = false;
        }

        public void ClearLookupTable()
        {
            LookupTable = null;
        }

        public double LookupFromLookupTable(List<double> lookupInputs)
        {
            double lookupValue = lookupInputs[0];
            if (lookupValue < Decision.StrategyBounds.LowerBound || lookupValue > Decision.StrategyBounds.UpperBound)
                return InterpolateOutputForPoint(lookupInputs);
            double stepNumUnrounded = (lookupValue - Decision.StrategyBounds.LowerBound) / (double)LookupTableStepSize;
            if (stepNumUnrounded < 0 || stepNumUnrounded > Decision.NumberPointsInLookupTable - 1)
                return InterpolateOutputForPoint(lookupInputs);
            // We use a weighted lookup because otherwise we get the same value over and over again, and this can mess up our algorithms.
            int floor = (int) Math.Floor(stepNumUnrounded);
            int ceiling = (int) Math.Ceiling(stepNumUnrounded);
            double weightOnFloor = (double)ceiling - stepNumUnrounded; // 1.0 if stepNumUnrounded == floor
            double weightOnCeiling = stepNumUnrounded - (double)floor;
            if (floor == ceiling)
                weightOnFloor = 1.0;
            double weightedLookup = weightOnFloor * LookupTable[floor] + weightOnCeiling * LookupTable[ceiling];
            return weightedLookup;
        }

        #endregion

        #region Scoring and calculation

        object nonlocalScoreLock = new object();
        public StatCollectorArray Scores
        {
            get
            {
                int numStatCollectors = 1 + Decision.SubsequentDecisionsToRecordScoresFor;
                if (UseThreadLocalScores)
                {
                    if (ThreadLocalScores == null)
                        ThreadLocalScores = new ThreadLocal<StatCollectorArray>(() => null);
                    if (ThreadLocalScores.Value == null)
                    {
                        ThreadLocalScores.Value = new StatCollectorArray();
                        ThreadLocalScores.Value.Initialize(numStatCollectors);
                    }
                    return ThreadLocalScores.Value;
                }
                else
                {
                    if (_Scores == null)
                    {
                        lock (nonlocalScoreLock)
                        {
                            if (_Scores == null)
                            {
                                _Scores = new StatCollectorArray();
                                _Scores.Initialize(numStatCollectors);
                            }
                            return _Scores;
                        }
                    }
                    return _Scores;
                }
            }
            set
            {
                if (UseThreadLocalScores)
                {
                    if (ThreadLocalScores == null)
                        ThreadLocalScores = new ThreadLocal<StatCollectorArray>(() => null);
                    ThreadLocalScores.Value = value;
                }
                else
                {
                    lock (nonlocalScoreLock)
                    {
                        _Scores = value;
                    }
                }
            }
        }

        object scoresLockObj = new object();
        public void AddScore(double theScore, double weightOfObservation, int? subsequentDecisionToStoreScoreFor = null)
        {
            int decisionToScore = 0 + (subsequentDecisionToStoreScoreFor ?? 0);
            int numStatCollectors = 1 + Decision.SubsequentDecisionsToRecordScoresFor;
            if (UseThreadLocalScores)
            {
                if (ThreadLocalScores.Value == null)
                {
                    ThreadLocalScores.Value = new StatCollectorArray();
                    ThreadLocalScores.Value.Initialize(numStatCollectors);
                }
                ThreadLocalScores.Value.StatCollectors[decisionToScore].Add(theScore, weightOfObservation);
            }
            else
            {
                if (!Scores.Initialized)
                    Scores.Initialize(numStatCollectors);
                //if (Scores == null)
                //{
                //    lock (scoresLockObj)
                //    {
                //        if (Scores == null)
                //        {
                //            Scores = new StatCollectorArray();
                //            Scores.Initialize(numStatCollectors);
                //        }
                //    }
                //}
                //retryhere:
                try 
                {
                    Scores.StatCollectors[decisionToScore].Add(theScore, weightOfObservation);
                }
                catch
                {
                    throw;
                    //goto retryhere;
                }
            }
        }

        public void ResetScores()
        {
            if (ThreadLocalScores != null && ThreadLocalScores.Value != null)
                ThreadLocalScores.Value = null;
            Scores = null;
        }

        object threadLocalLockObj = new object();
        public ThreadLocal<double?> ThreadLocalOverrideValue
        {
            get
            {
                if (_ThreadLocalOverrideValue == null)
                {
                    lock (threadLocalLockObj)
                    { // We must make sure that one thread does not replace the thread local variable created by another thread. Note that the thread local VALUE will differ across threads, but we must have a single ThreadLocal variable.
                        if (_ThreadLocalOverrideValue == null)
                            _ThreadLocalOverrideValue = new ThreadLocal<double?>(() => null);
                    }
                }
                return _ThreadLocalOverrideValue;
            }
            set
            {
                _ThreadLocalOverrideValue = value;
            }
        }

        public double Calculate(List<double> inputs, GameModule gameModuleToUseForDefaultDecision = null)
        {
            return CalculateHelper(inputs, gameModuleToUseForDefaultDecision) * ResultMultiplier + ResultIncrement;
        }

        private double CalculateHelper(List<double> inputs, GameModule gameModuleToUseForDefaultDecision = null)
        {
            if (GeneralOverrideValue != null)
                return (double)GeneralOverrideValue;

            if (ThreadLocalOverrideValue != null && ThreadLocalOverrideValue.Value != null)
            {
                return (double)ThreadLocalOverrideValue.Value;
            }

            if (DecisionReachedEnoughTimes && StrategyDevelopmentInitiated && (ActiveInputGroupPlus == null || ActiveInputGroupPlus.IStrategyComponent.InitialDevelopmentCompleted))
            {
                List<double> replacementInputs;
                if (UsingDefaultInputGroup)
                    replacementInputs = FilterInputsWhenUsingDefaultInputGroup(inputs.ToList());
                else
                    replacementInputs = inputs.ToList();
                if (Decision.ConvertOneDimensionalDataToLookupTable && Dimensions == 1 && !CurrentlyCreatingLookupTable && LookupTable != null)
                    return LookupFromLookupTable(replacementInputs);
                else
                    return InterpolateOutputForPoint(replacementInputs);
            }
            else
            {
                if (gameModuleToUseForDefaultDecision != null)
                    return gameModuleToUseForDefaultDecision.DefaultBehaviorBeforeEvolution(inputs, DecisionNumber);
                else
                    return (Decision.StrategyBounds.LowerBound + Decision.StrategyBounds.UpperBound) / 2.0;
            }
        }

        #endregion

        #region Input groups and dynamic inputs

        private void CreateDefaultInputGroup()
        {
            InputGroups = new List<InputGroup>();
            InputGroup theInputGroup = new InputGroup();
            theInputGroup.Name = Decision.Name;
            theInputGroup.Inputs = new List<InputInfo>();
            if (Decision.DynamicNumberOfInputs)
            {
                for (int dyn = 0; dyn < (int)DynamicDimensions; dyn++)
                {
                    if (!DimensionFilteredOut[dyn])
                        theInputGroup.Inputs.Add(new InputInfo() { InputName = "DynamicInput" + dyn.ToString(), SubGroups = null });
                }
            }
            else
            {
                foreach (var input in Decision.InputNames)
                    theInputGroup.Inputs.Add(new InputInfo() { InputName = input, SubGroups = null });
            }
            InputGroups.Add(theInputGroup);
            UsingDefaultInputGroup = true;
        }

        private void CreateInputGroupTree()
        {
            InputGroupTree = new List<InputGroupPlus>();
            foreach (InputGroup inputGroup in InputGroups)
            {
                InputGroupTree.Add(ConvertInputGroupToInputGroupPlus(inputGroup, null, InputGroupTree.ToList()));
            }
        }

        private InputGroupPlus ConvertInputGroupToInputGroupPlus(InputGroup inputGroup, InputInfoPlus parent, List<InputGroupPlus> earlierSiblings)
        {
            int dimensions = inputGroup.Inputs.Count;
            if (earlierSiblings != null && earlierSiblings.Any())
                dimensions++; // we will have an extra input, i.e. the output of the previous group
            IStrategyComponent strategyComponent;
            strategyComponent = EvolutionSettings.SmoothingOptions.GetStrategyComponent(this, dimensions, EvolutionSettings, Decision, inputGroup.Name);
            InputGroupPlus theInputGroupPlus = new InputGroupPlus() { Name = inputGroup.Name, IStrategyComponent = strategyComponent, EarlierSiblings = earlierSiblings, Parent = parent };
            theInputGroupPlus.Inputs = new List<InputInfoPlus>();
            foreach (InputInfo inputInfo in inputGroup.Inputs)
                theInputGroupPlus.Inputs.Add(ConvertInputInfoToInputInfoPlus(inputInfo, theInputGroupPlus));
            return theInputGroupPlus;
        }

        private InputInfoPlus ConvertInputInfoToInputInfoPlus(InputInfo inputInfo, InputGroupPlus containingInputGroupPlus)
        {
            InputInfoPlus theInputInfoPlus = new InputInfoPlus() { Group = containingInputGroupPlus, InputName = inputInfo.InputName };
            int index;
            if (Decision.DynamicNumberOfInputs)
            {
                index = Convert.ToInt32(inputInfo.InputName.Replace("DynamicInput", ""));
            }
            else
            {
                index = Decision.InputNames.FindIndex(x => x == inputInfo.InputName);
                if (index < 0)
                    throw new Exception("Input listed in InputGroup did not have a corresponding input in InputNames.");
            }
            theInputInfoPlus.Index = index;
            if (inputInfo.SubGroups != null && inputInfo.SubGroups.Any())
            {
                List<InputGroupPlus> subGroupPlusList = new List<InputGroupPlus>();
                foreach (InputGroup inputGroup in inputInfo.SubGroups)
                {
                    InputGroupPlus subGroupPlus = ConvertInputGroupToInputGroupPlus(inputGroup, theInputInfoPlus, subGroupPlusList.ToList());
                    subGroupPlusList.Add(subGroupPlus);
                }
                theInputInfoPlus.SubGroups = subGroupPlusList;
            }
            return theInputInfoPlus;
        }

        public double InterpolateOutputForPoint(List<double> inputs, InputGroupPlus stopWhenArrivingHere = null)
        {
            // We must interpolate the inputs taking into account the entire tree. Children must be interpolated before their parents.
            bool stopped;
            return InterpolateOutputForPointBySuccessivelyInterpolatingForEachInListOfInputGroupPlus(inputs, InputGroupTree, stopWhenArrivingHere, out stopped);
        }

        private double InterpolateOutputForPointBySuccessivelyInterpolatingForEachInListOfInputGroupPlus(List<double> inputs, List<InputGroupPlus> listInputGroupPlus, InputGroupPlus stopWhenArrivingHere, out bool stopped)
        {
            stopped = false;
            double? outputSoFar = null;
            foreach (InputGroupPlus inputGroupPlus in listInputGroupPlus)
            {
                if (stopWhenArrivingHere == inputGroupPlus) // abort early -- this should happen only when the inputGroupPlus has earlier siblings and there is thus an output to return
                {
                    stopped = true;
                    return (double)outputSoFar;
                }
                if (inputGroupPlus == ActiveInputGroupPlus && OverrideValueForActiveInputGroupPlus.Value != null)
                    outputSoFar = (double)OverrideValueForActiveInputGroupPlus.Value;
                else if (inputGroupPlus.IStrategyComponent.InitialDevelopmentCompleted)
                    outputSoFar = InterpolateOutputForPointForSingleInputGroupPlus(inputs, inputGroupPlus, outputSoFar, stopWhenArrivingHere);
            }
            return (double) outputSoFar;
        }

        private double InterpolateOutputForPointForSingleInputGroupPlus(List<double> inputs, InputGroupPlus inputGroupPlus, double? firstReplacementInput, InputGroupPlus stopWhenArrivingHere)
        {
            bool stopped;
            List<double> replacementInputs = PrepareInputs(inputs, inputGroupPlus, firstReplacementInput, stopWhenArrivingHere, out stopped);
            if (inputs.Count != replacementInputs.Count)
            {
                throw new Exception("The expected number of inputs for a decision was not received.");
            }
            if (stopped)
                return replacementInputs.Last(); // no more interpolating to do -- it's the last replacement input we want
            return inputGroupPlus.IStrategyComponent.CalculateOutputForInputs(replacementInputs);
        }

        private List<double> PrepareInputs(List<double> inputs, InputGroupPlus inputGroupPlus, double? firstReplacementInput, InputGroupPlus stopWhenArrivingHere, out bool stopped)
        {
            if (UsingDefaultInputGroup)
            {
                stopped = false;
                return inputs; // the inputs should be pre-filtered, and do not need any other preparation
            }
            if (inputGroupPlus == null)
            {
                stopped = false;
                return inputs == null ? null : inputs.ToList(); // we have not yet completed some preliminary steps (e.g., during ProcessDynamicInputs)
            }
            List<double> replacementInputs = new List<double>();
            if (firstReplacementInput != null)
                replacementInputs.Add((double)firstReplacementInput); // This is the output from the most recent input group processed
            stopped = false;
            foreach (InputInfoPlus inputInfoPlus in inputGroupPlus.Inputs)
            {
                double inputToAdd;
                if (inputInfoPlus.SubGroups == null || inputInfoPlus.SubGroups.Count == 0 || !inputInfoPlus.SubGroups[0].IStrategyComponent.InitialDevelopmentCompleted) // we need to look up the index in our list of indices
                    inputToAdd = inputs[inputInfoPlus.Index];
                else // we need to substitute a value fr
                    inputToAdd = InterpolateOutputForPointBySuccessivelyInterpolatingForEachInListOfInputGroupPlus(inputs, inputInfoPlus.SubGroups, stopWhenArrivingHere, out stopped);
                replacementInputs.Add(inputToAdd);
                if (stopped)
                    return replacementInputs;
            }
            return replacementInputs;
        }

        ThreadLocal<double?> OverrideValueForActiveInputGroupPlus
        {
            get
            {
                if (_overrideValueForActiveInputGroupPlus == null)
                    _overrideValueForActiveInputGroupPlus = new ThreadLocal<double?>(() => null);
                return _overrideValueForActiveInputGroupPlus;
            }
            set
            {
                _overrideValueForActiveInputGroupPlus = value;
            }
        }


        bool CurrentlyPreSerializing = false; // prevents double predeserialization from previous version of strategy, version before previous
        public virtual void PreSerialize()
        {
            if (CurrentlyPreSerializing)
                return;
            CurrentlyPreSerializing = true;
            if (DecisionReachedEnoughTimes && !Decision.DummyDecisionRequiringNoOptimization)
                foreach (InputGroupPlus igp in InputGroupTree)
                    igp.PreSerialize();
            if (_previousVersionOfThisStrategy != null)
                _previousVersionOfThisStrategy.PreSerialize();
            if (_versionOfStrategyBeforeThat != null)
                _versionOfStrategyBeforeThat.PreSerialize();
            CurrentlyPreSerializing = false;
        }

        bool CurrentlyPostDeserializing = false;
        public virtual void UndoPreSerialize()
        {
            if (CurrentlyPostDeserializing)
                return;
            CurrentlyPostDeserializing = true;
            if (DecisionReachedEnoughTimes && !Decision.DummyDecisionRequiringNoOptimization)
                foreach (InputGroupPlus igp in InputGroupTree)
                    igp.UndoPreSerialize();
            if (_previousVersionOfThisStrategy != null)
                _previousVersionOfThisStrategy.UndoPreSerialize();
            if (_versionOfStrategyBeforeThat != null)
                _versionOfStrategyBeforeThat.UndoPreSerialize();
            CurrentlyPostDeserializing = false;
        }


        private void ProcessDynamicInputs()
        {
            if (Decision.DynamicNumberOfInputs)
            {
                if (PresmoothingValuesComputedEarlier)
                {
                    Strategy previousStrategy = AllStrategies[GetPreviousNonDummyStrategyDecisionNumber()];
                    FilteringTemporarilyDisabled = previousStrategy.FilteringTemporarilyDisabled;
                    DynamicDimensions = previousStrategy.DynamicDimensions;
                    DimensionFilteredOut = previousStrategy.DimensionFilteredOut.ToArray();
                    MinObservedInputs = previousStrategy.MinObservedInputs.ToArray();
                    MaxObservedInputs = previousStrategy.MaxObservedInputs.ToArray();
                    NumFilteredDimensions = previousStrategy.NumFilteredDimensions;
                    FilteredInputsStatistics = previousStrategy.FilteredInputsStatistics; // can't currently deep copy but shouldn't be a problem
                    return;
                }
                const int minSuccesses = 50;
                FilteringTemporarilyDisabled = true;
                List<double[]> decisionInputs = GetSampleDecisionInputs(minSuccesses);
                FilteringTemporarilyDisabled = false;
                DynamicDimensions = decisionInputs[0].Length;
                if (decisionInputs.Any(x => x.Length != DynamicDimensions))
                    throw new Exception("Dynamic dimensions means that the number of dimensions may change from one evolutionary cycle to another, but the number must be constant within an evolutionary cycle.");
                DimensionFilteredOut = new bool[(int)DynamicDimensions];
                NumFilteredDimensions = 0;
                FilteredInputsStatistics = new StatCollectorArray();
                foreach (var decisionInput in decisionInputs)
                    FilteredInputsStatistics.Add(decisionInput);
                string sameOrDifferent = "";
                MinObservedInputs = FilteredInputsStatistics.StatCollectors.Select(x => x.Min).ToArray();
                MaxObservedInputs = FilteredInputsStatistics.StatCollectors.Select(x => x.Max).ToArray();
                for (int d = 0; d < DynamicDimensions; d++)
                {
                    // double firstValue = NumberPrint.RoundToSignificantFigures(decisionInputs[0][d]);
                    // DimensionFilteredOut[d] = decisionInputs.All(x => NumberPrint.RoundToSignificantFigures(x[d]) == firstValue); // if they're all equal, we filter it out
                    double sd = FilteredInputsStatistics.StatCollectors[d].StandardDeviation();
                    bool previousDimensionIdentical = false; // for example, P and D trial expenses may be identical if there is only a common component to them
                    for (int d2 = 0; d2 < d; d2++)
                        if (FilteredInputsStatistics.StatCollectors[d2].Average() == FilteredInputsStatistics.StatCollectors[d].Average() && FilteredInputsStatistics.StatCollectors[d2].StandardDeviation() == FilteredInputsStatistics.StatCollectors[d].StandardDeviation())
                            previousDimensionIdentical = true;
                        DimensionFilteredOut[d] = sd == 0 || double.IsNaN(sd) || previousDimensionIdentical;
                    if (DimensionFilteredOut[d])
                    {
                        NumFilteredDimensions++;
                        sameOrDifferent += "S";
                    }
                    else
                        sameOrDifferent += "D";
                }
                if (NumFilteredDimensions > 0)
                    TabbedText.WriteLine("Number dimensions for which value is same (and thus not counted as a dynamic input): " + NumFilteredDimensions + " out of " + DynamicDimensions + " (" + sameOrDifferent + ")");
            }
        }

        private bool FilteringTemporarilyDisabled = false; // set to true only when trying to determine how to filter
        public List<double> FilterInputsWhenUsingDefaultInputGroup(List<double> inputs)
        {
            List<double> replacementInputs;
            if (inputs == null || !Decision.DynamicNumberOfInputs || Dimensions == inputs.Count || FilteringTemporarilyDisabled)
                replacementInputs = inputs;
            else
            {
                replacementInputs = new List<double>();
                for (int d = 0; d < DynamicDimensions; d++)
                {
                    try
                    {
                        if (!DimensionFilteredOut[d])
                            replacementInputs.Add(inputs[d]);
                    }
                    catch
                    {
                        throw new Exception("Internal exception; possible problem 1: Running in report-only mode with different game settings from those that were used to generate the saved genomes.; possible problem 2: You may be using TestInputs with the wrong number of dimensions; check whether this is the case by looking at DevelopStrategyStandard in the call stack. Possible problem 3: The input abbreviations and names do not match the number of actual inputs.");
                    }
                }
            }
            return replacementInputs;
        }

        #endregion

        #region Oversampling and success replication

        bool DisableOversampling = false; 
        bool DisableSuccessReplication = true; 
        bool AllowSuccessReplicationWithoutOversampling { get { return DisableOversampling && !DisableSuccessReplication; } }

        private void DevelopOversamplingPlan(out bool decisionReachedEnoughTimes)
        {
            decisionReachedEnoughTimes = false; // initialize
            if ((!AllowSuccessReplicationWithoutOversampling || DisableSuccessReplication) && (!Decision.UseOversampling || DisableOversampling))
            {
                decisionReachedEnoughTimes = true;
                OversamplingPlanDuringOptimization = new OversamplingPlan();
                return;
            }
            bool mustNarrowOversamplingPlan;
            int? mostRecentSuperset;
            bool supersetReachedEnoughTimes;
            GetPreNarrowingOversamplingPlan(out mostRecentSuperset, out mustNarrowOversamplingPlan, out supersetReachedEnoughTimes); // if we can use the previous oversampling plan, we will copy it in
            //bool usingSupersetFromBeforeLastCumulativeDistribution = DecisionNumber > 0 && mostRecentSuperset != null && mostRecentSuperset < AllStrategies[DecisionNumber - 1].LastCumulativeDistributionDecisionNumber;
            bool supersetDecisionUsesSuccessReplication = mostRecentSuperset != null && AllStrategies[(int)mostRecentSuperset].SuccessReplicationTriggered;
            SuccessReplicationTriggered = supersetDecisionUsesSuccessReplication;
            if (mustNarrowOversamplingPlan && !supersetDecisionUsesSuccessReplication)
            { // superset decision, if any, does not use success replication
                OversamplingPlanDuringOptimization = new OversamplingPlan(); // must start from scratch, b/c the current version of SplitBasedOnSamples does not account for the weights. That would be an improvement, esp. if this takes a while.
                double successPerAttemptRatio;
                List<double[]> inputsList = GetSampleGameInputs(EvolutionSettings.SmoothingOptions.SizeOfOversamplingSample, out successPerAttemptRatio, null); // Note that this will affect IterationsWhereDecisionIs(Not)Reached
                if (inputsList.Count > 0)
                {
                    decisionReachedEnoughTimes = true;
                    if (supersetDecisionUsesSuccessReplication)
                        TabbedText.WriteLine("Superset decision being narrowed involved success replication.");
                    if (!DisableOversampling)
                    { 
                        OversamplingPlanDuringOptimization = new OversamplingPlan(); // create a fresh oversampling plan. even though we are narrowing, it may make sense to do so at the high levels of the hierarchy.
                        OversamplingPlanDuringOptimization.SplitBasedOnSamples(inputsList, successPerAttemptRatio);
                    }
                    int sampleSize = EvolutionSettings.SmoothingOptions.SizeOfOversamplingSample;
                    if (DisableOversampling)
                        sampleSize = EvolutionSettings.SmoothingOptions.SizeOfSuccessReplicationSample;
                    inputsList = GetSampleGameInputs(sampleSize, out successPerAttemptRatio, null);
                    // if the attempt per success ratio is too low, then we need to trigger success replication. We'll start with the IterationsWhereDecisionIsReached that we've already generated and replicate them.
                    if (successPerAttemptRatio != 0 && successPerAttemptRatio < Decision.SuccessReplicationIfSuccessAttemptRatioIsBelowThis && !DisableSuccessReplication)
                    {
                        //if (DisableSuccessReplication)
                        //{
                        //    decisionReachedEnoughTimes = false;
                        //    SuccessReplicationTriggered = false;
                        //    TabbedText.WriteLine("This decision was not reached enough times, so further development of the decision was skipped.");
                        //}
                        //else
                        InitiateSuccessReplication(null, true, ref decisionReachedEnoughTimes);
                    }
                    else
                    {
                        SuccessReplicationTriggered = false;
                        if (DisableSuccessReplication)
                            TabbedText.WriteLine("Success replication disabled. Attempts per 1000 successes: " + (1000.0 * (1.0 / successPerAttemptRatio)));
                        else
                            TabbedText.WriteLine("No success replication needed after narrowing. Attempts per 1000 successes: " + (1000.0 * (1.0 / successPerAttemptRatio)));
                    }
                }
                else
                {
                    decisionReachedEnoughTimes = false;
                    SuccessReplicationTriggered = false;
                    TabbedText.WriteLine("This decision was not reached, so further development of the decision was skipped.");
                }
            }
            else
            {
                if (supersetDecisionUsesSuccessReplication)
                {
                    if (supersetReachedEnoughTimes)
                    {
                        TabbedText.WriteLine("Triggering success replication based on superset decision.");
                        InitiateSuccessReplication(mostRecentSuperset, !mustNarrowOversamplingPlan, ref decisionReachedEnoughTimes);
                    }
                    else
                    {
                        TabbedText.WriteLine("The superset decision was skipped, so this is also being skipped.");
                        decisionReachedEnoughTimes = false;
                        SuccessReplicationTriggered = false;
                    }
                }
                else
                { // superset decision does not use success replication, and we do not need to narrow the prior decision.
                    SuccessReplicationTriggered = false;
                    decisionReachedEnoughTimes = supersetReachedEnoughTimes;
                    if (decisionReachedEnoughTimes)
                        TabbedText.WriteLine("The superset oversampling decision, which did not use success replication, is being used unchanged.");
                    else
                        TabbedText.WriteLine("The superset decision was skipped, so this too is being skipped.");

                }
            }
            if (!DisableOversampling)
                TabbedText.WriteLine("Oversampling plan: " + OversamplingPlanDuringOptimization.ToString());
        }

        private int GetPreviousNonDummyStrategyDecisionNumber()
        {
            Strategy previousStrategy = null;
            int previousN = 1;
            previousStrategy = AllStrategies[DecisionNumber - previousN];
            while (previousStrategy.Decision.DummyDecisionRequiringNoOptimization)
            {
                previousN++;
                previousStrategy = AllStrategies[DecisionNumber - previousN];
            }
            return DecisionNumber - previousN;
        }

        private int? GetMostRecentSupersetDecision()
        {
            if (DecisionNumber == 0)
                return null;
            if (Decision.OversamplingWillAlwaysBeSameAsPreviousDecision)
                return GetPreviousNonDummyStrategyDecisionNumber();
            bool neverBaseOversamplingOnPreviousDecision = false; 
            if (neverBaseOversamplingOnPreviousDecision)
                return null;
            for (int d = DecisionNumber - 1; d >= 0; d--)
            {
                if ((AllStrategies[d].Decision.UseOversampling && !DisableOversampling) && !AllStrategies[d].Decision.MustBeInEquilibriumWithNextDecision && !AllStrategies[d].Decision.MustBeInEquilibriumWithPreviousDecision && !AllStrategies[d].Decision.MustBeInEquilibriumWithPreviousAndNextDecision && AllStrategies[d].IterationsWhereDecisionIsNotReached != null)
                {
                    OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = AllStrategies[d].OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
                    // Check strategy d's iterations that were failures, i.e. where that decision was not reached.
                    bool atLeastOneDecisionReachedThoughNotReachedBefore = false;
                    // we'll count as a success a decision that shows us that d is NOT is a superset, because a decision that d did not reach IS reached here
                    if (AllStrategies[d].IterationsWhereDecisionIsNotReached.Any())
                        Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, AllStrategies[d].IterationsWhereDecisionIsNotReached.Count, (indexIntoIterations) =>
                            {
                                if (!atLeastOneDecisionReachedThoughNotReachedBefore) // once one is reached, we can skip the work (an improvement would be to add a cancellation token)
                                {
                                    bool decisionReached;
                                    GameProgress preplayedGameProgressInfo;
                                    List<double> inputsThisIteration = GetDecisionInputsForIteration(AllStrategies[d].IterationsWhereDecisionIsNotReached[indexIntoIterations], 1000000, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                                    if (decisionReached)
                                        atLeastOneDecisionReachedThoughNotReachedBefore = true;
                                }
                            });
                    if (!atLeastOneDecisionReachedThoughNotReachedBefore)
                        return d;
                    // otherwise, this is not a subset of decision d, because there are some occasions where we will reach a decision for DecisionNumber,
                    // even though we did not reach one for d. As a result, we can't use that oversampling plan.
                }
            }
            return null;
        }

        private bool DetermineWhetherToUseSameOversamplingPlanAsSupersetDecisionWithoutNarrowing(int decisionNumOfSuperset)
        {
            // We can use the same oversampling plan only if, wherever the superset decision reaches a decision,
            // we do too. Otherwise, we'll need to narrow the oversampling plan.
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = AllStrategies[decisionNumOfSuperset].OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
            bool atLeastOneDecisionNotReachedThoughReachedBefore = false;
            // we'll count as a success a decision that shows that we CAN'T simply duplicate decisionNumOfSuperset (but instead must narrow it),
            // because a decision that d did reach is NOT reached here
            if (AllStrategies[decisionNumOfSuperset].IterationsWhereDecisionIsReached != null && AllStrategies[decisionNumOfSuperset].IterationsWhereDecisionIsReached.Any())
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, AllStrategies[decisionNumOfSuperset].IterationsWhereDecisionIsReached.Count, (indexIntoIterations) =>
                {
                    if (!atLeastOneDecisionNotReachedThoughReachedBefore) // if we have already found one decision not reached though reached before, we don't keep doing the work (but we don't abort the loop)
                    {
                        bool decisionReached;
                        GameProgress preplayedGameProgressInfo;
                        List<double> inputsThisIteration = GetDecisionInputsForIteration(AllStrategies[decisionNumOfSuperset].IterationsWhereDecisionIsReached[indexIntoIterations], 1000000, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                        if (!decisionReached)
                            atLeastOneDecisionNotReachedThoughReachedBefore = true;
                    }
                });
            return !atLeastOneDecisionNotReachedThoughReachedBefore;
        }
        
        private void GetPreNarrowingOversamplingPlan(out int? mostRecentSuperset, out bool mustNarrowOversamplingPlan, out bool supersetDecisionReachedEnoughTimes)
        {
            supersetDecisionReachedEnoughTimes = true;
            mostRecentSuperset = GetMostRecentSupersetDecision();
            mustNarrowOversamplingPlan = true;
            if (mostRecentSuperset == null)
            {
                OversamplingPlanDuringOptimization = new OversamplingPlan();
                // This decision still might not be reached sometimes, so we need to see if we should narrow the oversampling plan.
            }
            else
            {
                TabbedText.WriteLine("Basing oversampling plan on decision " + (mostRecentSuperset).ToString());
                OversamplingPlanDuringOptimization = AllStrategies[(int)mostRecentSuperset].OversamplingPlanDuringOptimization.DeepCopy(null);
                supersetDecisionReachedEnoughTimes = AllStrategies[(int)mostRecentSuperset].DecisionReachedEnoughTimes;
                bool canUseSamePlan = AllStrategies[(int)mostRecentSuperset].IterationsWhereDecisionIsReached != null && AllStrategies[(int)mostRecentSuperset].IterationsWhereDecisionIsNotReached != null
                        && (Decision.OversamplingWillAlwaysBeSameAsPreviousDecision || DetermineWhetherToUseSameOversamplingPlanAsSupersetDecisionWithoutNarrowing((int)mostRecentSuperset));
                if (canUseSamePlan)
                {
                    IterationsWhereDecisionIsReached = AllStrategies[(int)mostRecentSuperset].IterationsWhereDecisionIsReached.ToList();
                    IterationsWhereDecisionIsNotReached = AllStrategies[(int)mostRecentSuperset].IterationsWhereDecisionIsNotReached.ToList();
                    mustNarrowOversamplingPlan = false;
                }
            }
        }

        private void InitiateSuccessReplication(int? supersetDecisionAlsoUsingSuccessReplication, bool successListAlreadyCreated, ref bool decisionSometimesReached)
        {
            Debug.WriteLine("Initiating success replication.");
            if (!successListAlreadyCreated)
            {
                if (supersetDecisionAlsoUsingSuccessReplication == null)
                    throw new Exception("Internal error: InitiateSuccessReplication should only create the success list where this is based on a prior decision.");
                double successPerAttemptRatio;
                if (EvolutionSettings.SmoothingOptions.StartSuccessReplicationFromScratch)
                {
                    SuccessReplicationTriggered = false; // because we need to generate iteration id's from scratch
                    OversamplingPlanDuringOptimization = new OversamplingPlan(); // must start from scratch, b/c we don't want to have success replication based on a biased sample
                    GetSampleGameInputs(EvolutionSettings.SmoothingOptions.SizeOfSuccessReplicationSample, out successPerAttemptRatio, null);
                    SuccessReplicationTriggered = true;
                }
                else
                    GetSampleGameInputs(EvolutionSettings.SmoothingOptions.SizeOfSuccessReplicationSample, out successPerAttemptRatio, supersetDecisionAlsoUsingSuccessReplication); // we are generating our sample from the superset decision also using success replication here, but this will generate a list of successes for this decision, and that list will be used to generate further IterationIDs during strategy optimization. That is, we use the old successes to find some new successes (since they might be hard to find), but then we can forget about the old successes, except insofar as they are already encoded in the IterationID.
                if (successPerAttemptRatio == 0)
                {
                    TabbedText.WriteLine("This decision was  not reached, so further development of the decision was skipped and initiation of success replication was aborted.");
                    SuccessReplicationTriggered = false;
                    decisionSometimesReached = false;
                    return;
                }
            }
            SuccessReplicationTriggered = true;
            if (supersetDecisionAlsoUsingSuccessReplication == null || !successListAlreadyCreated)
                DetermineWhichInputSeedIndicesAffectSuccess();
            else if (supersetDecisionAlsoUsingSuccessReplication != null)
            {
                KeepSourceGameInputSeedIndexDuringSuccessReplication = AllStrategies[(int)supersetDecisionAlsoUsingSuccessReplication].KeepSourceGameInputSeedIndexDuringSuccessReplication.ToArray();
                decisionSometimesReached = AllStrategies[(int)supersetDecisionAlsoUsingSuccessReplication].DecisionReachedEnoughTimes;
                // TabbedText.WriteLine("Copied success replication settings: " + String.Join(",", KeepSourceGameInputSeedIndexDuringSuccessReplication));
            }
            else if (KeepSourceGameInputSeedIndexDuringSuccessReplication == null)
                throw new Exception("Internal error here.");
            NextIterationDuringSuccessReplication = GetMaxIterationAlreadyProducingSuccess() + 1;
        }

        private long GetMaxIterationAlreadyProducingSuccess()
        {
            return IterationsWhereDecisionIsReached.Max(x => x.MaxIterationNum());
        }

        private void DetermineWhichInputSeedIndicesAffectSuccess()
        {
            bool[] newKeepSourceGameInputSeedIndexDuringSuccessReplication = new bool[NumGameInputSeedIndices];
            for (int inputSeedIndex = 0; inputSeedIndex < NumGameInputSeedIndices; inputSeedIndex++)
            {
                // Set KeepSourceGameInputSeedIndexDuringSuccessReplication so that we will keep the source game inputs for all but the input seed index we are considering. The goal is to see whether changing the one that we are considering has the potential to affect success.
                KeepSourceGameInputSeedIndexDuringSuccessReplication = new bool[NumGameInputSeedIndices];
                for (int innerInputSeedIndex = 0; innerInputSeedIndex < NumGameInputSeedIndices; innerInputSeedIndex++)
                    KeepSourceGameInputSeedIndexDuringSuccessReplication[innerInputSeedIndex] = inputSeedIndex != innerInputSeedIndex;

                const int numResultsSameBeforeWeDetermineNoEffect = 1000; // If this many times, a randomly selected previous success and a randomly selected previous failure lead to the same result when we're changing the input seed at this index, then we conclude that this input seed index does not have any effect on whether we are successful in reaching this decision, so this input seed index can be replaced and randomly generated. Even if it does have an effect in unusual circumstances, that won't be very harmful; we'll just have a failure when trying to generate an iteration, which happens often without success replication anyway.
                bool atLeastOneResultChanged = false;
                long nextIterationNumberToTest = GetMaxIterationAlreadyProducingSuccess() + 1;
                int testNumber = 0;

                OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
                while (!atLeastOneResultChanged && testNumber < numResultsSameBeforeWeDetermineNoEffect)
                {
                    // take a random success and failure
                    IterationID idOfCurrentSuccess = IterationsWhereDecisionIsReached[testNumber % IterationsWhereDecisionIsReached.Count];
                    bool countFailureTurningToSuccessAsChangedResult = false;
                    IterationID idOfCurrentFailure = null;
                    if (countFailureTurningToSuccessAsChangedResult && IterationsWhereDecisionIsNotReached.Count > 0)
                        idOfCurrentFailure = IterationsWhereDecisionIsNotReached[testNumber % IterationsWhereDecisionIsNotReached.Count];
                    const int totalNumIterationsToClaim = 1000000; // currently, this doesn't matter in the code, in fact because success replication and more generally the possibility of not reaching a decision makes it impossible to evenly spread out the game inputs anyway, and the total number iterations was specified to enable such spreading
                    bool decisionReached;
                    IterationIDComposite successModified = new IterationIDComposite(nextIterationNumberToTest, idOfCurrentSuccess, KeepSourceGameInputSeedIndexDuringSuccessReplication);
                    GameProgress preplayedGameProgressInfo;
                    GetDecisionInputsForIteration(successModified, totalNumIterationsToClaim, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                    if (!decisionReached)
                        atLeastOneResultChanged = true;
                    else if (idOfCurrentFailure != null)
                    {
                        IterationIDComposite failureModified = new IterationIDComposite(nextIterationNumberToTest, idOfCurrentFailure, KeepSourceGameInputSeedIndexDuringSuccessReplication);
                        GetDecisionInputsForIteration(failureModified, totalNumIterationsToClaim, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                        if (decisionReached)
                            atLeastOneResultChanged = true;
                    }

                    testNumber++;
                    nextIterationNumberToTest++;
                }

                newKeepSourceGameInputSeedIndexDuringSuccessReplication[inputSeedIndex] = atLeastOneResultChanged; // if at least one result has changed, then we want to keep the original source game input seed index since that input seed index is one that determines success in reaching the decision up to this point
            }
            KeepSourceGameInputSeedIndexDuringSuccessReplication = newKeepSourceGameInputSeedIndexDuringSuccessReplication;
            // TabbedText.WriteLine(String.Join(",", KeepSourceGameInputSeedIndexDuringSuccessReplication));
        }

        public IterationID GenerateIterationID(long preliminaryUnadjustedIterationNum, int? supersetDecisionToUseForInitialSuccessReplication = null)
        {
            bool useSuccessReplication = supersetDecisionToUseForInitialSuccessReplication != null || SuccessReplicationTriggered;
            if (useSuccessReplication)
            {
                int indexToIterationIDToCopy;
                long iterationNumberToSuperimpose;

                Strategy strategyToUse;
                if (supersetDecisionToUseForInitialSuccessReplication == null)
                    strategyToUse = this;
                else
                    strategyToUse = AllStrategies[(int)supersetDecisionToUseForInitialSuccessReplication];
                // just pick a random one to use, so that we ensure consistent parallelism (i.e., we don't want the result to depend on order of request, other than to the extent that iterationId always depends on the iteration number in a parallel loop)
                indexToIterationIDToCopy = (int) (FastPseudoRandom.GetRandom(preliminaryUnadjustedIterationNum, 211 /* arbitrary */) * ((double) (IterationsWhereDecisionIsReached.Count - 1)));
                iterationNumberToSuperimpose = Interlocked.Increment(ref strategyToUse.NextIterationDuringSuccessReplication);
                IterationID source = strategyToUse.IterationsWhereDecisionIsReached[indexToIterationIDToCopy];
                return new IterationIDComposite(iterationNumberToSuperimpose, source, strategyToUse.KeepSourceGameInputSeedIndexDuringSuccessReplication.ToArray());
            }
            else
                return new IterationID(preliminaryUnadjustedIterationNum);
        }
        
        public List<double[]> GetSampleGameInputs(int numberSamplesToGet, out double successPerAttempt, int? supersetDecisionToUseForInitialSuccessReplication = null)
        {
            ConcurrentBag<double[]> inputs = new ConcurrentBag<double[]>();
            ConcurrentBag<IterationID> successes = new ConcurrentBag<IterationID>();
            ConcurrentBag<IterationID> failures = new ConcurrentBag<IterationID>();
            int totalSuccesses = 0;
            int totalAttempts = 0;
            // We must be building on the existing plan (whether that's from scratch or not)
           
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, numberSamplesToGet, (successNum, iterationNum) =>
            {
                bool decisionReached;
                OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = true, StoreWeightsForAdjustmentOfScoreAverages = false };
                IterationID iterationID = GenerateIterationID(iterationNum, supersetDecisionToUseForInitialSuccessReplication);
                GameProgress preplayedGameProgressInfo;
                List<double> inputsThisIteration = GetDecisionInputsForIteration(iterationID, numberSamplesToGet, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    inputs.Add(oversamplingInfo.ReturnedInputSeeds);
                    Interlocked.Increment(ref totalSuccesses);
                    successes.Add(iterationID);
                }
                else
                    failures.Add(iterationID);
                Interlocked.Increment(ref totalAttempts);
                return decisionReached;
            }
            , 0, EvolutionSettings.SmoothingOptions.SkipIfSuccessAttemptRateFallsBelow);
            IterationsWhereDecisionIsReached = successes.OrderBy(x => x.MaxIterationNum()).ToList();
            IterationsWhereDecisionIsNotReached = failures.OrderBy(x => x.MaxIterationNum()).Take(EvolutionSettings.SmoothingOptions.MaxFailuresToRemember).ToList();
            bool abort = IterationsWhereDecisionIsReached.Count() < numberSamplesToGet;
            if (abort)
                TabbedText.WriteLine("Aborted ... too few successes.");
            // TabbedText.WriteLine("Total attempts: " + totalAttempts);
            List<double[]> inputsList = inputs.ToList();
            if (numberSamplesToGet == 0 || totalAttempts == 0 || abort || !IterationsWhereDecisionIsReached.Any())
                successPerAttempt = 0; // set to lower bound
            else
                successPerAttempt = (double)numberSamplesToGet / (double) totalAttempts;
            if (inputsList.Count > 0)
                NumGameInputSeedIndices = inputsList[0].Length;
            return inputsList;
        }

        public List<double[]> GetSampleDecisionInputs(int numSamplesToGet)
        {
            ConcurrentBag<double[]> inputs = new ConcurrentBag<double[]>();
            // We must be building on the existing plan (whether that's from scratch or not)
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, numSamplesToGet, (successNum, iterationNum) =>
            {
                bool decisionReached;
                OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OversamplingPlanDuringOptimization ?? new OversamplingPlan(), StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
                IterationID iterationID = GenerateIterationID(iterationNum); // GetSampleDecisionInputs is and must be called after success replication is completed.
                GameProgress preplayedGameProgressInfo;
                List<double> inputsThisIteration = GetDecisionInputsForIteration(iterationID, numSamplesToGet, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                    inputs.Add(inputsThisIteration.ToArray());
                return decisionReached;
            }, 0, 0.001);
            List<double[]> inputsList = inputs.ToList();
            return inputsList;
        }

        #endregion

        #region Cumulative distributions

        private void SetCumulativeDistributions()
        {
            if (Decision.UpdateCumulativeDistributionsWhenOptimizing)
            {
                const int numPointsInCumulativeDistribution = 51; 
                const int numGamePlaysToBuildCumulativeDistribution = 1001; 
                const int minNumberSuccessesToReplaceExistingCumulativeDistribution = 40;
                ConcurrentBag<Tuple<List<double?>,double>> varsToGetCumDistsOfWithOversamplingWeights = new ConcurrentBag<Tuple<List<double?>,double>>();
                bool abort = false;
                // We must be building on the existing plan (whether that's from scratch or not)
                Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, numGamePlaysToBuildCumulativeDistribution, (successNum, iterationNum) =>
                {
                    if (!abort)
                    {
                        bool decisionReached;
                        OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; 
                        IterationID iterationID = GenerateIterationID(iterationNum); // GetSampleDecisionInputs is and must be called after success replication is completed.
                        GameProgress preplayedGameProgressInfo;
                        List<double> inputsThisIteration = GetDecisionInputsForIteration(iterationID, numGamePlaysToBuildCumulativeDistribution, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo, useNextDecisionNumber: true /* We want to see if we get to the decision after this update module, i.e. past the previous decision */);
                        if (decisionReached)
                        {
                            double returnedOversamplingWeight = oversamplingInfo.GetWeightForObservation(0); // If we always use strict backwards induction, this is likely to always be zero
                            List<double?> values = preplayedGameProgressInfo.GetVariablesToTrackCumulativeDistributionsOf();
                            if (values == null)
                                abort = true;
                            else
                                varsToGetCumDistsOfWithOversamplingWeights.Add(new Tuple<List<double?>,double>(values, returnedOversamplingWeight));
                        }
                        return decisionReached;
                    }
                    else
                        return true; // go through loop without doing anything
                });
                if (!abort && varsToGetCumDistsOfWithOversamplingWeights.Any())
                {
                    List<Tuple<List<double?>,double>> varsToGetCumDistsOfWithOversamplingWeights2 = varsToGetCumDistsOfWithOversamplingWeights.ToList();
                    int numValues = varsToGetCumDistsOfWithOversamplingWeights2[0].Item1.Count;
                    if (CumulativeDistributions == null)
                        CumulativeDistributions = new CumulativeDistribution[numValues];
                    // convert this to a list of lists, where the outer list is the index within varsList and the inner list is all of the items for that index.
                    for (int v = 0; v < numValues; v++)
                    {
                        int numFound = 0;
                        WeightedPercentileTracker wpt = new WeightedPercentileTracker();
                        foreach (Tuple<List<double?>,double> valuesWithWeight in varsToGetCumDistsOfWithOversamplingWeights2)
                        {
                            double? value = valuesWithWeight.Item1[v];
                            if (value != null)
                            {
                                wpt.AddItem((double) value, valuesWithWeight.Item2);
                                numFound++;
                            }
                        }
                        if (numFound < minNumberSuccessesToReplaceExistingCumulativeDistribution)
                        {

                            Debug.WriteLine("Too little found to change cumulative distribution.");
                            if (CumulativeDistributions[v] == null)
                            { // find a cumulative distribution defined earlier
                                for (int d = DecisionNumber - 1; d >= 0; d--)
                                {
                                    var cds = AllStrategies[d].CumulativeDistributions;
                                    if (cds != null)
                                    {
                                        if (cds[v] != null)
                                        {
                                            CumulativeDistributions[v] = new CumulativeDistribution(cds[v].StoredPoints.Count(), cds[v].StoredPoints.ToList());
                                            break;
                                        }
                                    }
                                }
                                if (CumulativeDistributions[v] == null) // still none?!?
                                    throw new Exception("No previous cumulative distribution found.");
                            }
                            // else --> we'll just keep the previously defined cumulative distribution
                        }
                        else
                        {
                            List<double> unweightedList = wpt.GetUnweightedList(numPointsInCumulativeDistribution);
                            CumulativeDistribution cd = new CumulativeDistribution(numPointsInCumulativeDistribution, unweightedList);
                            cd.Name = Decision.Name + "_" + v;
                            CumulativeDistributions[v] = cd;
                            TabbedText.WriteLine("Cumulative distribution " + (v) + " set: " + cd.ToString());
                        }
                    }
                }
            }
        }

        #endregion

        #region Consistency checking

        private void CheckProgressIntegrity()
        {
            bool checkProgressIntegrity = false;
            const int maxIterationsToCheckIntegrityOn = 2;
            if (checkProgressIntegrity && IterationsWhereDecisionIsReached != null)
            {
                var itersToCheck = IterationsWhereDecisionIsReached.Take(maxIterationsToCheckIntegrityOn).ToList();
                OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, itersToCheck.Count, i =>
                {
                    IterationID iter = itersToCheck[i];
                    Player.CheckProgressIntegrityForParticularIteration(DecisionNumber, 1000000, iter, SimulationInteraction, oversamplingInfo);
                }
                );
                if (Parallelizer.VerifyConsistentResults)
                    Player.CheckConsistencyForSetOfIterations(1000000, IterationsWhereDecisionIsReached, SimulationInteraction, oversamplingInfo);
            }
        }

        #endregion

        #region Game playing

        // The following control the interaction with the game player and are called by the IStrategyComponent currently being developed.

        public List<double> GetDecisionInputsForIteration(IterationID iterationID, long totalNumIterations, OversamplingInfo oversamplingInfo, out bool decisionReached, out GameProgress preplayedGameProgressInfo, bool useNextDecisionNumber = false)
        {
            if (Player == null)
            {
                preplayedGameProgressInfo = null;
                decisionReached = false;
                return null;
            }
            List<double> inputs = Player.GetDecisionInputsForIteration(SimulationInteraction, DecisionNumber, iterationID, totalNumIterations, DecisionNumber, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
            List<double> replacementInputs;
            if (UsingDefaultInputGroup) // all inputs will be in the usual order
                replacementInputs = FilterInputsWhenUsingDefaultInputGroup(inputs);
            else
            {
                // We can't be using dynamic inputs, so we don't have to worry about filtering out dimensions.
                double? replacementInput = null;
                if (ActiveInputGroupPlus != null && ActiveInputGroupPlus.EarlierSiblings != null && ActiveInputGroupPlus.EarlierSiblings.Any())
                    replacementInput = InterpolateOutputForPoint(inputs, ActiveInputGroupPlus); // In this case, we don't get the final interpolation but the interpolation up to this point in the hierarchy, which would be the replacement input for this step.

                bool stopped;
                replacementInputs = PrepareInputs(inputs, ActiveInputGroupPlus, replacementInput, null, out stopped);
            }
            return replacementInputs;
        }

        public double PlaySpecificValueForLargeNumberOfIterations(
            double? overrideValue,
            int numberOfIterations,
            long totalNumIterations)
        {
            bool useSpecificValueAsOverallOutputOfStrategy = false;
            if (UsingDefaultInputGroup || ActiveInputGroupPlus == null)
                useSpecificValueAsOverallOutputOfStrategy = true;
            else
                useSpecificValueAsOverallOutputOfStrategy = ActiveInputGroupPlus.Parent == null; // this is one of the top-level list of input groups. As a result, we don't need to take the output of this as an input into anything else.
            if (useSpecificValueAsOverallOutputOfStrategy)
            {
                double score = Player.PlaySpecificValueForLargeNumberOfIterations(SimulationInteraction, overrideValue, DecisionNumber, numberOfIterations, this, totalNumIterations, DecisionNumber);
                return score;
            }
            else
            {
                throw new NotImplementedException("Not implemented. See below for similar code if we want to allow this with multiple input groups.");
            }
        }

        public double PlaySpecificValueForLargeNumberOfIterations_FAIL(
            double? overrideValue,
            int numberOfIterations,
            long totalNumIterations,
            OversamplingInfo oversamplingInfo)
        {
            const int numIterationsPerSet = 100;
            int numberSets = numberOfIterations / numIterationsPerSet + 1;
            StatCollector sc = new StatCollector();
            const int maxSetsAtOnce = 50;
            List<Tuple<int, int>> ranges = new List<Tuple<int, int>>();
            int startingSet = 0;
            do
            {
                int endingSet = startingSet + maxSetsAtOnce;
                if (endingSet > numberSets)
                    endingSet = numberSets;
                ranges.Add(new Tuple<int, int>(startingSet, endingSet));
                startingSet = endingSet + 1;
            }
            while (startingSet <= numberSets);

            foreach (var range in ranges)
            {
                Parallelizer.Go(Player.DoParallel, range.Item1, range.Item2 + 1 /* last item in range will not be included */, s =>
                    {
                        int startingNumber = s * numIterationsPerSet;
                        int endingNumber = (s + 1) * numIterationsPerSet;
                        if (endingNumber > numberOfIterations - 1)
                            endingNumber = numberOfIterations - 1;

                        Debug.WriteLine("Range: " + range.Item1 + " --> " + range.Item2 + " subrange: " + startingNumber + " --> " + (endingNumber - 1));
                        int totalNumber = endingNumber - startingNumber + 1;
                        if (totalNumber >= 1)
                        {
                            double averageScore = PlaySpecificValueForSomeIterations(overrideValue, Enumerable.Range(startingNumber, totalNumber).Select(x => GenerateIterationID((long)x)).ToList(), totalNumIterations, oversamplingInfo);
                            sc.Add(averageScore, (double)totalNumber); // TODO: This is a problem, because it includes cases in which we don't have any that are reached.
                        }
                    }
                );
            }

            return sc.Average(); 
        }

        public double PlaySpecificValueForSomeIterations(
            double? overrideValue,
            List<IterationID> specificIterationsToPlay,
            long totalNumIterations,
            OversamplingInfo oversamplingInfo)
        {
            double[] scoresForSubsequentDecisions = null;
            return PlaySpecificValueForSomeIterations(overrideValue, specificIterationsToPlay, totalNumIterations, oversamplingInfo, out scoresForSubsequentDecisions);
        }

        public double PlaySpecificValueForSomeIterations(
            double? overrideValue,
            List<IterationID> specificIterationsToPlay,
            long totalNumIterations,
            OversamplingInfo oversamplingInfo,
            out double[] scoresForSubsequentDecisions)
        {

            bool useSpecificValueAsOverallOutputOfStrategy = false;
            if (UsingDefaultInputGroup || ActiveInputGroupPlus == null)
                useSpecificValueAsOverallOutputOfStrategy = true;
            else
                useSpecificValueAsOverallOutputOfStrategy = ActiveInputGroupPlus.Parent == null; // this is one of the top-level list of input groups. As a result, we don't need to take the output of this as an input into anything else.
            if (useSpecificValueAsOverallOutputOfStrategy)
            {
                double score = Player.PlaySpecificValueForSomeIterations(SimulationInteraction, overrideValue, DecisionNumber, specificIterationsToPlay, totalNumIterations, oversamplingInfo, DecisionNumber, out scoresForSubsequentDecisions);
                return score;
            }
            else
            {
                // We are not on the top level list of input groups. Thus, the ultimate value returned from this input group is being used as an input into an input group on the next higher level up. So, we need to have the player go through the usual process of interpolating each point. That will eventually lead to trying to get the output of this input group. At that point, we'll substitute the constant value.
                if (Decision.SubsequentDecisionsToRecordScoresFor > 0)
                    throw new NotImplementedException("We have not yet implemented support for recording subsequent scores when multiple levels of input groups are used.");
                scoresForSubsequentDecisions = null;
                OverrideValueForActiveInputGroupPlus.Value = overrideValue;
                double returnVal;
                returnVal = Player.PlayForSomeIterations(SimulationInteraction, DecisionNumber, specificIterationsToPlay, totalNumIterations, oversamplingInfo);
                OverrideValueForActiveInputGroupPlus.Value = null;
                return returnVal;
            }
        }

        /// <summary>
        /// Note: This is currently called only by NeuralNetworkStrategyComponent, which is not currently in use.
        /// </summary>
        /// <param name="totalNumIterations"></param>
        /// <param name="iterationsToGet"></param>
        /// <param name="gameInputsSet"></param>
        /// <param name="preplayedGames"></param>
        public void GetListOfGameInputsAndPreplayedGamesForSpecificIterations(long totalNumIterations, List<IterationID> iterationsToGet, out List<GameInputs> gameInputsSet, out List<GameProgress> preplayedGames)
        {
            Player.GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(DecisionNumber, totalNumIterations, iterationsToGet, SimulationInteraction, null, out gameInputsSet, out preplayedGames, DecisionNumber);
        }

        public List<List<double>> GetDecisionInputsForSpecificIterations(List<IterationID> iterationNumbers, long totalNumIterations)
        {
            return Player.GetDecisionInputsForSpecificIterations(SimulationInteraction, DecisionNumber, iterationNumbers, totalNumIterations, DecisionNumber);
        }

        public double PlaySpecificValuesForSpecificIterations(
            List<IterationID> specificIterationsToPlay,
            List<double> overrideValue,
            int totalNumIterations,
            List<GameInputs> gameInputsSet,
            List<GameProgress> preplayedGameProgressInfos,
            OversamplingInfo oversamplingInfo)
        {
            return Player.PlaySpecificValuesForSomeIterations(SimulationInteraction, overrideValue, DecisionNumber, specificIterationsToPlay, totalNumIterations, gameInputsSet, preplayedGameProgressInfos, oversamplingInfo, false, DecisionNumber);
        }


        public IEnumerable<GameProgress> PlayStrategy(
            Strategy strategy,
            List<GameProgress> preplayedGameProgressInfos,
            int numIterations,
            GameInputs[] gameInputsArray,
            IterationID[] iterationIDArray,
            bool returnCompletedGameProgressInfos = false)
        {
            // We are now playing the strategy as a whole. As a result, we do not yet need to do any translating of inputs.
            return Player.PlayStrategy(strategy, DecisionNumber, preplayedGameProgressInfos, numIterations, SimulationInteraction, gameInputsArray, iterationIDArray, returnCompletedGameProgressInfos, DecisionNumber);
        }

        #endregion


    }
}

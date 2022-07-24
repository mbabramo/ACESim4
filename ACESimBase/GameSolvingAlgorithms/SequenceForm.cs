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
using ACESim.Util;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using Rationals;
using JetBrains.Annotations;
using NumSharp.Utilities;
using ACESimBase.Games.EFGFileGame;
using Microsoft.Azure.Storage;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class SequenceForm : StrategiesDeveloperBase
    {
        public enum SequenceFormApproach
        {
            Gambit,
            ECTA
        }
        SequenceFormApproach Approach = SequenceFormApproach.ECTA;

        static object MostRecentEquilibrium; // MaybeExact<T>[]

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
            if (EvolutionSettings.SkipIfEquilibriumFileAlreadyExists && EquilibriaFileAlreadyExists())
                return;
            GameDefinition.MakeAllChanceDecisionsKnowAllChanceActions(); // since there is just one chance player, each chance (and resolution) player must know all other chance decisions for ECTA algorithm to work properly
            AllowSkipEveryPermutationInitialization = false;
            StoreGameStateNodesInLists = true;
            await base.Initialize();
            InitializeInformationSets();
            //PrintGameTree();
            if (Approach == SequenceFormApproach.ECTA)
            {
                SetFinalUtilitiesToRoundedOffValues(); // because this may be an exact algorithm that uses integral pivoting, we have only a discrete number of utility points. This ensures that the utilities are at the precise discrete points that correspond to the integral values we will use.
                EvolutionSettings.RecalculateScoreReachWhenCalculatingBestResponseImprovement = true; // since we're adjusting the utilities, we need to make sure that we recalculate the score range.
            }
            //PrintGameTree();
            if (!EvolutionSettings.CreateInformationSetCharts) // otherwise this will already have been run
                InformationSetNode.IdentifyNodeRelationships(InformationSets);
        }

        [SupportedOSPlatform("windows")]
        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {

            ReportCollection reportCollection = new ReportCollection();
            if (EvolutionSettings.SkipIfEquilibriumFileAlreadyExists && EquilibriaFileAlreadyExists())
                return reportCollection;

            string filename = null;
            if (EvolutionSettings.CreateEFGFileForSequenceForm)
                filename = CreateGambitFile();

            if (Approach == SequenceFormApproach.ECTA)
            {
                List<(double[] equilibrium, int frequency)> equilibria = new List<(double[] equilibrium, int frequency)>(), additionalEquilibria;
                TabbedText.WriteLine($"Using exact arithmetic for initial prior");
                if (!EvolutionSettings.ParallelOptimization)
                {
                    var centroidEquilibrium = DetermineEquilibria<ExactValue>(1).First(); // first equilibrium should always be accomplished with exact values
                    equilibria.Add(centroidEquilibrium);
                }
                if (EvolutionSettings.TryInexactArithmeticForAdditionalEquilibria)
                {
                    int numPriorsToGet = EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria - equilibria.Count();
                    if (numPriorsToGet > 0)
                    {
                        TabbedText.WriteLine($"Trying inexact arithmetic for up to {numPriorsToGet} random priors");
                        additionalEquilibria = DetermineEquilibria<InexactValue>(numPriorsToGet);
                        equilibria.AddRange(additionalEquilibria);
                    }
                }
                if (equilibria == null || equilibria.Count() < EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria)
                {
                    // Suppose our target is 100 equilibria, and we've found 1 with a frequency of 10. 
                    int numPriorsToGet = EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria - equilibria.Sum(x => x.frequency);
                    TabbedText.WriteLine($"Resorting to exact arithmetic for up to {numPriorsToGet} random priors");
                    additionalEquilibria = DetermineEquilibria<ExactValue>(numPriorsToGet);
                    equilibria.AddRange(additionalEquilibria);
                }

                await ProcessIdentifiedEquilibria(reportCollection, equilibria.Select(x => x.equilibrium).ToList());
            }
            else if (Approach == SequenceFormApproach.Gambit)
            {
                await UseGambitToCalculateEquilibria(reportCollection, filename);
            }

            return reportCollection;
        }

        #region ECTA

        List<GameNodeRelationship> GameNodeRelationships;
        List<InformationSetInfo> InformationSetInfos;
        HashSet<int> PotentiallyReachableInformationSets;
        static Dictionary<int, List<byte>> BlockedPlayerActions; // static because this is determined across executions
        Dictionary<int, int> WhenInformationSetVisited;
        public List<int> MoveIndexToInfoSetIndex;
        public List<int> FirstInformationSetInfosIndexForPlayers;
        public List<int> FirstMovesIndexForPlayers;
        public Dictionary<(int informationSetIndex, int oneBasedMove), int> MoveIndexFromInfoSetIndexAndMoveWithinInfoSet;
        public List<(int informationSetIndex, int oneBasedMove)> NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet;


        public record InformationSetInfo(IGameState GameState, int ECTAPlayerID, int Index, int FirstMoveIndex, int MaxIntegralUtility)
        {
            public int NumPossibleMoves
            {
                get
                {
                    int numPossibleActions = GameState.GetNumPossibleActions();
                    if (BlockedPlayerActions != null && GameState.GetGameStateType() == GameStateTypeEnum.InformationSet && BlockedPlayerActions.ContainsKey(GameState.GetInformationSetNodeNumber()))
                        numPossibleActions -= BlockedPlayerActions[GameState.GetInformationSetNodeNumber()].Count();
                    return numPossibleActions;
                }
            }
            public bool IsChance => GameState is ChanceNode;
            public InformationSetNode InformationSetNode => IsChance ? null : (InformationSetNode)GameState;
            public ChanceNode ChanceNode => IsChance ? (ChanceNode)GameState : null;

            public override string ToString()
            {
                if (IsChance)
                    return ChanceNode.ShortString();
                else
                    return InformationSetNode.ToStringWithoutValues();
            }

            public double[] GetProbabilities()
            {
                return InformationSetNode?.GetCurrentProbabilitiesAsArray() ?? ChanceNode.GetActionProbabilities().ToArray();
            }
        }
        List<FinalUtilitiesNode> Outcomes => GameNodeRelationships.Where(x => x != null && x.GameState is FinalUtilitiesNode).Select(x => (FinalUtilitiesNode)x.GameState).ToList();

        private List<(double[] equilibrium, int frequency)> DetermineEquilibria<T>(int numPriorsToGet) where T : IMaybeExact<T>, new()
        {
            DetermineGameNodeRelationships();
            bool useManuallyDefinedEquilibria = false; // use this as a shortcut to replay some equilibrium
            List<(double[] equilibrium, int frequency)> equilibria = null;
            if (EvolutionSettings.PreloadedEquilibriaForSequenceForm)
            {
                equilibria = LoadEquilibriaFile().Select(x => (x, 1)).ToList();
                equilibria = NarrowDownToUniqueEquilibria(equilibria);
            }
            else if (useManuallyDefinedEquilibria)
            {
                double[] eq = new double[] {1,0,0,1,1/2,1/2,0,1,1,0,0,1,1/2,1/2,1139690000.0/1152971127.0,13281127.0/1152971127.0,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,1,0,1,0,13513.0/26983.0,13470.0/26983.0,1,0,1,0,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,0,1
                };
                equilibria = new List<(double[] equilibrium, int frequency)>() { (eq, 1) };
            }
            else
            {
                bool updateScenarios = false; // Doesn't work right now
                Action<int, ECTATreeDefinition<T>> scenarioUpdater = updateScenarios ? ScenarioUpdater<T>() : null;
                List<(IMaybeExact<T>[] equilibrium, int frequency)> results;
                if (EvolutionSettings.ParallelOptimization)
                {
                    results = new List<(IMaybeExact<T>[] equilibrium, int frequency)>();
                    Parallelizer.Go(true, 0, numPriorsToGet, priorNumber =>
                    {
                        ECTARunner<T> ecta = GetECTARunner<T>(1);
                        var individualResults = ecta.Execute(t => SetupECTA(t), scenarioUpdater, priorNumber);
                        lock (results)
                        {
                            var individualResult = individualResults.First();
                            AddEquilibriumToEquilibriaListIfUnique(results, individualResult);
                        }
                    });
                    if (EvolutionSettings.SequenceFormBlockDistantActionsWhenTracingEquilibrium)
                        throw new Exception();
                }
                else
                {
                    List<IMaybeExact<T>> initialProbabilities = null;
                    if (EvolutionSettings.ConsiderInitializingToMostRecentEquilibrium && GameDefinition.GameOptions.InitializeToMostRecentEquilibrium && MostRecentEquilibrium != null)
                    {
                        initialProbabilities = ((IMaybeExact<T>[])MostRecentEquilibrium).ToList();
                        TabbedText.WriteLine($"Initializing probabilities to {String.Join(",", initialProbabilities.Select(x => x.ToString()))}"); // DEBUG
                    }
                    else
                    {
                        if (EvolutionSettings.CustomSequenceFormInitialization)
                        {
                            initialProbabilities = GameDefinition.GetSequenceFormInitialization<T>();
                        }
                    }
                    
                    ECTARunner<T> ecta = GetECTARunner<T>(numPriorsToGet);
                    results = ecta.Execute(t => SetupECTA(t), scenarioUpdater, 0, initialProbabilities?.ToArray());
                }
                results = results.Select(x => (ReverseEffectsOfCuttingOffProbabilityZeroNodes(x.equilibrium), x.frequency)).ToList();
                MostRecentEquilibrium = results.Last().equilibrium;
                NarrowDownToValidEquilibria<T>(results);
                equilibria = results.Select(x => (x.equilibrium.Select(y => y.AsDouble).ToArray(), x.frequency)).ToList();
                if (EvolutionSettings.SequenceFormBlockDistantActionsWhenTracingEquilibrium)
                    IdentifyProbabilitiesToBlockWhenTracingPath<T>((IMaybeExact<T>[])MostRecentEquilibrium);
                else
                    BlockedPlayerActions = null;
            }

            return equilibria;
        }

        private static List<(double[] equilibrium, int frequency)> NarrowDownToUniqueEquilibria(List<(double[] equilibrium, int frequency)> equilibriaList)
        { 
            List<(double[] equilibrium, int frequency)> copy = new List<(double[] equilibrium, int frequency)>();
            foreach (var eq in equilibriaList)
                AddEquilibriumToEquilibriaListIfUnique(copy, eq);
            return copy;
        }

        private static void AddEquilibriumToEquilibriaListIfUnique(List<(double[] equilibrium, int frequency)> equilibriaList, (double[] equilibrium, int frequency) equilibrium)
        {
            bool found = false;
            for (int i = 0; i < equilibriaList.Count(); i++)
            {
                if (equilibrium.equilibrium.SequenceEqual(equilibriaList[i].equilibrium))
                {
                    found = true;
                    equilibriaList[i] = (equilibriaList[i].equilibrium, equilibriaList[i].frequency + equilibrium.frequency);
                    break;
                }
            }
            if (!found)
                equilibriaList.Add(equilibrium);
        }

        private static List<(IMaybeExact<T>[] equilibrium, int frequency)> NarrowDownToUniqueEquilibria<T>(List<(IMaybeExact<T>[] equilibrium, int frequency)> equilibriaList) where T : IMaybeExact<T>, new()
        {
            List<(IMaybeExact<T>[] equilibrium, int frequency)> copy = new List<(IMaybeExact<T>[] equilibrium, int frequency)>();
            foreach (var eq in equilibriaList)
                AddEquilibriumToEquilibriaListIfUnique(copy, eq);
            return copy;
        }

        private static void AddEquilibriumToEquilibriaListIfUnique<T>(List<(IMaybeExact<T>[] equilibrium, int frequency)> equilibriaList, (IMaybeExact<T>[] equilibrium, int frequency) equilibrium) where T : IMaybeExact<T>, new()
        {
            bool found = false;
            for (int i = 0; i < equilibriaList.Count(); i++)
            {
                if (equilibrium.equilibrium.SequenceEqual(equilibriaList[i].equilibrium))
                {
                    found = true;
                    equilibriaList[i] = (equilibriaList[i].equilibrium, equilibriaList[i].frequency + equilibrium.frequency);
                    break;
                }
            }
            if (!found)
                equilibriaList.Add(equilibrium);
        }

        [SupportedOSPlatform("windows")]
        private async Task ProcessIdentifiedEquilibria(ReportCollection reportCollection, List<double[]> equilibria)
        {
            if (EvolutionSettings.CreateEquilibriaFileForSequenceForm)
                CreateEquilibriaFile(equilibria);
            await GenerateReportsFromEquilibria(equilibria, reportCollection);
            if (equilibria.Any())
                SetInformationSetsToEquilibrium(equilibria.First());
        }

        private ECTARunner<T> GetECTARunner<T>(int numPriorsToGet) where T : IMaybeExact<T>, new()
        {
            var ecta = new ECTARunner<T>();
            ecta.numPriors = numPriorsToGet;

            ecta.outputPrior = false;
            ecta.outputGameTreeSetup = false;
            ecta.outputLCP = false;
            ecta.outputInitialAndFinalTableaux = false;
            ecta.outputPivotingSteps = false;
            ecta.outputTableauxAfterPivots = false;
            ecta.outputPivotResults = false;
            ecta.outputLCPSolution = false;
            ecta.outputEquilibrium = true;
            ecta.outputRealizationPlan = false;
            ecta.abortIfCycling = true;
            ecta.minRepetitionsForCycling = 3;
            T maybeExact = new T();
            if (maybeExact.IsExact)
            {
                if (numPriorsToGet > 1) // we already have at least 1 equilibrium, so we shouldn't try too hard to get another
                    ecta.maxPivotSteps = 1_000;
                else
                    ecta.maxPivotSteps = 0; // no limit for first equilibrium
            }
            else
                ecta.maxPivotSteps = 500;

            bool outputAll = false;
            if (outputAll)
            {
                ecta.outputPrior = true;
                ecta.outputGameTreeSetup = true;
                ecta.outputInitialAndFinalTableaux = true;
                ecta.outputLCP = true;
                ecta.outputLCPSolution = true;
                ecta.outputPivotingSteps = true;
                ecta.outputTableauxAfterPivots = true;
                ecta.outputPivotResults = true;
                ecta.outputEquilibrium = true;
                ecta.outputRealizationPlan = true;
            }

            return ecta;
        }

        public List<(IMaybeExact<T>[] equilibrium, int frequency)> NarrowDownToValidEquilibria<T>(List<(IMaybeExact<T>[] equilibrium, int frequency)> equilibria) where T : IMaybeExact<T>, new()
        {
            int numEquilibria = equilibria.Count();
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
            var infoSetNames = infoSets.Select(x => x.ToStringWithoutValues()).ToArray();
            Dictionary<int, IMaybeExact<T>[]> chanceProbabilities = new Dictionary<int, IMaybeExact<T>[]>();
            foreach (var chanceNode in InformationSetInfos.Where(x => x.IsChance))
            {
                chanceProbabilities[chanceNode.ChanceNode.GetInformationSetNodeNumber()] = chanceNode.ChanceNode.GetProbabilitiesAsRationals(!EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes, EvolutionSettings.MaxIntegralUtility).Select(x => IMaybeExact<T>.FromRational(x)).ToArray();
            }
            Dictionary<int, IMaybeExact<T>[]> utilities = new Dictionary<int, IMaybeExact<T>[]>();

            Rational[][] rationalUtilities = UtilitiesAsRationals.TransposeRowsAndColumns();
            for (int finalUtilitiesNodesIndex = 0; finalUtilitiesNodesIndex < FinalUtilitiesNodes.Count; finalUtilitiesNodesIndex++)
            {
                FinalUtilitiesNode finalUtilitiesNode = FinalUtilitiesNodes[finalUtilitiesNodesIndex];
                utilities[finalUtilitiesNode.GetInformationSetNodeNumber()] = rationalUtilities[finalUtilitiesNodesIndex].Select(x => IMaybeExact<T>.FromRational(x)).ToArray();
            }

            List<int> imperfect = new List<int>();
            bool isExact = new T().IsExact;
            for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
            {
                var actionProbabilities = equilibria[eqNum].equilibrium;
                bool perfect = CheckEquilibrium(infoSets, chanceProbabilities, utilities, actionProbabilities);
                if (!perfect)
                    imperfect.Add(eqNum);
            }
            bool eliminateImperfectIfPossible = true;
            if (eliminateImperfectIfPossible && (!isExact || (imperfect.Count() > 0 && imperfect.Count() < numEquilibria)))
            {
                // narrow down to perfect equilibrium -- in observation, the imperfection is only in a very late decimal place when converted to double
                imperfect.Reverse();
                foreach (int eqNum in imperfect)
                    equilibria.RemoveAt(eqNum);
            }
            return equilibria;
        }

        private bool CheckEquilibrium<T>(List<InformationSetNode> infoSets, Dictionary<int, IMaybeExact<T>[]> chanceProbabilities, Dictionary<int, IMaybeExact<T>[]> utilities, IMaybeExact<T>[] actionProbabilities) where T : IMaybeExact<T>, new()
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            Dictionary<(int playerIndex, int nodeIndex), IMaybeExact<T>[]> playerProbabilities = null;
            if (infoSets.Sum(x => x.Decision.NumPossibleActions) != actionProbabilities.Count())
            {
                throw new Exception("Mismatch in number of possible actions");
            }
            else
            {
                FixDegenerateProbabilities(infoSets, actionProbabilities, out playerProbabilities);
            }

            // check the best response improvement
            SetInformationSetsToEquilibrium(actionProbabilities.Select(x => double.IsNaN(x.AsDouble) ? 0 : x.AsDouble).ToArray());
            CalculateBestResponse(false);
            double maxBestResponseImprovementAdjAvg = 0.001;
            if (Status.BestResponseImprovementAdjAvg >= maxBestResponseImprovementAdjAvg)
            {
                TabbedText.WriteLine($"Best response improvement average was {Status.BestResponseImprovementAdjAvg}; equilibrium rejected");
                return false;
            }

            IMaybeExact<T> errorTolerance = new T().IsExact ? IMaybeExact<T>.Zero() : IMaybeExact<T>.One().DividedBy(IMaybeExact<T>.FromInteger(EvolutionSettings.MaxIntegralUtility));
            CalculateUtilitiesAtEachInformationSet_MaybeExact<T> calc = new CalculateUtilitiesAtEachInformationSet_MaybeExact<T>(chanceProbabilities, playerProbabilities, utilities, errorTolerance);
            TreeWalk_Tree(calc);
            if (EvolutionSettings.ConfirmPerfectEquilibria && !EvolutionSettings.SequenceFormBlockDistantActionsWhenTracingEquilibrium)
            {
                bool perfect = calc.VerifyPerfectEquilibrium(InformationSets, EvolutionSettings.ThrowIfNotPerfectEquilibrium);
                TabbedText.WriteLine($"Perfect equilibrium {(!perfect ? "not " : "")}confirmed {s.ElapsedMilliseconds} ms");
                return perfect;
            }
            else
                return true;
        }

        private static void FixDegenerateProbabilities<T>(List<InformationSetNode> infoSets, IMaybeExact<T>[] actionProbabilities, out Dictionary<(int playerIndex, int nodeIndex), IMaybeExact<T>[]> playerProbabilities) where T : IMaybeExact<T>, new()
        {
            playerProbabilities = new Dictionary<(int playerIndex, int nodeIndex), IMaybeExact<T>[]>();
            var numActionsPerSet = infoSets.Select(x => x.Decision.NumPossibleActions).ToList();
            int actionProbabilitiesIndex = 0;
            foreach (var infoSet in infoSets)
            {
                IMaybeExact<T>[] asArray = new IMaybeExact<T>[infoSet.NumPossibleActions];
                int initialActionProbabilitiesIndex = actionProbabilitiesIndex;
                IMaybeExact<T> total = IMaybeExact<T>.Zero();
                for (int i = 0; i < infoSet.NumPossibleActions; i++)
                {
                    var probability = actionProbabilities[actionProbabilitiesIndex++];
                    asArray[i] = probability;
                    total = total.Plus(probability);
                }
                if (!total.IsOne())
                {
                    // Fix degeneracy
                    if (total.IsZero() || total.IsNegative())
                    {
                        for (int i = 0; i < infoSet.NumPossibleActions; i++)
                        {
                            actionProbabilities[initialActionProbabilitiesIndex + i] = IMaybeExact<T>.One().DividedBy(IMaybeExact<T>.FromInteger(infoSet.NumPossibleActions));
                            asArray[i] = actionProbabilities[initialActionProbabilitiesIndex + i];
                        }

                    }
                    else
                    {
                        // It appears that when the equilibrium is imperfect (always only very slightly), there is always a degeneracy of this sort. 
                        // Neither of these approaches makes it go away; nor does using the approach used where total <= 0.
                        IMaybeExact<T> multiplier = IMaybeExact<T>.One().DividedBy(total);
                        for (int i = 0; i < infoSet.NumPossibleActions; i++)
                        {
                            actionProbabilities[initialActionProbabilitiesIndex + i] = actionProbabilities[initialActionProbabilitiesIndex + i].Times(multiplier);
                            asArray[i] = actionProbabilities[initialActionProbabilitiesIndex + i];
                        }
                    }
                }
                playerProbabilities[(infoSet.PlayerIndex, infoSet.GetInformationSetNodeNumber())] = asArray;
            }
        }

        private Action<int, ECTATreeDefinition<T>> ScenarioUpdater<T>() where T : IMaybeExact<T>, new()
        {
            return (index, treeDefinition) =>
            {
                ChangeScenarioForSubsequentPrior(index);
                UpdateECTAOutcomes(treeDefinition);
            };
        }

        private void ChangeScenarioForSubsequentPrior(int i)
        {
            if (i != 0)
                ReinitializeForScenario(i, false);
        }

        public void DetermineGameNodeRelationships()
        {
            if (NumNonChancePlayers != 2)
                throw new NotImplementedException();

            if (EvolutionSettings.SequenceFormBlockDistantActionsWhenTracingEquilibrium && GameDefinition.GameOptions.InitializeToMostRecentEquilibrium == false)
                BlockedPlayerActions = null;

            IGameState rootState = GetGameState(GetStartOfGameHistoryPoint());
            GameNodeRelationshipsFinder finder = new GameNodeRelationshipsFinder(rootState, EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes, EvolutionSettings.MaxIntegralUtility, BlockedPlayerActions);
            TreeWalk_Tree<GameNodeRelationshipsFinder.ForwardInfo, bool /* ignored */>(finder, new GameNodeRelationshipsFinder.ForwardInfo(0, new bool[] { false }));
            WhenInformationSetVisited = finder.WhenInformationSetVisited;
            PotentiallyReachableInformationSets = finder.PotentiallyReachableInformationSets;

            GameNodeRelationships = finder.NodeRelationships;
            var originalOrder = GameNodeRelationships.ToList();

            // information sets must be in player order
            var orderedByPlayer = GameNodeRelationships.OrderByDescending(x => x.GameState is ChanceNode)
                .ThenByDescending(x => x.GameState is InformationSetNode)
                .ThenByDescending(x => x.GameState is FinalUtilitiesNode)
                .ThenBy(x => (x.GameState as InformationSetNode)?.PlayerIndex ?? 0)
                .ThenBy(x => x.NodeID)
                .ThenBy(x => x.ActionAtParent)
                .ToList();
            var chanceInformationSets = orderedByPlayer.Where(x => x != null && x.GameState is ChanceNode).Select(x => (ChanceNode)x.GameState).DistinctBy(x => x.ChanceNodeNumber).ToList();
            var playerInformationSets = orderedByPlayer.Where(x => x != null && x.GameState is InformationSetNode).Select(x => (InformationSetNode)x.GameState).DistinctBy(x => x.InformationSetNodeNumber).OrderBy(x => PlayerIDToECTA(((InformationSetNode)x).PlayerIndex)).ThenBy(x => x.InformationSetNodeNumber).ToList();
            InformationSetInfos = new List<InformationSetInfo>();
            int index = 0;
            foreach (var chanceInformationSet in chanceInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(chanceInformationSet, 0, index++, -1, EvolutionSettings.MaxIntegralUtility));
            foreach (var playerInformationSet in playerInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(playerInformationSet, PlayerIDToECTA(playerInformationSet.PlayerIndex), index++, -1, EvolutionSettings.MaxIntegralUtility));
            for (int i = 0; i < InformationSetInfos.Count(); i++)
                InformationSetInfos[i].GameState.AltNodeNumber = i; // set the alt node number so that we can match the numbering scheme expected of our ECTA code

            // game nodes must be in game order, but with outcomes all at end
            var orderedWithOutcomesLast = GameNodeRelationships
                .OrderBy(x => x.GameState is FinalUtilitiesNode)
                .ThenBy(x => x.NodeID)
                .ToList();
            orderedWithOutcomesLast.Insert(0, null); // the ECTA code skips 0
            // record map of original ID to new one-based ID given new order
            Dictionary<int, int> originalIDToRevised = new Dictionary<int, int>();
            for (int i = 1; i < orderedWithOutcomesLast.Count(); i++) // skip 0 as in ECTA code
            {
                originalIDToRevised[orderedWithOutcomesLast[i].NodeID] = i;
            }
            // now, fix IDs so that we are using the REVISED order of IDs. 
            for (int i = 1; i < orderedWithOutcomesLast.Count(); i++)
            {
                int? originalNodeID = orderedWithOutcomesLast[i].NodeID;
                orderedWithOutcomesLast[i] = orderedWithOutcomesLast[i] with
                {
                    OriginalNodeID = originalNodeID,
                    NodeID = originalIDToRevised[orderedWithOutcomesLast[i].NodeID],
                    ParentNodeID = orderedWithOutcomesLast[i].ParentNodeID is int originalParentID ? originalIDToRevised[originalParentID] : null
                };
            }
            GameNodeRelationships = orderedWithOutcomesLast;

            MoveIndexToInfoSetIndex = new List<int>();
            FirstInformationSetInfosIndexForPlayers = new List<int>();
            FirstMovesIndexForPlayers = new List<int>();
            MoveIndexFromInfoSetIndexAndMoveWithinInfoSet = new Dictionary<(int informationSetIndex, int oneBasedMove), int>();
            NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet = new List<(int informationSetIndex, int oneBasedMove)>();
            int lastPlayerID = -1;
            for (int i = 0; i < InformationSetInfos.Count; i++)
            {
                InformationSetInfo informationSetInfo = InformationSetInfos[i];
                InformationSetInfos[i] = informationSetInfo with
                {
                    FirstMoveIndex = MoveIndexToInfoSetIndex.Count()
                };
                if (lastPlayerID != informationSetInfo.ECTAPlayerID)
                {
                    lastPlayerID++;
                    FirstInformationSetInfosIndexForPlayers.Add(informationSetInfo.Index);
                    // The first move for a player is the empty sequence. We'll represent that by -1. We'll add the first non-empty move below.
                    FirstMovesIndexForPlayers.Add(MoveIndexToInfoSetIndex.Count());
                    MoveIndexToInfoSetIndex.Add(-1);
                }
                int numMoves = informationSetInfo.NumPossibleMoves;
                for (int move = 1; move <= numMoves; move++)
                {
                    MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(informationSetInfo.Index, move)] = MoveIndexToInfoSetIndex.Count();
                    MoveIndexToInfoSetIndex.Add(informationSetInfo.Index); // Here's where we add the move.
                    if (lastPlayerID != 0) // omit chance moves
                        NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet.Add((informationSetInfo.Index, move));

                }
            }

            PrintRelationships(originalOrder);

            bool produceCCode = false; 
            if (produceCCode)
            {
                string ectaCodeInC = GetECTACodeInC();
            }

            VerifyPerfectRecall();
        }

        private void PrintRelationships(List<GameNodeRelationship> originalOrder)
        {
            bool printRelationships = false;
            // printing
            if (printRelationships)
            {
                StringBuilder asOriginallyOrdered = new StringBuilder();
                void PrintNodeAndChildren(int depth, int nodeID)
                {
                    for (int j = 0; j < 5 * depth; j++)
                        asOriginallyOrdered.Append(" ");
                    int nodeIndex = originalOrder.Select((item, index) => (item, index)).First(x => x.item.NodeID == nodeID).index;
                    var node = originalOrder[nodeIndex];
                    var correspondingNode = GameNodeRelationships.First(x => x != null && x.OriginalNodeID == node.NodeID);
                    asOriginallyOrdered.AppendLine(correspondingNode.ToString());
                    List<int> childrenNodeIDs = originalOrder.Select((item, index) => (item, index)).Where(x => x.item.ParentNodeID == nodeID).Select(x => x.index).ToList();
                    foreach (var childNodeID in childrenNodeIDs)
                        PrintNodeAndChildren(depth + 1, childNodeID);
                }
                PrintNodeAndChildren(0, 0);

                StringBuilder revisedOrder = new StringBuilder();
                for (int i = 1; i < GameNodeRelationships.Count(); i++)
                    revisedOrder.AppendLine(GameNodeRelationships[i].ToString());
            }
        }

        public void SetupECTA<T>(ECTATreeDefinition<T> t) where T : IMaybeExact<T>, new()
        {
            int[][] pay = GetOutcomesForECTA();

            t.allocateTree(GameNodeRelationships.Count(), InformationSetInfos.Count(), MoveIndexToInfoSetIndex.Count(), Outcomes.Count);

            int firstNonChancePlayerIndex;
            int secondNonChancePlayerIndex;
            int playerIndexForMove;
            switch (FirstInformationSetInfosIndexForPlayers.Count())
            {
                case 2:
                    playerIndexForMove = 0; // will skip chance player
                    firstNonChancePlayerIndex = 0;
                    secondNonChancePlayerIndex = 1;
                    break;
                case 3:
                    firstNonChancePlayerIndex = 1;
                    secondNonChancePlayerIndex = 2;
                    playerIndexForMove = -1; // chance player will be 0
                    break;
                default:
                    throw new NotImplementedException("Currently supporting only two-player games with or without a chance player");
            }

            t.firstInformationSet[0] = 0;
            t.firstInformationSet[1] = FirstInformationSetInfosIndexForPlayers[firstNonChancePlayerIndex];
            t.firstInformationSet[2] = FirstInformationSetInfosIndexForPlayers[secondNonChancePlayerIndex];
            t.firstMove[0] = 0;
            t.firstMove[1] = FirstMovesIndexForPlayers[firstNonChancePlayerIndex];
            t.firstMove[2] = FirstMovesIndexForPlayers[secondNonChancePlayerIndex];

            int zindex = 0;
            var z = t.outcomes[0];

            int firstOutcome = -1;
            t.nodes[ECTATreeDefinition<T>.rootindex].father = -1;
            for (int n = 2; n < t.nodes.Length; n++)
            {
                t.nodes[n].father = (int)GameNodeRelationships[n].ParentNodeID;
                if (GameNodeRelationships[n].GameState is FinalUtilitiesNode outcome)
                {
                    if (firstOutcome == -1)
                        firstOutcome = n;
                    t.nodes[n].terminal = true;
                    t.nodes[n].outcome = zindex;
                    z.nodeIndex = n;
                    z.pay[0] = IMaybeExact<T>.FromInteger(pay[0][zindex]);
                    z.pay[1] = IMaybeExact<T>.FromInteger(pay[1][zindex]);
                    if (zindex < t.outcomes.Length - 1)
                        z = t.outcomes[++zindex];
                }
            }

            for (int n = 1; n < GameNodeRelationships.Count(); n++)
            {
                if (GameNodeRelationships[n].GameState is not FinalUtilitiesNode)
                    t.nodes[n].iset = InformationSetInfoIndexForGameNode(n);
            }
            for (int n = 2; n < GameNodeRelationships.Count(); n++)
            {
                int movesIndex = GetIndexOfMoveLeadingToNode(n);
                t.nodes[n].moveAtFather = movesIndex;
            }
            for (int i = 0; i < InformationSetInfos.Count(); i++)
            {
                InformationSetInfo iinfo = InformationSetInfos[i];
                var orignode = iinfo.InformationSetNode;
                t.informationSets[i].playerIndex = iinfo.ECTAPlayerID;
                t.informationSets[i].firstMoveIndex = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(i, 1)];
                t.informationSets[i].numMoves = iinfo.NumPossibleMoves;
                t.informationSets[i].name = iinfo.ToString();
            }

            for (int moveIndex = 0; moveIndex < MoveIndexToInfoSetIndex.Count(); moveIndex++)
            {
                int infoSetIndex = MoveIndexToInfoSetIndex[moveIndex];
                if (infoSetIndex == -1)
                {
                    playerIndexForMove++;
                    // move moveIndex is empty sequence for player playerIndexForMove
                }
                else
                {
                    int moveIndexForFirstMove = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(infoSetIndex, 1)];
                    int moveNumber = moveIndex - moveIndexForFirstMove + 1;
                    t.moves[moveIndex].priorInformationSet = infoSetIndex;
                    if (playerIndexForMove == 0)
                    {
                        // chance player
                        var chance = InformationSetInfos[infoSetIndex].ChanceNode;
                        var rational = InformationSetInfos[infoSetIndex].ChanceNode.GetProbabilitiesAsRationals(!EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes, EvolutionSettings.MaxIntegralUtility)[moveNumber - 1];
                        if (rational.IsZero && !EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes)
                            throw new Exception("Zero chance probabilities not allowed");
                        if (chance.Decision.DistributedChanceDecision && EvolutionSettings.DistributeChanceDecisions)
                        {
                            t.moves[moveIndex].behavioralProbability = moveNumber == 1 ? IMaybeExact<T>.One() : IMaybeExact<T>.Zero();
                        }
                        else
                            t.moves[moveIndex].behavioralProbability = IMaybeExact<T>.FromRational(rational);
                    }
                }
            }
        }

        public void UpdateECTAOutcomes<T>(ECTATreeDefinition<T> t) where T : IMaybeExact<T>, new()
        {
            int[][] pay = GetOutcomesForECTA();

            int zindex = 0;
            var z = t.outcomes[0];

            int firstOutcome = -1;
            for (int n = 2; n < t.nodes.Length; n++)
            {
                if (GameNodeRelationships[n].GameState is FinalUtilitiesNode outcome)
                {
                    if (firstOutcome == -1)
                        firstOutcome = n;
                    t.nodes[n].outcome = zindex;
                    z.pay[0] = IMaybeExact<T>.FromInteger(pay[0][zindex]);
                    z.pay[1] = IMaybeExact<T>.FromInteger(pay[1][zindex]);
                    if (zindex < t.outcomes.Length - 1)
                        z = t.outcomes[++zindex];
                }
            }
        }

        private int[][] GetOutcomesForECTA()
        {
            int[][] pay = new int[2][];
            pay[0] = ConvertToIntegralUtilities(Outcomes.Select(x => x.Utilities[0]));
            pay[1] = ConvertToIntegralUtilities(Outcomes.Select(x => x.Utilities[1]));
            return pay;
        }

        private int PlayerIDToECTA(int playerID) => playerID switch
        {
            0 => 1,
            1 => 2,
            _ => 0, // chance players (we're assuming a two-player game)
        };


        int InformationSetInfoIndexForGameNode(int nodeIndex)
        {
            GameNodeRelationship gameNode = GameNodeRelationships[nodeIndex];
            var gameState = gameNode.GameState;
            return (int)gameState.AltNodeNumber;
            //for (int i = 0; i < InformationSetInfos.Count(); i++)
            //    if (gameState == InformationSetInfos[i].GameState)
            //        return i;
            //throw new Exception();
        }
        private int GetIndexOfMoveLeadingToNode(int nodeIndex)
        {
            int parentNodeID = (int)GameNodeRelationships[nodeIndex].ParentNodeID;
            int parentNodeInformationSetInfoIndex = InformationSetInfoIndexForGameNode(parentNodeID);
            byte actionAtParent = (byte)GameNodeRelationships[nodeIndex].ActionAtParent;
            int movesIndex = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(parentNodeInformationSetInfoIndex, actionAtParent)];
            return movesIndex;
        }
        private (int moveIndex, int ectaPlayerID) GetIndexAndPlayerOfMoveLeadingToNode(int nodeIndex)
        {
            int parentNodeID = (int)GameNodeRelationships[nodeIndex].ParentNodeID;
            int parentNodeInformationSetInfoIndex = InformationSetInfoIndexForGameNode(parentNodeID);
            byte actionAtParent = (byte)GameNodeRelationships[nodeIndex].ActionAtParent;
            int movesIndex = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(parentNodeInformationSetInfoIndex, actionAtParent)];
            int ectaPlayerID = InformationSetInfos[parentNodeInformationSetInfoIndex].ECTAPlayerID;
            return (movesIndex, ectaPlayerID);
        }

        private List<int> GetSequenceOfMovesLeadingToNode(int nodeIndex, int ectaPlayerID)
        {
            List<int> moves = new List<int>();
            while (nodeIndex != 1)
            {
                (int moveIndex, int previousECTAPlayerID) = GetIndexAndPlayerOfMoveLeadingToNode(nodeIndex);
                if (previousECTAPlayerID == ectaPlayerID)
                {
                    moves.Add(moveIndex);
                }
                nodeIndex = (int)GameNodeRelationships[nodeIndex].ParentNodeID;
            }
            moves.Reverse();
            return moves;
        }
        private List<IGrouping<int, int>> NodesGroupedByInformationSet()
        {
            var gameNodesWithInformationSets = GameNodeRelationships.Select((item, index) => (item, index)).Where(x => x.item != null && x.item.GameState is not FinalUtilitiesNode).Select(x => x.index).ToList();
            List<IGrouping<int, int>> grouped = gameNodesWithInformationSets.GroupBy(nodeIndex => InformationSetInfoIndexForGameNode(nodeIndex)).ToList();
            return grouped;
        }
        private void VerifyPerfectRecall()
        {
            // sequence must match for each node in information set
            var grouped = NodesGroupedByInformationSet();
            foreach (var group in grouped)
            {
                int firstNodeIndex = group.First();
                var informationSet = InformationSetInfos[InformationSetInfoIndexForGameNode(firstNodeIndex)];
                var ectaPlayerID = informationSet.ECTAPlayerID;
                bool verifyChancePlayer = true;
                if (ectaPlayerID == 0 && !verifyChancePlayer)
                    continue;
                List<int> sequence = GetSequenceOfMovesLeadingToNode(firstNodeIndex, ectaPlayerID);
                foreach (int additionalNodeIndex in group.Skip(1))
                {
                    List<int> additionalSequence = GetSequenceOfMovesLeadingToNode(additionalNodeIndex, ectaPlayerID);
                    if (!sequence.SequenceEqual(additionalSequence))
                        throw new Exception();
                }
            }
        }

        // This is to generate code that can be pasted into the original C code. This was used to ensure that the results were the same (when used with simple games where integral overflow did not occur).
        public string GetECTACodeInC()
        {
            // TODO: This would need to be updated (along the lines above) to work with games without a chance player.
            //const int ECTA_MultiplyOutcomesByThisBeforeRounding = 10_000;
            var outcomes = Outcomes;
            var player0Rounded = ConvertToIntegralUtilities(outcomes.Select(x => x.Utilities[0]));
            var player1Rounded = ConvertToIntegralUtilities(outcomes.Select(x => x.Utilities[1]));
            StringBuilder s = new StringBuilder();
            string s1 = $@"    int pay[2][{outcomes.Count}] = {{ 
        {{ {String.Join(", ", player0Rounded)} }},
        {{ {String.Join(", ", player1Rounded)} }} 
    }};";
            s.AppendLine(s1);
            string s2 = $@"    alloctree({GameNodeRelationships.Count()},{InformationSetInfos.Count()},{MoveIndexToInfoSetIndex.Count()},{outcomes.Count});
    Outcome z = outcomes;
    firstiset[0] = isets + 0;
    firstiset[1] = isets + {FirstInformationSetInfosIndexForPlayers[1]};
    firstiset[2] = isets + {FirstInformationSetInfosIndexForPlayers[2]};
    firstmove[0] = moves + 0;
    firstmove[1] = moves + {FirstMovesIndexForPlayers[1]};
    firstmove[2] = moves + {FirstMovesIndexForPlayers[2]};
                
    // root node is at index 1 (index 0 is skipped)
    root = nodes + ROOT;
    root->father = NULL;
";
            s.Append(s2);
            int firstOutcome = -1;
            for (int n = 2; n < GameNodeRelationships.Count(); n++)
            {
                s.AppendLine($@"    nodes[{n}].father = nodes + {GameNodeRelationships[n].ParentNodeID};
                    ");
                if (GameNodeRelationships[n].GameState is FinalUtilitiesNode outcome)
                {
                    if (firstOutcome == -1)
                        firstOutcome = n;
                    s.AppendLine($@"    nodes[{n}].terminal = 1;
    nodes[{n}].outcome = z;
    z->whichnode = nodes + {n};
    z->pay[0] = ratfromi(pay[0][{n - firstOutcome}]);
    z->pay[1] = ratfromi(pay[1][{n - firstOutcome}]);
    z++;");
                }
            }
            for (int n = 1; n < GameNodeRelationships.Count(); n++)
            {
                if (GameNodeRelationships[n].GameState is not FinalUtilitiesNode)
                    s.AppendLine($"    nodes[{n}].iset = isets + {InformationSetInfoIndexForGameNode(n)};");
            }
            for (int n = 2; n < GameNodeRelationships.Count(); n++)
            {
                int movesIndex = GetIndexOfMoveLeadingToNode(n);
                s.AppendLine($"    nodes[{n}].reachedby = moves + {movesIndex};");
            }
            for (int i = 0; i < InformationSetInfos.Count(); i++)
            {
                s.AppendLine($@"    isets[{i}].player = {InformationSetInfos[i].ECTAPlayerID};
    isets[{i}].move0 = moves + {MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(i, 1)]};
    isets[{i}].nmoves = {InformationSetInfos[i].NumPossibleMoves};");
            }
            int playerIndexForMove = -1;
            for (int moveIndex = 0; moveIndex < MoveIndexToInfoSetIndex.Count(); moveIndex++)
            {
                int infoSetIndex = MoveIndexToInfoSetIndex[moveIndex];
                if (infoSetIndex == -1)
                {
                    playerIndexForMove++;
                    s.AppendLine($"    // move {moveIndex} is empty sequence for player {playerIndexForMove}");
                }
                else
                {
                    s.AppendLine($"    moves[{moveIndex}].atiset = isets + {infoSetIndex};");
                    if (playerIndexForMove == 0)
                    {
                        // chance player
                        var chance = InformationSetInfos[infoSetIndex].ChanceNode;
                        int moveIndexForFirstMove = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(infoSetIndex, 1)];
                        int moveNumber = moveIndex - moveIndexForFirstMove + 1;
                        var rational = chance.GetProbabilitiesAsRationals(!EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes, EvolutionSettings.MaxIntegralUtility)[moveNumber - 1]; // note: this change not tested
                        s.AppendLine($@"    moves[{moveIndex}].behavprob.num = {rational.Numerator};
    moves[{moveIndex}].behavprob.den = {rational.Denominator};");
                    }
                }
            }

            return s.ToString();
        }


        #endregion

        #region Gambit

        [SupportedOSPlatform("windows")]
        private async Task UseGambitToCalculateEquilibria(ReportCollection reportCollection, string filename)
        {
            string output = await RunGambit(filename);
            var equilibria = ProcessGambitResults(reportCollection, output);
            await GenerateReportsFromEquilibria(equilibria, reportCollection);
            if (equilibria.Any())
                SetInformationSetsToEquilibrium(equilibria.First());
        }

        private List<double[]> ProcessGambitResults(ReportCollection reportCollection, string output)
        {
            string[] result = output.Split("\n\r".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            List<double[]> resultsAsDoubles = new List<double[]>();
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
                            numbers.Add(EFGFileReader.RationalStringToDouble(rationalNumberString));
                        }
                        resultsAsDoubles.Add(numbers.ToArray());
                    }
                }
            }
            return resultsAsDoubles;
        }

        private async Task<string> RunGambit(string filename)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            string output = await CreateGambitProcess(filename);
            TabbedText.WriteLine($"Gambit output for {filename} ({s.ElapsedMilliseconds} ms): {output}");
            return output;
        }

        private string CreateGambitFile()
        {
            EFGFileWriter efgCreator = new EFGFileWriter(GameDefinition.OptionSetName, GameDefinition.NonChancePlayerNames, EvolutionSettings.DistributeChanceDecisions);
            TreeWalk_Tree(efgCreator);
            string efgResult = efgCreator.FileText.ToString();
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, MasterReportName + "-" + GameDefinition.OptionSetName + ".efg");
            TextFileManage.CreateTextFile(filename, efgResult);
            return filename;
        }

        public bool EquilibriaFileAlreadyExists()
        {
            return File.Exists(GetEquilibriaFileName());
        }

        private string CreateEquilibriaFile(List<double[]> equilibria)
        {
            StringBuilder s = new StringBuilder();
            foreach (var equilibrium in equilibria)
            {
                s.AppendLine(String.Join(",", equilibrium));
            }
            string filename = GetEquilibriaFileName();
            TextFileManage.CreateTextFile(filename, s.ToString()); // TODO: Switch to azure/local
            return filename;
        }

        private string GetEquilibriaFileName()
        {
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, MasterReportName + "-" + GameDefinition.OptionSetName + "-equ.csv");
            return filename;
        }

        public List<double[]> LoadEquilibriaFile()
        {
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, MasterReportName + "-" + GameDefinition.OptionSetName + "-equ.csv");
            string[] lines = TextFileManage.GetLinesOfFile(filename);
            //List<Rational[]> numbersAsRationals = lines.Select(x => x.Split(",").Select(x => EFGFileReader.RationalStringToRational(x)).ToArray()).ToList();
            List<double[]> numbers = lines.Select(x => x.Split(",").Select(x => EFGFileReader.RationalStringToDouble(x)).ToArray()).ToList(); // there may be doubles or rationals in the string
            return numbers;
        }

        private async Task<string> CreateGambitProcess(string filename)
        {
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            // -D gives more detailed info.
            // -q suppresses the banner
            // Note that -d is supposed to use decimals instead of rationals, but it doesn't work.
            // -P limits to subgame perfect

            bool useDecimals = false;
            int numDecimals = 6;
            string argumentsString = "";
            bool useLCP = true; 
            if (useLCP)
            {
                p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-lcp.exe";
                bool suppressBanner = false;
                bool subgamePerfectOnly = false; // note: it's much faster if we constrain to subgame perfect
                if (suppressBanner)
                    argumentsString += " -q";
                if (subgamePerfectOnly)
                    argumentsString += " -P";
            }
            else
            {
                p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-logit.exe";
            }
            if (useDecimals)
                argumentsString += " " + "-d " + numDecimals.ToString();
            argumentsString += " " + filename;
            p.StartInfo.Arguments = argumentsString;
            p.StartInfo.RedirectStandardOutput = true; 

            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.

            string output = null;
            DateTime? nashEquilibriumFound = null;
            bool done = false;

            p.OutputDataReceived += new DataReceivedEventHandler(NetOutputDataHandler);
            p.Start();
            p.BeginOutputReadLine();

            void NetOutputDataHandler(object sendingProcess, DataReceivedEventArgs outLine)
            {
                string data = outLine.Data;
                if (data != null)
                {
                    TabbedText.WriteLine(data);
                    output += data;
                }
            }

            while (!done)
            {
                if (output != null)
                {
                    if (nashEquilibriumFound == null && output.Contains("NE,"))
                        nashEquilibriumFound = DateTime.Now;
                    bool processHasCompleted = p.HasExited;
                    done = processHasCompleted || (nashEquilibriumFound is DateTime foundTime && DateTime.Now > foundTime + TimeSpan.FromSeconds(1));
                    if (!done)
                        await Task.Delay(100);
                }
            }
            p.Kill(true); // don't wait for rest of equilibria
            return output;
        }

        #endregion

        #region Equilibria and reporting

        [SupportedOSPlatform("windows")]
        private async Task GenerateReportsFromEquilibria(List<double[]> equilibria, ReportCollection reportCollection)
        {
            bool includeAverageEquilibriumReport = true;
            bool includeCorrelatedEquilibriumReport = true;
            if (includeCorrelatedEquilibriumReport)
                SaveWeightedGameProgressesAfterEachReport = true;
            bool includeReportForFirstEquilibrium = true;
            bool includeReportForEachEquilibrium = false; 
            int numEquilibria = equilibria.Count();
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
            var infoSetNames = infoSets.Select(x => x.ToStringWithoutValues()).ToArray();
            for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
            {
                bool isFirst = eqNum == 0;
                bool isLast = eqNum == numEquilibria - 1;
                var actionProbabilities = equilibria[eqNum];
                await ProcessEquilibrium(reportCollection, includeAverageEquilibriumReport, includeCorrelatedEquilibriumReport, includeReportForFirstEquilibrium, includeReportForEachEquilibrium, numEquilibria, infoSets, eqNum, isFirst, isLast, actionProbabilities);
            }
        }

        [SupportedOSPlatform("windows")]
        private async Task ProcessEquilibrium(ReportCollection reportCollection, bool includeAverageEquilibriumReport, bool includeCorrelatedEquilibriumReport, bool includeReportForFirstEquilibrium, bool includeReportForEachEquilibrium, int numEquilibria, List<InformationSetNode> infoSets, int eqNum, bool isFirst, bool isLast, double[] actionProbabilities)
        {
            SetToEquilibriumConstructingAverage(infoSets, actionProbabilities, eqNum, includeCorrelatedEquilibriumReport);

            IdentifyPressureOnInformationSets();

            //double[] utils = GetAverageUtilities(false);
            //double[] maxPurifiedUtils = GetMaximumUtilitiesFromPurifiedStrategies();
            //if (Enumerable.Range(0, NumNonChancePlayers).Any(p => maxPurifiedUtils[p] > utils[p] + 0.001))
            //    TabbedText.WriteLine($"Maximum not achieved {String.Join(",", utils)} vs. {String.Join(",", maxPurifiedUtils)}"); 

            if ((includeReportForFirstEquilibrium && isFirst) || includeReportForEachEquilibrium)
            {
                await AddReportForEquilibrium(reportCollection, numEquilibria, eqNum);
                if (numEquilibria == 1)
                    return;
            }
            if (includeCorrelatedEquilibriumReport && isLast)
            {
                AddCorrelatedEquilibriumReport(reportCollection);
            }
            if (includeAverageEquilibriumReport && isLast)
            {
                await AddAverageEquilibriumReport(reportCollection);
            }
        }

        private IMaybeExact<T>[] ReverseEffectsOfCuttingOffProbabilityZeroNodes<T>(IMaybeExact<T>[] equilibrium) where T : IMaybeExact<T>, new()
        {
            if (!EvolutionSettings.SequenceFormCutOffProbabilityZeroNodes)
                return equilibrium;
            List<IMaybeExact<T>> expanded = new List<IMaybeExact<T>>();
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => WhenInformationSetVisited.GetValueOrDefault(x.InformationSetNodeNumber, int.MaxValue)).ToList();
            int equilibriumIndex = 0;
            foreach (var infoSet in infoSets)
            {
                bool notReachable = !PotentiallyReachableInformationSets.Contains(infoSet.InformationSetNodeNumber);
                for (byte i = 1; i <= infoSet.NumPossibleActions; i++)
                {
                    // There are two reasons that an information set probability may be omitted and therefore needs to be added back in.
                    // First, the entire information set may not be potentially reachable, because of zero-probability earlier decisions (including chance).
                    // Second, the particular action may be blocked.
                    bool actionBlocked = !notReachable && infoSet.IsChanceNode == false && BlockedPlayerActions != null && BlockedPlayerActions[infoSet.InformationSetNodeNumber].Contains(i);
                    if (notReachable || actionBlocked)
                        expanded.Add(IMaybeExact<T>.Zero());
                    else
                        expanded.Add(equilibrium[equilibriumIndex++]);
                }
            }
            TabbedText.WriteLine($"Changed to original form:");
            TabbedText.WriteLine(String.Join(",", expanded.Select(x => x.ToString())));
            return expanded.ToArray();
        }

        private void IdentifyProbabilitiesToBlockWhenTracingPath<T>(IMaybeExact<T>[] equilibrium) where T : IMaybeExact<T>, new()
        {
            // Note: This is relevant only when we are constraining the next equilibrium we find (with different settings) to be very close to this equilibrium, i.e. differing by no more than one step.
            if (!EvolutionSettings.SequenceFormBlockDistantActionsWhenTracingEquilibrium || !EvolutionSettings.ConsiderInitializingToMostRecentEquilibrium)
                throw new NotImplementedException();
            BlockedPlayerActions = new Dictionary<int, List<byte>>();
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => WhenInformationSetVisited.GetValueOrDefault(x.InformationSetNodeNumber, int.MaxValue)).ToList();
            int equilibriumIndex = 0;
            foreach (var infoSet in infoSets)
            {
                List<byte> actionsToAllow = new List<byte>();
                List<IMaybeExact<T>> probabilities = new List<IMaybeExact<T>>();
                for (int i = 1; i <= infoSet.NumPossibleActions; i++)
                    probabilities.Add(equilibrium[equilibriumIndex++]);
                // find the index of the highest value in probabilities
                byte highestIndex = (byte) (probabilities.Select((x, i) => new { x, i}).OrderByDescending(x => x.x).First().i + 1 /* one-based actions */);
                if (highestIndex > 1)
                    actionsToAllow.Add((byte) (highestIndex - 1));
                actionsToAllow.Add((byte)highestIndex);
                if (highestIndex < infoSet.NumPossibleActions)
                    actionsToAllow.Add((byte)(highestIndex + 1));
                var actionsToBlock = Enumerable.Range(1, infoSet.NumPossibleActions).Where(x => !actionsToAllow.Contains((byte)x)).Select(x => (byte) x).ToList();
                BlockedPlayerActions[infoSet.InformationSetNodeNumber] = actionsToBlock;
            }
            if (BlockedPlayerActions.Any())
                TabbedText.WriteLine($"Blocked actions: {String.Join(",", BlockedPlayerActions.Select(x => $"{x.Key}:{String.Join(",", x.Value)}"))}"); // DEBUG
        }

        private void SetInformationSetsToEquilibrium(double[] actionProbabilities)
        {
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
            int totalNumbersProcessed = 0;
            for (int i = 0; i < infoSets.Count(); i++)
            {
                double total = 0;
                InformationSetNode infoSet = infoSets[i];
                for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                {
                    double v = actionProbabilities[totalNumbersProcessed++];
                    total += v;
                    infoSet.SetCurrentStrategyValue(a, v, true);
                }
                if (total == 0)
                {
                    // This is a redundant check for ECTA.
                    // This information set cannot be reached. Use even probabilities.
                    double v = 1.0 / (double)infoSet.Decision.NumPossibleActions;
                    for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                    {
                        infoSet.SetCurrentStrategyValue(a, v, true);
                    }
                }
            }
        }

        private static void SetToEquilibriumConstructingAverage(List<InformationSetNode> infoSets, double[] actionProbabilities, int eqNum, bool recordProbabilitiesAsPastValues)
        {
            int totalNumbersProcessed = 0;
            double weightToGivePastValueInAverage = eqNum / (eqNum + 1.0);
            double weightToGiveNewValueInAverage = 1.0 - weightToGivePastValueInAverage;
            for (int i = 0; i < infoSets.Count(); i++)
            {
                double total = 0;
                InformationSetNode infoSet = infoSets[i];
                for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                {
                    double v = actionProbabilities[totalNumbersProcessed++];
                    total += v;
                    double weightedCurrentAverageValue = infoSet.GetAverageStrategiesAsArray()[a - 1] * weightToGivePastValueInAverage;
                    double revisedAverage = weightedCurrentAverageValue + v * weightToGiveNewValueInAverage;
                    infoSet.SetCurrentAndAverageStrategyValues(a, v, revisedAverage);
                }
                if (total == 0)
                {
                    // This is a redundant check for ECTA.
                    // This information set cannot be reached. Use even probabilities.
                    double v = 1.0 / (double)infoSet.Decision.NumPossibleActions;
                    for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                    {
                        double weightedCurrentAverageValue = infoSet.GetAverageStrategiesAsArray()[a - 1] * weightToGivePastValueInAverage;
                        double revisedAverage = weightedCurrentAverageValue + v * weightToGiveNewValueInAverage;
                        infoSet.SetCurrentAndAverageStrategyValues(a, v, revisedAverage);
                    }
                }
                if (recordProbabilitiesAsPastValues)
                    infoSet.RecordProbabilitiesAsPastValues();
            }
        }

        [SupportedOSPlatform("windows")]
        private async Task AddReportForEquilibrium(ReportCollection reportCollection, int numEquilibria, int eqNum)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.CurrentProbability }; // will use latest equilibrium 
            var reportResult = await GenerateReports(EvolutionSettings.ReportEveryNIterations ?? 0,
                () =>
                    $"{GameDefinition.OptionSetName}{(EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria > 1 ? $"-Eq{eqNum + 1}" : "")}");
            reportCollection.Add(reportResult, false, true);
            GenerateManualReports($"-eq{eqNum + 1}");
            TabbedText.WriteLine($"Elapsed milliseconds report for eq {eqNum + 1} of {numEquilibria}: {s.ElapsedMilliseconds}");
        }

        private void AddCorrelatedEquilibriumReport(ReportCollection reportCollection)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            var reportResult = GenerateReportFromSavedWeightedGameProgresses(false);
            reportResult.AddName($"{GameDefinition.OptionSetName}{("-Corr")}");
            PrintReportsToScreenIfNotSuppressed(reportResult);
            reportCollection.Add(reportResult, false, true);
            GenerateManualReports($"-Corr");
            SavedWeightedGameProgresses = new List<(GameProgress theProgress, double weight)>();
            TabbedText.WriteLine($"Elapsed milliseconds generating correlated equilibrium report: {s.ElapsedMilliseconds}");
        }

        [SupportedOSPlatform("windows")]
        private async Task AddAverageEquilibriumReport(ReportCollection reportCollection)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.AverageStrategy }; 
            var reportResult = await GenerateReports(EvolutionSettings.ReportEveryNIterations ?? 0,
                () => $"{GameDefinition.OptionSetName}-Avg", suppressPrintTree: true);
            reportCollection.Add(reportResult, false, true);
            GenerateManualReports($"-Avg");
            TabbedText.WriteLine($"Elapsed milliseconds generating average equilibrium report: {s.ElapsedMilliseconds}");
        }

        #endregion
    }
}

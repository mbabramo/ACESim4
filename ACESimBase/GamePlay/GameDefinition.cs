using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using ACESimBase.Util;
using ACESimBase.GameSolvingSupport;

namespace ACESim
{
    [Serializable]
    public class GameDefinition
    {
        public string OptionSetName;
        public GameOptions GameOptions;

        public IGameFactory GameFactory;

        public List<PlayerInfo> Players;

        public string[] PlayerNames;
        public IEnumerable<string> NonChancePlayerNames => Players.Where(x => !x.PlayerIsChance).Select(x => x.PlayerName);

        /// <summary>
        /// The number of players in the game, including chance (if applicable).
        /// </summary>
        public byte NumPlayers;

        public virtual byte PlayerIndex_ResolutionPlayer => throw new NotImplementedException();

        public List<GameModule> GameModules;


        public List<string> GameModuleNamesGameReliesOn;


        public List<int> GameModuleNumbersGameReliesOn;


        public List<ActionGroupRepetition> AutomaticRepetitions;

        /// <summary>
        /// A list of all execution groups, in execution order. Note that a single decision may be included multiple times, but if so, it must be evolved mutliple times
        /// </summary>

        public List<ActionGroup> ExecutionOrder;

        /// <summary>
        /// In a nonmodular game, the game definition should set these directly. In a modular game, this will be set automatically from the decisions in ExecutionOrder. Either way, each instance in this list represents a separately evolved decision. So, if a decision is repeated in execution, a single Decision object will be included multiple times. The index into this list represents the decision number
        /// </summary>

        public List<Decision> DecisionsExecutionOrder;


        public List<ActionPoint> DecisionPointsExecutionOrder;

        /// <summary>
        /// The index into ExecutionOrder for each decision in DecisionPointsExecutionOrder.
        /// </summary>

        private List<int> ExecutionOrderIndexForEachDecision;

        /// <summary>
        /// The index into ActionPoints within the ActionGroup indexed by ActionGroupNumberForEachDecision.
        /// </summary>

        private List<int> ActionPointIndexForEachDecision;

        public ActionPoint DecisionPointForDecisionNumber(int decisionNumber)
        {
            return ExecutionOrder[ExecutionOrderIndexForEachDecision[decisionNumber]].ActionPoints[ActionPointIndexForEachDecision[decisionNumber]] as ActionPoint;
        }

        public GameModule GetOriginalGameModuleForDecisionNumber(int decisionNumber)
        {
            ActionPoint dp = DecisionPointForDecisionNumber(decisionNumber);
            return GameModules[(int)dp.ActionGroup.ModuleNumber];
        }


        private bool DecisionsSetFromModules = false; // have we initialized this yet


        public virtual void Initialize(IGameFactory gameFactory)
        {
            if (DecisionsSetFromModules)
                return;
            DecisionsSetFromModules = true;

            if (GameModules == null)
                InitializeNonmodularGame();
            else
                InitializeModularGame();

            GameFactory = gameFactory;
        }

        public virtual void Setup(GameOptions gameOptions)
        {
            GameOptions = gameOptions;
        }

        private void InitializeModularGame()
        {
            // Modules need to be able to find one another, regardless of their order. We thus set module numbers, and then have a list of 
            // game module numbers that the game relies on and that each module relies on, so that the game and individual modules can then
            // look up specific modules by these saved numbers, which will then work even if the order changes.
            SetModuleNumbers();
            SetGameModuleNumbersGameReliesOn();
            foreach (GameModule gm in GameModules)
                gm.SetGameModuleNumbersThisModuleReliesOn(GameModules);

            // -- Start with a list of execution groups
            List<ActionGroup> initialActionGroupList = new List<ActionGroup>();
            foreach (GameModule gm in GameModules)
                initialActionGroupList.AddRange(gm.GetActionGroupsForModule());
            // -- Get the relative order of this initial list, and then allow each module to modify it.
            ExecutionOrder = ModifyActionGroupList(initialActionGroupList, forEvolution: false);
            // -- Set various class variables based on the execution list.
            ProcessExecutionOrderList();
            // -- Duplicate the execution order list, turn it into an evolution order list, and process it.
            List<ActionGroup> executionOrderCopy = ExecutionOrder.Select(x => x.DeepCopy()).ToList();

            //PrintOutOrderingInformation();
        }

        private void InitializeNonmodularGame()
        {
            ActionGroup exGroup =
                new ActionGroup()
                {
                    Name = "GameGroup"
                };
            exGroup.ActionPoints = new List<ActionPoint>();
            byte decisionNum = 0;
            foreach (var gamewideDecision in DecisionsExecutionOrder)
            {
                exGroup.ActionPoints.Add(
                    new ActionPoint() { DecisionNumber = decisionNum, DecisionNumberWithinActionGroup = decisionNum, Name = gamewideDecision.Name, Decision = DecisionsExecutionOrder[decisionNum], ActionGroup = exGroup }
                );
                decisionNum++;
            }
            ExecutionOrder = new List<ActionGroup>
            {
                exGroup
            };
            ProcessExecutionOrderList();
        }

        public void SetModuleNumbers()
        {
            byte moduleNumber = 0;
            foreach (var module in GameModules)
            {
                module.ModuleNumber = moduleNumber;
                moduleNumber++;
            }
        }

        public void SetGameModuleNumbersGameReliesOn()
        {
            GameModuleNumbersGameReliesOn = new List<int>();
            if (GameModuleNamesGameReliesOn == null)
                return;
            foreach (string gmstring in GameModuleNamesGameReliesOn)
            {
                int indexToAdd = GameModules.Select((item, index) => new { Item = item, Index = index }).Single(x => x.Item.GameModuleName == gmstring).Index;
                GameModuleNumbersGameReliesOn.Add(indexToAdd);
            }
        }

        private List<ActionGroup> OrderActionGroups(List<ActionGroup> initialList, bool forEvolution)
        {
            List<ConstrainedPair<ActionGroup>> constraints = new List<ConstrainedPair<ActionGroup>>();
            // Go through all pairs of action groups.
            for (int i = 0; i < initialList.Count; i++)
                for (int j = 0; j < initialList.Count; j++)
                    if (i != j)
                    {
                        OrderingConstraint? constraint = GameModules[(int)initialList[i].ModuleNumber].DetermineOrderingConstraint(initialList, initialList[i], initialList[j], forEvolution);
                        if (constraint != null)
                        {
                            constraints.Add(new ConstrainedPair<ActionGroup>() { Constraint = (OrderingConstraint)constraint, First = initialList[i], Second = initialList[j] });
                        }
                    }
            List<ActionGroup> reorderedList = ConstrainedOrder<ActionGroup>.Order(initialList, constraints);
            return reorderedList;
        }

        private List<ActionGroup> ModifyActionGroupList(List<ActionGroup> initialActionGroupList, bool forEvolution)
        {
            List<ActionGroup> modifiedList;
            modifiedList = OrderActionGroups(initialActionGroupList, forEvolution: forEvolution);
            if (!forEvolution)
                AddAutomaticExecutionRepetitions(modifiedList);
            for (int i = 0; i <= 1; i++)
                foreach (GameModule gm in GameModules)
                    gm.ModifyActionGroupList(ref modifiedList, forEvolution: forEvolution, secondPass: i == 1);

            return modifiedList;
        }


        private void SetFirstAndPreviousRepetitionsForTags(List<ActionGroup> actionGroupList)
        {
            foreach (ActionGroup ag in actionGroupList)
                ag.SetFirstAndPreviousRepetitionsFromList(actionGroupList);
        }

        private void AddAutomaticExecutionRepetitions(List<ActionGroup> actionGroupList)
        {
            foreach (ActionGroupRepetition agr in AutomaticRepetitions)
            {
                if (agr.Tag != null && agr.Tag.Trim() != "")
                {
                    // find ranges of items to repeat
                    List<int> indicesWithTag = actionGroupList.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Item.ContainsTag(agr.Tag) || agr.Tag == "All").Select(x => x.Index).ToList();
                    List<Tuple<int, int>> rangesOfItemsToRepeat = GetRangesOfConsecutiveNumbers(indicesWithTag);
                    rangesOfItemsToRepeat.Reverse(); // reverse order so that we can insert at end first, without affecting earlier insertion points
                    // go through each set of items to repeat
                    foreach (Tuple<int, int> range in rangesOfItemsToRepeat)
                    {
                        List<ActionGroup> itemsToAdd = new List<ActionGroup>();
                        // add items for each repetition
                        for (int r = 1; r <= agr.Repetitions; r++)
                        {
                            // go through each item potentially to be repeated
                            for (int index = range.Item1; index <= range.Item2; index++)
                            {
                                bool excludeItem = (r == 1 && agr.TagToOmitFirstTime != "" && actionGroupList[index].ContainsTag(agr.TagToOmitFirstTime))
                                    || (r == agr.Repetitions && agr.TagToOmitLastTime != "" && actionGroupList[index].ContainsTag(agr.TagToOmitLastTime));
                                if (!excludeItem)
                                { // create a new action group, with the repetition information
                                    ActionGroup copyOfActionGroup = actionGroupList[index].DeepCopy();
                                    copyOfActionGroup.SetRepetitionForTag(agr.Tag, r, r == 1, r == agr.Repetitions);
                                    itemsToAdd.Add(copyOfActionGroup);
                                }
                            }
                        }
                        // replace the existing items with the repetition set
                        actionGroupList.RemoveRange(range.Item1, range.Item2 - range.Item1 + 1);
                        actionGroupList.InsertRange(range.Item1, itemsToAdd);
                    }
                }
            }
        }

        /// <summary>
        /// Given a list of numbers, return all ranges of numbers.
        /// E.g., 1, 2, 3, 5, 7, 8 ==> {1, 3}, {5, 5}, {7, 8}
        /// </summary>
        /// <param name="numbers"></param>
        /// <returns></returns>
        private static List<Tuple<int, int>> GetRangesOfConsecutiveNumbers(List<int> numbers)
        {
            List<Tuple<int, int>> ranges = new List<Tuple<int, int>>();
            if (numbers.Any())
            {
                int currentStartRange = numbers[0];
                int currentEndRange = numbers[0];
                int i = 1;
                while (i < numbers.Count)
                {
                    if (numbers[i] == (int)currentEndRange + 1)
                        currentEndRange = (int)currentEndRange + 1; // extend the current range
                    else
                    { // add the current range to the list, and start a new range
                        ranges.Add(new Tuple<int, int>(currentStartRange, currentEndRange));
                        currentStartRange = currentEndRange = numbers[i];
                    }
                    i++;
                }
                ranges.Add(new Tuple<int, int>(currentStartRange, currentEndRange)); // add last range to the list
            }
            return ranges;
        }

        private void ProcessExecutionOrderList()
        {
            DecisionsExecutionOrder = new List<Decision>(); // we have a list of just Decisions since that was easier to put in xml files for gamewide decisions, but we'll rebuild it here
            DecisionPointsExecutionOrder = new List<ActionPoint>(); // this has more information
            ExecutionOrderIndexForEachDecision = new List<int>();
            ActionPointIndexForEachDecision = new List<int>();
            if (GameModules == null)
                GameModules = new List<GameModule>();
            byte[] decisionsProcessedWithinModules = new byte[GameModules.Count()];
            string[] lastRepetitionTagProcessedForModule = new string[GameModules.Count()];
            for (int m = 0; m < GameModules.Count(); m++)
            {
                decisionsProcessedWithinModules[m] = 0;
                lastRepetitionTagProcessedForModule[m] = null;
            }
            byte decisionNumber = 0;
            for (byte actionGroupIndex = 0; actionGroupIndex < ExecutionOrder.Count; actionGroupIndex++)
            {
                ActionGroup ag = ExecutionOrder[actionGroupIndex];
                ag.ActionGroupExecutionIndex = actionGroupIndex;
                byte decisionNumberWithinActionGroup = 0;
                for (int actionPointIndex = 0; actionPointIndex < (ag.ActionPoints == null ? 0 : ag.ActionPoints.Count); actionPointIndex++)
                {
                    ActionPoint ap = ag.ActionPoints[actionPointIndex];
                    ap.ActionPointIndex = actionPointIndex;
                    if (ap.Decision != null)
                    {
                        ActionPoint dp = ((ActionPoint)ap);
                        ExecutionOrderIndexForEachDecision.Add(actionGroupIndex);
                        ActionPointIndexForEachDecision.Add(actionPointIndex);
                        dp.DecisionNumber = decisionNumber;
                        if (ag.FirstDecisionNumber == null)
                            ag.FirstDecisionNumber = decisionNumber;
                        ag.LastDecisionNumber = decisionNumber;

                        dp.DecisionNumberWithinActionGroup = decisionNumberWithinActionGroup;
                        if (ag.ModuleNumber != null)
                        {
                            if (lastRepetitionTagProcessedForModule[(int)ag.ModuleNumber] != null && lastRepetitionTagProcessedForModule[(int)ag.ModuleNumber] != ag.RepetitionTagString())
                                decisionsProcessedWithinModules[(int)ag.ModuleNumber] = 0;
                            dp.DecisionNumberWithinModule = decisionsProcessedWithinModules[(int)ag.ModuleNumber];
                            decisionsProcessedWithinModules[(int)ag.ModuleNumber]++;
                            lastRepetitionTagProcessedForModule[(int)ag.ModuleNumber] = ag.RepetitionTagString();
                        }

                        DecisionsExecutionOrder.Add(dp.Decision); // note that the same Decision object can be included multiple times on this list
                        DecisionPointsExecutionOrder.Add(dp);
                        decisionNumber++;
                        decisionNumberWithinActionGroup++;
                    }
                }
            }
            if (GameModules != null)
                foreach (var module in GameModules)
                {
                    ActionPoint dp = DecisionPointsExecutionOrder.FirstOrDefault(x => x.ActionGroup.ModuleNumber == module.ModuleNumber);
                    if (dp != null)
                        module.FirstDecisionNumberInGameModule = dp.DecisionNumber;
                }
            SetFirstAndPreviousRepetitionsForTags(ExecutionOrder);
        }

        public void PrintOutOrderingInformation()
        {
            PrintOutOrderedDecisionPoints("Execution Order of Decisions", DecisionPointsExecutionOrder);
            //PrintOutActionGroupList("Execution Order of Action Groups", ExecutionOrder);
        }

        private void PrintOutActionGroupList(string header, List<ActionGroup> actionGroupList)
        {
            TabbedText.WriteLine(header);
            TabbedText.TabIndent();
            foreach (ActionGroup ag in actionGroupList)
                TabbedText.WriteLine(ag.ToString());
            TabbedText.TabUnindent();
        }

        private static void PrintOutOrderedDecisionPoints(string heading, List<ActionPoint> decisionPointsToPrint)
        {
            TabbedText.WriteLine("");
            TabbedText.WriteLine(heading);
            TabbedText.TabIndent();
            foreach (ActionPoint dp in decisionPointsToPrint)
            {
                string repetitionTag = dp.ActionGroup.RepetitionTagString();
                if (repetitionTag != "")
                    repetitionTag = " (Repetition:" + repetitionTag + ")";
                TabbedText.WriteLine($"{dp.DecisionNumber} {dp.Decision}");
            }
            TabbedText.TabUnindent();
        }

        public virtual void CalculateDistributorChanceInputDecisionMultipliers()
        {
            int multiplier = 1;
            foreach (var decision in DecisionsExecutionOrder.Where(x => x.DistributorChanceInputDecision))
            {
                decision.DistributorChanceInputDecisionMultiplier = multiplier;
                multiplier *= (decision.NumPossibleActions + 1); // plus one since the actions are one-based (a zero would indicate a skipped decision).
            }
        }

        public virtual double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            return null; // subclass should define if needed
        }

        public virtual bool ShouldMarkGameHistoryComplete(Decision currentDecision, in GameHistory gameHistory, byte actionChosen)
        {
            // Entirely subclass. During full game play, the game marks the game complete as necessary, and this automatically calls
            // the game history. But during cached game play (without using a game tree), we must determine whether the game is complete
            // solely by looking at the game history.
            return false;
        }

        public virtual void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte action, ref GameHistory gameHistory, GameProgress gameProgress)
        {
            // Entirely subclass. This method allow one to change information sets in ways other than mechanically adding items to an information set.
            // It is defined in the GameDefinition, rather than in the Game, because it needs to be called when playing a cached game (i.e., going through 
            // the game tree without creating Game or GameProgress objects).
        }

        public virtual void ReverseSwitchToBranchEffects(Decision decisionToReverse, in HistoryPoint historyPoint)
        {
            // SwitchToBranch does not change the original HistoryPoint object because it can't (HistoryPoint is readonly), and it doesn't change the 
            // fields of the GameHistory (which is not readonly, but is a struct and thus can't be changed within the readonly HistoryPoint). Thus,
            // SwitchToBranch produces a new HistoryPoint. 
            GameHistory gameHistory = historyPoint.HistoryToPoint;
            if (gameHistory.IsEmpty)
                return; // we aren't tracking the game history (maybe because we are using a game tree instead of cached history)
            if (decisionToReverse.PlayersToInform != null)
                    gameHistory.ReverseAdditionsToInformationSet(decisionToReverse.PlayersToInform, null);
            if (decisionToReverse.IncrementGameCacheItem != null)
                foreach (byte cacheIndex in decisionToReverse.IncrementGameCacheItem)
                    gameHistory.DecrementItemAtCacheIndex(cacheIndex);
            if (decisionToReverse.StoreActionInGameCacheItem != null)
                gameHistory.SetCacheItemAtIndex((byte)decisionToReverse.StoreActionInGameCacheItem, 0);

            if (historyPoint.GameProgress != null)
            {
                throw new Exception("Cannot use reverse switch to branch when using GameProgress for navigation, because we don't have a way of rolling back the GameProgress itself.");
                // if we enable rolling back gameprogress, then we will need to also update various fields within gameprogress. Overall, this probably is not worth it.
                //historyPoint.GameProgress.GameComplete = false;
               // historyPoint.GameProgress.GameHistoryStorable = gameHistory.DeepCopy();
                //historyPoint.GameProgress.GameFullHistoryStorable.MarkIncomplete();
            }

            //NOTE: Originally, we were undoing all effects on the HistoryPoint object. But now HistoryPoint is readonly. Changing gameHistory will have no effect,
            //since that is just a copy. Meanwhile, it's no longer necessary to make the following changes, because we always keep the original HistoryPoint, which
            //will thus have the original GameHistory before SwitchToBranch was called.
            //gameHistory.RemoveLastActionFromSimpleActionsList();
            //gameHistory.Complete = false; // just in case it was marked true
            //historyPoint = historyPoint.WithHistoryToPoint(gameHistory).WithGameState(originalGameState);
        }

        public void GetNextDecision(in GameHistory gameHistory, out Decision decision, out byte nextDecisionIndex)
        {
            if (gameHistory.IsComplete())
            {
                decision = null;
                nextDecisionIndex = 255;
                return;
            }
            byte? lastDecisionIndex = gameHistory.LastDecisionIndexAdded;
            if (lastDecisionIndex == 255)
            {
                decision = DecisionsExecutionOrder[0];
                nextDecisionIndex = 0; // note: first decision is not skippable
                return;
            }
            nextDecisionIndex = (byte)lastDecisionIndex;
            int numDecisionsExecutionOrder = DecisionsExecutionOrder.Count();
            do
            {
                nextDecisionIndex++;
                decision = DecisionsExecutionOrder[nextDecisionIndex]; 
                // NOTE: If we get an argument out of range exception here, it can be caused by failure to properly note that game is complete in GameDefinition.ShouldMarkGameDefinitionComplete or to specify CanTerminateGame = true for all decisions with that property, including the last one.
            } while (nextDecisionIndex < numDecisionsExecutionOrder - 1 && SkipDecision(decision, in gameHistory));
        }

        public virtual bool SkipDecision(Decision decision, in GameHistory gameHistory)
        {
            return false;
        }


        /// <summary>
        /// If there are multiple chance players, then some algorithms will not require each chance player's information set to know of each other chance decision.
        /// But some algorithms do -- e.g., the ECTA sequence form algorithm requires a single chance player, who must have perfect recall of all chance decisions.
        /// </summary>
        public void MakeAllChanceDecisionsKnowAllChanceActions()
        {
            foreach (var decision in DecisionsExecutionOrder.Where(x => x.IsChance))
            {
                var playersToInform = decision.PlayersToInform ?? new byte[0];
                int numPlayers = Players.Count();
                List<byte> revisedPlayersToInform = playersToInform.Where(x => x <= 1).ToList();
                for (int i = 2; i < numPlayers; i++)
                    revisedPlayersToInform.Add((byte)i);
                decision.PlayersToInform = revisedPlayersToInform.ToArray();
            }
        }

        /// <summary>
        /// Specifies whether the game is a two-player symmetric game. In such a game, for each player 0 information set, there is a corresponding player 1 information set, but some information actions and decisions may appear in reverse. Final utilities will also be exactly reversed; that is the utilities player 0 earns in a particular resolution information set is the same as what player 1 earns in the corresponding resolution information set. In a symmetric game, both players must make all their decisions simultaneously, each without knowing the decision of the other. 
        /// </summary>
        /// <returns></returns>
        public virtual bool GameIsSymmetric() => false;
        

        public virtual List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            return new List<SimpleReportDefinition>();
        }


        // It may make sense to use play multiple scenarios (a) where there is a large cost to initialization and each scenario can share the same initialization (so that we don't have to, for example, calculate accelerated best response multiple times); or (b) we want to use different settings during a "warmup phase".

        // When playing multiple scenarios, we can define one or more concluding scenarios

        public virtual bool PlayMultipleScenarios => false;
        public virtual int NumPostWarmupPossibilities => 1;
        public virtual int NumWarmupPossibilities => 0;
        public virtual int WarmupIterations_IfWarmingUp => 200;
        public bool UseDifferentWarmup => PlayMultipleScenarios && (NumWarmupPossibilities > 0 || NumDifferentWeightsOnOpponentsStrategy > 0);
        public int NumScenariosToInitialize => PlayMultipleScenarios ? NumPostWarmupPossibilities + NumWarmupPossibilities : 1;
        public int? IterationsForWarmupScenario => UseDifferentWarmup ? (int?) WarmupIterations_IfWarmingUp : (int?) null;


        public virtual bool MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy => false;
        public virtual int NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios => 25;
        public virtual bool VaryWeightOnOpponentsStrategySeparatelyForEachPlayer => false;
        public int NumDifferentWeightsOnOpponentsStrategyPerPlayer => MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy ? NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios
            : 1;
        public int NumDifferentWeightsOnOpponentsStrategy => MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy ? ( VaryWeightOnOpponentsStrategySeparatelyForEachPlayer
             ? NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios * NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios : NumDifferentWeightsOnOpponentsStrategyPerWarmupScenario_IfMultiplyingScenarios) 
            : 1;
        public virtual (double, double) MinMaxWeightOnOpponentsStrategyDuringWarmup => (-1, 1); // weight will be relatively small early and then increase to random numbers in range 

        public static double GetParameterInRange(double min, double max, int permutationIndex, int numPermutations) => min + (max - min) * (((double)permutationIndex) / ((double)(numPermutations - 1)));
        public double WeightOnOpponentsStrategyDuringWarmup(int weightsPermutationValue) =>  MultiplyWarmupScenariosByAlteringWeightOnOpponentsStrategy ? 
            (GetParameterInRange(MinMaxWeightOnOpponentsStrategyDuringWarmup.Item1, MinMaxWeightOnOpponentsStrategyDuringWarmup.Item2, weightsPermutationValue, NumDifferentWeightsOnOpponentsStrategyPerPlayer))
            : 0;
        private int WarmupsContributionToPermutations => (NumWarmupPossibilities == 0 ? 1 : NumWarmupPossibilities);
        public int NumScenarioPermutations => PlayMultipleScenarios ? NumPostWarmupPossibilities * WarmupsContributionToPermutations * NumDifferentWeightsOnOpponentsStrategy : 1;

        public List<List<int>> AllScenarioPermutations;
        public int[] PostWarmupScenarioIndices; // the indices in AllScenarioPermutations corresponding to the first scenario for each post-warmup possibility

        public virtual double MakeMarginalChangeToTestInformationSetPressure(bool changeToAlternate)
        {
            return 0;
        }

        public (int indexInPostWarmupScenarios, int? indexInWarmupScenarios, double weightOnOpponentsStrategyP0, double weightOnOpponentsStrategyOtherPlayers) GetScenarioIndexAndWeightValues(int overallScenarioIndex, bool warmupPhase)
        {
            // e.g., suppose we have two postwarmup scenarios and three warmup scenarios, multiplied by five weight options.
            // Then there are a total of 17 initialized scenarios but 30 scenario indices, since each postwarmup scenario must
            // be permuted with every warmup-weight permutation.
            if (AllScenarioPermutations == null)
            {
                AllScenarioPermutations = PermutationMaker.GetPermutations(new List<int>() { NumPostWarmupPossibilities, WarmupsContributionToPermutations, NumDifferentWeightsOnOpponentsStrategyPerPlayer, VaryWeightOnOpponentsStrategySeparatelyForEachPlayer ? NumDifferentWeightsOnOpponentsStrategyPerPlayer : 1 }, true);
                RandomSubset.Shuffle(AllScenarioPermutations, 1); // note that we will use a fixed random seed (can't be zero) to get consistent results (we won't be running this twice)
                //AllScenarioPermutations = AllScenarioPermutations.OrderBy(x => Math.Abs(WeightOnOpponentsStrategyDuringWarmup(x[2]))).ThenBy(x => Math.Abs(WeightOnOpponentsStrategyDuringWarmup(x[3]))).ToList(); // place lowest absolute weight values first
                if (!VaryWeightOnOpponentsStrategySeparatelyForEachPlayer)
                    foreach (var p in AllScenarioPermutations)
                        p[3] = p[2];
                PostWarmupScenarioIndices = AllScenarioPermutations.Select((item, index) => (item, index)).GroupBy(x => x.item[0]).Select(x => x.First().index).ToArray();
            }
            List<int> permutation = AllScenarioPermutations[overallScenarioIndex];
            int postWarmupPermutationValue = permutation[0];
            if (!warmupPhase)
                return (postWarmupPermutationValue, null, 0.0, 0.0);
            if (!UseDifferentWarmup) // i.e., NumWarmupOptionSets == 0
                throw new Exception("Not using different warmup");
            int warmupPermutationValue = permutation[1];
            if (NumWarmupPossibilities == 0 || NumWarmupPossibilities == 1)
                warmupPermutationValue = -1; // there are no warmup sets, so we want to use the last postwarmup set as the warmup set
            int weightsPermutationValueP0 = permutation[2];
            int weightsPermutationValueOtherPlayers = permutation[3];
            return (postWarmupPermutationValue, NumPostWarmupPossibilities + warmupPermutationValue,  WeightOnOpponentsStrategyDuringWarmup(weightsPermutationValueP0), WeightOnOpponentsStrategyDuringWarmup(weightsPermutationValueOtherPlayers));

        }

        public int CurrentOverallScenarioIndex = 0;
        public int CurrentPostWarmupScenarioIndex = 0; // we may be playing a warmup scenario, but we still need to know this scenario for purposes of setting the utilities in each individual node
        public int? CurrentWarmupScenarioIndex = null;
        public bool CurrentlyWarmingUp => CurrentWarmupScenarioIndex != null;
        public double CurrentWeightOnOpponentP0 = 0;
        public double CurrentWeightOnOpponentOtherPlayers = 0;
        public string ScenarioEquilibriumName = null;

        public virtual void SetScenario(int overallScenarioIndex, bool warmupVersion, bool alwaysResetScenario)
        {
            int originalScenarioIndex = CurrentOverallScenarioIndex;
            double originalWeightOnOpponentP0 = CurrentWeightOnOpponentP0;
            double originalWeightOnOpponentOtherPlayers = CurrentWeightOnOpponentOtherPlayers;
            int? originalWarmupScenarioIndex = CurrentWarmupScenarioIndex;
            CurrentOverallScenarioIndex = overallScenarioIndex;
            (CurrentPostWarmupScenarioIndex, CurrentWarmupScenarioIndex, CurrentWeightOnOpponentP0, CurrentWeightOnOpponentOtherPlayers) = GetScenarioIndexAndWeightValues(overallScenarioIndex, warmupVersion);
            if (originalScenarioIndex != CurrentOverallScenarioIndex || originalWarmupScenarioIndex != CurrentWarmupScenarioIndex || originalWeightOnOpponentP0 != CurrentWeightOnOpponentP0 || originalWeightOnOpponentOtherPlayers != CurrentWeightOnOpponentOtherPlayers || alwaysResetScenario)
            {
                ChangeOptionsToCurrentScenario();
                if (PlayMultipleScenarios && !CurrentlyWarmingUp)
                {
                    TabbedText.WriteLine("Scenario post-warmup:" + GetNameForScenario_WithOpponentWeight());
                }
            }
        }

        public string GetNameForScenario_WithOpponentWeight()
        {
            return $"{GetNameForScenario()}{(CurrentWeightOnOpponentP0 == 0 && CurrentWeightOnOpponentOtherPlayers == 0 ? "" : $"(Weight on opponent: {CurrentWeightOnOpponentP0.ToSignificantFigures(3)},{CurrentWeightOnOpponentOtherPlayers.ToSignificantFigures(3)})")}";
        }

        public virtual void RememberOriginalChangeableOptions()
        {

        }
        public virtual void RestoreOriginalChangeableOptions()
        {

        }

        public void ChangeOptionsToOverallScenarioIndex(int overallScenarioIndex, bool warmupVersion)
        {
            var result = GetScenarioIndexAndWeightValues(overallScenarioIndex, warmupVersion);
            if (warmupVersion)
                ChangeOptionsBasedOnScenario(null, result.indexInWarmupScenarios - NumPostWarmupPossibilities);
            else
                ChangeOptionsBasedOnScenario(result.indexInPostWarmupScenarios, null);
        }

        public void ChangeOptionsToCurrentScenario() => ChangeOptionsToOverallScenarioIndex(CurrentOverallScenarioIndex, CurrentlyWarmingUp);

        public virtual void ChangeOptionsBasedOnScenario(int? postWarmupScenarioIndex, int? warmupScenarioIndex)
        {

        }


        public virtual string GetNameForScenario()
        {
            return null;
        }

        public SimpleReportDefinition GetMinimalistReport()
        {
            var colItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, SimpleReportColumnFilterOptions.ProportionOfAll),
            };
            List<SimpleReportFilter> rows = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true),
            };

            return new SimpleReportDefinition(
                "MinimalistReport",
                null,
                rows,
                colItems
            );
        }

        public virtual IEnumerable<(string filename, string reportcontent)> ProduceManualReports(List<(GameProgress theProgress, double weight)> gameProgresses, string supplementalString)
        {
            return new List<(string filename, string reportcontent)>();
        }

        public virtual string GetActionString(byte action, byte decisionByteCode) => action.ToString();

        public virtual (Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> excludeBelow, Func<ConstructGameTreeInformationSetInfo.GamePointNode, bool> includeBelow) GetTreeDiagramExclusions() => (null, null);

        public virtual List<MaybeExact<T>> GetSequenceFormInitialization<T>() where T : MaybeExact<T>, new()
        {
            return null;
        }

    }
}

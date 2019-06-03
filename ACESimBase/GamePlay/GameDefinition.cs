using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using ACESimBase.Util;

namespace ACESim
{
    [Serializable]
    public class GameDefinition
    {
        public IGameFactory GameFactory;

        public List<PlayerInfo> Players;

        public string[] PlayerNames;


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
            TabbedText.Tabs++;
            foreach (ActionGroup ag in actionGroupList)
                TabbedText.WriteLine(ag.ToString());
            TabbedText.Tabs--;
        }

        private static void PrintOutOrderedDecisionPoints(string heading, List<ActionPoint> decisionPointsToPrint)
        {
            TabbedText.WriteLine("");
            TabbedText.WriteLine(heading);
            TabbedText.Tabs++;
            foreach (ActionPoint dp in decisionPointsToPrint)
            {
                string repetitionTag = dp.ActionGroup.RepetitionTagString();
                if (repetitionTag != "")
                    repetitionTag = " (Repetition:" + repetitionTag + ")";
                TabbedText.WriteLine($"{dp.DecisionNumber} {dp.Decision}");
            }
            TabbedText.Tabs--;
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

        public virtual unsafe double[] GetUnevenChanceActionProbabilitiesFromChanceInformationSet(byte decisionByteCode, List<(List<byte>, double)> distributionOfChanceValues)
        {
            return null;
        }

        /// <summary>
        /// This method 
        /// </summary>
        /// <param name="decisionByteCode"></param>
        /// <param name="informationSet"></param>
        /// <returns></returns>
        public virtual unsafe double[] GetUnevenChanceActionProbabilitiesFromChanceInformationSet(byte decisionByteCode, byte* informationSet)
        {
            return null; // subclass should define if needed
        }

        public virtual bool ShouldMarkGameHistoryComplete(Decision currentDecision, ref GameHistory gameHistory, byte actionChosen)
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

        public virtual void ReverseDecision(Decision decisionToReverse, ref HistoryPoint historyPoint, IGameState originalGameState)
        {
            ref GameHistory gameHistory = ref historyPoint.HistoryToPoint;
            if (decisionToReverse.PlayersToInform != null)
                foreach (byte playerIndex in decisionToReverse.PlayersToInform)
                    gameHistory.ReverseAdditionsToInformationSet(playerIndex, 1, null);
            if (decisionToReverse.PlayersToInformOfOccurrenceOnly != null)
                foreach (byte playerIndex in decisionToReverse.PlayersToInformOfOccurrenceOnly)
                    gameHistory.ReverseAdditionsToInformationSet(playerIndex, 1, null);
            if (decisionToReverse.IncrementGameCacheItem != null)
                foreach (byte cacheIndex in decisionToReverse.IncrementGameCacheItem)
                    gameHistory.DecrementItemAtCacheIndex(cacheIndex);
            if (decisionToReverse.StoreActionInGameCacheItem != null)
                gameHistory.SetCacheItemAtIndex((byte)decisionToReverse.StoreActionInGameCacheItem, 0);
            gameHistory.RemoveLastActionFromSimpleActionsList();
            gameHistory.Complete = false; // just in case it was marked true
            historyPoint.GameState = originalGameState;
            if (historyPoint.GameProgress != null || historyPoint.TreePoint != null)
                throw new Exception();
        }

        public void GetNextDecision(ref GameHistory gameHistory, out Decision decision, out byte nextDecisionIndex)
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
            } while (nextDecisionIndex < numDecisionsExecutionOrder - 1 && SkipDecision(decision, ref gameHistory));
        }

        public virtual bool SkipDecision(Decision decision, ref GameHistory gameHistory)
        {
            return false;
        }

        public virtual List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            return new List<SimpleReportDefinition>();
        }

        public void AddPotentiallySubdividableDecision(List<Decision> decisionsList, Decision decision, bool subdivide, byte subdivisionDecisionByteCode, byte numOptionsPerBranch, byte aggregateNumPossibleActions)
        {
            if (subdivide)
            {
                decision.Subdividable = true;
                decision.Subdividable_CorrespondingDecisionByteCode = subdivisionDecisionByteCode;
                decision.Subdividable_NumOptionsPerBranch = numOptionsPerBranch;
                int n = aggregateNumPossibleActions;
                while (n > 1)
                {
                    n /= 2;
                    decision.Subdividable_NumLevels++;
                }
            }
            decision.AddDecisionOrSubdivisions(decisionsList);
        }
    }
}

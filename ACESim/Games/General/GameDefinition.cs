using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class GameDefinition
    {

        [OptionalSetting]
        public List<GameModule> GameModules;

        [OptionalSetting]
        public List<string> GameModuleNamesGameReliesOn;

        [OptionalSetting]
        public List<int> GameModuleNumbersGameReliesOn;

        /// <summary>
        /// If true, then the execution order of action groups is initially reversed to determine evolution order, before any other changes are made.
        /// </summary>
        [OptionalSetting]
        public bool ActionGroupsEvolveInOppositeOrderFromExecution;

        [OptionalSetting]
        public List<ActionGroupRepetition> AutomaticRepetitions;

        /// <summary>
        /// A list of all execution groups, in execution order. Note that a single decision may be included multiple times, but if so, it must be evolved mutliple times
        /// </summary>
        [InternallyDefinedSetting]
        public List<ActionGroup> ExecutionOrder;

        /// <summary>
        /// A list of all execution groups, in evolution order. Note that a decision that is executed only once may be included multiple times.
        /// </summary>
        [InternallyDefinedSetting]
        public List<ActionGroup> EvolutionOrder;


        /// <summary>
        /// In a nonmodular game, the game definition should set these directly. In a modular game, this will be set automatically from the decisions in ExecutionOrder. Either way, each instance in this list represents a separately evolved decision. So, if a decision is repeated in execution, a single Decision object will be included multiple times. The index into this list represents the decision number
        /// </summary>
        [OptionalSetting]
        public List<Decision> DecisionsExecutionOrder;

        [InternallyDefinedSetting]
        public List<DecisionPoint> DecisionPointsExecutionOrder;

        [InternallyDefinedSetting]
        public List<DecisionPoint> DecisionPointsEvolutionOrder;

        [InternallyDefinedSetting]
        public List<Tuple<string, List<int?>>> AutomaticRepetitionsIndexNumbersInEvolutionOrder;

        [InternallyDefinedSetting]
        public List<int> DecisionIndexForEachCumulativeDistributionsUpdate;

        /// <summary>
        /// The index into ExecutionOrder for each decision in DecisionPointsExecutionOrder.
        /// </summary>
        [InternallyDefinedSetting]
        private List<int> ExecutionOrderIndexForEachDecision;

        /// <summary>
        /// The index into ActionPoints within the ActionGroup indexed by ActionGroupNumberForEachDecision.
        /// </summary>
        [InternallyDefinedSetting]
        private List<int> ActionPointIndexForEachDecision;

        public DecisionPoint DecisionPointForDecisionNumber(int decisionNumber)
        {
            return ExecutionOrder[ExecutionOrderIndexForEachDecision[decisionNumber]].ActionPoints[ActionPointIndexForEachDecision[decisionNumber]] as DecisionPoint;
        }

        public GameModule GetOriginalGameModuleForDecisionNumber(int decisionNumber)
        {
            DecisionPoint dp = DecisionPointForDecisionNumber(decisionNumber);
            return GameModules[(int) dp.ActionGroup.ModuleNumber];
        }

        [InternallyDefinedSetting]
        private bool DecisionsSetFromModules = false; // have we initialized this yet

        public virtual void Initialize()
        {
            if (DecisionsSetFromModules)
                return;
            DecisionsSetFromModules = true;

            if (GameModules == null)
                InitializeNonmodularGame(); 
            else
                InitializeModularGame();
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
            EvolutionOrder = ModifyActionGroupList(executionOrderCopy, forEvolution: true);
            ProcessEvolutionOrderList();

            PrintOutOrderingInformation();
        }

        private void InitializeNonmodularGame()
        {
            ActionGroup exGroup =
                new ActionGroup()
                {
                    Name = "GameGroup"
                };
            exGroup.ActionPoints = new List<ActionPoint>();
            int decisionNum = 0;
            foreach (var gamewideDecision in DecisionsExecutionOrder)
            {
                exGroup.ActionPoints.Add(
                    new DecisionPoint() { DecisionNumber = decisionNum, DecisionNumberWithinActionGroup = decisionNum, Name = gamewideDecision.Name, Decision = DecisionsExecutionOrder[decisionNum], ActionGroup = exGroup }
                );
                decisionNum++;
            }
            ExecutionOrder = new List<ActionGroup>();
            ExecutionOrder.Add(exGroup);
            ProcessExecutionOrderList();
            EvolutionOrder = ExecutionOrder.Select(x => x.DeepCopy()).ToList();
            ProcessEvolutionOrderList();
        }

        public void SetModuleNumbers()
        {
            int moduleNumber = 0;
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

        internal class ActionGroupOrderingInfo
        {
            public OrderingConstraint Constraint;
            public ActionGroup ActionGroup1;
            public ActionGroup ActionGroup2;
        }

        private List<ActionGroup> OrderActionGroups(List<ActionGroup> initialList, bool forEvolution)
        {
            List<ConstrainedPair<ActionGroup>> constraints = new List<ConstrainedPair<ActionGroup>>();
            // Go through all pairs of action groups.
            for (int i = 0; i < initialList.Count; i++)
                for (int j = 0; j < initialList.Count; j++)
                    if (i != j)
                    {
                        OrderingConstraint? constraint = GameModules[(int) initialList[i].ModuleNumber].DetermineOrderingConstraint(initialList, initialList[i], initialList[j], forEvolution);
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
            if (ActionGroupsEvolveInOppositeOrderFromExecution && forEvolution)
                initialActionGroupList.Reverse();
            modifiedList = OrderActionGroups(initialActionGroupList, forEvolution: forEvolution);
            if (!forEvolution)
                AddAutomaticExecutionRepetitions(modifiedList);
            for (int i = 0; i <= 1; i++)
                foreach (GameModule gm in GameModules)
                    gm.ModifyActionGroupList(ref modifiedList, forEvolution: forEvolution, secondPass: i == 1);

            return modifiedList;
        }

        private void IdentifySubstituteDecisions()
        {
            List<DecisionPoint> decisionPointsUsualOrder = DecisionPointsExecutionOrder;
            IdentifySubstituteDecisionsOneDirection(decisionPointsUsualOrder, true);
            List<DecisionPoint> decisionPointsReversed = DecisionPointsExecutionOrder.ToList();
            decisionPointsReversed.Reverse();
            IdentifySubstituteDecisionsOneDirection(decisionPointsUsualOrder, false);
        }

        private void IdentifySubstituteDecisionsOneDirection(List<DecisionPoint> decisionPointsOrdered, bool evolveOnlyFirstRepetitionRatherThanLastRepetition)
        {
            foreach (DecisionPoint dp in decisionPointsOrdered)
            {
                if ((
                        (evolveOnlyFirstRepetitionRatherThanLastRepetition && dp.Decision.EvolveOnlyFirstRepetitionInExecutionOrder) ||
                        (!evolveOnlyFirstRepetitionRatherThanLastRepetition && dp.Decision.EvolveOnlyLastRepetitionInExecutionOrder)
                    )
                    && dp.SubstituteDecisionNumberInsteadOfEvolving == null)
                {
                    foreach (DecisionPoint dp2 in decisionPointsOrdered)
                    {
                        if (dp2.Decision == dp.Decision && dp2 != dp)
                            dp2.SubstituteDecisionNumberInsteadOfEvolving = dp.DecisionNumber;
                    }
                }
            }
        }

        private void SetFirstAndPreviousRepetitionsForTags(List<ActionGroup> actionGroupList)
        {
            foreach (ActionGroup ag in actionGroupList)
                ag.SetFirstAndPreviousRepetitionsFromList(actionGroupList);
        }
        
        private void AddAutomaticExecutionRepetitions(List<ActionGroup> actionGroupList)
        {
            foreach (ActionGroupRepetition agr in AutomaticRepetitions.Where(x => !x.IsEvolutionRepetitionOnly))
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
            DecisionPointsExecutionOrder = new List<DecisionPoint>(); // this has more information
            ExecutionOrderIndexForEachDecision = new List<int>();
            ActionPointIndexForEachDecision = new List<int>();
            DecisionIndexForEachCumulativeDistributionsUpdate = new List<int>();
            if (GameModules == null)
                GameModules = new List<GameModule>();
            int[] decisionsProcessedWithinModules = new int[GameModules.Count()];
            string[] lastRepetitionTagProcessedForModule = new string[GameModules.Count()];
            for (int m = 0; m < GameModules.Count(); m++)
            {
                decisionsProcessedWithinModules[m] = 0;
                lastRepetitionTagProcessedForModule[m] = null;
            }
            int decisionNumber = 0;
            for (int actionGroupIndex = 0; actionGroupIndex < ExecutionOrder.Count; actionGroupIndex++)
            {
                ActionGroup ag = ExecutionOrder[actionGroupIndex];
                ag.ActionGroupExecutionIndex = actionGroupIndex;
                int decisionNumberWithinActionGroup = 0;
                for (int actionPointIndex = 0; actionPointIndex < (ag.ActionPoints == null ? 0 : ag.ActionPoints.Count); actionPointIndex++)
                {
                    ActionPoint ap = ag.ActionPoints[actionPointIndex];
                    ap.ActionPointIndex = actionPointIndex;
                    if (ap is DecisionPoint)
                    {
                        DecisionPoint dp = ((DecisionPoint)ap);
                        CumulativeDistributionUpdateInfo cdUpdateInfo = ag.ActionGroupSettings as CumulativeDistributionUpdateInfo;
                        if (cdUpdateInfo != null)
                            DecisionIndexForEachCumulativeDistributionsUpdate.Add(decisionNumber);
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
                    DecisionPoint dp = DecisionPointsExecutionOrder.FirstOrDefault(x => x.ActionGroup.ModuleNumber == module.ModuleNumber);
                    if (dp != null)
                        module.FirstDecisionNumberInGameModule = dp.DecisionNumber;
                }
            IdentifySubstituteDecisions();
            SetupSubsequentDecisionsShortcut();
            SetFirstAndPreviousRepetitionsForTags(ExecutionOrder);
        }

        private void SetupSubsequentDecisionsShortcut()
        {
            int numberDecisionsAfterFirst = 0; // the first decision is the one that will record scores for subsequent decisions with same input
            for (int d = 1; d < DecisionPointsExecutionOrder.Count(); d++)
            {
                Decision decision = DecisionPointsExecutionOrder[d].Decision;
                bool isDecisionToUseShortcutFor = decision.InputsAndOccurrencesAlwaysSameAsPreviousDecision && decision.ScoreRepresentsCorrectAnswer && decision.OversamplingWillAlwaysBeSameAsPreviousDecision && !decision.DisablePrescoringForThisDecision && DecisionPointsExecutionOrder[d].DecisionNumber == DecisionPointsExecutionOrder[d - 1].DecisionNumber + 1;
                if (isDecisionToUseShortcutFor)
                {
                    numberDecisionsAfterFirst++;
                    DecisionPointsExecutionOrder[d - numberDecisionsAfterFirst].Decision.SubsequentDecisionsToRecordScoresFor = numberDecisionsAfterFirst;
                    DecisionPointsExecutionOrder[d].Decision.ScoresRecordedByDecisionNPrevious = numberDecisionsAfterFirst;
                }
                else
                    numberDecisionsAfterFirst = 0;
            }
            for (int d = 0; d < DecisionPointsExecutionOrder.Count(); d++)
            {
                int numberGroupsToCache = 0;
                bool keepLooking = d < DecisionPointsExecutionOrder.Count() - 1 && DecisionPointsExecutionOrder[d].Decision.ScoresRecordedByDecisionNPrevious == null;
                int indexOfFirstInLastKnownGroup = d; // first group won't count in numberGroupsToCache
                int indexOfLastDecisionToRecordOrCache = d;
                while (keepLooking)
                {
                    indexOfLastDecisionToRecordOrCache = indexOfFirstInLastKnownGroup + DecisionPointsExecutionOrder[indexOfFirstInLastKnownGroup].Decision.SubsequentDecisionsToRecordScoresFor;
                    if (numberGroupsToCache > 0)
                        DecisionPointsExecutionOrder[indexOfLastDecisionToRecordOrCache].Decision.DecisionIsLastInGroupOfDecisionsToCache = true;
                    int nextIndexNotIncludedAmongSubsequentDecisions = indexOfLastDecisionToRecordOrCache + 1;
                    if (nextIndexNotIncludedAmongSubsequentDecisions < DecisionPointsExecutionOrder.Count())
                    {
                        Decision possibleBeginningOfAnotherGroup = DecisionPointsExecutionOrder[nextIndexNotIncludedAmongSubsequentDecisions].Decision;
                        if (possibleBeginningOfAnotherGroup.OversamplingWillAlwaysBeSameAsPreviousDecision && possibleBeginningOfAnotherGroup.ScoreRepresentsCorrectAnswer && !possibleBeginningOfAnotherGroup.DisableCachingForThisDecision)
                        {
                            possibleBeginningOfAnotherGroup.DecisionIsFirstInGroupOfDecisionsToCache = true;
                            indexOfFirstInLastKnownGroup = nextIndexNotIncludedAmongSubsequentDecisions;
                            numberGroupsToCache++;
                        }
                        else
                            keepLooking = false;
                    }
                    else
                        keepLooking = false;
                }
                DecisionPointsExecutionOrder[d].Decision.NumberGroupsOfDecisionsToCache = numberGroupsToCache;
                //if (numberGroupsToCache > 0)
                    DecisionPointsExecutionOrder[d].Decision.NumberDecisionsToEitherRecordOrCacheBeyondThisOne = indexOfLastDecisionToRecordOrCache - d;
                Debug.WriteLine("Decision " + d + " " + String.Format("{0, -70}", DecisionPointsExecutionOrder[d].Name) + " subsequent decisions to record " + DecisionPointsExecutionOrder[d].Decision.SubsequentDecisionsToRecordScoresFor + " decision was recorded n ago " + DecisionPointsExecutionOrder[d].Decision.ScoresRecordedByDecisionNPrevious + " number groups to cache " + numberGroupsToCache + " numdecisionsbeyondthis " + DecisionPointsExecutionOrder[d].Decision.NumberDecisionsToEitherRecordOrCacheBeyondThisOne + " firstInGroup " + DecisionPointsExecutionOrder[d].Decision.DecisionIsFirstInGroupOfDecisionsToCache + " lastInGroup " + DecisionPointsExecutionOrder[d].Decision.DecisionIsLastInGroupOfDecisionsToCache);
            }
        }

        private void ProcessEvolutionOrderList()
        {
            DecisionPointsEvolutionOrder = new List<DecisionPoint>();
            for (int actionGroupIndex = 0; actionGroupIndex < EvolutionOrder.Count; actionGroupIndex++)
            {
                ActionGroup ag = EvolutionOrder[actionGroupIndex];
                string repetitionTagString = ag.RepetitionTagString();
                for (int actionPointIndex = 0; actionPointIndex < ag.ActionPoints.Count; actionPointIndex++)
                {
                    ActionPoint ap = ag.ActionPoints[actionPointIndex];
                    if (ap is DecisionPoint)
                    {
                        DecisionPoint dp = ap as DecisionPoint;
                        // find the corresponding decision point from execution order. That way, we can be sure that we have the same objects in the execution order and evolution order list.
                        DecisionPoint correspondingDecisionPointInExecutionOrder = DecisionPointsExecutionOrder.FirstOrDefault(x => x.DecisionNumber == dp.DecisionNumber);
                        if (correspondingDecisionPointInExecutionOrder == null)
                            throw new Exception("Internal error: A decision included in evolution order was excluded from execution order.");
                        if (dp.SubstituteDecisionNumberInsteadOfEvolving == null)
                        {
                            for (int i = 0; i < (dp.Decision.RepeatEvolutionNTimes ?? 1); i++)
                                DecisionPointsEvolutionOrder.Add(correspondingDecisionPointInExecutionOrder);
                        }
                    }
                }
            }
            SetFirstAndPreviousRepetitionsForTags(EvolutionOrder);
            AddAutomaticRepetitionsForEvolutionOnly();
        }

        private void AddAutomaticRepetitionsForEvolutionOnly()
        {
            AutomaticRepetitionsIndexNumbersInEvolutionOrder = new List<Tuple<string, List<int?>>>();
            foreach (ActionGroupRepetition evolutionRepetition in AutomaticRepetitions?.Where(x => x.IsEvolutionRepetitionOnly) ?? new List<ActionGroupRepetition>())
            {
                List<DecisionPoint> decisionPointsEvolutionOrderCopy = new List<DecisionPoint>();
                int? startOfTagRange = null;
                Func<int, bool> dpContainsTag = d => DecisionPointsEvolutionOrder[d].ActionGroup.Tags != null && DecisionPointsEvolutionOrder[d].ActionGroup.Tags.Any(x => x == evolutionRepetition.Tag);
                for (int d = 0; d < DecisionPointsEvolutionOrder.Count(); d++)
                {
                    decisionPointsEvolutionOrderCopy.Add(DecisionPointsEvolutionOrder[d]); // copy in this point at least once
                    if (startOfTagRange == null && dpContainsTag(d)) // this is the beginning of the tag range
                        startOfTagRange = d;
                    if (startOfTagRange != null && (d == DecisionPointsEvolutionOrder.Count() - 1 || !dpContainsTag(d + 1)))
                    { // this is the end of the tag range
                        // copy the range in for the remaining repetitions
                        for (int r = 0; r < evolutionRepetition.Repetitions - 1; r++)
                            for (int i = (int)startOfTagRange; i <= d; i++)
                                decisionPointsEvolutionOrderCopy.Add(DecisionPointsEvolutionOrder[i]);
                        // reset
                        startOfTagRange = null;
                    }
                }
                DecisionPointsEvolutionOrder = decisionPointsEvolutionOrderCopy;
            }
            foreach (ActionGroupRepetition evolutionRepetition in AutomaticRepetitions?.Where(x => x.IsEvolutionRepetitionOnly) ?? new List<ActionGroupRepetition>())
            {
                List<int?> tagsForRepetition = new List<int?>();
                int matchNumber = 1;
                foreach (var dp in DecisionPointsEvolutionOrder)
                {
                    if (dp.ActionGroup.Tags != null && dp.ActionGroup.Tags.Any(x => x == evolutionRepetition.Tag))
                    {
                        tagsForRepetition.Add(matchNumber);
                        matchNumber++;
                    }
                    else
                        tagsForRepetition.Add(null);
                }
                AutomaticRepetitionsIndexNumbersInEvolutionOrder.Add(new Tuple<string, List<int?>>(evolutionRepetition.Tag, tagsForRepetition));
            }
        }

        private void PrintOutOrderingInformation()
        {
            PrintOutOrderedDecisionPoints("Execution Order of Decisions", DecisionPointsExecutionOrder);
            PrintOutActionGroupList("Execution Order of Action Groups", ExecutionOrder);
            PrintOutOrderedDecisionPoints("Evolution Order of Decisions", DecisionPointsEvolutionOrder);
            PrintOutActionGroupList("Evolution Order By Action Group", EvolutionOrder);
        }

        private void PrintOutActionGroupList(string header, List<ActionGroup> actionGroupList)
        {
            TabbedText.WriteLine(header);
            TabbedText.Tabs++;
            foreach (ActionGroup ag in actionGroupList)
                TabbedText.WriteLine(ag.ToString());
            TabbedText.Tabs--;
        }

        private static void PrintOutOrderedDecisionPoints(string heading, List<DecisionPoint> decisionPointsToPrint)
        {
            TabbedText.WriteLine("");
            TabbedText.WriteLine(heading);
            TabbedText.Tabs++;
            foreach (DecisionPoint dp in decisionPointsToPrint)
            {
                string repetitionTag = dp.ActionGroup.RepetitionTagString();
                if (repetitionTag != "")
                    repetitionTag = " (Repetition:" + repetitionTag + ")";
                TabbedText.WriteLine(dp.Name + repetitionTag + " [" + dp.DecisionNumber + "]");
            }
            TabbedText.Tabs--;
        }
    }
}

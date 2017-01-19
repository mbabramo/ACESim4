﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class GameModule
    {
        /// <summary>
        /// A list of decisions. This should be set in the game module xml file or in the GenerateSetting method. After this list is created, the module can override the 
        /// GroupDecisions method to group the decisions into anything other than a single ActionGroup. It can also override the RelativeOrder function to determine the 
        /// relative order of ActionGroups within the module and outside the module, and the ModifyActionGroups function to make further changes, such as repeating sets
        /// of execution groups.
        /// </summary>
        [OptionalSetting]
        public List<Decision> DecisionsCore;

        /// <summary>
        /// If any are listed, then the default will be to create a single ActionGroup with these actions listed at the beginning.
        /// </summary>
        [OptionalSetting]
        public List<string> ActionsAtBeginningOfModule;

        /// <summary>
        /// If any are listed, then the default will be to create a single ExecutionGroup with these actions listed at the end.
        /// </summary>
        [OptionalSetting]
        public List<string> ActionsAtEndOfModule;

        /// <summary>
        /// When creating a default action group, these tags will automatically be added
        /// </summary>
        [OptionalSetting]
        public List<string> Tags;

        /// <summary>
        /// Before a game module is executed, its inputs are copied here.
        /// </summary>
        [InternallyDefinedSetting]
        public GameModuleInputs GameModuleInputs;

        /// <summary>
        /// A name can be given to a game module, so that other modules can find it. Often, this will be the name of the superclass.
        /// </summary>
        [OptionalSetting]
        public string GameModuleName;

        /// <summary>
        /// Modules will be evolved in the order in which they appear. Execution order by default will be the same, with the first module evolving
        /// having an ExecutionNumberOfModule == 1, etc. However, a module can specify an alternative execution number here.
        /// </summary>
        [OptionalSetting]
        public int ExecutionNumberOfModule;

        /// <summary>
        /// If a module needs to access information from other modules, then it can list their names in a comma-delimited string. The GameDefinition will then determine the corresponding game module numbers for the decisions this module relies on, and the relevant game module can then be loaded by calling GetGameModuleThisModuleReliesOn().
        /// </summary>
        [OptionalSetting]
        public List<string> GameModuleNamesThisModuleReliesOn;

        /// <summary>
        /// This is a list of indices into the list of game modules for the game, corresponding to the game module names that this module relies on.
        /// </summary>
        [InternallyDefinedSetting]
        public List<int> GameModuleNumbersThisModuleReliesOn;

        /// <summary>
        /// Settings of a game module that are constant for all iterations can be stored here. Ordinarily, this would be set by GenerateSettings.
        /// </summary>
        public object GameModuleSettings;

        [InternallyDefinedSetting]
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal Game Game;

        [InternallyDefinedSetting]
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal List<Strategy> AllStrategies;

        [InternallyDefinedSetting]
        public int? ModuleNumber;

        [InternallyDefinedSetting]
        public int? FirstDecisionNumberInGameModule;

        [OptionalSetting]
        public bool IgnoreWhenCountingProgress;

        /// <summary>
        /// If this is set, and if the default GroupDecisions is used, then a tag will be set in the only created action group indicating that cumulative distributions should be updated before it.
        /// </summary>
        [OptionalSetting]
        public bool UpdateCumulativeDistributionsBeforeSingleActionGroup;

        /// <summary>
        /// If this is set, and if the default GroupDecisions is used, then a tag will be set in the only created action group indicating that cumulative distributions should be updated after it.
        /// </summary>
        [OptionalSetting]
        public bool UpdateCumulativeDistributionsAfterSingleActionGroup;

        /// <summary>
        /// This should be set when when a cumulative distribution must occur after a set of action groups in execution and after that same set in evolution, rather than after just a single
        /// action group in execution and evolution. For example, bargaining aggressiveness and bargaining are effectively a package, and so this should be set for the bargaining aggressiveness
        /// module. That way, we will have a cumulative distribution that executes after bargaining aggressiveness and bargaining, and is then updated after bargaining and then bargaining aggressiveness
        /// are evolved.
        /// This will be imprinted on the action groups in the default GroupDecisions by adding a tag with the same name to the action group.
        /// </summary>
        [OptionalSetting]
        public bool WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter;

        public virtual void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new NotImplementedException();
        }

        public virtual void ExecuteModule()
        {
            throw new NotImplementedException();
        }

        internal void SetGameAndStrategies(Game theGame, List<Strategy> theStrategies, string gameModuleName, List<string> gameModuleNamesThisModuleReliesOn, List<int> gameModuleNumbersThisModuleReliesOn, object gameModuleSettings)
        {
            Game = theGame;
            AllStrategies = theStrategies;
            GameModuleName = gameModuleName;
            GameModuleNamesThisModuleReliesOn = gameModuleNamesThisModuleReliesOn;
            GameModuleNumbersThisModuleReliesOn = gameModuleNumbersThisModuleReliesOn;
            GameModuleSettings = gameModuleSettings;
        }

        public GameModuleProgress GameModuleProgress
        {
            get
            {
                return Game.Progress.GameModuleProgresses[(int)ModuleNumber];
            }
            set
            {
                Game.Progress.GameModuleProgresses[(int)ModuleNumber] = value;
            }
        }

        public void SetGameModuleFields(GameModule copy)
        {
            copy.FirstDecisionNumberInGameModule = FirstDecisionNumberInGameModule;
            copy.ModuleNumber = ModuleNumber;
            copy.GameModuleName = GameModuleName;
            copy.GameModuleNamesThisModuleReliesOn = GameModuleNamesThisModuleReliesOn;
            copy.GameModuleNumbersThisModuleReliesOn = GameModuleNumbersThisModuleReliesOn;
        }

        internal virtual void Initialize(Game theGame, List<Strategy> theStrategies)
        {
            Game = theGame;
            AllStrategies = theStrategies;
        }

        public virtual double GetScoreForParticularDecision(int decisionIndexWithinActionGroup)
        {
            throw new NotImplementedException("Either override Score for the GameModule or override GetScoreForParticularDecision.");
        }

        public virtual void Score()
        {
            int firstDecisionToScore = (int)Game.CurrentlyEvolvingDecisionIndex;
            int firstDecisionToScoreIndexWithinActionGroup = (int)Game.CurrentlyEvolvingDecisionIndexWithinActionGroup;
            int subsequentDecisionsToInclude = AllStrategies[firstDecisionToScore].Decision.NumberDecisionsToEitherRecordOrCacheBeyondThisOne; // SubsequentDecisionsToRecordScoresFor;
            for (int i = 0; i < 1 + subsequentDecisionsToInclude; i++)
                Game.Score(firstDecisionToScore + i, GetScoreForParticularDecision(firstDecisionToScoreIndexWithinActionGroup + i));
        }

        public virtual List<Tuple<string,string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            return new List<Tuple<string, string>>();
        }

        object inputNamesLock = new object();
        private void SetInputNamesAndAbbreviationsForCurrentDecision()
        {
            Decision currentDecision = Game.CurrentDecision;
            if (currentDecision.InputAbbreviations == null)
            {
                lock (inputNamesLock)
                {
                    if (currentDecision.InputAbbreviations == null)
                    {
                        List<Tuple<string, string>> inputNamesAndAbbreviations = GetInputNamesAndAbbreviations((int)Game.CurrentDecisionIndexWithinActionGroup);
                        currentDecision.InputNames = inputNamesAndAbbreviations.Select(x => x.Item1).ToList();
                        currentDecision.InputAbbreviations = inputNamesAndAbbreviations.Select(x => x.Item2).ToList();
                    }
                }
            }
        }

        public void SpecifyInputs(List<double> inputs)
        {
            if (Game.CurrentlyEvolvingCurrentlyExecutingDecision)
                GameModuleProgress.InputsOfCurrentlyEvolvingDecision = inputs.ToList();
            GameModuleProgress.TemporaryInputsStorage = inputs;
            Game.RecordInputsIfNecessary(inputs);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Inputs specified: " + String.Join(",", inputs));
        }

        public virtual double Calculate()
        {
            if (Game.CurrentDecisionIndex == Game.CurrentlyEvolvingDecisionIndex)
                Game.Progress.CurrentlyEvolvingDecisionIndexReached = true;
            bool usePreviousVersion = false;
            bool averageThisStrategyAndPreviousVersion = false;
            bool disableAllAveragingThisStrategy = false;
            int currentDecisionIndexOrSubstitute = Game.CurrentDecisionPoint.SubstituteDecisionNumberInsteadOfEvolving ?? (int)Game.CurrentDecisionIndex;

            if (Game.CurrentDecisionIndexWithinActionGroup != null)
            {
                Decision theDecision = Game.CurrentDecision;
                
                if (theDecision.InputAbbreviations == null)
                    SetInputNamesAndAbbreviationsForCurrentDecision();
                bool alwaysUsePreviousVersionWhenOptimizingOtherDecisionInModule = true;
                if (alwaysUsePreviousVersionWhenOptimizingOtherDecisionInModule)
                {
                    if (Game.CurrentlyEvolvingDecisionIndex != null)
                    {
                        Strategy theStrategy = Game.Strategies[currentDecisionIndexOrSubstitute];
                        Strategy theStrategyCurrentlyEvolving = Game.Strategies[(int)Game.CurrentlyEvolvingDecisionIndex];
                        if (theStrategy != theStrategyCurrentlyEvolving && !theStrategy.Decision.AlwaysUseLatestVersion && theStrategy.CyclesStrategyDevelopmentThisEvolveStep > theStrategyCurrentlyEvolving.CyclesStrategyDevelopmentThisEvolveStep)
                            usePreviousVersion = true;
                    }

                }
                else if (Game.CurrentlyEvolvingModule == this && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup != Game.CurrentDecisionIndexWithinActionGroup)
                    usePreviousVersion = true;
                bool alwaysAveragePreviousVersion = false;
                if ((theDecision.AverageInPreviousVersion || alwaysAveragePreviousVersion) && !disableAllAveragingThisStrategy)
                    averageThisStrategyAndPreviousVersion = true;
            }

            double returnVal = Calculate(currentDecisionIndexOrSubstitute, usePreviousVersion, false, averageThisStrategyAndPreviousVersion);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Calculate result: " + returnVal);
            return returnVal;
        }

        public virtual double BuiltInCalculationResult(int decisionNumberWithinActionGroup)
        {
            return 0; // default for a built-in strategy is to just use a value we can ignore; can be overriden
        }

        public double Calculate(int decisionNumber, bool usePreviousVersionOfStrategy = false, bool useVersionBeforeThat = false, bool averageThisStrategyAndPreviousVersion = false)
        {
            Strategy theStrategy = AllStrategies[decisionNumber];
            int numToConsider = 0;
            double total = 0;
            if ((usePreviousVersionOfStrategy || averageThisStrategyAndPreviousVersion) && theStrategy.PreviousVersionOfThisStrategy == null)
            {
                usePreviousVersionOfStrategy = false;
                averageThisStrategyAndPreviousVersion = false;
            }
            if ((!usePreviousVersionOfStrategy && !useVersionBeforeThat) || averageThisStrategyAndPreviousVersion)
            {
                total += CalculateStrategyWithoutUsingPreviousVersions(decisionNumber, theStrategy);
                numToConsider++;
            }
            if (usePreviousVersionOfStrategy)
            {
                total += CalculateStrategyWithoutUsingPreviousVersions(decisionNumber, theStrategy.PreviousVersionOfThisStrategy);
                numToConsider++;
            }
            if (useVersionBeforeThat)
            {
                total += CalculateStrategyWithoutUsingPreviousVersions(decisionNumber, theStrategy.VersionOfStrategyBeforePrevious);
                numToConsider++;
            }
            //int? repetitionOfModuleForDecision = Game.DecisionPointForDecisionNumber(decisionNumber).RepetitionOfModule;
            //int phaseOutRepetitions = Game.GameDefinition.DecisionsExecutionOrder[decisionNumber].PhaseOutDefaultBehaviorOverRepetitions;
            //if (phaseOutRepetitions > 1 && repetitionOfModuleForDecision != null && repetitionOfModuleForDecision <= phaseOutRepetitions)
            //{
            //    double weightOfCalculation = ((int) repetitionOfModuleForDecision - 1) * (1.0 / (double) phaseOutRepetitions);
            //    double weightOfDefaultBehavior = 1.0 - weightOfCalculation;
            //    returnVal = weightOfDefaultBehavior * DefaultBehaviorBeforeEvolution(GameModuleProgress.TemporaryInputsStorage, decisionNumber) + weightOfCalculation * returnVal;
            //}

            GameModuleProgress.TemporaryInputsStorage = null;
            double returnVal = total / (double)numToConsider;
            return returnVal;
        }

        private double CalculateStrategyWithoutUsingPreviousVersions(int decisionNumber, Strategy theStrategy)
        {
            if (GameModuleProgress.TemporaryInputsStorage == null)
                throw new Exception("Must call SpecifyInputs before calling Calculate.");
            if (theStrategy.UseBuiltInStrategy)
                return BuiltInCalculationResult((int)Game.CurrentDecisionIndexWithinActionGroup);
            else if (theStrategy.GeneralOverrideValue != null)
                return (double)theStrategy.GeneralOverrideValue;
            else if (!theStrategy.StrategyDevelopmentInitiated)
                return DefaultBehaviorBeforeEvolution(GameModuleProgress.TemporaryInputsStorage, decisionNumber);
            else if (!theStrategy.DecisionReachedEnoughTimes)
                return DefaultBehaviorWhenDecisionNotReached(GameModuleProgress.TemporaryInputsStorage, decisionNumber);
            else
                return theStrategy.Calculate(GameModuleProgress.TemporaryInputsStorage, this);
        }

        public virtual double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            StrategyBounds bounds = Game.GameDefinition.DecisionsExecutionOrder[decisionNumber].StrategyBounds;
            return (bounds.LowerBound + bounds.UpperBound) / 2.0;
        }

        //public virtual double DefaultBehaviorWhenDecisionNotReached(double[] inputs, int decisionNumber)
        //{
        //    return DefaultBehaviorBeforeEvolution(inputs, decisionNumber);
        //}

        public virtual double DefaultBehaviorWhenDecisionNotReached(List<double> inputs, int decisionNumber)
        {
            ActionPoint info = Game.DecisionPointForDecisionNumber(decisionNumber); 
            return DefaultBehaviorBeforeEvolution(inputs, decisionNumber);
            //int? previousRepetition = info.DecisionNumberOfSameDecisionInPreviousRepetition;
            //if (previousRepetition == null)
            //    return DefaultBehaviorBeforeEvolution(inputs, decisionNumber);
            //else
            //{
            //    double previousRepetitionApproach = Calculate((int)previousRepetition, false, false);
            //    double noise = 0;
            //    if (inputs.Count() > 0)
            //    { // get a pseudo-random number by looking at the value well after the decimal point
            //        noise = inputs[0];
            //        while (noise < 1000)
            //            noise *= 10.0;
            //        noise = 2.0 * (noise - Math.Floor(noise) - 0.5); // now goes from -1 to 1
            //    }
            //    StrategyBounds bounds = Game.GameDefinition.DecisionsExecutionOrder[decisionNumber].StrategyBounds;
            //    double newValue = previousRepetitionApproach + 0.05 * noise * (bounds.UpperBound - bounds.LowerBound);
            //    newValue = ConstrainToRange.Constrain(newValue, bounds.LowerBound, bounds.UpperBound);
            //    return newValue;
            //}
        }

        public double CalculateWithoutAffectingEvolution(List<double> inputs, int decisionNumber, bool usePreviousVersionOfStrategy = false, bool useVersionBeforeThat = false)
        {
            Strategy theStrategy = AllStrategies[decisionNumber];
            if (!theStrategy.StrategyDevelopmentInitiated || 
                (theStrategy.CurrentlyDevelopingStrategy && theStrategy.CyclesStrategyDevelopment == 0) ||
                ((usePreviousVersionOfStrategy || useVersionBeforeThat) && AllStrategies[decisionNumber].PreviousVersionOfThisStrategy == null))
                return DefaultBehaviorBeforeEvolution(inputs, decisionNumber);
            List<double> previousInputs = GameModuleProgress.TemporaryInputsStorage;
            GameModuleProgress.TemporaryInputsStorage = inputs;
            double result = Calculate(decisionNumber, usePreviousVersionOfStrategy, useVersionBeforeThat);
            GameModuleProgress.TemporaryInputsStorage = previousInputs;
            return result;
        }

        internal static string GetStringCodeGeneratorOption(string optionsSpecifiedByUserAndPassedToGenerateSetting, string codeGeneratorOptionName)
        {
            string optionValue = null;
            if (optionsSpecifiedByUserAndPassedToGenerateSetting != "")
            {
                bool containsSemicolons = optionsSpecifiedByUserAndPassedToGenerateSetting.Contains(";"); // if so, ignore commas
                string[] optionStrings = optionsSpecifiedByUserAndPassedToGenerateSetting.Split(containsSemicolons ? ';' : ',');
                foreach (var optionString in optionStrings)
                    if (optionString.Contains(codeGeneratorOptionName))
                        optionValue = optionString.Replace(codeGeneratorOptionName + ":", "");
            }
            return optionValue == null ? null : optionValue.Trim();
        }

        internal static int GetIntCodeGeneratorOption(string optionsSpecifiedByUserAndPassedToGenerateSetting, string codeGeneratorOptionName)
        {
            string stringOption = GetStringCodeGeneratorOption(optionsSpecifiedByUserAndPassedToGenerateSetting, codeGeneratorOptionName);
            if (stringOption == null)
                throw new Exception("Code generator option " + codeGeneratorOptionName + " was expected but not specified.");
            return Convert.ToInt32(stringOption.Replace(",", ""));
        }

        internal static double GetDoubleCodeGeneratorOption(string optionsSpecifiedByUserAndPassedToGenerateSetting, string codeGeneratorOptionName)
        {
            string stringOption = GetStringCodeGeneratorOption(optionsSpecifiedByUserAndPassedToGenerateSetting, codeGeneratorOptionName);
            if (stringOption == null)
                throw new Exception("Code generator option " + codeGeneratorOptionName + " was expected but not specified.");
            return Convert.ToDouble(stringOption);
        }

        internal static bool GetBoolCodeGeneratorOption(string optionsSpecifiedByUserAndPassedToGenerateSetting, string codeGeneratorOptionName)
        {
            string stringOption = GetStringCodeGeneratorOption(optionsSpecifiedByUserAndPassedToGenerateSetting, codeGeneratorOptionName);
            if (stringOption == null)
                throw new Exception("Code generator option " + codeGeneratorOptionName + " was expected but not specified."); // Note: This could arise if you pass the $ as part of the codeGeneratorOptionName parameter; if that parameter starts with a $, delete the $.
            string boolText = stringOption.Trim().ToUpper();
            return boolText == "TRUE" || boolText == "T";
        }

        internal void AddToProgressList(ref List<double?> list, double? val)
        {
            if (list == null)
                list = new List<double?>();
            list.Add(val);
        }

        public void SetGameModuleNumbersThisModuleReliesOn(List<GameModule> gameModules)
        {
            GameModuleNumbersThisModuleReliesOn = new List<int>();
            foreach (string gmstring in GameModuleNamesThisModuleReliesOn)
            {
                int indexToAdd = gameModules.Select((item, index) => new { Item = item, Index = index }).Single(x => x.Item.GameModuleName == gmstring).Index;
                GameModuleNumbersThisModuleReliesOn.Add(indexToAdd);
            }
        }

        public GameModule GetGameModuleThisModuleReliesOn(int indexInListOfGameModulesThisModuleReliesOn)
        {
            int indexInListOfGameModules = GameModuleNumbersThisModuleReliesOn[indexInListOfGameModulesThisModuleReliesOn];
            return Game.GameModules[indexInListOfGameModules];
        }

        public virtual List<ActionGroup> GetActionGroupsForModule()
        {
            if (DecisionsCore == null)
                DecisionsCore = new List<Decision>();
            ActionGroup actionGroup = CreateActionGroup(DecisionsCore, ActionsAtBeginningOfModule, ActionsAtEndOfModule);
            if (UpdateCumulativeDistributionsBeforeSingleActionGroup)
                actionGroup.AddTag("UpdateCumulativeDistributionsBeforeActionGroup");
            if (UpdateCumulativeDistributionsAfterSingleActionGroup)
                actionGroup.AddTag("UpdateCumulativeDistributionsAfterActionGroup");
            if (WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter)
                actionGroup.AddTag("WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter");
            return new List<ActionGroup> { actionGroup };
        }

        internal ActionGroup CreateActionGroup(List<Decision> decisionsForGroup, List<string> actionsAtBeginningOfModule, List<string> actionsAtEndOfModule)
        {
            ActionGroup actionGroup =
                new ActionGroup()
                {
                    Name = this.GameModuleName,
                    ModuleNumber = ModuleNumber
                };
            if (Tags != null)
                foreach (string tag in Tags)
                    actionGroup.AddTag(tag);
            actionGroup.ActionPoints = new List<ActionPoint>();
            if (actionsAtBeginningOfModule != null)
                foreach (string actionName in actionsAtBeginningOfModule)
                    actionGroup.ActionPoints.Add(new ActionPoint() { ActionGroup = actionGroup, Name = actionName });
            int decisionNum = 0;
            if (decisionsForGroup != null)
                foreach (var dec in decisionsForGroup)
                {
                    actionGroup.ActionPoints.Add(
                        new DecisionPoint() { DecisionNumberWithinActionGroup = decisionNum, Name = dec.Name, Decision = decisionsForGroup[decisionNum], ActionGroup = actionGroup }
                    );
                    decisionNum++;
                }
            if (actionsAtEndOfModule != null)
                foreach (string actionName in actionsAtEndOfModule)
                    actionGroup.ActionPoints.Add(new ActionPoint() { ActionGroup = actionGroup, Name = actionName });
            return actionGroup;
        }

        public virtual OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            return null;
        }

        public virtual void ModifyActionGroupList(ref List<ActionGroup> egList, bool forEvolution, bool secondPass)
        {
        }

        public virtual void UpdateBasedOnTagInfo(string tag, int matchNumber, int totalMatches, ref Decision d)
        {
        }
    }
}

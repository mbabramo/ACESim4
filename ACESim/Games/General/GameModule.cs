using System;
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
        public byte? ModuleNumber;

        [InternallyDefinedSetting]
        public byte? FirstDecisionNumberInGameModule;

        [OptionalSetting]
        public bool IgnoreWhenCountingProgress;

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

        public virtual List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            return new List<Tuple<string, string>>();
        }

        public virtual double BuiltInCalculationResult(int decisionNumberWithinActionGroup)
        {
            return 0; // default for a built-in strategy is to just use a value we can ignore; can be overriden
        }

        public double Calculate()
        {
            throw new NotImplementedException();
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

        internal static byte GetByteCodeGeneratorOption(string optionsSpecifiedByUserAndPassedToGenerateSetting, string codeGeneratorOptionName)
        {
            return (byte)GetIntCodeGeneratorOption(optionsSpecifiedByUserAndPassedToGenerateSetting, codeGeneratorOptionName);
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
            byte decisionNum = 0;
            if (decisionsForGroup != null)
                foreach (var dec in decisionsForGroup)
                {
                    actionGroup.ActionPoints.Add(
                        new ActionPoint() { DecisionNumberWithinActionGroup = decisionNum, Name = dec.Name, Decision = decisionsForGroup[decisionNum], ActionGroup = actionGroup }
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

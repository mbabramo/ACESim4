using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class InputVariables
    {
        CurrentExecutionInformation currentExecutionInformation;

        public InputVariables(CurrentExecutionInformation currentExecutionInformation)
        {
            this.currentExecutionInformation = currentExecutionInformation;
        }
        
        public EvolutionSettings GetEvolutionSettings()
        {

            if (currentExecutionInformation.EvolutionSettingsSet.settings.Count() == 1)
            { // see if this is code-generated.
                var first = currentExecutionInformation.EvolutionSettingsSet.settings.First() as SettingClass;
                if (first.Generator != null)
                {
                    EvolutionSettings result = (EvolutionSettings)first.Generator.GenerateSetting(first.CodeGeneratorOptions);
                    return result;
                }
            }
            //return (EvolutionSettings)GetSettings(typeof(EvolutionSettings), 1, false, null, null)[0];
            return (EvolutionSettings)GetSettings(typeof(EvolutionSettings), currentExecutionInformation.EvolutionSettingsSet.settings, 1, false, null, null);
        }
        
        public GameDefinition GetGameDefinitionSettings(Type theType)
        {
            if (currentExecutionInformation.GameDefinitionsSet.settings.Count() == 1)
            { // see if this is code-generated.
                var first = currentExecutionInformation.GameDefinitionsSet.settings.First() as SettingClass;
                if (first.Generator != null)
                {
                    GameDefinition result = (GameDefinition) first.Generator.GenerateSetting(first.CodeGeneratorOptions);
                    return result;
                }
            }
            return (GameDefinition)GetSettings(theType, currentExecutionInformation.GameDefinitionsSet.settings, 1, false, null, null);
        }

        Type lastGameInputsType = null;
        public GameInputs GetGameInputs(Type theType, long numIterations, IterationID iterationID, CurrentExecutionInformation settings)
        {
            object gameInputsAsObject = GetSettings(theType, currentExecutionInformation.GameInputsSet.settings, numIterations, true, settings, null, iterationID);
            return (GameInputs)gameInputsAsObject;
        }

        InputSeeds lastInputSeeds = null;

        static object storedSettingsInfoLock = new object();

        internal object GetSettings(
            Type theType,
            List<Setting> settingsSet,
            long numIterations,
            bool gameInputSettings,
            CurrentExecutionInformation currentExecutionInformation,
            SettingAndFieldInfo outerSettingAndFieldInfo,
            IterationID iterationID = null)
        {
            Dictionary<int, StoredSettingAndFieldInfo> storedSettingsDictionary = null; 
            List<SettingAndFieldInfo> theSettingsAndFieldInfos = null;
            Func<double[], Dictionary<string, double>, object> compiledExpression = null;
            int numSeeds = 0;
            bool[] flipSeed = null;
            int?[] substituteSeed = null;
            if (currentExecutionInformation == null)
            {
                theSettingsAndFieldInfos = GetSettingAndFieldInfos(theType, outerSettingAndFieldInfo);
                numSeeds = theSettingsAndFieldInfos.Sum(x => x.setting == null ? 0 : x.setting.GetNumSeedsRequired(currentExecutionInformation));
            }
            else
            {
                storedSettingsDictionary = currentExecutionInformation.CurrentCommand.storedSettingsInfo;
                if (storedSettingsDictionary == null || !storedSettingsDictionary.ContainsKey(settingsSet.GetHashCode()))
                { // if it contains the key, we can avoid a lock (it won't be deleted in a parallel loop)
                    lock (storedSettingsInfoLock)
                    {
                        if (storedSettingsDictionary == null)
                            storedSettingsDictionary = currentExecutionInformation.CurrentCommand.storedSettingsInfo;
                        if (storedSettingsDictionary == null || !storedSettingsDictionary.ContainsKey(settingsSet.GetHashCode()))
                        {
                            theSettingsAndFieldInfos = GetSettingAndFieldInfos(theType, outerSettingAndFieldInfo);
                            numSeeds = theSettingsAndFieldInfos.Sum(x => x.setting.GetNumSeedsRequired(currentExecutionInformation));
                            string name;
                            if (outerSettingAndFieldInfo == null)
                                name = theType.ToString();
                            else
                                name = outerSettingAndFieldInfo.setting.Name;
                            compiledExpression = GetCompiledSettings(theType, theSettingsAndFieldInfos, name, out flipSeed, out substituteSeed);
                            if (storedSettingsDictionary == null)
                                storedSettingsDictionary = currentExecutionInformation.CurrentCommand.storedSettingsInfo = new Dictionary<int, StoredSettingAndFieldInfo>();

                            storedSettingsDictionary.Add(settingsSet.GetHashCode(), new StoredSettingAndFieldInfo { settingAndFieldInfoList = theSettingsAndFieldInfos, numSeeds = numSeeds, compiledExpression = compiledExpression, flipSeed = flipSeed, substituteSeed = substituteSeed });
                        }
                    }
                }
                if (theSettingsAndFieldInfos == null) // look it up in the dictionary
                {
                    StoredSettingAndFieldInfo theStoredInfo = storedSettingsDictionary[settingsSet.GetHashCode()];
                    theSettingsAndFieldInfos = theStoredInfo.settingAndFieldInfoList;
                    numSeeds = theStoredInfo.numSeeds;
                    compiledExpression = theStoredInfo.compiledExpression;
                    flipSeed = theStoredInfo.flipSeed;
                    substituteSeed = theStoredInfo.substituteSeed;
                }
            }

            // DEBUG -- THE NON-COMPILED APPROACH IS NOT CURRENTLY WORKING, BUT IT IS BEING TRIGGERED BY CHANGESIMULATIONSETTING.
            bool blockCompilation = false; // change to true if there is a possible problem with compilation -- BUT currently only compilation works. 
            object result;
            if (blockCompilation || (compiledExpression == null || (currentExecutionInformation.SettingOverride != null && currentExecutionInformation.SettingOverride.groupOfSimultaneousSettingChanges.Any())))
                result = GetSettingsWithoutCompilation(theType, numIterations, gameInputSettings, currentExecutionInformation, outerSettingAndFieldInfo, iterationID, theSettingsAndFieldInfos, numSeeds); 
            else
                result = GetSettingsThroughCompilation(numIterations, iterationID, numSeeds, compiledExpression, flipSeed, substituteSeed);
            return result;
        }

        internal Func<double[], Dictionary<string, double>, object> GetCompiledSettings(Type outerType, List<SettingAndFieldInfo> theSettingAndFieldInfos, string name, out bool[] flipSeed, out int?[] substituteSeed)
        {
            SettingCompilation compiler = new SettingCompilation();
            return compiler.GetCompiledExpressionFromSettingAndFieldInfos(outerType, theSettingAndFieldInfos, name, out flipSeed, out substituteSeed);
        }

        internal object GetSettingsThroughCompilation(
            long numIterations,
            IterationID iterationID,
            int numSeeds,
            Func<double[], Dictionary<string, double>, object> compiledExpression,
            bool[] flipSeed,
            int?[] substituteSeed)
        {
            InputSeeds theInputSeeds = GetInputSeeds(numIterations, numSeeds + 1 /* to include oversampling coefficient */, flipSeed, substituteSeed);
            bool usingOddIterationMirroring = theInputSeeds.inputMirroringEnabled && iterationID.GetIterationNumber(0) % 2 == 1;
            double[] inputs = new double[numSeeds];
            for (int s = 0; s < numSeeds; s++)
                inputs[s] = theInputSeeds[s, iterationID];
            
            object theObject = compiledExpression(inputs, currentExecutionInformation.AllVariablesFromProgram);
            return theObject;
        }

        internal object GetSettingsWithoutCompilation(
            Type theType,
            long numIterations,
            bool gameInputSettings,
            CurrentExecutionInformation settingsForSimulationVariablesOverride,
            SettingAndFieldInfo outerSettingAndFieldInfo,
            IterationID iterationID,
            List<SettingAndFieldInfo> theSettingsAndFieldInfos,
            int numSeeds)
        {
            SettingVariableFromSettingTracker variableFromSettingTracker;
            variableFromSettingTracker = GetVariableFromSettingTracker(outerSettingAndFieldInfo, theSettingsAndFieldInfos);
            
            object objectToReturn;
            if (theType == typeof(System.String))
                objectToReturn = "";
            else
                objectToReturn = Activator.CreateInstance(theType);

            if (!gameInputSettings && numSeeds > 0)
                throw new Exception("The evolution settings should not include any distributions requiring randomized inputs.");

            InputSeeds theInputSeeds = GetInputSeeds(numIterations, numSeeds + 1 /* to include oversampling coefficient */, null, null); // todo: support flip seeds without compilation
            
            int currentSeedNum = 0;
            foreach (SettingAndFieldInfo theSettingAndFieldInfo in theSettingsAndFieldInfos)
            {
                // uncomment below to assist with resolving settings files problems
                //if (theSettingAndFieldInfo.fieldInfo != null)
                //{
                //    for (int i = 0; i < theSettingAndFieldInfo.level; i++)
                //        Debug.Write("   ");
                //    Debug.WriteLine("SettingAndFieldInfo name " + theSettingAndFieldInfo.fieldInfo.Name);
                //}
                currentSeedNum = ProcessSettingAndFieldInfoUncompiled(theType, numIterations, gameInputSettings, settingsForSimulationVariablesOverride, outerSettingAndFieldInfo, iterationID, variableFromSettingTracker, ref objectToReturn, currentSeedNum, theSettingAndFieldInfo);
            }
            return objectToReturn;
        }

        private int ProcessSettingAndFieldInfoUncompiled(Type theType, long numIterations, bool gameInputSettings, CurrentExecutionInformation settingsForSimulationVariablesOverride, SettingAndFieldInfo outerSettingAndFieldInfo, IterationID iterationID, SettingVariableFromSettingTracker variableFromSettingTracker, ref object objectToReturn, int currentSeedNum, SettingAndFieldInfo theSettingAndFieldInfo)
        {
            int numSeedsRequired = theSettingAndFieldInfo.setting.GetNumSeedsRequired(settingsForSimulationVariablesOverride); 
            GetSettingValueForIteration(theType, numIterations, theSettingAndFieldInfo, gameInputSettings, settingsForSimulationVariablesOverride, outerSettingAndFieldInfo, variableFromSettingTracker, ref objectToReturn);
            //objectToReturn = GetSettingValueForIteration2(theType, numIterations, iterationNum, theSettingAndFieldInfo, theInputSeeds, currentSeedNum, numSeedsRequired, gameInputSettings, oversamplingInfo, settingsForSimulationVariablesOverride, variableFromSettingTracker);
            currentSeedNum += numSeedsRequired;
            return currentSeedNum;
        }

        private InputSeeds GetInputSeeds(
            long numIterations, 
            int numSeeds,
            bool[] flipSeed,
            int?[] substituteSeed)
        {
            InputSeeds theInputSeeds = null;
            theInputSeeds = currentExecutionInformation.InputSeedsSet.GetInputSeeds(numSeeds, numIterations, false, true, flipSeed, substituteSeed);
            return theInputSeeds;
        }

        private SettingVariableFromSettingTracker GetVariableFromSettingTracker(SettingAndFieldInfo outerSettingAndFieldInfo, List<SettingAndFieldInfo> theSettingsAndFieldInfos)
        {
            SettingVariableFromSettingTracker variableFromSettingTracker;
            if (outerSettingAndFieldInfo == null)
            {
                if (!theSettingsAndFieldInfos.Any() || theSettingsAndFieldInfos[0].setting.VariableFromSettingTracker.Value == null)
                    variableFromSettingTracker = InitializeVariableFromSettingTracker(theSettingsAndFieldInfos);
                else
                {
                    variableFromSettingTracker = theSettingsAndFieldInfos[0].setting.VariableFromSettingTracker.Value;
                    variableFromSettingTracker.ResetRequestsAndStorageTracking();
                }
            }
            else
                variableFromSettingTracker = outerSettingAndFieldInfo.setting.VariableFromSettingTracker.Value;
            return variableFromSettingTracker;
        }

        private SettingVariableFromSettingTracker InitializeVariableFromSettingTracker(List<SettingAndFieldInfo> theSettingsAndFieldInfos)
        {
            List<Setting> settingsRequested = new List<Setting>();
            Dictionary<string, Setting> namesOfRequestableSettings = new Dictionary<string, Setting>();
            int requestNumber = 0;
            foreach (SettingAndFieldInfo settingAndFieldInfo in theSettingsAndFieldInfos)
            {
                if (settingAndFieldInfo.setting != null)
                    settingAndFieldInfo.setting.RequestVariableFromSettingValues(namesOfRequestableSettings, settingsRequested, ref requestNumber); // If this settingAndFieldInfo is a variable, or is a class, list, or calculation containing a variable, then initialize the value of the variable by looking to previous settings. Otherwise, add the setting to the namesOfSettingsRequested list.
            }
            SettingVariableFromSettingTracker variableFromSettingTracker = new SettingVariableFromSettingTracker(settingsRequested);
            foreach (SettingAndFieldInfo settingAndFieldInfo in theSettingsAndFieldInfos)
            {
                if (settingAndFieldInfo.setting != null)
                    settingAndFieldInfo.setting.SetVariableFromSettingTracker(variableFromSettingTracker);
            }
            return variableFromSettingTracker;
        }

        private void GetSettingValueForIteration(
            Type theType,
            long numIterations,
            SettingAndFieldInfo theSettingAndFieldInfo,
            bool gameInputSettings,
            CurrentExecutionInformation settingsForSimulationVariablesOverride,
            SettingAndFieldInfo outerSettingAndFieldInfo,
            SettingVariableFromSettingTracker variableFromSettingTracker,
            ref object objectToSet)
        {
            switch (theSettingAndFieldInfo.setting.Type)
            {
                case SettingType.List:
                    variableFromSettingTracker.IterationNum = 0;
                    Type externalType = theSettingAndFieldInfo.type;
                    Type internalType = externalType.GetGenericArguments()[0];
                    GetSettings(internalType, ((SettingList)theSettingAndFieldInfo.setting).ContainedSettings, 1, gameInputSettings, settingsForSimulationVariablesOverride, theSettingAndFieldInfo);
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theSettingAndFieldInfo.itemsForList);
                    break;

                case SettingType.Class:
                    object theClass;
                    if (((SettingClass)theSettingAndFieldInfo.setting).Generator == null)
                    {
                        theClass = GetSettings(theSettingAndFieldInfo.type, ((SettingClass)theSettingAndFieldInfo.setting).ContainedSettings, 1, gameInputSettings, settingsForSimulationVariablesOverride, theSettingAndFieldInfo);
                    }
                    else
                        theClass = ((SettingClass)theSettingAndFieldInfo.setting).Generator.GenerateSetting(((SettingClass)theSettingAndFieldInfo.setting).CodeGeneratorOptions);
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theClass);
                    break;

                case SettingType.Distribution:
                case SettingType.Double:
                case SettingType.VariableFromProgram:
                case SettingType.VariableFromSetting:
                    variableFromSettingTracker.IterationNum = 0;
                    double theValue = theSettingAndFieldInfo.setting.GetDoubleValueOrOverride(null, currentExecutionInformation);
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theValue);
                    break;

                case SettingType.Boolean:
                    variableFromSettingTracker.IterationNum = 0;
                    bool theBool = ((SettingBoolean)theSettingAndFieldInfo.setting).Value;
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theBool);
                    break;

                case SettingType.String:
                    variableFromSettingTracker.IterationNum = 0;
                    string theText = ((SettingString)theSettingAndFieldInfo.setting).Value;
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theText);
                    break;

                case SettingType.Strategy:
                    variableFromSettingTracker.IterationNum = 0;
                    object theObject = ((SettingStrategy)theSettingAndFieldInfo.setting).GetStrategy();
                    theSettingAndFieldInfo.SetValue(ref objectToSet, theObject);
                    break;

                case SettingType.Int32:
                    variableFromSettingTracker.IterationNum = 0;
                    int intValue = ((SettingInt32)theSettingAndFieldInfo.setting).Value;
                    theSettingAndFieldInfo.SetValue(ref objectToSet, intValue);
                    break;


                case SettingType.Int64:
                    variableFromSettingTracker.IterationNum = 0;
                    long longValue = ((SettingInt64)theSettingAndFieldInfo.setting).Value;
                    theSettingAndFieldInfo.SetValue(ref objectToSet, longValue);
                    break;

                case SettingType.Calc:
                    variableFromSettingTracker.IterationNum = 0;
                    object objectValue = ((SettingCalc)theSettingAndFieldInfo.setting).GetValue(null, currentExecutionInformation);
                    theSettingAndFieldInfo.SetValue(ref objectToSet, objectValue);
                    break;

                default:
                    throw new Exception(String.Format("Exhausted SettingTypes ({0})", theSettingAndFieldInfo.setting.Type));
            }
        }

        public List<double> GetInputs(InputSeeds theInputSeeds, int currentSeedNum, int numSeeds, IterationID iterationID)
        {
            List<double> theInputs = new List<double>();
            for (int i = currentSeedNum; i < currentSeedNum + numSeeds; i++)
                theInputs.Add(theInputSeeds[i, iterationID]);
            double weight;
            double[] inputs = theInputs.ToArray();
            return inputs.ToList();
        }

        public List<SettingAndFieldInfo> GetSettingAndFieldInfos(Type theType, SettingAndFieldInfo outerSettingAndFieldInfo)
        {
            List<SettingAndFieldInfo> theList = new List<SettingAndFieldInfo>();
            if (outerSettingAndFieldInfo != null && outerSettingAndFieldInfo.setting.Type == SettingType.List)
            {
                SettingList theSettingList = ((SettingList)outerSettingAndFieldInfo.setting);
                foreach (var containedSetting in theSettingList.ContainedSettings)
                {
                    SettingAndFieldInfo theSettingAndFieldInfo = 
                        new SettingAndFieldInfo(containedSetting, outerSettingAndFieldInfo, theType);
                    theList.Add(theSettingAndFieldInfo);
                }
                return theList;
            }
            else
            {
                FieldInfo[] fields = theType.GetFields();
                int i = 0;
                foreach (FieldInfo field in fields)
                {
                    if (!Attribute.IsDefined(field, typeof(InternallyDefinedSetting)))
                    {
                        bool optional = Attribute.IsDefined(field, typeof(OptionalSettingAttribute));
                        Setting theSetting;
                        if (outerSettingAndFieldInfo == null)
                        {
                            theSetting = currentExecutionInformation.GetSetting(field.Name, optional);
                            if (theSetting != null)
                                ConfirmSettingType(field, theSetting);
                        }
                        else
                        { // outerSettingAndFieldInfo is a classSetting
                            SettingClass theClassSetting = ((SettingClass)outerSettingAndFieldInfo.setting);
                            theSetting = theClassSetting.ContainedSettings.SingleOrDefault(x => x.Name == field.Name);
                            if (!optional && theSetting == null)
                                throw new Exception("Setting " + field.Name + " was expected and not optional, but was not found in " + outerSettingAndFieldInfo.fieldInfo.Name);
                        }
                        if (theSetting != null)
                            theList.Add(new SettingAndFieldInfo(theSetting, field, outerSettingAndFieldInfo == null ? 0 : outerSettingAndFieldInfo.level + 1));
                        i++;
                    }
                }
                return theList;
            }
        }

        public void ConfirmSettingType(FieldInfo theField, Setting theSetting)
        {
            Type theFieldType = theField.FieldType;
            if (theSetting is SettingClassOrList)
                return;
            Type theSettingType = theSetting.GetReturnType();
            if (theFieldType != theSettingType)
                throw new Exception("Field " + theField.Name + " is of type " + theFieldType.Name + " but should be of type " + theSettingType.Name);

        }


    }
}

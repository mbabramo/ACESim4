using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml.Linq;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace ACESim
{
    [Serializable]
    public class SettingsLoader
    {
        CompleteSettings theCompleteSettings;
        public string baseOutputDirectory;
        XElement defineTemplatesContainer;
        CodeBasedSettingGeneratorFactory codeGeneratorFactory = new CodeBasedSettingGeneratorFactory();

        [Serializable]
        internal class InfoNeededToGenerateInstanceOfCompleteSettings
        {
            public List<int> DefineVariableAlternativeIndices;
            public XElement SingleSettingsFile;
            public List<string> DefineVariablesSetNames;
        }
        List<InfoNeededToGenerateInstanceOfCompleteSettings> CompleteSettingsInstanceInfo;
        bool UseAceSimMultipleRootElement;
        List<FindReplaceVariable> FindReplaceVariables;
        string SettingsPath;

        public SettingsLoader(string theBaseOutputDirectory)
        {
            baseOutputDirectory = theBaseOutputDirectory;
        }

        /// <summary>
        /// Processes a settings file, which may consist of an ACESim element (which in turn may include parentSettingsFiles or direct settings), in which case a single-element CompleteSettings list is returned, or an ACESimMultiple element (which in turn consists only of singleSettingsFile elements), in which case multiple CompleteSettings elements are returned (useful when generating many reports at once).
        /// </summary>
        /// <param name="settingsPath">The complete path of the file</param>
        /// <returns></returns>
        public int CountCompleteSettingsSets(string settingsPath)
        {
            SettingsPath = settingsPath;
            FileStream xmlFile = new FileStream(settingsPath, FileMode.Open,
FileAccess.Read);
            XDocument theSettingsFile = XDocument.Load(xmlFile);
            XElement rootElement = theSettingsFile.Element("ACESimMultiple");
            IEnumerable<XElement> singleSettingsFiles;
            FindReplaceVariables = new List<FindReplaceVariable>();
            CompleteSettingsInstanceInfo = new List<InfoNeededToGenerateInstanceOfCompleteSettings>();
            if (rootElement == null)
            { // the settings file has no ACESimMultiple element ... so, process it as a regular set of settings to be run once
                XElement singleOrParentSettingsFile = theSettingsFile.Element("ACESim");
                CompleteSettingsInstanceInfo.Add(new InfoNeededToGenerateInstanceOfCompleteSettings() { SingleSettingsFile = singleOrParentSettingsFile, DefineVariableAlternativeIndices = new List<int>(), DefineVariablesSetNames = new List<string>() });
                UseAceSimMultipleRootElement = false;
                return 1;
            }
            else
            {
                UseAceSimMultipleRootElement = true;
                int skip = 0, take = 0;
                singleSettingsFiles = rootElement.Elements("singleSettingsFile");
                skip = ((int?)GetNumericAttribute(rootElement, "skip", false)) ?? 0;
                take = ((int?)GetNumericAttribute(rootElement, "take", false)) ?? 999999; // very large number// send back a separate complete settings for each singleSettingsFile in ACESimMultiple
                foreach (XElement singleSettingsFile in singleSettingsFiles)
                {
                    List<string> defineVariablesSetNames = GetNamesOfDefineVariableAlternativesSets(singleSettingsFile);
                    List<int> defineVariableAlternativesCount = GetListOfNumberOfAlternativesInEachDefineVariableAlternativesSubelement(singleSettingsFile);
                    List<List<int>> defineVariableAlternativeIndicesList = PermutationMaker.GetPermutations(defineVariableAlternativesCount, true);
                    foreach (List<int> defineVariableAlternativeIndices in defineVariableAlternativeIndicesList)
                    {
                        int unstarred = CountUnstarredInDefineVariableAlternativePermutation(singleSettingsFile, defineVariableAlternativeIndices);
                        if (unstarred <= 1)
                            CompleteSettingsInstanceInfo.Add(new InfoNeededToGenerateInstanceOfCompleteSettings() { SingleSettingsFile = singleSettingsFile, DefineVariableAlternativeIndices = defineVariableAlternativeIndices, DefineVariablesSetNames = defineVariablesSetNames });
                    }
                    CompleteSettingsInstanceInfo = CompleteSettingsInstanceInfo.Skip(skip).Take(take).ToList();
                }
                return CompleteSettingsInstanceInfo.Count;
            }
            
        }

        public CompleteSettings GetCompleteSettingsSet(int index)
        {
            if (!UseAceSimMultipleRootElement)
            {
                // definitely just one file to process
                if (index != 0)
                    throw new Exception("Internal error: There was only was file to process, but the code has requested more than one.");
                ProcessSingleSettingsFileGivenFilePath(SettingsPath, FindReplaceVariables);
                theCompleteSettings.NameOfRun = "SingleRun";
                theCompleteSettings.NamesOfVariablesSets = new List<string>() { "Run" };
                theCompleteSettings.NamesOfVariablesSetsChosen = new List<string>() { "Single" + DateTime.Now.ToShortTimeString() };
                return theCompleteSettings; // completeSettingsList;
            }
            InfoNeededToGenerateInstanceOfCompleteSettings info = CompleteSettingsInstanceInfo[index];
            Reset();
            string nameOfRun;
            List<string> namesOfVariableSetsChosen;
            ProcessParentOrSingleSettingsFileGivenXElement(SettingsPath, theCompleteSettings.AllVariablesFromProgram, FindReplaceVariables, info.SingleSettingsFile, info.DefineVariableAlternativeIndices, out nameOfRun, out namesOfVariableSetsChosen);
            theCompleteSettings.NamesOfVariablesSets = info.DefineVariablesSetNames;
            theCompleteSettings.NamesOfVariablesSetsChosen = namesOfVariableSetsChosen;
            theCompleteSettings.NameOfRun = nameOfRun;
            return theCompleteSettings;
        }

        /// <summary>
        /// Reset all information accumulated, if any, from other single settings files, and sets theCompleteSettings to a new set of CompleteSettings from the file specified.
        /// </summary>
        /// <param name="settingsPath"></param>
        /// <param name="findReplaceVariables"></param>
        /// <returns></returns>
        public void ProcessSingleSettingsFileGivenFilePath(string settingsPath, List<FindReplaceVariable> findReplaceVariables)
        {
            try
            {
                Reset();
                ProcessParentOrSingleSettingsFileGivenFilePath(settingsPath, theCompleteSettings.AllVariablesFromProgram, findReplaceVariables);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException(
                    String.Format(
                        "Failed to process file {0}: {1}",
                        System.IO.Path.GetFileName(settingsPath),
                        ex.Message),
                    ex);
            }
        }

        public void Reset()
        {
            codeGeneratorFactory = new CodeBasedSettingGeneratorFactory(); 
            DistributionFactoryDictionary = new Dictionary<int, DistributionFactory>(); 
            theCompleteSettings = new CompleteSettings();
            defineTemplatesContainer = new XElement("defineTemplatesContainer");
        }

        public void ProcessParentOrSingleSettingsFileGivenFilePath(string settingsPath, Dictionary<string, double> allVariablesFromProgram, List<FindReplaceVariable> findReplaceVariables)
        {
            string[] allLines = File.ReadAllLines(settingsPath);
            string singleString = "";
            foreach (string line in allLines)
            {
                string replacement = line;
                foreach (FindReplaceVariable var in findReplaceVariables)
                {
                    // We want to search and replace the variables in most elements, but NOT in defineVariable elements themselves. Eventually, the values in findReplaceVariables will supercede the defineVariable elements, but if we do the replacement now, then we will not be able to figure out what to supercede later.
                    replacement = replacement.Replace("findString=\"$", "findStringTEMP");
                    replacement = replacement.Replace(var.FindString, var.ReplaceString);
                    replacement = replacement.Replace("findStringTEMP", "findString=\"$");
                }
                singleString += replacement;
            }
            StringReader sr = new StringReader(singleString);

            XDocument theSettingsFile = XDocument.Load(sr);
            XElement rootElement = theSettingsFile.Element("ACESim");

            var templates = theSettingsFile.Descendants("defineTemplate").ToList();
            foreach (var defineTemplate in templates)
            {
                defineTemplate.Remove();
                defineTemplatesContainer.Add(defineTemplate);
            }

            var parentFiles = rootElement.Elements("parentSettingsFile");
            foreach (XElement parentFile in parentFiles)
            {
                List<int> numberAlternativesInEachDefineVariableAlternatives = new List<int>(); // empty because we only allow this currently for the singleSettingsFile (since otherwise we would need to process this a step further back in the call hierarchy)
                string nameOfRun;
                List<string> namesOfVariableSetsChosen;
                ProcessParentOrSingleSettingsFileGivenXElement(settingsPath, allVariablesFromProgram, findReplaceVariables, parentFile, numberAlternativesInEachDefineVariableAlternatives, out nameOfRun, out namesOfVariableSetsChosen);
            }

            var insertTemplates = theSettingsFile.Descendants("insertTemplate").ToList();
            foreach (var elementToReplace in insertTemplates)
            {
                bool optional = GetBoolAttribute(elementToReplace, "optional", false);
                ReplaceElementWithDefinedTemplate(elementToReplace, optional);
            }

            XElement xmlAllEvolutionSettings = rootElement.Element("evolutionSettingsAll");
            if (xmlAllEvolutionSettings != null)
            {
                foreach (XElement xmlEvolutionSettings in xmlAllEvolutionSettings.Elements("evolutionSettings"))
                    ProcessEvolutionSettings(xmlEvolutionSettings, allVariablesFromProgram, codeGeneratorFactory);
                foreach (EvolutionSettingsSet theSet in theCompleteSettings.EvolutionSettingsSets)
                    theSet.ConfirmNoInputsNeeded();
            }

            XElement xmlAllGameDefinitions = rootElement.Element("gameDefinitionsAll");
            if (xmlAllGameDefinitions != null)
            {
                foreach (XElement xmlGameDefinition in xmlAllGameDefinitions.Elements("gameDefinition"))
                    ProcessGameDefinition(xmlGameDefinition, allVariablesFromProgram);
            }

            XElement xmlAllGameInputs = rootElement.Element("gameInputsAll");
            if (xmlAllGameInputs != null)
            {
                foreach (XElement xmlGameInputs in xmlAllGameInputs.Elements("gameInputs"))
                    ProcessGameInputs(xmlGameInputs, allVariablesFromProgram, codeGeneratorFactory);
            }

            var xmlRowOrColGroups = rootElement.Elements("rowOrColGroup");
            foreach (var xmlRowOrColGroup in xmlRowOrColGroups)
                ProcessRowOrColGroup(xmlRowOrColGroup);

            XElement xmlCommandSetsAll = rootElement.Element("commandSetsAll");
            if (xmlCommandSetsAll != null)
            {
                foreach (XElement xmlCommandSet in xmlCommandSetsAll.Elements("commandSet"))
                    ProcessCommandSet(xmlCommandSet, allVariablesFromProgram);
            }

            XElement xmlExecuteCommandList = rootElement.Element("executeCommandList");
            if (xmlExecuteCommandList != null)
                ProcessExecuteList(xmlExecuteCommandList);
        }

        private void ProcessParentOrSingleSettingsFileGivenXElement(string settingsPath, Dictionary<string, double> allVariablesFromProgram, List<FindReplaceVariable> findReplaceVariables, XElement parentOrSingleSettingsFile, List<int> defineVariableAlternativesIndices, out string nameOfRun, out List<string> namesOfVariableSetsChosen)
        {
            string parentSettingsPath;
            ProcessParentOrSingleSettingsFileElement(settingsPath, ref findReplaceVariables, parentOrSingleSettingsFile, out parentSettingsPath, defineVariableAlternativesIndices, out nameOfRun, out namesOfVariableSetsChosen);
            ProcessParentOrSingleSettingsFileGivenFilePath(parentSettingsPath, allVariablesFromProgram, findReplaceVariables);
        }

        private List<string> GetNamesOfDefineVariableAlternativesSets(XElement parentOrSingleSettingsFile)
        {
            List<string> names = new List<string>();
            int index = -1;
            foreach (XElement set in parentOrSingleSettingsFile.Elements("defineVariableAlternatives"))
            {
                index++;
                string theName = GetStringAttribute(set, "name", false);
                if (theName == null || theName == "")
                    theName = "Set" + index;
                names.Add(theName);
            }
            return names;
        }

        private List<int> GetListOfNumberOfAlternativesInEachDefineVariableAlternativesSubelement(XElement parentOrSingleSettingsFile)
        {
            List<int> numberAlternativesInEachDefineVariableAlternatives = new List<int>();
            foreach (XElement set in parentOrSingleSettingsFile.Elements("defineVariableAlternatives"))
                numberAlternativesInEachDefineVariableAlternatives.Add(set.Elements("defineVariableAlternative").Count());
            return numberAlternativesInEachDefineVariableAlternatives;
        }

        private int CountUnstarredInDefineVariableAlternativePermutation(XElement parentOrSingleSettingsFile, List<int> permutation)
        {
            int unstarred = 0;
            int index = -1;
            foreach (XElement set in parentOrSingleSettingsFile.Elements("defineVariableAlternatives"))
            {
                index++;
                bool starAll = GetBoolAttribute(set, "starAll", false);
                if (!starAll)
                {
                    List<XElement> elements = set.Elements("defineVariableAlternative").ToList();
                    XElement specificAlternative = elements[permutation[index]];
                    bool star = GetBoolAttribute(specificAlternative, "star", false);
                    if (elements.Count == 1)
                        star = true;
                    if (!star)
                        unstarred++;
                }
            }
            return unstarred;
        }

        private void ProcessParentOrSingleSettingsFileElement(string originalSettingsPath, ref List<FindReplaceVariable> findReplaceVariables, XElement parentOrSingleSettingsFile, out string parentSettingsPath, List<int> defineVariableAlternativesIndices, out string nameOfRun, out List<string> namesOfVariableSetsChosen)
        {
            nameOfRun = "";
            namesOfVariableSetsChosen = new List<string>();
            string settingsDirectory = System.IO.Path.GetDirectoryName(originalSettingsPath);
            string parentFilename = GetStringAttribute(parentOrSingleSettingsFile, "filename", true);
            parentSettingsPath = System.IO.Path.Combine(settingsDirectory, parentFilename);
            List<FindReplaceVariable> vars = new List<FindReplaceVariable>();
            if (defineVariableAlternativesIndices != null && defineVariableAlternativesIndices.Any())
            {
                var defineVariableAlternatives = parentOrSingleSettingsFile.Elements("defineVariableAlternatives");
                for (int altIndex = 0; altIndex < defineVariableAlternativesIndices.Count; altIndex++)
                {
                    var chosenAlternative = defineVariableAlternatives.Skip(altIndex).First().Elements("defineVariableAlternative").Skip(defineVariableAlternativesIndices[altIndex]).First();
                    string chosenAlternativeName = GetStringAttribute(chosenAlternative, "name", false);
                    if (chosenAlternativeName != null && chosenAlternativeName != "")
                    {
                        nameOfRun += chosenAlternativeName + " ";
                        namesOfVariableSetsChosen.Add(chosenAlternativeName);
                    }
                    var findReplaceDefinitions = chosenAlternative.Elements("defineVariable").ToList();
                    foreach (XElement findReplaceVar in findReplaceDefinitions)
                        vars.Add(ProcessFindReplaceVar(findReplaceVar));
                }
            }
            var findReplaceDefinitionsParent = parentOrSingleSettingsFile.Elements("defineVariable").ToList();
            foreach (XElement findReplaceVar in findReplaceDefinitionsParent)
                vars.Add(ProcessFindReplaceVar(findReplaceVar));
            vars.Add(new FindReplaceVariable() { FindString = "$ReportPrefix", ReplaceString = nameOfRun });
            List<FindReplaceVariable> copy = findReplaceVariables; // so it can be used in a lambda
            vars = vars.Where(x => !copy.Any(y => x.FindString == y.FindString)).ToList(); // filter out the child definitions that are superceded by parent definitions
            vars.AddRange(findReplaceVariables); // add in the parent definitions
            findReplaceVariables = vars;
        }

        //private void ProcessSingleSettingsFileElement(string settingsPath, List<FindReplaceVariable> findReplaceVariables, XElement parentFile)
        //{
        //    string singleSettingsFilePath;
        //    ProcessParentOrSingleSettingsFileElement(settingsPath, ref findReplaceVariables, parentFile, out singleSettingsFilePath);
        //    ProcessSingleSettingsFile(singleSettingsFilePath, findReplaceVariables);
        //}


        private FindReplaceVariable ProcessFindReplaceVar(XElement findReplaceVar)
        {
            string findString = GetStringAttribute(findReplaceVar, "findString", true);
            string replaceString = GetStringAttribute(findReplaceVar, "replaceString", true);
            return new FindReplaceVariable() { FindString = findString, ReplaceString = replaceString };
        }

        private void ReplaceElementWithDefinedTemplate(XElement elementToReplace, bool optional)
        {
            string name = GetStringAttribute(elementToReplace, "name", true);
            var allMatches = defineTemplatesContainer.Elements().Where(x => name == GetStringAttribute(x, "name", true));
            if (allMatches.Count() > 1)
                throw new Exception("Error: More than one template matched.");
            XElement replacement = allMatches.SingleOrDefault();
            if (replacement == null)
            {
                if (!optional)
                    throw new Exception("A required template " + name + " specified in the insertTemplate element was not found.");
            }
            else
            {
                bool keepGoing = true;
                int r = 1;
                while (keepGoing)
                {
                    string stringToReplace = GetStringAttribute(elementToReplace, "find" + r.ToString(), false);
                    if (stringToReplace != null && stringToReplace != "")
                    {
                        string replaceWith = GetStringAttribute(elementToReplace, "replace" + r.ToString(), true);
                        string elementString = replacement.ToString();
                        string revisedElementString = elementString.Replace(stringToReplace, replaceWith);
                        replacement = XElement.Parse(revisedElementString);
                        r++;
                    }
                    else
                        keepGoing = false;
                }
                var recursiveTemplates = replacement.Descendants("insertTemplate").ToList();
                if (recursiveTemplates.Any())
                {
                    foreach (var recursive in recursiveTemplates)
                    {
                        bool recursiveOptional = GetBoolAttribute(recursive, "optional", false);
                        ReplaceElementWithDefinedTemplate(recursive, recursiveOptional);
                    }
                }
                elementToReplace.ReplaceWith(replacement.Elements());
            }
        }

        public void ProcessCommandSet(XElement settingsToProcess, Dictionary<string, double> allVariablesFromProgram)
        {
            string commandSetName = GetName(settingsToProcess);
            try
            {
                CommandSet existingSet = theCompleteSettings.CommandSets.SingleOrDefault(x => x.Name == commandSetName);
                if (existingSet == null)
                {
                    CommandSet newSet = new CommandSet(commandSetName);
                    newSet.Commands = new List<MultiPartCommand>();
                    theCompleteSettings.CommandSets.Add(newSet);
                    existingSet = theCompleteSettings.CommandSets.SingleOrDefault(x => x.Name == commandSetName);
                }
                ProcessCommands(settingsToProcess, existingSet, allVariablesFromProgram);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process command set " + commandSetName + ": " + ex.Message, ex);
            }
        }

        public void ProcessEvolutionSettings(XElement settingsToProcess, Dictionary<string, double> allVariablesFromProgram, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            string settingSetName = GetName(settingsToProcess);
            try
            {
                EvolutionSettingsSet existingSet = theCompleteSettings.EvolutionSettingsSets.SingleOrDefault(x => x.name == settingSetName);
                if (existingSet == null)
                {
                    EvolutionSettingsSet newSettingsSet = new EvolutionSettingsSet(settingSetName);
                    newSettingsSet.settings = new List<Setting>();
                    theCompleteSettings.EvolutionSettingsSets.Add(newSettingsSet);
                    existingSet = theCompleteSettings.EvolutionSettingsSets.SingleOrDefault(x => x.name == settingSetName);
                }
                ProcessSettings(settingsToProcess, existingSet, allVariablesFromProgram, codeGeneratorFactory);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process evolution settings set " + settingSetName + ": " + ex.Message, ex);
            }
        }

        public void ProcessGameInputs(XElement settingsToProcess, Dictionary<string, double> allVariablesFromProgram, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            string settingSetName = GetName(settingsToProcess);
            try
            {
                string gameName = GetStringAttribute(settingsToProcess, "gameName", true);
                GameInputsSet existingSet = theCompleteSettings.GameInputsSets.SingleOrDefault(x => x.name == settingSetName);
                if (existingSet == null)
                {
                    GameInputsSet newSettingsSet = new GameInputsSet(settingSetName, gameName);
                    newSettingsSet.settings = new List<Setting>();
                    theCompleteSettings.GameInputsSets.Add(newSettingsSet);
                    existingSet = theCompleteSettings.GameInputsSets.SingleOrDefault(x => x.name == settingSetName);
                }
                ProcessSettings(settingsToProcess, existingSet, allVariablesFromProgram, codeGeneratorFactory);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process simulation settings set " + settingSetName + ": " + ex.Message, ex);
            }
        }

        public void ProcessGameDefinition(XElement settingsToProcess, Dictionary<string, double> allVariablesFromProgram)
        {
            string gameName = GetName(settingsToProcess);
            try
            {
                GameDefinitionSettingsSet existingSet = theCompleteSettings.GameDefinitionSets.SingleOrDefault(x => x.name == gameName);
                if (existingSet == null)
                {
                    GameDefinitionSettingsSet newSettingsSet = new GameDefinitionSettingsSet(gameName);
                    newSettingsSet.settings = new List<Setting>();
                    theCompleteSettings.GameDefinitionSets.Add(newSettingsSet);
                    existingSet = theCompleteSettings.GameDefinitionSets.SingleOrDefault(x => x.name == gameName);
                }
                ProcessSettings(settingsToProcess, existingSet, allVariablesFromProgram, codeGeneratorFactory);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process game definition set " + gameName + ": " + ex.Message, ex);
            }
        }

        public void ProcessRowOrColGroup(XElement settingsToProcess)
        {
            string rowOrColGroupName = GetName(settingsToProcess);
            try
            {
                List<RowOrColInfo> theRowOrColInfos = new List<RowOrColInfo>();
                List<RowOrColInfoGenerator> theRowOrColInfoGenerators = new List<RowOrColInfoGenerator>();
                var xmlRowOrColInfos = settingsToProcess.Elements("rowOrColInfo");
                foreach (var xmlRowOrColInfo in xmlRowOrColInfos)
                    theRowOrColInfos.Add(ProcessRowOrColInfo(xmlRowOrColInfo));
                var xmlRowOrColInfoGenerators = settingsToProcess.Elements("rowOrColInfoGenerator");
                foreach (var xmlRowOrColInfoGenerator in xmlRowOrColInfoGenerators)
                    theRowOrColInfoGenerators.Add(ProcessRowOrColInfoGenerator(xmlRowOrColInfoGenerator));
                RowOrColumnGroup existingGroup = theCompleteSettings.RowOrColumnGroups.SingleOrDefault(x => x.name == rowOrColGroupName);
                if (existingGroup != null)
                    theCompleteSettings.RowOrColumnGroups.Remove(existingGroup);
                theCompleteSettings.RowOrColumnGroups.Add(new RowOrColumnGroup(rowOrColGroupName, theRowOrColInfos, theRowOrColInfoGenerators));
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process row or column group " + rowOrColGroupName + ": " + ex.Message, ex);
            }
        }

        public RowOrColInfo ProcessRowOrColInfo(XElement settingsToProcess)
        {
            // rowOrColInfo element. Includes a name attribute and a statistic attribute. Consists of zero or more filter elements.
            string rowOrColInfoName = GetName(settingsToProcess);
            try
            {
                string variableName = GetStringAttribute(settingsToProcess, "variableName", false);
                string statistic = GetStringAttribute(settingsToProcess, "statistic", false);
                List<Filter> theFilters = ProcessFilters(settingsToProcess);
                return new RowOrColInfo(rowOrColInfoName, variableName, null, statistic, theFilters);
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process RowOrColInfo " + rowOrColInfoName + ": " + ex.Message, ex);
            }
        }

        public RowOrColInfoGenerator ProcessRowOrColInfoGenerator(XElement settingsToProcess)
        {
            try
            {
                string filterVariableName = GetStringAttribute(settingsToProcess, "filterVariableName", false);
                if (filterVariableName == null)
                    filterVariableName = "";
                string rowOrColNamePrefix = GetStringAttribute(settingsToProcess, "rowOrColNamePrefix", false);
                string outputVariableName = GetStringAttribute(settingsToProcess, "outputVariableName", false);
                string numDynamicRangesString = GetStringAttribute(settingsToProcess, "numDynamicRanges", false);
                int? numDynamicRanges = null;
                if (numDynamicRangesString != "")
                    numDynamicRanges = Convert.ToInt32(numDynamicRangesString);
                bool evenlySpaceDynamicRanges = GetBoolAttribute(settingsToProcess, "evenlySpaceDynamicRanges", false);

                string evenlySpaceDynamicRangesMinOverrideString = GetStringAttribute(settingsToProcess, "evenlySpaceDynamicRangesMinOverride", false);
                double? evenlySpaceDynamicRangesMinOverride = null;
                double? evenlySpaceDynamicRangesMaxOverride = null;
                if (evenlySpaceDynamicRangesMinOverrideString != "")
                    evenlySpaceDynamicRangesMinOverride = Convert.ToDouble(evenlySpaceDynamicRangesMinOverrideString);
                string evenlySpaceDynamicRangesMaxOverrideString = GetStringAttribute(settingsToProcess, "evenlySpaceDynamicRangesMaxOverride", false);
                if (evenlySpaceDynamicRangesMaxOverrideString != "")
                    evenlySpaceDynamicRangesMaxOverride = Convert.ToDouble(evenlySpaceDynamicRangesMaxOverrideString);

                string reportIndexStartingAtZeroString = GetStringAttribute(settingsToProcess, "reportIndexStartingAtZero", false);
                bool reportIndexStartingAtZero = true;
                if (reportIndexStartingAtZeroString == "false" || reportIndexStartingAtZeroString == "FALSE")
                    reportIndexStartingAtZero = false;

                string numberElementsInListString = GetStringAttribute(settingsToProcess, "numberElementsInList", false);
                int? numberElementsInList = null;
                if (numberElementsInListString != "")
                    numberElementsInList = Convert.ToInt32(numberElementsInListString);

                string statistic = GetStringAttribute(settingsToProcess, "statistic", false);
                List<double> theRanges = ProcessRanges(settingsToProcess);
                if (theRanges.Count == 1)
                    throw new FileLoaderException("You must have either no ranges or at least two ranges.");
                List<Filter> theFilters = ProcessFilters(settingsToProcess);
                return new RowOrColInfoGenerator(filterVariableName, rowOrColNamePrefix, outputVariableName, statistic, theRanges, theFilters, numDynamicRanges, evenlySpaceDynamicRanges, evenlySpaceDynamicRangesMinOverride, evenlySpaceDynamicRangesMaxOverride, reportIndexStartingAtZero, numberElementsInList);
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to RowOrColInfoGenerator: " + ex.Message, ex);
            }
        }

        public List<double> ProcessRanges(XElement settingsToProcess)
        {
            var xmlRanges = settingsToProcess.Elements("range");
            List<double> theRanges = new List<double>();
            foreach (var xmlRange in xmlRanges)
            {
                double theValue;
                try
                {
                    theValue = (double)Convert.ToDouble(xmlRange.Value);
                }
                catch (Exception ex)
                {
                    throw new FileLoaderException("Invalid range value " + xmlRange.Value, ex);
                }
                theRanges.Add(theValue);
            }
            return theRanges;
        }

        public List<Filter> ProcessFilters(XElement settingsToProcess)
        {
            try
            {
                var xmlFilters = settingsToProcess.Elements("filter");
                List<Filter> theFilters = new List<Filter>();
                foreach (var xmlFilter in xmlFilters)
                {
                    theFilters.Add(ProcessFilter(xmlFilter));
                }
                return theFilters;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process filters: " + ex.Message, ex);
            }
        }

        public Filter ProcessFilter(XElement settingsToProcess)
        {
            string type = GetStringAttribute(settingsToProcess, "varType", true).ToLower().Trim();
            if (type == "or")
                return new FilterOr(ProcessFilters(settingsToProcess));
            if (type == "and")
                return new FilterAnd(ProcessFilters(settingsToProcess));
            if (type != "bool" && type != "double" && type != "text" && type != "string" && type != "int")
                throw new FileLoaderException("Filter must be of type bool, double, int, text, or and/or.");
            XElement variableName = settingsToProcess.Element("variableName");
            if (variableName == null)
                throw new FileLoaderException("Filter must contain a variableName element.");
            string theVariableName = variableName.Value.Trim();
            XElement operation = settingsToProcess.Element("operation");
            if (operation == null)
                throw new FileLoaderException("Filter must contain a operation element.");
            string theOperation = operation.Value.Trim();
            if (theOperation != "=" && theOperation != "EQ" && theOperation != "NE" && theOperation != "GT" && theOperation != "LT" && theOperation != "GTEQ" && theOperation != "LTEQ")
                throw new FileLoaderException("Filter operation must be either EQ, NE, GT, LT, GTEQ, or LTEQ.");
            XElement value = settingsToProcess.Element("value");
            if (value == null)
                throw new FileLoaderException("Filter must contain a value element.");
            string valueText = value.Value.Trim();
            if (type == "bool")
            {
                valueText = valueText.ToLower();
                if (valueText != "true" && valueText != "false" && valueText != "t" && valueText != "f")
                    throw new FileLoaderException("Boolean filter must be true or false.");
                return new FilterBool(theVariableName, theOperation, valueText == "true" || valueText == "t");
            }
            else if (type == "double")
            {
                double theValue;
                try
                {
                    theValue = (double)Convert.ToDouble(valueText);
                }
                catch (Exception ex)
                {
                    throw new FileLoaderException("double filter must be set to a floating point value.", ex);
                }
                return new FilterDouble(theVariableName, theOperation, theValue);
            }
            else if (type == "int")
            {
                int theValue;
                try
                {
                    theValue = (int)Convert.ToInt32(valueText);
                }
                catch (Exception ex)
                {
                    throw new FileLoaderException("int filter must be set to an integral value.", ex);
                }
                return new FilterInt(theVariableName, theOperation, theValue);
            }
            else if (type == "text" || type == "string")
            {
                return new FilterText(theVariableName, theOperation, valueText);
            }
            else
                throw new FileLoaderException("Unknown filter type.");
        }

        public void ProcessCommands(XElement settingsToProcess, CommandSet existingSet, Dictionary<string, double> allVariablesFromProgram)
        {
            List<MultiPartCommand> existingCommandsInSet = existingSet.Commands;
            foreach (XElement anXMLSetting in settingsToProcess.Elements("multipartCommand"))
            {
                MultiPartCommand theMultipartCommand = ProcessMultipartCommandForSimulation(anXMLSetting, allVariablesFromProgram);
                MultiPartCommand existingCommand = existingCommandsInSet.SingleOrDefault(x => x.Name == theMultipartCommand.Name);
                if (existingCommand != null)
                    existingSet.Commands.Remove(existingCommand);
                existingSet.Commands.Add(theMultipartCommand);
            }
        }

        /// <summary>
        /// Adds a Setting subclass to existingSet for each subelement of settingsElement that is a "setting" element
        /// </summary>
        /// <param name="settingsElement"></param>
        /// <param name="existingSet"></param>
        public void ProcessSettings(XElement settingsElement, SettingsSet existingSet, Dictionary<string, double> allVariablesFromProgram, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            try
            {
                List<Setting> existingSettingsInSet = existingSet.settings;
                foreach (XElement settingElement in settingsElement.Elements("setting"))
                {
                    Setting theSetting = ProcessSetting(settingElement, allVariablesFromProgram, codeGeneratorFactory);
                    Setting existingSetting = existingSettingsInSet.SingleOrDefault(x => x.Name == theSetting.Name);
                    if (existingSetting != null)
                        existingSet.settings.Remove(existingSetting);
                    existingSet.settings.Add(theSetting);
                }
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process settings: " + ex.Message, ex);
            }
        }

        Dictionary<int, DistributionFactory> DistributionFactoryDictionary = new Dictionary<int, DistributionFactory>();

        /// <summary>
        /// Returns a Setting subclass populated with the information in settingElement
        /// </summary>
        /// <param name="settingElement"></param>
        /// <returns></returns>
        public Setting ProcessSetting(XElement settingElement, Dictionary<string, double> allVariablesFromProgram, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            string name = GetName(settingElement);
            try
            {
                string type = GetType(settingElement);
                string contents = settingElement.Value.Trim();
                if (contents.StartsWith("$"))
                    throw new Exception("A variable name from a settings file has not been resolved. Check that you have included the defineVariable element in the settings file or a parent file, that it is spelled correctly, and that it is within the correct section of the settings file (e.g., under the game definition where necessary).");
                switch (type)
                {
                    case "bool":
                        if (contents.Trim().ToUpper() == "TRUE" || contents.Trim().ToUpper() == "T")
                            return new SettingBoolean(name, allVariablesFromProgram, true);
                        else if (contents.Trim().ToUpper() == "FALSE" || contents.Trim().ToUpper() == "F")
                            return new SettingBoolean(name, allVariablesFromProgram, false);
                        else
                            throw new FileLoaderException("Invalid bool value in setting " + name);

                    case "double":
                        try
                        {
                            double floatValue = Convert.ToDouble(contents);
                            return new SettingDouble(name, allVariablesFromProgram, floatValue);
                        }
                        catch (Exception ex)
                        {
                            throw new FileLoaderException(String.Format("Invalid double value ({0}) in setting {1}", contents, name), ex);
                        }

                    case "int":

                        int intValue;

                        try
                        {
                            intValue = Convert.ToInt32(contents.Replace(",",""));
                        }
                        catch (FormatException ex)
                        {
                            throw new FileLoaderException(
                                String.Format("Cannot convert the string \"{0}\" to an int: {1}", contents, ex.Message, ex),
                                ex);
                        }
                        catch (OverflowException ex)
                        {
                            throw new FileLoaderException(
                                String.Format("The integer represented by \"{0}\" is too large to be an int: {1}", contents, ex.Message, ex),
                                ex);
                        }

                        return new SettingInt32(name, allVariablesFromProgram, intValue);

                    case "long":

                        long longValue;

                        try
                        {
                            longValue = Convert.ToInt64(contents.Replace(",", ""));
                        }
                        catch (FormatException ex)
                        {
                            throw new FileLoaderException(
                                String.Format("Cannot convert the string \"{0}\" to a long: {1}", contents, ex.Message, ex),
                                ex);
                        }
                        catch (OverflowException ex)
                        {
                            throw new FileLoaderException(
                                String.Format("The integer represented by \"{0}\" is too large to be a long: {1}", contents, ex.Message, ex),
                                ex);
                        }

                        return new SettingInt64(name, allVariablesFromProgram, longValue);

                    case "text":
                    case "string":
                        return new SettingString(name, allVariablesFromProgram, contents);

                    case "variableFromSetting":
                        return new SettingVariableFromSetting(name, allVariablesFromProgram, contents);

                    case "variableFromProgram":
                        return new SettingVariableFromProgram(name, allVariablesFromProgram, contents);

                    case "distribution":
                        List<Setting> subelements = settingElement.Elements("setting").Select(x => ProcessSetting(x, allVariablesFromProgram, codeGeneratorFactory)).ToList();
                        string subtype = GetStringAttribute(settingElement, "subtype", true);
                        Tuple<string, List<Setting>> distributionID = new Tuple<string, List<Setting>>(subtype, subelements);
                        int hashCode = distributionID.GetHashCode();
                        DistributionFactory theDistributionFactory; // it takes a while to set up a distribution factory so we keep a dictionary to speed things up
                        if (DistributionFactoryDictionary.ContainsKey(hashCode))
                            theDistributionFactory = DistributionFactoryDictionary[hashCode];
                        else
                        {
                            theDistributionFactory = new DistributionFactory();
                            DistributionFactoryDictionary.Add(hashCode, theDistributionFactory);
                        }
                        IDistribution theDistribution = theDistributionFactory.GetDistribution(subtype);
                        theDistribution.Initialize(subelements);
                        return new SettingDistribution(name, allVariablesFromProgram, theDistribution);

                    case "class":
                        string numberRepetitionsString = GetStringAttribute(settingElement, "repetitions", false);
                        List<Setting> subelements2 = settingElement.Elements("setting").Select(x => ProcessSetting(x, allVariablesFromProgram, codeGeneratorFactory)).ToList();

                        string subclassName = GetStringAttribute(settingElement, "subclass", false);
                        string codeGeneratorName = GetStringAttribute(settingElement, "codeGeneratorName", false);
                        string codeGeneratorOptions = GetStringAttribute(settingElement, "codeGeneratorOptions", false);
                        Type subclassToUse = null;
                        if (subclassName != null && subclassName != "")
                        {
                            Assembly currentAssembly = Assembly.GetExecutingAssembly();
                            string fullNameOfCurrentAssembly = currentAssembly.GetName().Name;
                            subclassToUse = currentAssembly.GetType(fullNameOfCurrentAssembly + "." + subclassName);
                            if (subclassToUse == null)
                                subclassToUse = currentAssembly.GetType(subclassName); // it's in a different assembly.
                            if (subclassToUse == null)
                                throw new Exception("Could not find subclass type " + subclassName);
                        }
                        if (numberRepetitionsString == null || numberRepetitionsString == "")
                        {

                            return new SettingClass(name, subclassToUse, allVariablesFromProgram, subelements2, codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions);
                        }
                        else
                        { // we are going to repeat this class some number of times, and thus will instead produce a setting list.
                            string startingValueString = GetStringAttribute(settingElement, "startingValue", false);
                            if (startingValueString == null || startingValueString == "")
                                startingValueString = "0";
                            string incrementString = GetStringAttribute(settingElement, "increment", false);
                            if (incrementString == null || incrementString == "")
                                incrementString = "1";
                            int numberRepetitions = Convert.ToInt32(numberRepetitionsString);
                            double startingValue = Convert.ToDouble(startingValueString);
                            double increment = Convert.ToDouble(incrementString);
                            List<Setting> subelementsOfList = new List<Setting>();
                            for (int repetition = 0; repetition < numberRepetitions; repetition++)
                            {
                                double value = (double) (startingValue + repetition * increment);
                                SettingClass classToAddToList = new SettingClass(name + repetition.ToString(), subclassToUse, allVariablesFromProgram, subelements2.Select(x => x.DeepCopy()).ToList(), codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions);
                                classToAddToList.ReplaceContainedVariableSettingWithDouble("repetition", value);
                                subelementsOfList.Add(classToAddToList);
                            }
                            return new SettingList(name, allVariablesFromProgram, subelementsOfList, codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions);
                        }
                    case "list":
                        codeGeneratorName = GetStringAttribute(settingElement, "codeGeneratorName", false);
                        codeGeneratorOptions = GetStringAttribute(settingElement, "codeGeneratorOptions", false);
                        List<Setting> subelements3 = settingElement.Elements("setting").Select(x => ProcessSetting(x, allVariablesFromProgram, codeGeneratorFactory)).ToList();
                        if (subelements3.Count > 1)
                        {
                            Setting firstSetting = subelements3[0];
                            for (int i = 1; i < subelements3.Count; i++)
                                if (firstSetting.Type != subelements3[i].Type)
                                    throw new FileLoaderException("Setting of type list must contain all elements of the same type.");
                        }
                        return new SettingList(name, allVariablesFromProgram, subelements3, codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions);

                    case "strategy":
                        string filename = GetStringAttribute(settingElement, "filename", true);
                        int strategyNum = (int)GetNumericAttribute(settingElement, "decisionNum", true);
                        return new SettingStrategy(name, allVariablesFromProgram, filename, strategyNum);

                    case "calc":
                        string operatorString = GetStringAttribute(settingElement, "operator", true);
                        SettingCalcOperator oper8r = SettingCalc.OperatorStringsToOperators[operatorString];
                        List<Setting> operands = settingElement.Elements("setting").Select(x => ProcessSetting(x, allVariablesFromProgram, codeGeneratorFactory)).ToList();
                        return new SettingCalc(name, allVariablesFromProgram, oper8r, operands);

                    default:
                        throw new FileLoaderException("Setting " + name + " has unknown type " + type);
                }
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process setting " + name + ": " + ex.Message, ex);
            }
        }

        public void ProcessExecuteList(XElement commandListToProcess)
        {
            try
            {
                var xmlCommandSetReferences = commandListToProcess.Elements("executeCommandSet");
                foreach (var xmlCommandSetReference in xmlCommandSetReferences)
                {
                    string name = GetName(xmlCommandSetReference);
                    CommandSet theCommandSet = theCompleteSettings.CommandSets.SingleOrDefault(x => x.Name == name);
                    if (theCommandSet == null)
                        throw new FileLoaderException("The command set " + name + " included in the executeCommandSet could not be found.");
                    theCompleteSettings.CommandSetsToExecute.Add(theCommandSet);
                }
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to execute command list: " + ex.Message, ex);
            }
        }

        public MultiPartCommand ProcessMultipartCommandForSimulation(XElement commandToProcess, Dictionary<string, double> allVariablesFromProgram)
        {
            List<Command> theList = new List<Command>();
            string name = GetName(commandToProcess);
            try
            {
                string gameInputsName = GetStringAttribute(commandToProcess, "gameInputsName", true);

                GameInputsSet theSimulationSettingsSet = theCompleteSettings.GameInputsSets.SingleOrDefault(x => x.name == gameInputsName);
                if (theSimulationSettingsSet == null)
                    throw new FileLoaderException("The command " + name + " specified nonexistent simulation settings set " + gameInputsName);

                GameDefinitionSettingsSet theGameDefinitionSet = theCompleteSettings.GameDefinitionSets.SingleOrDefault(x => x.name == theSimulationSettingsSet.GameName);
                if (theGameDefinitionSet == null)
                    throw new FileLoaderException("The simulation settings specified a simulationName " + theSimulationSettingsSet.GameName + ", but no game definition with this name could be found.");

                var specificCommands = commandToProcess.Elements();
                MultiPartCommand theCommandsForSimulation = new MultiPartCommand(name, gameInputsName, theList, theGameDefinitionSet);
                foreach (var specificCommand in specificCommands)
                {
                    Command theCommandToAdd = null;
                    if (specificCommand.Name == "evolveCommand")
                        theCommandToAdd = ProcessEvolveCommand(specificCommand, theCommandsForSimulation);
                    else if (specificCommand.Name == "playCommand")
                        theCommandToAdd = ProcessPlayCommand(specificCommand, theCommandsForSimulation, theSimulationSettingsSet, allVariablesFromProgram);
                    else if (specificCommand.Name == "reportCommand")
                        theCommandToAdd = ProcessReportCommand(specificCommand, theCommandsForSimulation);
                    if (theCommandToAdd != null)
                        theList.Add(theCommandToAdd);
                }
                return theCommandsForSimulation;
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process multipart command " + name + ": " + ex.Message, ex);
            }
        }

        public EvolveCommand ProcessEvolveCommand(XElement commandToProcess, MultiPartCommand theMultipartCommand)
        {
            try
            {
                string evolutionSettingsName = GetStringAttribute(commandToProcess, "evolutionSettingsName", true);
                string InitializeStrategiesFile = GetStringAttribute(commandToProcess, "initializeStrategiesFile", true);
                bool UseInitializeStrategiesFile = GetBoolAttribute(commandToProcess, "useInitializeStrategiesFile", true);
                string StoreStrategiesFile = GetStringAttribute(commandToProcess, "storeStrategiesFile", true);
                bool evolveOnlyIfNecessary = GetBoolAttribute(commandToProcess, "evolveOnlyIfNecessary", true);
                if (evolveOnlyIfNecessary && !UseInitializeStrategiesFile)
                    throw new Exception("If evolveOnlyIfNecessary is true, an initialize strategies file must be specified.");

                List<ReportCommand> embeddedReportsBetweenDecisions = new List<ReportCommand>();
                List<ReportCommand> embeddedReportsAfterEvolveSteps = new List<ReportCommand>();
                var specificCommands = commandToProcess.Elements().Where(x => x.Name == "reportCommand");
                foreach (var specificCommand in specificCommands)
                {
                    bool betweenDecisions = GetBoolAttribute(specificCommand, "betweenDecisions", true);
                    ReportCommand theCommandToAdd = null;
                    theCommandToAdd = ProcessReportCommand(specificCommand, null);
                    if (theCommandToAdd != null)
                    {
                        if (betweenDecisions)
                            embeddedReportsBetweenDecisions.Add(theCommandToAdd);
                        else
                            embeddedReportsAfterEvolveSteps.Add(theCommandToAdd);
                    }
                }

                return new EvolveCommand(theMultipartCommand, evolutionSettingsName, InitializeStrategiesFile, UseInitializeStrategiesFile, StoreStrategiesFile,
                    evolveOnlyIfNecessary, baseOutputDirectory, embeddedReportsBetweenDecisions, embeddedReportsAfterEvolveSteps);
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process evolve command: " + ex.Message, ex);
            }
        }

        public ChangeSimulationSettingContainer ProcessChangeSimulationSettingContainer(XElement commandToProcess, GameInputsSet theSimulationSettingsSet, Dictionary<string, double> allVariablesFromProgram, bool isConcurrent, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            try
            {
                List<ChangeSimulationSettings> theChangeSimulationSettings = new List<ChangeSimulationSettings>();
                List<ChangeSimulationSettingGenerator> theGenerators = new List<ChangeSimulationSettingGenerator>();
                List<ChangeSimulationSettingContainer> theContainers = new List<ChangeSimulationSettingContainer>();
                var xmlChangeSimulationSettingsGroup = commandToProcess.Elements("changeSimulationSettings");
                foreach (XElement xmlChangeSimulationSettings in xmlChangeSimulationSettingsGroup)
                {
                    ChangeSimulationSettings theSettings = ProcessChangeSimulationSettings(xmlChangeSimulationSettings, theSimulationSettingsSet, allVariablesFromProgram, codeGeneratorFactory);
                    theChangeSimulationSettings.Add(theSettings);
                }
                var xmlChangeSimulationSettingGeneratorGroup = commandToProcess.Elements("changeSimulationSettingsGenerator");
                foreach (XElement xmlChangeSimulationSettingGenerator in xmlChangeSimulationSettingGeneratorGroup)
                {
                    ChangeSimulationSettingGenerator theSettings = ProcessChangeSimulationSettingGenerator(xmlChangeSimulationSettingGenerator, theSimulationSettingsSet, allVariablesFromProgram);
                    theGenerators.Add(theSettings);
                }
                var xmlChangeSimulationSettingContainerGroup = commandToProcess.Elements("changeSimulationSettingsContainer");
                foreach (XElement xmlChangeSimulationSettingContainer in xmlChangeSimulationSettingContainerGroup)
                {
                    bool containedIsConcurrent = GetBoolAttribute(xmlChangeSimulationSettingContainer, "isConcurrent", true);
                    ChangeSimulationSettingContainer theSettings = ProcessChangeSimulationSettingContainer(xmlChangeSimulationSettingContainer, theSimulationSettingsSet, allVariablesFromProgram, containedIsConcurrent, codeGeneratorFactory);
                    theContainers.Add(theSettings);
                }
                ChangeSimulationSettingContainer theContainer = new ChangeSimulationSettingContainer() { ChangeSimulationSettingsList = theChangeSimulationSettings, ChangeSimulationSettingGeneratorList = theGenerators, ChangeSimulationSettingContainerList = theContainers, IsConcurrent = isConcurrent};
                return theContainer;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process play command: " + ex.Message, ex);
            }
        }

        public PlayCommand ProcessPlayCommand(XElement commandToProcess, MultiPartCommand theMultipartCommand, GameInputsSet theSimulationSettingsSet, Dictionary<string, double> allVariablesFromProgram)
        {
            try
            {
                int numberIterations = (int)GetNumericAttribute(commandToProcess, "numberIterations", true);
                string StoreStrategiesFile = GetStringAttribute(commandToProcess, "StoreStrategiesFile", false);
                string storeIterationResultsFile = GetStringAttribute(commandToProcess, "storeIterationResultsFile", true);
                bool parallelReporting = GetBoolAttribute(commandToProcess, "parallelReporting", true);
                bool savePlayedGamesInMemoryOnly = GetBoolAttribute(commandToProcess, "savePlayedGamesInMemoryOnly", true);

                ChangeSimulationSettingContainer implicitContainer = ProcessChangeSimulationSettingContainer(commandToProcess, theSimulationSettingsSet, allVariablesFromProgram, false, codeGeneratorFactory); // by default, all changes are consecutive

                PlayCommand theCommand = new PlayCommand(theMultipartCommand, numberIterations, StoreStrategiesFile,
                    storeIterationResultsFile, implicitContainer, baseOutputDirectory, parallelReporting, savePlayedGamesInMemoryOnly);
                return theCommand;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process play command: " + ex.Message, ex);
            }
        }


        public ReportCommand ProcessReportCommand(XElement commandToProcess, MultiPartCommand theMultipartCommand)
        {
            try
            {
                string reportName = GetName(commandToProcess);
                string storeIterationResultsFile = GetStringAttribute(commandToProcess, "storeIterationResultsFile", true);
                string storeReportResultsFile = GetStringAttribute(commandToProcess, "storeReportResultsFile", true);
                bool parallelReporting = GetBoolAttribute(commandToProcess, "parallelReporting", false);
                bool requireCumulativeDistributions = GetBoolAttribute(commandToProcess, "requireCumulativeDistributions", false);
                List<RowOrColumnGroup> theRowsGroups = ProcessRowsGroupListOrColsGroupList(commandToProcess, true);
                List<RowOrColumnGroup> theColsGroups = ProcessRowsGroupListOrColsGroupList(commandToProcess, false);
                List<PointChartReport> thePointChartReports = ProcessPointChartReportList(commandToProcess);
                ReportCommand theCommand = new ReportCommand(theMultipartCommand, reportName, storeIterationResultsFile,
                    storeReportResultsFile, theRowsGroups, theColsGroups, thePointChartReports, baseOutputDirectory, requireCumulativeDistributions, parallelReporting);
                return theCommand;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process report command: " + ex.Message, ex);
            }
        }

        public List<PointChartReport> ProcessPointChartReportList(XElement commandToProcess)
        {
            try
            {
                var xmlPointChartReports = commandToProcess.Elements("pointChartReport");
                List<PointChartReport> pointChartReports = new List<PointChartReport>();
                foreach (var xmlPointChartReport in xmlPointChartReports)
                    pointChartReports.Add(ProcessPointChartReport(xmlPointChartReport));
                return pointChartReports;
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process point chart reports: " + ex.Message, ex);
            }
        }

        public PointChartReport ProcessPointChartReport(XElement pointChartReportElement)
        {
            string xAxisVariableName = GetStringAttribute(pointChartReportElement, "xAxisVariableName", true);
            string yAxisVariableName = GetStringAttribute(pointChartReportElement, "yAxisVariableName", true);
            XElement graph2DSettingsElement = pointChartReportElement.Elements("graph2DSettings").Single();
            Graph2DSettings graph2DSettings = ProcessGraph2DSettings(graph2DSettingsElement);
            return new PointChartReport() { XAxisVariableName = xAxisVariableName, YAxisVariableName = yAxisVariableName, Graph2DSettings = graph2DSettings };
        }

        public Graph2DSettings ProcessGraph2DSettings(XElement graph2DSettingsElement)
        {
            string graphName = GetStringAttribute(graph2DSettingsElement, "graphName", true);
            string seriesName = GetStringAttribute(graph2DSettingsElement, "seriesName", false) ?? "";
            double? xMin = GetNumericAttribute(graph2DSettingsElement, "xMin", false);
            double? xMax = GetNumericAttribute(graph2DSettingsElement, "xMax", false);
            double? yMin = GetNumericAttribute(graph2DSettingsElement, "yMin", false);
            double? yMax = GetNumericAttribute(graph2DSettingsElement, "yMax", false);
            string xAxisLabel = GetStringAttribute(graph2DSettingsElement, "xAxisLabel", false) ?? "";
            string yAxisLabel = GetStringAttribute(graph2DSettingsElement, "yAxisLabel", false) ?? "";
            string replacementXValues = GetStringAttribute(graph2DSettingsElement, "replacementXValues", false);
            string replacementYValues = GetStringAttribute(graph2DSettingsElement, "replacementYValues", false);
            string downloadLocation = GetStringAttribute(graph2DSettingsElement, "downloadLocation", false);
            bool replaceSeriesOfSameName = GetBoolAttribute(graph2DSettingsElement, "replaceSeriesOfSameName", false);
            bool fadeSeriesOfSameName = GetBoolAttribute(graph2DSettingsElement, "fadeSeriesOfSameName", false);
            bool fadeSeriesOfDifferentName = GetBoolAttribute(graph2DSettingsElement, "fadeSeriesOfDifferentName", false);
            bool spline = GetBoolAttribute(graph2DSettingsElement, "spline", false);
            bool scatterplot = GetBoolAttribute(graph2DSettingsElement, "scatterplot", false);
            bool dockLegendLeft = GetBoolAttribute(graph2DSettingsElement, "dockLegendLeft", false);
            bool exportFramesOfMovies = GetBoolAttribute(graph2DSettingsElement, "exportFramesOfMovies", false);
            int? maxNumberPoints = (int?)GetNumericAttribute(graph2DSettingsElement, "maxNumberPoints", false);
            int? maxVisiblePerSeries = (int?)GetNumericAttribute(graph2DSettingsElement, "maxVisiblePerSeries", false);
            string superimposeLinesToSeriesWithName = GetStringAttribute(graph2DSettingsElement, "superimposeLinesToSeriesWithName", false);
            bool highlightSuperimposedWhenFirstHigher = GetBoolAttribute(graph2DSettingsElement, "highlightSuperimposedWhenFirstHigher", false);
            return new Graph2DSettings() { graphName = graphName, seriesName = seriesName, xMin = xMin, xMax = xMax, yMin = yMin, yMax = yMax, xAxisLabel = xAxisLabel, yAxisLabel = yAxisLabel, downloadLocation = downloadLocation, replaceSeriesOfSameName = replaceSeriesOfSameName, fadeSeriesOfSameName = fadeSeriesOfSameName, fadeSeriesOfDifferentName = fadeSeriesOfDifferentName, spline = spline, scatterplot = scatterplot, maxNumberPoints = maxNumberPoints, superimposeLinesToSeriesWithName = superimposeLinesToSeriesWithName, highlightSuperimposedWhenFirstHigher = highlightSuperimposedWhenFirstHigher, dockLegendLeft = dockLegendLeft, exportFramesOfMovies = exportFramesOfMovies, maxVisiblePerSeries = maxVisiblePerSeries, replacementXValues = replacementXValues, replacementYValues = replacementYValues, superimposedLines = null /* can't do this in xml file for now */ };
        }

        public List<RowOrColumnGroup> ProcessRowsGroupListOrColsGroupList(XElement commandToProcess, bool rows)
        {
            try
            {
                var xmlRowsOrColsGroups = commandToProcess.Elements(rows ? "rowsGroup" : "colsGroup");
                List<RowOrColumnGroup> theRowsOrColsGroups = new List<RowOrColumnGroup>();
                foreach (var xmlRowsOrColsGroup in xmlRowsOrColsGroups)
                    theRowsOrColsGroups.Add(ProcessRowsGroupOrColsGroup(xmlRowsOrColsGroup));
                return theRowsOrColsGroups;
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to rowsGroup list or colsGroup list: " + ex.Message, ex);
            }
        }

        public RowOrColumnGroup ProcessRowsGroupOrColsGroup(XElement settingsToProcess)
        {
            string name = GetName(settingsToProcess);
            try
            {
                RowOrColumnGroup existingRowOrColumnGroup = theCompleteSettings.RowOrColumnGroups.SingleOrDefault(x => x.name == name);
                if (existingRowOrColumnGroup == null)
                    throw new FileLoaderException("Row or column group " + name + " not found. The row or column group must be included before the report command.");
                return existingRowOrColumnGroup;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process rowsGroup or colsGroup " + name + ": " + ex.Message, ex);
            }
        }

        public ChangeSimulationSettings ProcessChangeSimulationSettings(XElement settingToProcess, GameInputsSet theSimulationSettingsSet, Dictionary<string, double> allVariablesFromProgram, CodeBasedSettingGeneratorFactory codeGeneratorFactory)
        {
            try
            {
                List<Setting> theNewSettings = new List<Setting>();
                var theNewSettingsXML = settingToProcess.Elements("setting");
                foreach (var theNewSettingXML in theNewSettingsXML)
                {
                    Setting theNewSetting = ProcessSetting(theNewSettingXML, allVariablesFromProgram, codeGeneratorFactory);
                    if (theSimulationSettingsSet.settings.Where(x => x.Name == theNewSetting.Name).Count() != 1)
                        throw new FileLoaderException("The changeSimulationSettings tries to change " + theNewSetting.Name + " but this setting is not included in the settings for the simulation set " + theSimulationSettingsSet.name);
                    theNewSettings.Add(theNewSetting);
                }
                return new ChangeSimulationSettings(theNewSettings);
            }

            catch (FileLoaderException ex)
            {
                throw new FileLoaderException("Failed to process changeSimulationSetting: " + ex.Message, ex);
            }
        }

        public ChangeSimulationSettingGenerator ProcessChangeSimulationSettingGenerator(
            XElement settingToProcess, GameInputsSet theSimulationSettingsSet, Dictionary<string, double> allVariablesFromProgram)
        {
            // attributes simulationSettingName, startingValue, increment, and numValues
            string simulationSettingName = GetStringAttribute(settingToProcess, "simulationSettingName", true);
            try
            {

                if (theSimulationSettingsSet.settings.Where(x => x.Name == simulationSettingName).Count() != 1)
                    throw new FileLoaderException("The changeSimulationSettingsGenerator tries to change " + simulationSettingName + " but this setting is not included in the settings for the simulation set " + theSimulationSettingsSet.name);

                double startingValue = (double) GetNumericAttribute(settingToProcess, "startingValue", true);
                double increment = (double) GetNumericAttribute(settingToProcess, "increment", true);
                int numValues = (int)GetNumericAttribute(settingToProcess, "numValues", true);

                ChangeSimulationSettings theOriginalChanges = null;
                var theOriginalChangesXML = settingToProcess.Elements("changeSimulationSettings");
                if (theOriginalChangesXML.Count() == 0)
                    theOriginalChanges = new ChangeSimulationSettings(new List<Setting>());
                else if (theOriginalChangesXML.Count() == 1)
                    theOriginalChanges = ProcessChangeSimulationSettings(theOriginalChangesXML.First(), theSimulationSettingsSet, allVariablesFromProgram, codeGeneratorFactory);
                else
                    throw new FileLoaderException("A changeSimulationSettingsGenerator can have no more than one changeSimulationSettings element.");

                string numericSettingTypeString = GetStringAttribute(settingToProcess, "numericType", true);
                SettingType numericSettingType = Setting.SettingTypeStringToSettingType[numericSettingTypeString];

                ChangeSimulationSettingGenerator theGenerator =
                    new ChangeSimulationSettingGenerator(simulationSettingName, startingValue, increment,
                        numValues, numericSettingType);
                return theGenerator;
            }
            catch (FileLoaderException ex)
            {
                throw new FileLoaderException(
                    "Failed to process change simulation setting generator for " + simulationSettingName + ": " + ex.Message,
                    ex);
            }
        }


        public string GetType(XElement theElement)
        {
            return GetStringAttribute(theElement, "type", true);
        }

        public string GetName(XElement theElement)
        {
            return GetStringAttribute(theElement, "name", true);
        }

        public string GetStringAttribute(XElement theElement, string theAttribute, bool required)
        {

            XName theAttributeName = theAttribute;
            string attributeContents = (string)theElement.Attribute(theAttributeName);
            if (attributeContents == null)
            {
                if (required)
                    throw new FileLoaderException("Element missing required attribute " + theAttribute);
                return "";
            }
            return attributeContents;
        }

        public bool GetBoolAttribute(XElement theElement, string theAttribute, bool required)
        {
            string boolText = GetStringAttribute(theElement, theAttribute, required).Trim().ToUpper();
            if (!required && (boolText == "" || boolText == null))
                return false;
            if (boolText == "TRUE" || boolText == "T")
                return true;
            else if (boolText == "FALSE" || boolText == "F")
                return false;
            throw new Exception("Invalid boolean attribute.");
        }

        public double? GetNumericAttribute(XElement theElement, string theAttribute, bool required)
        {
            string theString = GetStringAttribute(theElement, theAttribute, required);
            if (theString == null || theString == "")
                return null;
            decimal theNum = 0;
            try
            {
                theNum = Convert.ToDecimal(theString);
            }
            catch (Exception ex)
            {
                throw new FileLoaderException("Attribute " + theAttribute + " should contain numeric data only.", ex);
            }
            return (double)theNum;
        }
    }


}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class CompleteSettings
    {
        public List<EvolutionSettingsSet> EvolutionSettingsSets;
        public List<GameDefinitionSettingsSet> GameDefinitionSets;
        public List<GameInputsSet> GameInputsSets;
        public List<RowOrColumnGroup> RowOrColumnGroups;
        public List<CommandSet> CommandSets;
        public List<CommandSet> CommandSetsToExecute;
        [System.Xml.Serialization.XmlIgnore] // this is by necessity, since dictionaries cannot be xml serialized -- but it would be a problem if we relied on xml serialization other than for reporting purposes
        public Dictionary<string, double> AllVariablesFromProgram;
        public string NameOfRun;
        public List<string> NamesOfVariablesSetsChosen;
        public List<string> NamesOfVariablesSets;

        public CompleteSettings()
        {
            EvolutionSettingsSets = new List<EvolutionSettingsSet>();
            GameInputsSets = new List<GameInputsSet>();
            GameDefinitionSets = new List<GameDefinitionSettingsSet>();
            RowOrColumnGroups = new List<RowOrColumnGroup>();
            CommandSets = new List<CommandSet>();
            CommandSetsToExecute = new List<CommandSet>();
            AllVariablesFromProgram = new Dictionary<string, double>();
            NameOfRun = "Untitled";
            NamesOfVariablesSetsChosen = new List<string>() { "Single" + DateTime.Now.ToShortTimeString() };
            NamesOfVariablesSets = new List<string>() { "Run" };
        }
    }
}

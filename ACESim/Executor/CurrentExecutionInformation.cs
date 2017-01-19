using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;

namespace ACESim
{
    [Serializable]
    public class CurrentExecutionInformation
    {
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public IUiInteraction UiInteraction;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public EvolutionSettingsSet EvolutionSettingsSet;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public GameDefinitionSettingsSet GameDefinitionsSet;
        public GameInputsSet GameInputsSet;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public ChangeSimulationSettings SettingOverride;
        public Command CurrentCommand;
        public InputSeedsStorage InputSeedsSet;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public ConcurrentStack<GameProgressReportable> Outputs;
        [System.Xml.Serialization.XmlIgnore] // by necessity since it is an interface
        public IGameFactory GameFactory;
        [System.Xml.Serialization.XmlIgnore] // this is by necessity, since dictionaries cannot be xml serialized -- but it would be a problem if we relied on xml serialization other than for reporting purposes
        public Dictionary<string, double> AllVariablesFromProgram;
        public string NameOfRunAndCommandSet;
        public List<string> NameOfVariableSetsChosen;
        [NonSerialized]
        ProgressResumptionManager ProgressResumptionManager;

        // parameterless constructor for serialization
        public CurrentExecutionInformation()
        {
        }

        public CurrentExecutionInformation(
            Dictionary<string, double> allVariablesFromProgram,
            EvolutionSettingsSet evolutionSettingsSet, 
            GameDefinitionSettingsSet gameDefinitionsSettingSet, 
            GameInputsSet simulationSettingsSet, 
            Command currentCommand, 
            IUiInteraction uiInteraction,
            ProgressResumptionManager prm)
        {
            this.AllVariablesFromProgram = allVariablesFromProgram;
            this.EvolutionSettingsSet = evolutionSettingsSet;
            this.GameDefinitionsSet = gameDefinitionsSettingSet;
            this.GameInputsSet = simulationSettingsSet;
            this.CurrentCommand = currentCommand;
            this.UiInteraction = uiInteraction;
            this.ProgressResumptionManager = prm;
            Reset();
        }

        public void Reset()
        {
            SettingOverride = null;
            InputSeedsSet = new InputSeedsStorage();
            Outputs = new ConcurrentStack<GameProgressReportable>();

            if (GameDefinitionsSet != null)
            {
                GameFactoryFactory theGameFactoryFactory = new GameFactoryFactory();
                GameFactory = theGameFactoryFactory.GetGameFactory(GameDefinitionsSet.name);
            }

            if (EvolutionSettingsSet != null)
            {
            }
            else if (CurrentCommand is PlayCommand)
            {
                (CurrentCommand as PlayCommand).PrepareToExecute();
            }
        }

        public void AddOutput(GameProgressReportable output)
        {
            Outputs.Push(output);
        }

        public Setting GetSetting(string name, bool optional)
        {
            Setting theSetting = null;
            if (EvolutionSettingsSet != null)
                theSetting = EvolutionSettingsSet.settings.FirstOrDefault(x => x.Name == name);
            if (theSetting == null && GameDefinitionsSet != null)
                theSetting = GameDefinitionsSet.settings.FirstOrDefault(x => x.Name == name);
            if (theSetting == null && GameInputsSet != null)
                theSetting = GameInputsSet.settings.FirstOrDefault(x => x.Name == name);
            if (theSetting == null && !optional)
                throw new Exception("Setting " + name + " not found.");
            return theSetting;
        }
    }
}

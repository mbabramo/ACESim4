using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Path = System.IO.Path;
using Directory = System.IO.Directory;

namespace ACESim
{
    [Serializable]
    public class SimulationInteraction : IUiInteraction
    {
        public string BaseOutputDirectory;
        public const string storedStrategiesSubdirectory = "Strategies";
        public const string iterationsSubdirectory = "Iterations";
        public const string reportsSubdirectory = "Reports";
        public const string settingsSubdirectory = "Settings";

        public SimulationInteraction()
        {
        }

        internal DateTime? _startTime = null;
        public DateTime StartTime 
        { 
            get 
            { 
                if (_startTime == null) 
                    _startTime = DateTime.Now; 
                return (DateTime) _startTime; 
            } 
        }

        public bool StopAfterOptimizingCurrentDecisionPoint = false;
        
        public int? HighestCumulativeDistributionUpdateIndexEvolved;

        public CurrentExecutionInformation CurrentExecutionInformation
        { get; set; }

        public StringBuilder metaReport;
        public List<string> namesOfVariableSetsChosen;
        public List<string> namesOfVariableSets;

        public SimulationInteraction(
            CurrentExecutionInformation theCurrentExecutionInformation,
            string theBaseOutputDirectory,
            StringBuilder theMetaReport,
            List<string> theNamesOfVariableSets,
            List<string> theNamesOfVariableSetChosen,
            int theTotalCommandCount)
        {
            CurrentExecutionInformation = theCurrentExecutionInformation;
            BaseOutputDirectory = theBaseOutputDirectory;
            CreateOutputSubdirectories(theBaseOutputDirectory);
            metaReport = theMetaReport;
            namesOfVariableSetsChosen = theNamesOfVariableSetChosen;
            namesOfVariableSets = theNamesOfVariableSets;
        }

        private void CreateOutputSubdirectories(string theBaseOutputDirectory)
        {
            string[] subDirectories = new string[] { storedStrategiesSubdirectory, iterationsSubdirectory, reportsSubdirectory, settingsSubdirectory };
            IEnumerable<string> directories = subDirectories.Select(sub => Path.Combine(theBaseOutputDirectory, sub));

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        public string GetSimulationName()
        {
            return CurrentExecutionInformation.GameInputsSet.GameName;
        }

        public EvolutionSettings GetEvolutionSettings()
        {
            if (CurrentExecutionInformation.CurrentCommand is EvolveCommand)
            {
                InputVariables theInputVariables = new InputVariables(CurrentExecutionInformation);
                var evolSettings = theInputVariables.GetEvolutionSettings();
                return evolSettings;
            }
            return null;
        }

        public GameDefinition PreviouslyLoadedGameDefinition;
        public GameDefinition GetGameDefinition()
        {
            InputVariables theInputVariables = new InputVariables(CurrentExecutionInformation);
            Type theType = CurrentExecutionInformation.GameFactory.GetGameDefinitionType();
            GameDefinition theGameDefinition = theInputVariables.GetGameDefinitionSettings(theType);
            theGameDefinition.Initialize();
            PreviouslyLoadedGameDefinition = theGameDefinition;
            return PreviouslyLoadedGameDefinition;
        }


        

        public void ReportVariableFromProgram(string name, double value)
        {
            //if (CurrentExecutionInformation.Variables.ContainsKey(name))
            //    CurrentExecutionInformation.Variables[name] = value;
            //else
            //    CurrentExecutionInformation.Variables.Add(name, value);
            CurrentExecutionInformation.AllVariablesFromProgram[name] = value;
        }


        public void ReportDecisionNumber(int currentValue)
        {
            CurrentExecutionInformation.UiInteraction.ReportDecisionNumber(currentValue);
        }

        public void HandleException(Exception ex)
        {
            CurrentExecutionInformation.UiInteraction.HandleException(ex);
        }

        public void SetRunStatus(RunStatus running)
        {
            CurrentExecutionInformation.UiInteraction.SetRunStatus(running);
        }

        public void ReportTextToUser(string text, bool append)
        {
            CurrentExecutionInformation.UiInteraction.ReportTextToUser(text, append);
        }

        public ProgressStep GetCurrentProgressStep()
        {
            if (CurrentExecutionInformation.UiInteraction == null)
                return null;
            return CurrentExecutionInformation.UiInteraction.GetCurrentProgressStep();
        }

        public void AbortChildSteps(string stepTypeToNotSetComplete)
        {
            bool done = false;
            while (!done)
            {
                ProgressStep current = GetCurrentProgressStep();
                if (current.StepType != stepTypeToNotSetComplete)
                    current.SetProportionOfStepComplete(1.0, true, current.StepType);
                else
                    done = true;
            }
        }

        public void ResetProgressStep()
        {
            CurrentExecutionInformation.UiInteraction.ResetProgressStep();
        }

        public void ReportComplete()
        {
            CurrentExecutionInformation.UiInteraction.ReportComplete();
        }

        public void CheckStopOrPause(out bool stop)
        {
            CurrentExecutionInformation.UiInteraction.CheckStopOrPause(out stop);
        }

        public bool CheckStopSoon()
        {
            return CurrentExecutionInformation.UiInteraction.CheckStopSoon();
        }

        /// <summary>
        /// ACESim's Play command should call the following to report one or more outputs from the
        /// game. The game class should make sure that we are in "Play" mode rather than Evolve</summary>
        /// mode before doing this.
        /// <param name="output"></param>
        public void ReportOutputForIteration(GameProgressReportable output)
        {
            CurrentExecutionInformation.AddOutput(output);
        }

        // Keep track of a strategy that is reported as complete. When the Evolve step is completed,
        // this code will automatically serialize the evolvedStrategy to a file.
        public void ReportEvolvedStrategies(List<Strategy> evolvedStrategies)
        {
            Command currentCommand = CurrentExecutionInformation.CurrentCommand;
            if (currentCommand is EvolveCommand && evolvedStrategies.Any())
            {
                // In order to get the right Xml, make a list that is typed to the actual type of the strategies, not Strategy (which is just the generic interface.)
                object typedList = Activator.CreateInstance(typeof(List<>).MakeGenericType(evolvedStrategies[0].GetType()));
                foreach (Strategy strategy in evolvedStrategies)
                {
                    typedList.GetType().InvokeMember("Add", System.Reflection.BindingFlags.InvokeMethod, null, 
                        typedList, new object[] { strategy });
                }

                EvolveCommand theCommand = currentCommand as EvolveCommand;
                if (String.IsNullOrEmpty(theCommand.StoreStrategiesFile))
                    throw new Exception("ReportEvolvedStrategies called without specifying StoreStrategiesFile.");
                StrategySerialization.SerializeStrategies(evolvedStrategies.ToArray(), Path.Combine(BaseOutputDirectory, storedStrategiesSubdirectory, theCommand.StoreStrategiesFile));
            }
        }

        // Load previously evolved strategies for the current game being played.
        public Strategy[] LoadEvolvedStrategies(GameDefinition theGameDefinition)
        {
            Command currentCommand = CurrentExecutionInformation.CurrentCommand;
            Strategy[] loadedStrategies = null;

            if (currentCommand is PlayCommand)
            {
                PlayCommand theCommand = currentCommand as PlayCommand;
                if (!String.IsNullOrEmpty(theCommand.StoreStrategiesFile))
                {
                    try
                    {
                        loadedStrategies = theCommand.StoredStrategies; 
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(String.Format(
                            "Error loading the stored strategies file needed to play the game: {0}. Make sure that the PlayCommand specifies the correct type of strategy.",
                            theCommand.StoreStrategiesFile),
                            ex);
                    }
                }
                else
                {
                    throw new Exception(String.Format(
                        "The PlayCommand in MultipartCommand {0} did not specify the required stored strategies file.",
                        theCommand.MultiPartCommand.Name));
                }
            }
            else if (currentCommand is EvolveCommand)
            {
                EvolveCommand theCommand = currentCommand as EvolveCommand;
                if (theCommand.UseInitializeStrategiesFile)
                {
                    try
                    {
                        loadedStrategies = StrategySerialization.DeserializeStrategies(Path.Combine(BaseOutputDirectory, storedStrategiesSubdirectory, theCommand.InitializeStrategiesFile));
                        try
                        {
                            for (int d = 0; d < theGameDefinition.DecisionsExecutionOrder.Count; d++)
                            {
                                Decision decision = theGameDefinition.DecisionsExecutionOrder[d];
                                if (decision.PreevolvedStrategyFilename != "")
                                { // This overrides whatever strategy might have been loaded. We can also use this without UseInitializeStrategiesFile -- see below.
                                    loadedStrategies[d] = (Strategy) BinarySerialization.GetSerializedObject(
                                        Path.Combine(BaseOutputDirectory, storedStrategiesSubdirectory, decision.PreevolvedStrategyFilename), true);
                                }
                                // If we are not evolving only if necessary (i.e., by default are evolving all), then we want to set loadedStrategy to null unless we're supposed to skip this decision when evolving, in which case we keep the loaded decision.
                                // If we are evolving only if necessary (i.e., by default are evolving none), then we want to keep the loadedStrategy, unless EvolveThisDecisionEvenWhenSkippingByDefault is set, in which case we to set laodedStrategies[d] to null so that we reevolve it.
                            // NOTE: This is not the best place to do this, because if we initialize the strategy, we should keep the initialized version. We might want to implement this feature by skipping strategy development.
                                //else if ((theCommand.EvolveOnlyIfNecessary == false && !decision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved)
                                //    || (theCommand.EvolveOnlyIfNecessary == true && decision.EvolveThisDecisionEvenWhenSkippingByDefault))
                                //    loadedStrategies[d] = null;

                            }
                        }
                        catch (IndexOutOfRangeException ex)
                        {
                            throw new Exception("Strategy file not found. Consider setting useInitializeStrategiesFile to false.");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(String.Format(
                            "Error loading the initial strategies file: {0}",
                            theCommand.InitializeStrategiesFile),
                            ex);
                    }
                }
                else
                {
                    loadedStrategies = null;
                    if (theGameDefinition.DecisionsExecutionOrder.Any(x => x.PreevolvedStrategyFilename != null))
                    {
                        loadedStrategies = new Strategy[theGameDefinition.DecisionsExecutionOrder.Count];
                        for (int d = 0; d < theGameDefinition.DecisionsExecutionOrder.Count; d++)
                        {
                            Decision decision = theGameDefinition.DecisionsExecutionOrder[d];
                            if (decision.PreevolvedStrategyFilename != "")
                            { // This overrides whatever strategy might have been loaded. We can also use this with UseInitializeStrategiesFile -- see above.
                                loadedStrategies[d] = (Strategy)BinarySerialization.GetSerializedObject(
                                    Path.Combine(BaseOutputDirectory, storedStrategiesSubdirectory, decision.PreevolvedStrategyFilename), true);
                            }
                        }
                    }
                }
            }
            else
                throw new Exception("LoadEvolvedStrategies should be called only during play or evolve phase.");

            return (Strategy[])loadedStrategies;
        }

        public void Create3DPlot(List<double[]> points, List<System.Windows.Media.Color> colors, string name)
        {
            CurrentExecutionInformation.UiInteraction.Create3DPlot(points, colors, name);
        }

        public void Create2DPlot(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
            if (graph2DSettings.downloadLocation == null || graph2DSettings.downloadLocation == "")
                graph2DSettings.downloadLocation = BaseOutputDirectory + "\\" + reportsSubdirectory + "\\" + CurrentExecutionInformation.NameOfRunAndCommandSet + " " + graph2DSettings.graphName + ".jpg";
            CurrentExecutionInformation.UiInteraction.Create2DPlot(points, graph2DSettings, repetitionTagString);
        }

        public void ExportAll2DCharts()
        {
            CurrentExecutionInformation.UiInteraction.ExportAll2DCharts();
        }

        public void CloseAllCharts()
        {
            CurrentExecutionInformation.UiInteraction.CloseAllCharts();
        }
    }
}

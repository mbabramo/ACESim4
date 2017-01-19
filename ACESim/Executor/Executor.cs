using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class Executor
    {
        internal SimulationInteraction SimulationInteraction;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal CompleteSettings CompleteSettings;
        internal CurrentExecutionInformation CurrentExecutionInformation;
        public IUiInteraction UserInterface;
        ConcurrentStack<GameProgressReportable> LastSetOfOutputs;

        /// <summary>
        /// The directory that contains all output directories, e.g., Strategies, Iterations, Reports, and Settings.
        /// These directories will be created if they do not exist.
        /// </summary>
        internal string BaseOutputDirectory;

        public Executor(CompleteSettings theCompleteSettings, string theBaseOutputDirectory, IUiInteraction theUserInterface)
        {
            CompleteSettings = theCompleteSettings;
            CurrentExecutionInformation = null;
            UserInterface = theUserInterface;
            BaseOutputDirectory = theBaseOutputDirectory;
        }

        public void ExecuteACESimSingleSetOfSettings(StringBuilder metaReport, List<string> namesOfVariableSets, List<string> namesOfVariableSetsChosen, ProgressResumptionManager prm, out bool stop)
        {
            RandomGeneratorInstanceManager.Reset(RandomGeneratorInstanceManager.useDateTime, true); // if we are doing multiple repetitions, this will be a handy way of moving from one selected random instance to another, so that when we find a problem, we can just repeat the one repetition
            FastPseudoRandom.Reinitialize();
            stop = false;
            try
            {
                // Setup reporting progress
                int totalCommandCount = 0;
                foreach (var commandSet in CompleteSettings.CommandSetsToExecute)
                {
                    foreach (var multipartCommand in commandSet.Commands)
                    {
                        totalCommandCount += multipartCommand.Commands.Count;
                    }
                }

                CurrentExecutionInformation = new CurrentExecutionInformation(null, null, null, null, null, UserInterface, prm);

                SimulationInteraction = new SimulationInteraction(CurrentExecutionInformation, BaseOutputDirectory, metaReport, namesOfVariableSets, namesOfVariableSetsChosen, totalCommandCount);
                
                SimulationInteraction.GetCurrentProgressStep().AddChildSteps(CompleteSettings.CommandSetsToExecute.Select(x => x.CommandDifficulty).ToArray(), "CommandSet");
                int startingCS = 0;
                if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
                { 
                    startingCS = prm.Info.ExecuteSingleSetCommandSet;
                    SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(startingCS, "CommandSet");
                }
                for (int cs = startingCS; cs < CompleteSettings.CommandSetsToExecute.Count; cs++)
                {
                    prm.Info.ExecuteSingleSetCommandSet = cs;
                    CommandSet theCommandSet = CompleteSettings.CommandSetsToExecute[cs];
                    string fullName = CompleteSettings.NameOfRun + " " + theCommandSet.Name;
                    CurrentExecutionInformation.NameOfRunAndCommandSet = fullName;
                    SimulationInteraction.ReportTextToUser(Environment.NewLine + fullName, true);
                    SimulationInteraction.ReportTextToUser(Environment.NewLine + String.Format("Processing command set {0} of {1} ({2})... ", cs + 1, CompleteSettings.CommandSetsToExecute.Count, theCommandSet.Name), true);
                    SimulationInteraction.GetCurrentProgressStep().AddChildSteps(theCommandSet.Commands.Select(x => x.CommandDifficulty).ToArray(), "MultiPartCommand");

                    DateTime commandSetStartTime = DateTime.Now;

                    int startingMPC = 0;
                    if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
                    {
                        startingMPC = prm.Info.ExecuteSingleSetMultipartCommand;
                        SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(startingMPC, "MultiPartCommand");
                    }
                    for (int mpc = startingMPC; mpc < theCommandSet.Commands.Count; mpc++)
                    {
                        prm.Info.ExecuteSingleSetMultipartCommand = mpc;
                        MultiPartCommand theMultipartCommand = theCommandSet.Commands[mpc];
                        SimulationInteraction.ReportTextToUser(Environment.NewLine + String.Format("Processing multipart command {0} of {1} ({2})... ", mpc + 1, theCommandSet.Commands.Count, theMultipartCommand.Name), true);
                        SimulationInteraction.GetCurrentProgressStep().AddChildSteps(theMultipartCommand.Commands.Select(x => x.CommandDifficulty).ToArray(), "Command");
                        int startingCmd = 0;
                        if (prm.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
                        {
                            startingCmd = prm.Info.ExecuteSingleSetCommand;
                            SimulationInteraction.GetCurrentProgressStep().SetSeveralStepsComplete(startingCmd, "Command");
                        }
                        for (int cmd = startingCmd; cmd < theMultipartCommand.Commands.Count; cmd++)
                        {
                            Command theCommand = theMultipartCommand.Commands[cmd];
                            prm.Info.ExecuteSingleSetCommand = cmd;

                            SimulationInteraction.ReportTextToUser(Environment.NewLine + String.Format("Processing command {0} of {1} ({2})... ", cmd + 1, theMultipartCommand.Commands.Count, theCommand is PlayCommand ? "Play" : theCommand is EvolveCommand ? "Evolve" : theCommand is ReportCommand ? "Report" : "Unknown command"), true);
                            ExecuteCommand(SimulationInteraction, theCommand, UserInterface, CurrentExecutionInformation.NameOfRunAndCommandSet, CurrentExecutionInformation.NameOfVariableSetsChosen, commandSetStartTime, prm);
                            SimulationInteraction.AbortChildSteps("Command"); // if we aborted, we need to clear this
                            SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "Command"); // command is done
                            // if this was a play command, save the played games to the outputs for upcoming ReportCommands
                            if (theCommand is PlayCommand)
                            {
                                var memorySavedGames = ((PlayCommand)theCommand).InMemoryStoredGames;
                                if (memorySavedGames != null)
                                {
                                    List<GameProgressReportable> memorySavedGamesList = memorySavedGames.ToList();
                                    for (int cmd2 = cmd + 1; cmd2 < theMultipartCommand.Commands.Count; cmd2++)
                                    {
                                        Command futureCommand = theMultipartCommand.Commands[cmd2];
                                        if (futureCommand is ReportCommand)
                                        {
                                            ReportCommand futureReportCommand = (ReportCommand)futureCommand;
                                            futureReportCommand.theOutputs = memorySavedGamesList;
                                        }
                                    }
                                }
                            }
                        }
                        SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "MultiPartCommand"); // multipart command is done
                    }
                    SimulationInteraction.GetCurrentProgressStep().SetProportionOfStepComplete(1, true, "CommandSet"); // commandset is done
                }
                CurrentExecutionInformation.Outputs = null;

            }
            catch (Exception ex)
            {
                UserInterface.HandleException(ex);
                stop = true;
            }
            SimulationInteraction.CheckStopOrPause(out stop);

        }

        internal void ExecuteCommand(
            SimulationInteraction simulationInteraction,
            Command theCommand, 
            IUiInteraction theUserInterface,
            string nameOfRunSetAndCommand,
            List<string> nameOfVariableSetsChosen,
            DateTime commandSetStartTime,
            ProgressResumptionManager prm)
        {
            theCommand.CommandSetStartTime = commandSetStartTime;

            if (theCommand is EvolveCommand)
            {
                CurrentExecutionInformation = new CurrentExecutionInformation(CompleteSettings.AllVariablesFromProgram,
                    CompleteSettings.EvolutionSettingsSets.Single(x => x.name == ((EvolveCommand)theCommand).EvolutionSettingsName),
                    theCommand.MultiPartCommand.GameDefinitionSet,
                    CompleteSettings.GameInputsSets.Single(x => x.name == theCommand.MultiPartCommand.GameInputsName),
                    theCommand,
                    theUserInterface, 
                    prm
                    );
                ((EvolveCommand)theCommand).SetProgressResumptionManager(prm);
            }
            else if (theCommand is PlayCommand)
            {
                CurrentExecutionInformation = new CurrentExecutionInformation(CompleteSettings.AllVariablesFromProgram,
                    null, // not currently evolving
                    theCommand.MultiPartCommand.GameDefinitionSet,
                    CompleteSettings.GameInputsSets.Single(x => x.name == theCommand.MultiPartCommand.GameInputsName),
                    theCommand,
                    theUserInterface,
                    prm
                );
            }
            else if (theCommand is ReportCommand)
            {
                CurrentExecutionInformation = new CurrentExecutionInformation(CompleteSettings.AllVariablesFromProgram, null, theCommand.MultiPartCommand.GameDefinitionSet, null, theCommand, theUserInterface, prm);
                if (LastSetOfOutputs != null)
                    ((ReportCommand)theCommand).theOutputs = LastSetOfOutputs.ToList();
            }

            CurrentExecutionInformation.NameOfRunAndCommandSet = nameOfRunSetAndCommand;
            CurrentExecutionInformation.NameOfVariableSetsChosen = nameOfVariableSetsChosen;
            simulationInteraction.CurrentExecutionInformation = CurrentExecutionInformation;

            bool stop;
            simulationInteraction.CheckStopOrPause(out stop);
            if (!stop)
            {
                theCommand.Execute(simulationInteraction);
                if (CurrentExecutionInformation.Outputs != null && CurrentExecutionInformation.Outputs.Any())
                    LastSetOfOutputs = CurrentExecutionInformation.Outputs;
            }
        }
    }
}

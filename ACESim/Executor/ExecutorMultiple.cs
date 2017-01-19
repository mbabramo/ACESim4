using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ACESim
{


    public class ExecutorMultiple
    {
        string BaseOutputDirectory;
        string SettingsPath;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        IUiInteraction ACESimWindow;
        int NumberCompleteSettingsSets;
        [NonSerialized]
        ProgressResumptionManager ProgressResumptionManager;

        public ExecutorMultiple(string theBaseOutputDirectory, string theSettingsPath, IUiInteraction window, ProgressResumptionManager prm)
        {
            this.BaseOutputDirectory = theBaseOutputDirectory;
            this.SettingsPath = theSettingsPath;
            this.ACESimWindow = window;
            this.ProgressResumptionManager = prm;
        }

        public void RunAll()
        {
            SettingsLoader settingsLoader;
            settingsLoader = new SettingsLoader(BaseOutputDirectory);
            NumberCompleteSettingsSets = settingsLoader.CountCompleteSettingsSets(SettingsPath);
            DateTime lastDateTime = DateTime.Now;
            StringBuilder metaReport = new StringBuilder();
            ACESimWindow.GetCurrentProgressStep().AddChildSteps(NumberCompleteSettingsSets, "CompleteSettingsSet");
            long totalMemory = GC.GetTotalMemory(true);
            int startingSetNumber = 0;
            if (ProgressResumptionManager.ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
            { 
                startingSetNumber = ProgressResumptionManager.Info.ExecutorMultipleSetNumber;
                ACESimWindow.GetCurrentProgressStep().SetSeveralStepsComplete(startingSetNumber, "CompleteSettingsSet");
            }
            if (AzureSetup.runCompleteSettingsInAzure)
                RunAllInAzure(settingsLoader, metaReport);
            else
                RunAllSynchronously(settingsLoader, ref lastDateTime, metaReport, ref totalMemory, startingSetNumber);
            ProgressResumptionManager.Info.IsComplete = true;
            ProgressResumptionManager.SaveProgressIfSaving();
            ACESimWindow.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "TopStep");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ACESimWindow.ReportComplete();
        }

        [Serializable]
        public class ExecuteSettingsSetFileName
        {
            public string SettingFileName;
        }

        [Serializable]
        public class ExecuteSettingsSetRemotelyResult
        {
            public string CompletedMetaReport;
        }

        private void RunAllInAzure(SettingsLoader settingsLoader, StringBuilder metaReport)
        {
            var az = new ProcessInAzureWorkerRole();
            int numberCompleted = 0;
            string[] reportsBack = new string[NumberCompleteSettingsSets];
            az.ExecuteTask(new ExecuteSettingsSetFileName() { SettingFileName = Path.GetFileName(SettingsPath) }, "SettingsSet", NumberCompleteSettingsSets, false, (object r, int setNum) =>
                {
                    ExecuteSettingsSetRemotelyResult essrr = (ExecuteSettingsSetRemotelyResult)r;
                    numberCompleted++;
                    reportsBack[setNum] = essrr.CompletedMetaReport;
                    ACESimWindow.GetCurrentProgressStep().SetProportionOfStepComplete(((double)numberCompleted) / ((double)NumberCompleteSettingsSets), false, "CompleteSettingsSet");
                    return numberCompleted == NumberCompleteSettingsSets;
                }
                );
            foreach (string report in reportsBack)
                metaReport.Append(report);
            for (int nc = 0; nc < numberCompleted; nc++)
                ACESimWindow.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "CompleteSettingsSet");
        }

        // This is called when running a single settings set through the Azure worker role.
        public string RunSingleSettingsSet(int setNumber)
        {
            // Note that we load ALL of the settings, but then only run the one that we're looking for.
            // this is a big inefficient, but the alternative is trying to serialize ExecutorMultiple,
            // which runs into difficulties when serializing XElement.
            SettingsLoader settingsLoader;
            settingsLoader = new SettingsLoader(BaseOutputDirectory);
            NumberCompleteSettingsSets = settingsLoader.CountCompleteSettingsSets(SettingsPath);
            StringBuilder metaReport = new StringBuilder();
            bool stop;
            string nameOfRun;
            GetCompleteSettingsAndExecute(settingsLoader, metaReport, setNumber, new ProgressResumptionManager(ProgressResumptionOptions.ProceedNormallyWithoutSavingProgress, "N/A"), out nameOfRun, out stop);
            return metaReport.ToString();
        }

        private void RunAllSynchronously(SettingsLoader settingsLoader, ref DateTime lastDateTime, StringBuilder metaReport, ref long totalMemory, int startingSetNumber)
        {
            for (int setNumber = startingSetNumber; setNumber < NumberCompleteSettingsSets; setNumber++)
            {
                ProgressResumptionManager.Info.ExecutorMultipleSetNumber = setNumber;
                settingsLoader.Reset(); // good place to do extra reset so that we can look for memory leaks right after this
                long newTotalMemory;
                do
                {
                    totalMemory = GC.GetTotalMemory(true);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    newTotalMemory = GC.GetTotalMemory(true);
                }
                while (newTotalMemory < totalMemory);
                bool stop;
                string nameOfRun;
                GetCompleteSettingsAndExecute(settingsLoader, metaReport, setNumber, ProgressResumptionManager, out nameOfRun, out stop);
                if (stop)
                    break;
                TabbedText.WriteLine("Single settings set " + (setNumber + 1) + " (" + nameOfRun + ") Elapsed time: " + (DateTime.Now - lastDateTime));
                lastDateTime = DateTime.Now;
                GC.Collect();
                ACESimWindow.GetCurrentProgressStep().SetProportionOfStepComplete(1.0, true, "CompleteSettingsSet");
            }
        }

        public void GetCompleteSettingsAndExecute(SettingsLoader settingsLoader, StringBuilder metaReport, int setNumber, ProgressResumptionManager prm, out string nameOfRun, out bool stop)
        {
            CompleteSettings completeSettingsSet;
            completeSettingsSet = settingsLoader.GetCompleteSettingsSet(setNumber);
            nameOfRun = completeSettingsSet.NameOfRun;
            if (setNumber == 0)
            {
                metaReport.Append("Row,Column,Value,Date");
                List<string> namesOfVariableSets = completeSettingsSet.NamesOfVariablesSets;
                for (int n = 0; n < namesOfVariableSets.Count; n++)
                {
                    metaReport.Append("," + namesOfVariableSets[n]);
                }
                metaReport.Append("\n");
            }
            stop = ExecuteCompleteSettingsSet(metaReport, completeSettingsSet, prm);
        }

        private bool ExecuteCompleteSettingsSet(StringBuilder metaReport, CompleteSettings completeSettingsSet, ProgressResumptionManager prm)
        {
            Executor theExecutor = new Executor(completeSettingsSet, BaseOutputDirectory, ACESimWindow);
            ACESimWindow.ReportTextToUser("Starting...", false);
            ACESimWindow.SetRunStatus(RunStatus.Running);
            bool stop = false;
            theExecutor.ExecuteACESimSingleSetOfSettings(metaReport, completeSettingsSet.NamesOfVariablesSets, completeSettingsSet.NamesOfVariablesSetsChosen, prm, out stop);
            return stop;
        }
    }
}

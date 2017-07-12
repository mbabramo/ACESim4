using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ACESim;
using System.Threading;
using Path = System.IO.Path;
using System.Diagnostics;
using System.Windows.Forms.Integration;
using System.Threading.Tasks;

namespace ACESim
{

    public partial class Window : Form, IUiInteraction
    {
        string settingsPath;
        RunStatus runStatus;
        double proportionComplete;
        string outputText;

        public CurrentExecutionInformation CurrentExecutionInformation
        { get; set; }

        public Window()
        {
            //AzureReset.Go(); // should reset now before any worker roles get started
            InitializeComponent();
            RunStatus = RunStatus.Uninitialized;
        }

        #region Properties and Property Helpers

        delegate void SetOutputTextCallback(string text);
        delegate void SetPercentCompleteCallback(double progress);
        delegate void SetRunStatusCallback(RunStatus status);
        delegate void SetBestStrategiesCallback(List<Strategy> bestStrategies);
        delegate void SetEvolvingStrategiesCallback(List<Strategy> evolvingStrategies);
        delegate void SetGenerationLabelCallback(int generationNum);
        delegate void Create3DPlotCallback(List<double[]> points, List<System.Windows.Media.Color> colors, string name);
        delegate void Create2DPlotCallback(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString);

        void SetOutputText(string text)
        {
            OutputText = text;
        }
        void SetProportionComplete(double proportion)
        {
            ProportionComplete = proportion;
        }
        public void SetRunStatus(RunStatus status)
        {
            RunStatus = status;
        }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        Dictionary<string, LineChart> lineChartList = new Dictionary<string, LineChart>();

        void Create2DPlotHelper(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
            List<double[]> points2;
            if (graph2DSettings.scatterplot)
                points2 = points.ToList(); // maintain order, because we may be adding additional series and drawing lines between them
            else
                points2 = points.OrderBy(x => x[0]).ToList(); // put these in order, since we are drawing lines between them.
            if (graph2DSettings.seriesName == "")
                graph2DSettings.seriesName = "Output";
            LineChart form = null;
            if (lineChartList.ContainsKey(graph2DSettings.graphName))
            {
                form = lineChartList[graph2DSettings.graphName];
                form.CreateOrAddToGraph(points2, graph2DSettings, repetitionTagString);
                if (graph2DSettings.exportFramesOfMovies)
                    form.ExportFrameOfMovieIfDownloadLocationSpecified();
            }
            else
            {
                form = new LineChart();
                form.CreateOrAddToGraph(points2, graph2DSettings, repetitionTagString);
                form.SetName(graph2DSettings.graphName);
                lineChartList.Add(graph2DSettings.graphName, form);
                form.Show();
                if (graph2DSettings.exportFramesOfMovies)
                    form.ExportFrameOfMovieIfDownloadLocationSpecified();
            }
        }

        public void Create2DPlot(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
            Create2DPlotCallback callback =
                new Create2DPlotCallback(Create2DPlotHelper);
            this.Invoke(callback, new object[] { points, graph2DSettings, repetitionTagString });
        }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        Dictionary<string, ScatterplotHostForm> scatterplotList = new Dictionary<string, ScatterplotHostForm>();
        void Create3DPlotHelper(List<double[]> points, List<System.Windows.Media.Color> colors, string name)
        {
            if (scatterplotList.ContainsKey(name))
            {
                ScatterplotHostForm form = scatterplotList[name];
                form.SetPoints(points, colors);
            }
            else
            {
                ScatterplotHostForm form = new ScatterplotHostForm();
                form.SetPoints(points, colors);
                form.SetName(name);
                scatterplotList.Add(name, form);
                form.Show();
            }
        }

        public void Create3DPlot(List<double[]> points, List<System.Windows.Media.Color> colors, string name)
        {
            Create3DPlotCallback callback =
                new Create3DPlotCallback(Create3DPlotHelper);
            this.Invoke(callback, new object[] { points, colors, name });
        }

        delegate void Close2DChartCallback(LineChart chart);

        public void Close2DChart(LineChart chart)
        {
            Close2DChartCallback callback = new Close2DChartCallback(Close2DChartHelper);
            this.Invoke(callback, new object[] { chart });
        }

        public void Close2DChartHelper(LineChart chart)
        {
            chart.Close();
        }


        delegate void Close3DChartCallback(ScatterplotHostForm chart);

        public void Close3DChart(ScatterplotHostForm chart)
        {
            Close3DChartCallback callback = new Close3DChartCallback(Close3DChartHelper);
            this.Invoke(callback, new object[] { chart });
        }

        public void Close3DChartHelper(ScatterplotHostForm chart)
        {
            chart.Close();
        }

        public void CloseAllCharts()
        {
            var charts = lineChartList.ToList();
            foreach (var chart in charts)
                Close2DChart(chart.Value);
            lineChartList = new Dictionary<string, LineChart>();
            var charts2 = scatterplotList.ToList();
            foreach (var chart in charts2)
                Close3DChart(chart.Value);
            scatterplotList = new Dictionary<string, ScatterplotHostForm>();
        }


        delegate void Export2DChartCallback(LineChart chart);

        public void Export2DChart(LineChart chart)
        {
            Export2DChartCallback callback = new Export2DChartCallback(Export2DChartHelper);
            this.Invoke(callback, new object[] { chart });
        }

        public void Export2DChartHelper(LineChart chart)
        {
            chart.ExportImageIfDownloadLocationSpecified();
        }

        public void ExportAll2DCharts()
        {
            var charts = lineChartList.ToList();
            foreach (var chart in charts)
                Export2DChart(chart.Value);
        }

        String SettingsPath
        {
            get
            {
                return settingsPath;
            }
            set
            {
                settingsPath = value;
                // In our cases we only set the SettingsPath from the UI thread, so no need to invoke.
                settingsFileNameTextBox.Text = Path.GetFileName(settingsPath);
            }
        }

        /// <summary>
        /// The text displayed in the <see cref="Window"/>'s outputTextBox.
        /// </summary>
        /// <value cref="String"></value>
        public string OutputText
        {
            get
            {
                return outputText;
            }
            set
            {
                // InvokeRequired required compares the thread ID of the
                // calling thread to the thread ID of the creating thread.
                // If these threads are different, it returns true.
                if (outputTextBox.InvokeRequired)
                {
                    SetOutputTextCallback callback =
                        new SetOutputTextCallback(SetOutputText);
                    this.Invoke(callback, new object[] { value });
                    return;
                }

                outputText = value;

                outputTextBox.Text = outputText;
            }
        }

        double ProportionComplete
        {
            get
            {
                return proportionComplete;
            }
            set
            {
                if (progressBar.InvokeRequired)
                {
                    SetPercentCompleteCallback callback =
                        new SetPercentCompleteCallback(SetProportionComplete);
                    this.Invoke(callback, new object[] { value });
                    return;
                }

                proportionComplete = value;

                progressBar.Value = (int)(proportionComplete * progressBar.Maximum);
            }
        }

        public RunStatus RunStatus
        {
            get
            {
                return runStatus;
            }
            set
            {
                if (
                    runButton.InvokeRequired ||
                    pauseOrContinueButton.InvokeRequired ||
                    stopButton.InvokeRequired ||
                    progressBar.InvokeRequired
                    )
                {
                    SetRunStatusCallback callback = new SetRunStatusCallback(SetRunStatus);
                    this.Invoke(callback, new object[] { value });
                    return;
                }

                runStatus = value;

                switch (runStatus)
                {
                    case RunStatus.Uninitialized:
                        runButton.Enabled = false;
                        pauseOrContinueButton.Enabled = false;
                        pauseOrContinueButton.Text = "Pause/Continue";
                        stopButton.Enabled = false;
                        progressBar.Enabled = false;
                        stopSoonButton.Enabled = false;
                        break;

                    case RunStatus.Running:
                    case RunStatus.StopSoon:
                        runButton.Enabled = false;
                        pauseOrContinueButton.Enabled = true;
                        pauseOrContinueButton.Text = "Pause";
                        stopButton.Enabled = true;
                        if (runStatus == RunStatus.Running)
                            stopSoonButton.Enabled = true;
                        else
                            stopSoonButton.Enabled = false;
                        progressBar.Enabled = true;
                        break;

                    case RunStatus.Paused:
                        runButton.Enabled = false;
                        pauseOrContinueButton.Enabled = true;
                        pauseOrContinueButton.Text = "Continue";
                        stopButton.Enabled = true;
                        progressBar.Enabled = true;
                        stopSoonButton.Enabled = false;
                        break;

                    case RunStatus.Stopped:
                        runButton.Enabled = true;
                        pauseOrContinueButton.Enabled = false;
                        pauseOrContinueButton.Text = "Pause/Continue";
                        stopButton.Enabled = false;
                        progressBar.Enabled = false;
                        stopSoonButton.Enabled = false;
                        // PriorityQueueSmallRAMFootprintMonitor.Complete = true;
                        break;
                }
            }
        }

        #endregion

        # region Simulation Interaction


        public ProgressStep EntireProgressBarStep = null;

        public ProgressStep GetCurrentProgressStep()
        {
            if (EntireProgressBarStep == null)
                ResetProgressStep();
            return EntireProgressBarStep.GetCurrentStep();
        }

        public void ResetProgressStep()
        {
            EntireProgressBarStep = new ProgressStep() { ReportPercentComplete = ReportPercentComplete, StepType="TopStep" };
            ReportPercentComplete(0);
        }

        public void ReportPercentComplete(double percentComplete)
        {
            ProportionComplete = percentComplete;
        }

        string lastTextReported;
        public void ReportTextToUser(string text, bool append)
        {
            if (text.Contains("UpdateCumulative"))
            {
                text = "%"; // this will be an abbreviation so we can see where the cumulative distributions are updated without interfering too much with the screen
                if (lastTextReported == text)
                    return;
            }
            else if (text.Contains("Dummy"))
                return;
            lastTextReported = text;
            if (append)
            {
                OutputText += text;
            }
            else
            {
                outputText = text;
            }

            //Debug.WriteLine(text);
        }

        int decisionNumber;
        public void ReportDecisionNumber(int currentDecisionNumber)
        {
            decisionNumber = currentDecisionNumber;
        }


        public void HandleException(Exception ex)
        {
            while (ex != null)
            {
                OutputText += ex.Message;
                ex = ex.InnerException;
            }
            RunStatus = RunStatus.Stopped;
        }

        public void CheckStopOrPause(out bool stop)
        {
            if (RunStatus == RunStatus.Stopped)
            {
                stop = true;
                EntireProgressBarStep.ReportStoppedProcess();
            }
            else
                stop = false;
            while (RunStatus == RunStatus.Paused)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        public bool CheckStopSoon()
        {
            if (RunStatus == RunStatus.StopSoon)
            {
                RunStatus = RunStatus.Running;
                return true;
            }
            return false;
        }

        public void ReportComplete()
        {
            RunStatus = RunStatus.Stopped;
            OutputText += "Complete.";
        }

        #endregion

        #region Event Handlers

        private void browseButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = "XML files|*.xml";
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                SettingsPath = openFileDialog1.FileName;
                RunStatus = RunStatus.Stopped;
            }
        }

        private void runButton_Click(object sender, EventArgs e)
        {
            const string UP = "..";
            try
            {
                // The base output path is one directory up from the directory containing the main settings file.
                string settingsDirectory = Path.GetDirectoryName(SettingsPath);
                string baseOutputDirectory = Path.Combine(settingsDirectory, UP);
                baseOutputDirectory = Path.GetFullPath(baseOutputDirectory);

                ResetProgressStep();
                ProgressResumptionOptions pro = ProgressResumptionOptions.ProceedNormallyWithoutSavingProgress;
                if (saveProgress.Checked)
                {
                    if (resumeProgress.Checked)
                        pro = ProgressResumptionOptions.SkipToPreviousPositionThenResume;
                    else
                        pro = ProgressResumptionOptions.ProceedNormallySavingPastProgress;
                }
                StartRunning.Go(baseOutputDirectory, SettingsPath, this, pro);
                
                // NOTE: We must leave from this method for the program to function correctly -- can't wait here for RunStatus to change. That is why we create a new thread.
            }
            catch (Exception ex)
            {
                string exceptionMessage = "Error in processing file: " + ex.ExtractMessage();
                OutputText += exceptionMessage;
                RunStatus = RunStatus.Stopped;
            }
        }

        

        private void stopButton_Click(object sender, EventArgs e)
        {
            //if ((aceSimThread.ThreadState & ThreadState.Suspended) == ThreadState.Suspended)
            //{
            //    aceSimThread.Resume(); // Must Resume in order to Abort
            //    aceSimThread.Abort();
            //}
            //else if ((aceSimThread.ThreadState & ThreadState.WaitSleepJoin) == ThreadState.WaitSleepJoin)
            //{
            //    aceSimThread.Interrupt();
            //    aceSimThread.Abort(); // Does an Interrupt require an abort?
            //}
            //else if ((aceSimThread.ThreadState & ThreadState.Running) == ThreadState.Running)
            //{
            //    // Note it is possible for a Thread to be both Running and Suspended
            //    aceSimThread.Abort();
            //}
            RunStatus = RunStatus.Stopped;
            OutputText += "Execution Stopped.";
        }

        private void pauseOrContinueButton_Click(object sender, EventArgs e)
        {
            if (RunStatus == RunStatus.Paused)
            {
                RunStatus = RunStatus.Running;
            }
            else if (RunStatus == RunStatus.Running)
            {
                RunStatus = RunStatus.Paused;
            }
        }

        #endregion


        private void AllStrategiesLabel_Click(object sender, EventArgs e)
        {

        }

        private void stopSoonButton_Click(object sender, EventArgs e)
        {
            if (RunStatus == RunStatus.Running)
                RunStatus = RunStatus.StopSoon;
        }

        private void resumeProgress_Click(object sender, EventArgs e)
        {
            if (resumeProgress.Checked)
                saveProgress.Checked = true;
        }

        public GameInputs GetGameInputs(long numIterations, IterationID iterationID)
        {
            throw new NotImplementedException();
        }
    }
}

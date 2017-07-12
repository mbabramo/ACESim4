using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public class Interactionless : IUiInteraction
    {
        public CurrentExecutionInformation CurrentExecutionInformation { get; set; }

        public void HandleException(Exception ex)
        {
            while (ex != null)
            {
                Trace.TraceError(ex.Message);
                ex = ex.InnerException;
            }
        }

        public void SetRunStatus(RunStatus status)
        {
        }

        public virtual void ReportTextToUser(string text, bool append)
        {
            Debug.WriteLine(text);
        }

        double ProportionComplete;
        public ProgressStep EntireProgressBarStep;

        public ProgressStep GetCurrentProgressStep()
        {
            if (EntireProgressBarStep == null)
                ResetProgressStep();
            return EntireProgressBarStep.GetCurrentStep();
        }

        public virtual void ResetProgressStep()
        {
            EntireProgressBarStep = new ProgressStep() { ReportPercentComplete = ReportPercentComplete, StepType = "TopStep" };
            ReportPercentComplete(0);
        }

        public virtual void ReportPercentComplete(double percentComplete)
        {
            ProportionComplete = percentComplete;
        }

        public void CheckStopOrPause(out bool stop)
        {
            stop = false;
        }

        public bool CheckStopSoon()
        {
            return false;
        }

        public void ReportComplete()
        {
        }

        public void ReportDecisionNumber(int currentValue)
        {
        }

        public void Create3DPlot(List<double[]> points, List<System.Windows.Media.Color> colors, string name)
        {
        }

        public virtual void Create2DPlot(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
        }

        public virtual void ExportAll2DCharts()
        {
        }

        public void CloseAllCharts()
        {
        }
        InputVariables lastInputVariables = null;
        public GameInputs GetGameInputs(long numIterations, IterationID iterationID)
        {
            InputVariables theInputVariables = null;
            if (iterationID.IterationNumber > 0 && lastInputVariables != null)
                theInputVariables = lastInputVariables; // use stored input variables
            else
            {
                theInputVariables = new InputVariables(CurrentExecutionInformation);
                if (numIterations != 1)
                    lastInputVariables = theInputVariables; // store this for use in the next group of iterations
            }
            Type theType = CurrentExecutionInformation.GameFactory.GetSimulationSettingsType();
            GameInputs returnVal = theInputVariables.GetGameInputs(theType, numIterations, iterationID, CurrentExecutionInformation);
            return returnVal;
        }
    }


}

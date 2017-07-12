using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public interface IUiInteraction
    {
        void HandleException(Exception ex); 
        void SetRunStatus(RunStatus status);
        void ReportTextToUser(string text, bool append);
        ProgressStep GetCurrentProgressStep();
        void ResetProgressStep();
        void CheckStopOrPause(out bool stop);
        bool CheckStopSoon();
        void ReportComplete(); 
        void ReportDecisionNumber(int currentValue);
        void Create3DPlot(List<double[]> points, List<System.Windows.Media.Color> colors, string name);
        void Create2DPlot(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString);
        void ExportAll2DCharts();
        void CloseAllCharts();
        GameInputs GetGameInputs(long numIterations, IterationID iterationID);
        CurrentExecutionInformation CurrentExecutionInformation { get; set; }
    }
}

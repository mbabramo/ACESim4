using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WorkerCoordinator
{
    public class AzureInteraction : Interactionless
    {
        Dictionary<string, LineChart> lineChartList = new Dictionary<string, LineChart>();

        double lastPercentComplete = 0;

        public override void ResetProgressStep()
        {
            EntireProgressBarStep = new ProgressStep() { ReportPercentComplete = ReportPercentComplete2, StepType = "TopStep" };
            ReportPercentComplete(0);
        }

        public override void ReportTextToUser(string text, bool append)
        {
            Trace.TraceInformation(text);
        }

        public void ReportPercentComplete2(double percentComplete)
        {
            if (Math.Abs(percentComplete - lastPercentComplete) > 0.005)
                Trace.TraceInformation("Percent complete: " + percentComplete.ToSignificantFigures());
        }

        public override void ExportAll2DCharts()
        {
            var charts = lineChartList.ToList();
            foreach (var chart in charts)
                chart.Value.ExportImageIfDownloadLocationSpecified();
        }

        public void Create2DPlotHelper2(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
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

        delegate void Create2DPlotCallback(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString);

        public override void Create2DPlot(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
            Create2DPlotHelper2(points, graph2DSettings, repetitionTagString);
            //Create2DPlotCallback callback =
            //    new Create2DPlotCallback(Create2DPlotHelper);
            //this.Invoke(callback, new object[] { points, graph2DSettings, repetitionTagString });
        }
    }
}

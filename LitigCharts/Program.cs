using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using System;
using System.IO;
using CsvHelper;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using ACESimBase.Util.Tikz;

namespace LitigCharts
{
    class Program
    {

        static void Main(string[] args)
        {

            var lineScheme = new List<string>()
                {
                    "blue, line width=0.5mm, double",
                    "red, line width=1mm, dashed",
                    "green, line width=1mm, solid",
                };
            Random ran = new Random();
            List<List<double>> getMiniGraph() => new List<List<double>>()
                {
                    new List<double>() { ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), },
                    new List<double>() { ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), },
                    new List<double>() { ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), ran.NextDouble(), },
                };
            TikzLineGraphData lineGraphData() => new TikzLineGraphData(getMiniGraph(), lineScheme);
            List<TikzLineGraphData> lineGraphData3() => new List<TikzLineGraphData>() { lineGraphData(), lineGraphData(), lineGraphData() };
            List<List<TikzLineGraphData>> lineGraphData5x3() => new List<List<TikzLineGraphData>>() { lineGraphData3(), lineGraphData3(), lineGraphData3(), lineGraphData3(), lineGraphData3() };


            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = new List<string>() { "X1", "X2", "X3" },
                majorXAxisLabel = "Major X",
                majorYValueNames = new List<string>() { "Y1", "Y2", "Y3", "Y4", "Y5" },
                majorYAxisLabel = "Major Y",
                minorXValueNames = new List<string>() { "x1", "x2", "x3", "x4", "x5" },
                minorXAxisLabel = "Minor X",
                minorYValueNames = new List<string>() { "Y1", "Y2", "Y3", "Y4" },
                minorYAxisLabel = "Minor Y",
                sourceRectangle = new TikzRectangle(0, 0, 20, 20),
                lineGraphData = lineGraphData5x3(),
            };


            var result = TikzHelper.GetStandaloneDocument(r.GetDrawCommands(), additionalHeaderInfo: $@"
\usetikzlibrary{{calc}}
\usepackage{{relsize}}
\tikzset{{fontscale/.style = {{font=\relsize{{#1}}}}}}");


            //FileFixer();
            //KlermanData.Execute();
            FeeShiftingDataProcessing.OrganizeIntoFolders();
            FeeShiftingDataProcessing.BuildMainFeeShiftingReport();
            //FeeShiftingDataProcessing.BuildOffersReport(); // we're no longer generating the offers data in csv, since we're directly generating a Latex file with the heatmap
            FeeShiftingDataProcessing.ProduceLatexDiagrams();
        }


    }
}

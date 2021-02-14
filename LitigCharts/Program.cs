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
            TikzAxisSet major = new TikzAxisSet(new List<string>() { "X1", "X2", "X3" }, new List<string>() { "Y1", "Y2", "Y3" }, "XVals", "YVals", new TikzRectangle(0, 0, 20, 20), fontAttributes: "fontscale=3");
            var result = TikzHelper.GetStandaloneDocument(major.GetDrawAxesCommands(), additionalHeaderInfo: $@"
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

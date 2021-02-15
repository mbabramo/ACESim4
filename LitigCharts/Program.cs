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
            //KlermanData.Execute();

            //FeeShiftingDataProcessing.OrganizeIntoFolders();
            //FeeShiftingDataProcessing.BuildMainFeeShiftingReport();
            ////FeeShiftingDataProcessing.BuildOffersReport(); // we're no longer generating the offers data in csv, since we're directly generating a Latex file with the heatmap
            //FeeShiftingDataProcessing.ProduceLatexDiagramsFromTexFiles();
            FeeShiftingDataProcessing.ProduceLatexDiagramsAggregatingReports();
        }
    }
}

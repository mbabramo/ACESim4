using ACESim;
using ACESim.Util;
using ACESimBase.Util;
using System;
using System.IO;
using CsvHelper;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace LitigCharts
{
    class Program
    {
        static void Main(string[] args)
        {
            //KlermanData.Execute();
            FeeShiftingDataProcessing.BuildMainFeeShiftingReport();
            //FeeShiftingDataProcessing.BuildOffersReport();
            FeeShiftingDataProcessing.ProduceLatexDiagrams("-scr");
            FeeShiftingDataProcessing.ProduceLatexDiagrams("-heatmap");
        }

        
    }
}

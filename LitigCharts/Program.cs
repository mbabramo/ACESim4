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
            //Runner.KlermanData.Execute();

            // Runner.AdditiveEvidenceArticle();

            Runner.ProcessForFeeShiftingArticle(true /* DEBUG */);
        }
    }
}

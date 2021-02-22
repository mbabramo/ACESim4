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

            bool doSignalsDiagram = false; 
            bool printIndividualLatexDiagrams = true; // DEBUG // this is the time consuming one
            bool doDeletion = printIndividualLatexDiagrams; // don't delete if we haven't done the diagrams yet
            bool organizeIntoFolders = true;
            bool printAggregatedDiagrams = true; 

            if (doSignalsDiagram)
            {
                SignalsDiagram diagram = new SignalsDiagram();
                diagram.CreateDiagram();
                return;
            }

            FeeShiftingDataProcessing.BuildMainFeeShiftingReport();
            if (printIndividualLatexDiagrams)
                FeeShiftingDataProcessing.ProduceLatexDiagramsFromTexFiles(); // this code assumes that all data is in the ReportResults folder, so must do before organization
            if (organizeIntoFolders)
                FeeShiftingDataProcessing.OrganizeIntoFolders(doDeletion); // now we organize, including the diagrams just made
            if (printAggregatedDiagrams)
                FeeShiftingDataProcessing.ProduceLatexDiagramsAggregatingReports(); // now we produce diagrams that aggregate info from multiple reports

            ////FeeShiftingDataProcessing.BuildOffersReport(); // we're no longer generating the offers data in csv, since we're directly generating a Latex file with the heatmap

            //FeeShiftingDataProcessing.ExampleLatexDiagramsAggregatingReports();
        }
    }
}

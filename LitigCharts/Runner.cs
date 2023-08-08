using ACESimBase.Util.Tikz;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LitigCharts
{
    public static class Runner
    {

        public static void CostDiseaseAnalysis()
        {
            string mainSignalsDiagramCode = "";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("True liability probability,Case strength noise,Signal noise,Litigation with high costs,Litigation with low costs,Ratio");
            List<List<TikzLineGraphData>> repeatedGraphData = new List<List<TikzLineGraphData>>();
            foreach (var exogenousProbabilityTrulyLiable in new double[] { 0.25, 0.5, 0.75 })
            {
                List<TikzLineGraphData> repeatedGraphRow = new List<TikzLineGraphData>();
                foreach (var caseStrengthNoise in new double[] { 0.1, 0.2, 0.3 })
                {
                    List<double?> litigationRatesHighCosts = new List<double?>();
                    List<double?> litigationRatesLowCosts = new List<double?>();
                    foreach (var signalNoise in new double[] { 0.1, 0.2, 0.3 })
                    {
                        double lastProbabilityOfLitigation = 0;
                        foreach (var costsPerParty in new double[] { 0.1, 0.2 })
                        {
                            SignalsDiagram diagram = new SignalsDiagram()
                            {
                                ExogenousProbabilityTrulyLiable = exogenousProbabilityTrulyLiable,
                                StdevNoiseToProduceLiabilityStrength = caseStrengthNoise,
                                PLiabilityNoiseStdev = signalNoise,
                                CostPerParty = costsPerParty
                            };
                            string texCode = diagram.CreateDiagram();
                            if (exogenousProbabilityTrulyLiable == 0.5 && caseStrengthNoise == 0.2 && signalNoise == 0.2 && costsPerParty == 0.2)
                                mainSignalsDiagramCode = texCode;
                            if (costsPerParty == 0.1)
                                litigationRatesLowCosts.Add(diagram.ProbabilityOfLitigation);
                            else
                                litigationRatesHighCosts.Add(diagram.ProbabilityOfLitigation);
                            if (costsPerParty == 0.2)
                            {
                                double ratio = lastProbabilityOfLitigation / diagram.ProbabilityOfLitigation;
                                sb.AppendLine($"{exogenousProbabilityTrulyLiable}, {caseStrengthNoise}, {signalNoise}, {diagram.ProbabilityOfLitigation:P1},  {lastProbabilityOfLitigation:P1}, {ratio:P1}");
                            }
                            else
                                lastProbabilityOfLitigation = diagram.ProbabilityOfLitigation;
                        }
                    }
                    TikzLineGraphData innerGraph = new TikzLineGraphData(new List<List<double?>>() { litigationRatesHighCosts, litigationRatesLowCosts }, new List<string>() { "orange, opacity=0.50, line width=0.5mm, dashed", "blue, opacity=0.50, line width=0.5mm" }, new List<string>() { "H", "L" });
                    repeatedGraphRow.Add(innerGraph);
                }
                repeatedGraphData.Add(repeatedGraphRow);
            }
            string csv = sb.ToString();
            TikzRepeatedGraph r = new TikzRepeatedGraph()
            {
                majorXValueNames = new List<string>() { "0.25", "0.50", "0.75" },
                majorXAxisLabel = "Probability truly liable",
                majorYValueNames = new List<string>() { "L", "M", "H" },
                majorYAxisLabel = "Universal noise",
                minorXValueNames = new List<string>() { "L", "M", "H" },
                minorXAxisLabel = "Party noise",
                minorYValueNames = new List<string>() { "0\\%", "50\\%", "100\\%" },
                minorYAxisLabel = "Litigation rate",
                xAxisLabelOffsetDown = 1.0,
                yAxisLabelOffsetMicro = 1.3,
                graphType = TikzAxisSet.GraphType.Line,
                lineGraphData = repeatedGraphData,
            };
            string latexDoc = r.GetStandaloneDocument();
        }

        
    }
}

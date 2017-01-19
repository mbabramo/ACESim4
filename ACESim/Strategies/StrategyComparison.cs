using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class StrategyComparison
    {
        public static void DoComparison(List<Strategy> strategiesToCompare)
        {
            if (strategiesToCompare.Any(x => x.Decision.DummyDecisionRequiringNoOptimization || x.Decision.DummyDecisionSkipAltogether))
            {
                TabbedText.WriteLine("Skipping dummy decision");
                return;
            }
            const int numObsPerStrategy = 500;
            List<double[]> decisionInputsDrawnFromAll = new List<double[]>();
            foreach (Strategy s in strategiesToCompare)
            {
                var samples = s.GetSampleDecisionInputs(1000);
                if (samples != null)
                    decisionInputsDrawnFromAll.AddRange(samples);
            }
            if (!decisionInputsDrawnFromAll.Any())
            {
                TabbedText.WriteLine("Cannot do comparison");
                return;
            }
            int totalNumberDecisionInputsToUse = decisionInputsDrawnFromAll.Count();
            List<double[]> calculatedResults = new List<double[]>();
            List<double[]> normalizedResults = new List<double[]>();
            StatCollectorArray normalizedResultStats = new StatCollectorArray();
            for (int i = 0; i < totalNumberDecisionInputsToUse; i++)
            {
                double[] resultsForThisDecisionInput = new double[strategiesToCompare.Count()];
                double[] normalizedResultsForThisDecisionInput = new double[strategiesToCompare.Count()];
                StatCollector sc = new StatCollector();
                // first get raw calculation
                for (int s = 0; s < strategiesToCompare.Count(); s++)
                {
                    resultsForThisDecisionInput[s] = strategiesToCompare[s].Calculate(decisionInputsDrawnFromAll[i].ToList());
                    sc.Add(resultsForThisDecisionInput[s]);
                }
                calculatedResults.Add(resultsForThisDecisionInput);
                // now normalize the results in absolute terms
                for (int s = 0; s < strategiesToCompare.Count(); s++)
                {
                    normalizedResultsForThisDecisionInput[s] = Math.Abs((resultsForThisDecisionInput[s] - sc.Average()) / sc.StandardDeviation());
                }

                //if (strategiesToCompare.Count() > 2)
                //{ // normalize again, so we see more clearly what is an outlier among standard deviations
                //    sc = new StatCollector();
                //    for (int s = 0; s < strategiesToCompare.Count(); s++)
                //    {
                //        sc.Add(normalizedResultsForThisDecisionInput[s]);
                //    }
                //    for (int s = 0; s < strategiesToCompare.Count(); s++)
                //    {
                //        normalizedResultsForThisDecisionInput[s] = Math.Abs((normalizedResultsForThisDecisionInput[s] - sc.Average()) / sc.StandardDeviation());
                //    }
                //}
                normalizedResults.Add(normalizedResultsForThisDecisionInput);
                normalizedResultStats.Add(normalizedResultsForThisDecisionInput);
            }
            // Now report on whether there are outliers.
            TabbedText.WriteLine(String.Join(", ", normalizedResultStats.Average().Select(x => x.ToSignificantFigures()).ToArray()));
        }
    }
}

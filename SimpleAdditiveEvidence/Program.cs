using ACESim;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{


    class Program
    {
        static double[] allQualities = new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 };
        static double[] allCosts = new double[] { 0, 0.15, 0.30, 0.45, 0.60 };
        static double[] allFeeShifting = new double[] { 0, 0.25, 0.50, 0.75, 1.0 };

        static void Main(string[] args)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            StringBuilder b = new StringBuilder();
            string headerRow = "Cost,Quality,Threshold," + DMSApproximatorOutcome.GetHeaderString();
            b.AppendLine(headerRow);
            foreach (double c in allCosts) 
            {
                foreach (double q in allQualities)
                {
                    foreach (double t in allFeeShifting)
                    {
                        DMSApproximator e = new DMSApproximator(q, c, t);
                        string rowPrefix = $"{c},{q},{t},";
                        string row = rowPrefix + e.TheOutcome.ToString();
                        b.AppendLine(row);
                        TabbedText.WriteLine("Output: " + row);
                        TabbedText.WriteLine();
                    }
                }
                TabbedText.WriteLine($"Results for costs {c}");
                TabbedText.WriteLine(b.ToString());
            }

            TabbedText.WriteLine($"Overall results");
            TabbedText.WriteLine(b.ToString());
            TabbedText.WriteLine($"Time {s.ElapsedMilliseconds}");
        }

        

    }
}

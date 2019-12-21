using ACESim;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{


    class Program
    {
        static void Main(string[] args)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            StringBuilder b = new StringBuilder();
            string headerRow = "Cost,Quality,Threshold," + DMSApproximatorOutcome.GetHeaderString();
            b.AppendLine(headerRow);
            foreach (double c in new double[] { /* DEBUG 0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, */ 0.35, 0.4 }) // 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.8, 1.0 })
            {
                foreach (double q in new double[] { /* DEBUG 0, 0.1, 0.2, 0.3, */ 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 })
                {
                    foreach (double t in new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1.0 })
                    {
                        DMSApproximator e = new DMSApproximator(q, c, t);
                        string rowPrefix = $"{c},{q},{t},";
                        string row = rowPrefix + e.TheOutcome.ToString();
                        b.AppendLine(row);
                        TabbedText.WriteLine("Output: " + row);
                        TabbedText.WriteLine();
                    }
                }
            }

            TabbedText.WriteLine(b.ToString());
            TabbedText.WriteLine($"Time {s.ElapsedMilliseconds}");
        }

        

    }
}

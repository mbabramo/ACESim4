using ACESimBase.GameSolvingSupport.Reporting;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract partial class StrategiesDeveloperBase
    {
        public double[] BenchmarkLastEnumeratedUtilities { get; private set; }

        /// <summary>Compatibility entrypoint. New callers receive reports and utilities together
        /// from NativeOutcomeReportWriter instead of reading a side-channel property.</summary>
        public async Task<ReportCollection> GenerateBenchmarkReportsAsync()
        {
            var result = await NativeOutcomeReportWriter.GenerateAsync(this, Status.UtilitiesOverall?.ToArray());
            BenchmarkLastEnumeratedUtilities = result.Utilities;
            return result.Reports;
        }
    }
}

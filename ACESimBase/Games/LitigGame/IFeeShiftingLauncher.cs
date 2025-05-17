using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESim.Launcher;
using static ACESim.LitigGameLauncher;

namespace ACESimBase
{
    public interface IFeeShiftingLauncher
    {
        // Launch the simulation and return the collection of reports (asynchronous).
        Task<ReportCollection> Launch();

        // All scenario GameOptions for this launcher (excluding duplicate baseline scenarios).
        List<GameOptions> AllGameOptions { get; }

        // Mapping from each full scenario name to its base (baseline) scenario name.
        Dictionary<string, string> NameMap { get; }

        // Article variation metadata: list of variation info sets for grouping scenarios in reports.
        List<ArticleVariationInfoSets> VariationInfoSets { get; }

        // Stable report prefix used for output files (e.g., "FS036" or "APP001").
        string ReportPrefix { get; }
    }
}

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

        // Categorizes each option set based on the grouping variables.
        IEnumerable<(string OptionSetName, List<GroupingVariableInfo> Variables)> GetVariableInfoPerOption();


        // Mapping from each full scenario name to its baseline scenario name.
        Dictionary<string, string> NameMap { get; }

        // Article variation metadata: list of variation info sets for grouping scenarios in reports.
        List<ArticleVariationInfoSets> VariationInfoSets { get; }

        // Names of variation sets for grouping scenarios in reports (e.g., "Risk Aversion")
        List<string> NamesOfVariationSets { get; }

        // For each named variation set, a string representation of the default value of the variation.
        List<(string, string)> DefaultNonCriticalValues { get; }

        // Stable report prefix used for output files (e.g., "FS036" or "APP001").
        string ReportPrefix { get; }

    }
}

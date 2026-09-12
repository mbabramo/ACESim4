using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame
{
    /// <summary>Opt-in, isolated article experiment. Never invokes Launch(), distributed work, or production output paths.</summary>
    public static class LitigGameEnumeratedPureLauncher
    {
        public static LitigGameOptions CreateOptions(int signals, int offers, bool british)
        {
            if (signals < 1 || signals > 10 || offers < 2 || offers > 15) throw new ArgumentOutOfRangeException(nameof(signals));
            if (MonotonePureStrategyCatalog.ExpectedCount(signals, offers) > 10_000)
                throw new ArgumentException("This experiment is limited to 10,000 strategies per party; use a smaller grid.");
            var article = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            // Production validation is intentionally left intact and exercised before deriving a fresh option object.
            var options = article.GetOptionsSets().Cast<LitigGameOptions>().Single(o =>
                Convert.ToString(o.VariableSettings["Specification"], CultureInfo.InvariantCulture) == "Baseline" &&
                o.CostsMultiplier == 1 && o.NumOffers == 10 && o.LoserPaysMultiple == (british ? 1 : 0));
            options.NumLiabilitySignals = (byte)signals;
            options.NumOffers = (byte)offers;
            options.Name = $"CSPURE-v1-n{signals}-m{offers}-{(british ? "British" : "American")}";
            options.VariableSettings["Number of Signals"] = signals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Offers"] = offers.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Experimental Algorithm"] = "EnumeratedPureStrategies-v1";
            Validate(options);
            return options;
        }

        public static void Validate(LitigGameOptions o)
        {
            if (o.LitigGameDisputeGenerator is not LitigGameUniformQualityDisputeGenerator g ||
                g.QualityDistribution != ContinuousQualityDistribution.Uniform ||
                o.NumLiabilityStrengthPoints != 2 || o.NumCourtLiabilitySignals != 2 || o.NumDamagesStrengthPoints != 1 ||
                !o.CollapseChanceDecisions || !o.CollapseAlternativeEndings ||
                o.NumPotentialBargainingRounds != 1 || !o.BargainingRoundsSimultaneous ||
                !o.PredeterminedAbandonAndDefaults || !o.AllowAbandonAndDefaults || o.SkipFileAndAnswerDecisions ||
                o.IncludeAgreementToBargainDecisions || o.LitigGameRunningSideBets != null || o.LitigGamePretrialDecisionGeneratorGenerator != null)
                throw new ArgumentException("The monotone catalog requires the article's continuous-merits, one-round simultaneous-offer game with endogenous participation and precommitted exit.");
        }

        public static string Configuration(LitigGameOptions options) => JsonConvert.SerializeObject(new
        {
            Schema = 1, options.Name,
            Options = options,
            Numerical = new { EvolutionSettings.MaxIntegralUtility, EvolutionSettings.RoundOffChanceDigits, ChanceMinimumPositive = false },
            Restrictions = MonotonePureStrategyCatalog.Restrictions
        }, Formatting.Indented, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, ContractResolver = new OptionSnapshotResolver() });

        private sealed class OptionSnapshotResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization)
            {
                var property = base.CreateProperty(member, serialization);
                // Delegates capture launchers and entire option matrices. Their implementation is
                // fingerprinted with the source; their effective numerical settings are recorded separately.
                if (typeof(Delegate).IsAssignableFrom(property.PropertyType)) property.Ignored = true;
                return property;
            }
        }

        public static async Task<EnumeratedPureStrategies> InitializeAsync(LitigGameOptions options, string outputDirectory,
            string sourceStateJson, int workers = 1, bool resume = false)
        {
            Validate(options);
            var settings = new EvolutionSettings
            {
                Algorithm = GameApproximationAlgorithm.EnumeratedPureStrategies,
                ParallelOptimization = false, UseAcceleratedBestResponse = false,
                CreateEFGFile = false, CreateEquilibriaFile = false, GenerateManualReports = false,
                GenerateReportsByPlaying = false, SequenceFormCutOffProbabilityZeroNodes = true,
                EnumeratedPure = new EnumeratedPureSettings
                {
                    OutputDirectory = outputDirectory, ConfigurationJson = Configuration(options), SourceStateJson = sourceStateJson,
                    Workers = workers, Resume = resume,
                    SpaceFactory = developer => new MonotonePureStrategyCatalog(options.NumLiabilitySignals, options.NumOffers, developer)
                }
            };
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits);
            return (EnumeratedPureStrategies)await launcher.GetInitializedDevelper(options, options.Name, settings);
        }

        public static string CaptureSourceState(string repository)
        {
            string Git(params string[] args)
            {
                var start = new ProcessStartInfo("git") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new IOException(error);
                return output.TrimEnd();
            }
            var paths = Git("ls-files", "--cached", "--others", "--exclude-standard").Split('\n')
                .Where(p => new[] { ".cs", ".csproj", ".props", ".targets", ".sln", ".json" }.Contains(Path.GetExtension(p)))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var hashes = paths.Select(p => new { Path = p, Sha256 = File.Exists(Path.Combine(repository, p)) ? PureRunStore.Hash(File.ReadAllBytes(Path.Combine(repository, p))) : "DELETED" }).ToArray();
            return System.Text.Json.JsonSerializer.Serialize(new { Commit = Git("rev-parse", "HEAD"), Status = Git("status", "--short"), SourceFiles = hashes }, PureRunStore.Json);
        }

        public static async Task RunCommandAsync(string[] args)
        {
            var parameters = ParseArguments(args);
            string output = Required(parameters, "output");
            int signals = Integer(parameters, "signals", 5), offers = Integer(parameters, "offers", 5), workers = Integer(parameters, "workers", 1);
            string fee = parameters.GetValueOrDefault("fee", "both");
            if (fee != "both" && fee != "American" && fee != "British") throw new ArgumentException("--fee must be American, British, or both.");
            string source = CaptureSourceState(parameters.GetValueOrDefault("repository", Directory.GetCurrentDirectory()));
            foreach (bool british in fee == "both" ? new[] { false, true } : new[] { fee == "British" })
            {
                var options = CreateOptions(signals, offers, british);
                var solver = await InitializeAsync(options, Path.Combine(output, options.Name), source, workers, parameters.ContainsKey("resume"));
                try
                {
                    var reports = await solver.RunAlgorithm(options.Name);
                    Console.WriteLine(reports.standardReport);
                    Console.WriteLine(solver.Store.DirectoryPath);
                }
                finally { solver.Store?.Dispose(); }
            }
        }
        public static Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument {args[i]}.");
                string key = args[i][2..];
                if (key is "resume" or "verify-all") result.Add(key, "true");
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) result.Add(key, args[++i]);
                else throw new ArgumentException($"Missing value for --{key}.");
            }
            var known = new[] { "output", "signals", "offers", "workers", "fee", "repository", "resume", "input", "tolerances", "verify", "verify-all", "outcome-radius", "strategy-radius" };
            if (result.Keys.Any(k => !known.Contains(k))) throw new ArgumentException("Unknown command option.");
            return result;
        }
        public static string Required(Dictionary<string, string> args, string key) => args.TryGetValue(key, out var value) ? value : throw new ArgumentException($"--{key} is required.");
        public static int Integer(Dictionary<string, string> args, string key, int fallback) => args.TryGetValue(key, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
    }
}

using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Individual-profile audit for the observable agreement-stage study. No solver calls.</summary>
public static class AgreementToBargainStudy
{
    public static object Hash(string path) => new { Path = Path.GetFullPath(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    private static void Write(string path, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(data, ArticleWorkedPathExtraction.JsonOptions));
    }
    private static double? Ratio(double numerator, double denominator) => denominator > 0 ? numerator / denominator : null;
    private static void Near(double a, double b, string label, double tolerance = 1e-8)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a - b) > tolerance)
            throw new InvalidDataException($"{label}: {a:G17} versus {b:G17}");
    }
    // Preserve all public configuration fields, auto-properties, nested calculators,
    // distributions and numerical settings, without serializing executable delegates.
    private static object Configuration(object value, int depth = 0)
    {
        if (value == null || value is Delegate) return null;
        Type t = value.GetType();
        if (t.IsEnum) return value.ToString();
        if (t.IsPrimitive || value is string || value is decimal) return value;
        if (depth > 7) return t.FullName;
        if (value is IDictionary dictionary)
            return dictionary.Keys.Cast<object>().ToDictionary(k => k.ToString(), k => Configuration(dictionary[k], depth + 1));
        if (value is IEnumerable enumerable) return enumerable.Cast<object>().Select(v => Configuration(v, depth + 1)).ToArray();
        var result = new SortedDictionary<string, object> { ["$type"] = t.FullName };
        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
            if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) result[field.Name] = Configuration(field.GetValue(value), depth + 1);
        foreach (var property in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && !typeof(Delegate).IsAssignableFrom(property.PropertyType))
                result[property.Name] = Configuration(property.GetValue(value), depth + 1);
        return result;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string input = null, output = null, plan = "agreement-to-bargain"; bool parametersOnly = false;
            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--input": input = Path.GetFullPath(args[++i]); break;
                    case "--output": output = Path.GetFullPath(args[++i]); break;
                    case "--plan": plan = args[++i]; break;
                    case "--parameters-only": parametersOnly = true; break;
                    default: throw new ArgumentException("Unknown argument: " + args[i]);
                }
            if (output == null || !parametersOnly && input == null) throw new ArgumentException("Supply --input and --output.");
            bool baselineSingle = plan == "baseline-single";
            var launcher = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ParseProductionRunPlan(baselineSingle ? "agreement-to-bargain" : plan));
            bool numbered = launcher.IsMultipleEquilibriaPlan;
            if (!numbered && launcher.RunPlan != LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                throw new ArgumentException("Expected an agreement study or a saved baseline study.");
            var options = baselineSingle ? LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans
                .SelectMany(p => new LitigGameCorrelatedSignalsArticleLauncher(p).GetOptionsSets()).Cast<LitigGameOptions>()
                .Where(o => o.NumOffers == 10 && (string)o.VariableSettings["Specification"] is "Baseline" or "Moderate risk aversion")
                .ToArray() : launcher.GetOptionsSets().Cast<LitigGameOptions>().ToArray();
            if (baselineSingle && options.Length != 30)
            {
                var names = launcher.GetOptionsSets().Select(o => o.Name.Replace("Agreement-Enabled__", "")).ToHashSet();
                options = LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans
                    .SelectMany(p => new LitigGameCorrelatedSignalsArticleLauncher(p).GetOptionsSets()).Cast<LitigGameOptions>()
                    .Where(o => names.Contains(o.Name)).ToArray();
            }
            if (!numbered && options.Length != 30) throw new InvalidDataException("Expected thirty single-equilibrium cases.");
            var parameters = new List<object>();
            foreach (var option in options)
            {
                var settings = launcher.GetEvolutionSettings(); option.ModifyEvolutionSettings(settings);
                var developer = await ArticleWorkedPathExtraction.InitializeAsync(option);
                var recorder = new RecordGamePathsProcessor(); developer.TreeWalk_Tree(recorder);
                parameters.Add(new { option.Name, Options = Configuration(option), Solver = Configuration(settings),
                    OptionSummary = option.ToString(), MaxIntegralUtility = EvolutionSettings.MaxIntegralUtility,
                    RoundOffChanceDigits = EvolutionSettings.RoundOffChanceDigits,
                    Tree = new { TerminalHistories = recorder.Paths.Count, InformationSets = developer.InformationSets.Count,
                        InformationSetsByDecision = developer.InformationSets.GroupBy(n => ((LitigGameDecisions)n.DecisionByteCode).ToString()).ToDictionary(g => g.Key, g => g.Count()),
                        StrategyEntries = developer.InformationSets.Sum(n => n.NumPossibleActions), ChanceNodes = developer.ChanceNodes.Count,
                        FinalUtilityNodes = developer.FinalUtilitiesNodes.Count },
                    Arithmetic = numbered ? "Initial exact; additional inexact with exact fallback; unchanged perfect-equilibrium validation" : "One exact initialization per new case; existing saved baseline profiles are only revalidated",
                    PivotLimits = new { InitialExact = 0, Inexact = 500, AdditionalExactBatch = 1000, SingleExactFallback = 0 },
                    SeedPolicy = numbered ? "Initial exact seed 0; inexact seeds 1000000..1000048; exact fallback batch restarts at seed 0; see each start audit log" : "Initial exact prior seed 0; no additional starts" });
            }
            Write(Path.Combine(output, "Sources", "parameters.json"), new { Plan = plan, CreatedUtc = DateTime.UtcNow, Parameters = parameters,
                GameAssembly = Hash(typeof(LitigGame).Assembly.Location), ReportingAssembly = Hash(typeof(AgreementToBargainStudy).Assembly.Location) });
            if (parametersOnly) return 0;
            var profiles = await MultipleEquilibriaStrategyAudit.RunAsync(options, input, output,
                baselineSingle ? null : launcher.MasterReportNameForDistributedProcessing, writeStudyData: true, generateDiagrams: false, numbered: numbered);
            Write(Path.Combine(output, "Sources", "strategy-verification.json"), new { Method = "Reload each individual profile; normalize; reproduce action reports; unrestricted full unilateral best responses; replay numeric reports; enumerate joint probabilities", Tolerance = 1e-7, Profiles = profiles,
                Inputs = profiles.SelectMany(p => new[] { p.ProfileFile, p.ActionReport, p.ReplayReport }).Distinct().Select(Hash).ToArray(),
                GameAssembly = Hash(typeof(LitigGame).Assembly.Location), ReportingAssembly = Hash(typeof(AgreementToBargainStudy).Assembly.Location) });
            if (!numbered)
            {
                CorrelatedSignalsMultipleEquilibriaReport.BuildSingleAndValidate(options,
                    option => Path.Combine(input, (baselineSingle ? option.LoserPaysAfterAbandonment ? "CS006EF" : "CS004" : launcher.MasterReportNameForDistributedProcessing) + " " + option.Name + ".csv"),
                    Path.Combine(output, "Sources", "equilibrium-outcomes.csv"), profiles.ToDictionary(p => (p.OptionSet, p.Equilibrium), p => p.MaximumGain));
                return 0;
            }
            string previous = Environment.GetEnvironmentVariable(ACESimBase.Util.Serialization.FolderFinder.ReportResultsDirectoryEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ACESimBase.Util.Serialization.FolderFinder.ReportResultsDirectoryEnvironmentVariable, input);
                CorrelatedSignalsMultipleEquilibriaReport.BuildAndValidate(launcher,
                    Path.Combine(output, "Sources", "equilibrium-outcomes.csv"), Path.Combine(output, "Sources", "equilibrium-ranges.csv"),
                    profiles.ToDictionary(p => (p.OptionSet, p.Equilibrium), p => p.MaximumGain));
            }
            finally { Environment.SetEnvironmentVariable(ACESimBase.Util.Serialization.FolderFinder.ReportResultsDirectoryEnvironmentVariable, previous); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private sealed record Context(byte Signal, byte? OwnExit);
    private sealed record History(double Probability, byte PSignal, byte DSignal, byte File, byte Answer,
        byte PExit, byte DExit, byte PAgree, byte DAgree, byte POffer, byte DOffer);

    public static void ExportProfile(StrategiesDeveloperBase developer, LitigGameOptions option, int equilibrium,
        string profileFile, string actionReport, string replayReport, string output, HashSet<int> fallbacks)
    {
        var calculator = new CalculateUtilitiesAtEachInformationSet(); developer.TreeWalk_Tree(calculator);
        var recorder = new RecordGamePathsProcessor(); developer.TreeWalk_Tree(recorder);
        Near(recorder.Paths.Sum(p => p.Probability), 1, "Whole-profile probability");
        var contexts = new Dictionary<int, Context>();
        var histories = new List<History>();
        foreach (var path in recorder.Paths)
        {
            var actions = new Dictionary<LitigGameDecisions, byte>();
            byte A(LitigGameDecisions d) => actions.GetValueOrDefault(d);
            foreach (var step in path.Steps)
            {
                var code = (LitigGameDecisions)((IAnyNode)step.FromNode).Decision.DecisionByteCode;
                if (step.FromNode is InformationSetNode node)
                {
                    byte ownSignal = A(node.PlayerIndex == 0 ? LitigGameDecisions.PLiabilitySignal : LitigGameDecisions.DLiabilitySignal);
                    byte ownExit = A(node.PlayerIndex == 0 ? LitigGameDecisions.PAbandon : LitigGameDecisions.DDefault);
                    var context = new Context(ownSignal, ownExit == 0 ? null : ownExit);
                    if (contexts.TryGetValue(node.InformationSetNodeNumber, out var previous) && previous != context)
                        throw new InvalidDataException("Signal or private exit differs within an information set.");
                    contexts[node.InformationSetNodeNumber] = context;
                }
                actions[code] = step.ActionIndex;
            }
            histories.Add(new(path.Probability, A(LitigGameDecisions.PLiabilitySignal), A(LitigGameDecisions.DLiabilitySignal),
                A(LitigGameDecisions.PFile), A(LitigGameDecisions.DAnswer), A(LitigGameDecisions.PAbandon), A(LitigGameDecisions.DDefault),
                A(LitigGameDecisions.PAgreeToBargain), A(LitigGameDecisions.DAgreeToBargain), A(LitigGameDecisions.POffer), A(LitigGameDecisions.DOffer)));
        }
        double Sum(Func<History, bool> filter) => histories.Where(filter).Sum(h => h.Probability);
        bool Stage(History h) => h.File == 1 && h.Answer == 1;
        bool Both(History h) => Stage(h) && (!option.IncludeAgreementToBargainDecisions || h.PAgree == 1 && h.DAgree == 1);
        bool Settles(History h) => Both(h) && h.POffer > 0 && h.DOffer >= h.POffer;
        bool Trial(History h) => Stage(h) && !Settles(h) && h.PExit == 2 && h.DExit == 2;
        double filed = Sum(h => h.File == 1), stage = Sum(Stage), trial = Sum(Trial);
        double settlement = Sum(Settles), mutual = Sum(h => Stage(h) && !Settles(h) && h.PExit == 1 && h.DExit == 1);
        double abandon = Sum(h => Stage(h) && !Settles(h) && h.PExit == 1 && h.DExit == 2) + mutual / 2;
        double defaults = Sum(h => Stage(h) && !Settles(h) && h.PExit == 2 && h.DExit == 1) + mutual / 2;
        Near(1, 1 - filed + filed - stage + settlement + abandon + defaults + trial, "Terminal dispositions");
        var all = PublicationFigures.ReadCsv(replayReport).Single(r => r["Filter"] == "All");
        double N(string k) => double.Parse(all[k], CultureInfo.InvariantCulture);
        Near(filed, N("PFiles"), "Filing replay", 1e-5); Near(stage, N("DAnswers"), "Joint participation replay", 1e-5);
        Near(settlement, N("SettlesBR1"), "Settlement replay", 1e-5); Near(trial, N("Trial"), "Trial replay", 1e-5);
        Near(abandon, N("PAbandonsBR1") + 0.5 * N("BothReadyToGiveUp"), "Abandonment replay", 1e-5);
        Near(defaults, N("DDefaultsBR1") + 0.5 * N("BothReadyToGiveUp"), "Default replay", 1e-5);
        // The legacy CSV formats wealth to six significant digits (about 5e-5
        // resolution at wealth 20). Audit the unrounded terminal accounting instead.
        var terminalAccounting = developer.SavedWeightedGameProgresses.SelectMany(item =>
        {
            var p = (LitigGameProgress)item.theProgress;
            return p.AlternativeEndings == null ? new[] { p } : p.AlternativeEndings.Select(e => e.completedGame);
        }).ToArray();
        double RealCost(LitigGameProgress p) => option.CostsMultiplier *
            ((p.PFiles ? option.PFilingCost * (p.DAnswers ? 1 : 1 - option.PFilingCost_PortionSavedIfDDoesntAnswer) : 0) +
             (p.DAnswers ? option.DAnswerCost : 0) + (p.TrialOccurs ? option.PTrialCosts + option.DTrialCosts : 0));
        double accountingResidual = terminalAccounting.Max(p => Math.Abs(p.PFinalWealth + p.DFinalWealth + RealCost(p) - option.PInitialWealth - option.DInitialWealth));
        Near(accountingResidual, 0, "Unrounded terminal monetary conservation", 1e-10);
        double expenseResidual = terminalAccounting.Max(p => Math.Abs(p.TotalExpensesIncurred - RealCost(p)));
        Near(expenseResidual, 0, "Disposition-based real expenditure", 1e-10);
        var strategies = developer.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber).Select(n =>
        {
            var c = contexts[n.InformationSetNodeNumber];
            var p = n.GetCurrentProbabilitiesAsArray(); Near(p.Sum(), 1, "Action normalization", 1e-9);
            double reach = calculator.GetUtilitiesAndReachProbability(n.InformationSetNodeNumber).reachProbability;
            if (fallbacks.Contains(n.InformationSetNodeNumber) && reach > 1e-12)
                throw new InvalidDataException("A saved strategy is unspecified at a reached information set.");
            return new { InformationSet = n.InformationSetNodeNumber, Player = n.PlayerIndex, Decision = ((LitigGameDecisions)n.DecisionByteCode).ToString(),
                c.Signal, c.OwnExit, Contents = n.InformationSetContentsString, Reach = reach, Probabilities = p,
                ReachedConditionalProbabilities = reach > 0 ? p : null,
                Actions = Enumerable.Range(1, p.Length).Select(a => developer.GameDefinition.GetActionString((byte)a, n.DecisionByteCode)).ToArray() };
        }).ToArray();
        var bySignal = Enumerable.Range(1, option.NumLiabilitySignals).Select(s => new
        {
            Signal = s,
            PlaintiffSignalProbability = Sum(h => h.PSignal == s), DefendantSignalProbability = Sum(h => h.DSignal == s),
            Filing = Ratio(Sum(h => h.PSignal == s && h.File == 1), Sum(h => h.PSignal == s)),
            AnsweringGivenFiling = Ratio(Sum(h => h.DSignal == s && Stage(h)), Sum(h => h.DSignal == s && h.File == 1))
        }).ToArray();
        var refusal = recorder.Paths.Where(p => p.Steps.Any(s => s.FromNode is InformationSetNode n &&
            (n.DecisionByteCode == (byte)LitigGameDecisions.PAgreeToBargain || n.DecisionByteCode == (byte)LitigGameDecisions.DAgreeToBargain) && s.ActionIndex == 2))
            .OrderByDescending(p => p.Probability).FirstOrDefault();
        object worked = refusal == null ? null : ArticleWorkedPathExtraction.ExtractPath(developer, option, calculator, fallbacks, refusal,
            "Refusal branch", refusal.Probability > 0 ? "Verified equilibrium path" : "Counterfactual refusal branch; zero equilibrium probability");
        Write(Path.Combine(output, "Sources", "Profiles", option.Name + $"-Eq{equilibrium}.json"), new
        {
            OptionSet = option.Name, Equilibrium = equilibrium, AgreementEnabled = option.IncludeAgreementToBargainDecisions,
            CostMultiplier = option.CostsMultiplier,
            FeeRule = LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(option), Alpha = Convert.ToDouble(option.VariableSettings["CARA Alpha"]),
            Profile = Hash(profileFile), ActionReport = Hash(actionReport), ReplayReport = Hash(replayReport),
            Metrics = new { Filing = filed, JointFileAnswer = stage, AnsweringGivenFiling = Ratio(stage, filed),
                BothAgreeGivenStage = option.IncludeAgreementToBargainDecisions ? Ratio(Sum(Both), stage) : null,
                OnlyPlaintiffDeclinesGivenStage = option.IncludeAgreementToBargainDecisions ? Ratio(Sum(h => Stage(h) && h.PAgree == 2 && h.DAgree == 1), stage) : null,
                OnlyDefendantDeclinesGivenStage = option.IncludeAgreementToBargainDecisions ? Ratio(Sum(h => Stage(h) && h.PAgree == 1 && h.DAgree == 2), stage) : null,
                BothDeclineGivenStage = option.IncludeAgreementToBargainDecisions ? Ratio(Sum(h => Stage(h) && h.PAgree == 2 && h.DAgree == 2), stage) : null,
                Settlement = settlement, Abandonment = abandon, Default = defaults, MutualExitBeforeAllocation = mutual,
                Trial = trial, TrialGivenFiling = Ratio(trial, filed), TrialGivenFileAnswer = Ratio(trial, stage) },
            BySignal = bySignal, Strategies = strategies, ReachedHistories = histories.Where(h => h.Probability > 0).ToArray(), WorkedRefusalPath = worked,
            MaximumNormalizationResidual = strategies.Max(n => Math.Abs(n.Probabilities.Sum() - 1)),
            UnspecifiedOffPathInformationSets = fallbacks.OrderBy(n => n).ToArray(),
            Accounting = new { TerminalsChecked = terminalAccounting.Length, MaximumUnroundedResidual = accountingResidual,
                MaximumExpenseResidual = expenseResidual,
                PrintedReportResidual = N("TotWealth") + N("TotExpense") - option.PInitialWealth - option.DInitialWealth },
            DistinctnessNotes = "Complete strategies include unreached actions. Reached behavior is the joint distribution of reached histories. Outcomes are compared separately. Recovery frequency is not equilibrium selection."
        });
    }
}

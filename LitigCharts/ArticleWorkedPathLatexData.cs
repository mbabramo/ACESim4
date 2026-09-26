using ACESim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static ACESimBase.Games.LitigGame.ManualReports.ArticleWorkedPathExtraction;

namespace LitigCharts
{
    /// <summary>Numerical bindings for ONE bespoke figure, not a tree-layout engine.</summary>
    public static class ArticleWorkedPathLatexData
    {
        public static string Build(Extraction data)
        {
            bool agreement=data.OptionSetName=="Agreement-Enabled__Specification-Baseline__Cost-1__Fee-American";
            if ((!agreement&&data.OptionSetName != "Specification-Baseline__Cost-1__Fee-American") || data.EquilibriumNumber != 1)
                throw new ArgumentException("This bespoke layout is for the baseline American-rule equilibrium 1 at cost 1.");
            PathData Path(string name) => data.Paths.Single(x => x.Name == name);
            StepData Step(string path, LitigGameDecisions decision) => Path(path).Steps.Single(x => x.Decision == decision);
            ActionData Action(string path, LitigGameDecisions decision, byte action) =>
                Step(path, decision).Actions.Single(x => x.Action == action);
            OutcomeData Outcome(string path, byte? court = null) => Path(path).Outcomes.Single(x => x.CourtSignal == court);
            void SameInfo(LitigGameDecisions decision, params string[] paths)
            {
                if (paths.Select(p => Step(p, decision).InformationSetNumber).Distinct().Count() != 1)
                    throw new InvalidDataException("The figure's information-set connector is invalid: " + decision);
            }
            SameInfo(LitigGameDecisions.PFile, "trial", "adjacent-defendant-signal");
            SameInfo(LitigGameDecisions.DAnswer, "trial", "settlement");
            SameInfo(LitigGameDecisions.DDefault, "trial", "settlement");
            SameInfo(LitigGameDecisions.DOffer, "trial", "settlement", "lower-demand-deviation");
            Choice[] Bargaining(byte p, byte d, byte demand, byte offer = 1, byte exit = 2) => new[]
            {
                new Choice(LitigGameDecisions.PLiabilitySignal, p),
                new Choice(LitigGameDecisions.DLiabilitySignal, d),
                new Choice(LitigGameDecisions.PFile, 1),
                new Choice(LitigGameDecisions.DAnswer, 1),
                new Choice(LitigGameDecisions.PAbandon, exit),
                new Choice(LitigGameDecisions.DDefault, 2),
            }.Concat(agreement?new[]{new Choice(LitigGameDecisions.PAgreeToBargain,1),new Choice(LitigGameDecisions.DAgreeToBargain,1)}:Array.Empty<Choice>())
             .Concat(new[]{new Choice(LitigGameDecisions.POffer,demand),new Choice(LitigGameDecisions.DOffer,offer)}).ToArray();
            var expectedPaths = new Dictionary<string, Choice[]>
            {
                ["trial"] = Bargaining(4, 3, 8),
                ["settlement"] = Bargaining(3, 3, 1),
                ["adjacent-defendant-signal"] = Bargaining(4, 4, 8),
                ["lower-demand-deviation"] = Bargaining(4, 3, 1),
                ["higher-offer-deviation"] = Bargaining(4, 3, 8, 8),
                ["no-filing"] = Bargaining(3, 3, 1).Take(2)
                    .Append(new Choice(LitigGameDecisions.PFile, 2)).ToArray(),
                ["no-answer-deviation"] = Bargaining(4, 3, 8).Take(3)
                    .Append(new Choice(LitigGameDecisions.DAnswer, 2)).ToArray(),
            };
            // The hand-drawn geometry assumes exactly these selected branches. Refuse a
            // silently changed request/profile that would make the hard-coded labels false.
            foreach (var expected in expectedPaths)
                if (!Path(expected.Key).Steps.Select(x => new Choice(x.Decision, x.SelectedAction))
                    .SequenceEqual(expected.Value))
                    throw new InvalidDataException("Request does not match bespoke layout: " + expected.Key);
            foreach (string path in new[] { "trial", "settlement" })
            {
                foreach (var step in Path(path).Steps.Where(x => x.Player != "Chance"))
                {
                    double p = step.Actions.Single(x => x.Action == step.SelectedAction).Probability;
                    if (!(path == "settlement" && step.Decision == LitigGameDecisions.PFile) && Math.Abs(p - 1) > 1E-10)
                        throw new InvalidDataException("An unlabeled probability-one branch is no longer pure.");
                }
            }
            if (new[] { "lower-demand-deviation", "higher-offer-deviation", "no-answer-deviation" }
                .Any(p => Path(p).EquilibriumProbability != 0))
                throw new InvalidDataException("A branch drawn as a zero-probability deviation is now on path.");

            var macros = new SortedDictionary<string, string>(StringComparer.Ordinal);
            void Number(string name, double value, string format = "0.000") =>
                macros.Add(name, (Math.Abs(value) < 0.0000001 ? 0 : value).ToString(format, CultureInfo.InvariantCulture));
            void Info(string macro, string path, LitigGameDecisions decision) =>
                macros.Add(macro, Step(path, decision).InformationSetNumber.Value.ToString(CultureInfo.InvariantCulture));
            void Utility(string macro, string path, LitigGameDecisions decision, byte action) =>
                Number(macro, Action(path, decision, action).ConditionalActingPlayerUtility ??
                    throw new InvalidDataException("Cannot display a conditional utility at an off-path information set."));
            void Pair(string macro, OutcomeData outcome) => macros.Add(macro,
                outcome.PlaintiffNetMonetaryPayoff.ToString("0.00", CultureInfo.InvariantCulture) + @",\," +
                outcome.DefendantNetMonetaryPayoff.ToString("0.00", CultureInfo.InvariantCulture));

            Info("FileMainInfo", "trial", LitigGameDecisions.PFile);
            Info("FileMixedInfo", "settlement", LitigGameDecisions.PFile);
            Info("AnswerInfo", "trial", LitigGameDecisions.DAnswer);
            Info("PExitMainInfo", "trial", LitigGameDecisions.PAbandon);
            Info("PExitMixedInfo", "settlement", LitigGameDecisions.PAbandon);
            Info("DExitInfo", "trial", LitigGameDecisions.DDefault);
            Info("DemandMainInfo", "trial", LitigGameDecisions.POffer);
            Info("DemandMixedInfo", "settlement", LitigGameDecisions.POffer);
            Info("OfferInfo", "trial", LitigGameDecisions.DOffer);
            if(agreement)
            {
                SameInfo(LitigGameDecisions.DAgreeToBargain,"trial","settlement");
                Info("PAgreeMainInfo","trial",LitigGameDecisions.PAgreeToBargain);
                Info("PAgreeMixedInfo","settlement",LitigGameDecisions.PAgreeToBargain);
                Info("DAgreeInfo","trial",LitigGameDecisions.DAgreeToBargain);
            }
            Number("SignalMainProbability", Action("trial", LitigGameDecisions.PLiabilitySignal, 4).Probability);
            Number("SignalMixedProbability", Action("settlement", LitigGameDecisions.PLiabilitySignal, 3).Probability);
            Number("DSignalMainProbability", Action("trial", LitigGameDecisions.DLiabilitySignal, 3).Probability);
            Number("DSignalAdjacentProbability", Action("trial", LitigGameDecisions.DLiabilitySignal, 4).Probability);
            Number("DSignalMixedProbability", Action("settlement", LitigGameDecisions.DLiabilitySignal, 3).Probability);
            Number("FileMixedProbability", Action("settlement", LitigGameDecisions.PFile, 1).Probability);
            Number("NoFileMixedProbability", Action("settlement", LitigGameDecisions.PFile, 2).Probability);
            Utility("FileMainUtility", "trial", LitigGameDecisions.PFile, 1);
            Utility("NoFileMainUtility", "trial", LitigGameDecisions.PFile, 2);
            Utility("FileMixedUtility", "settlement", LitigGameDecisions.PFile, 1);
            Utility("NoFileMixedUtility", "settlement", LitigGameDecisions.PFile, 2);
            Utility("AnswerUtility", "trial", LitigGameDecisions.DAnswer, 1);
            Utility("NoAnswerUtility", "trial", LitigGameDecisions.DAnswer, 2);
            Utility("DemandMainUtility", "trial", LitigGameDecisions.POffer, 8);
            Utility("DemandLowDeviationUtility", "trial", LitigGameDecisions.POffer, 1);
            Utility("DemandMixedUtility", "settlement", LitigGameDecisions.POffer, 1);
            Utility("OfferUtility", "trial", LitigGameDecisions.DOffer, 1);
            Utility("OfferHighUtility", "trial", LitigGameDecisions.DOffer, 8);
            Number("DemandMainValue", double.Parse(Action("trial", LitigGameDecisions.POffer, 8).Label, CultureInfo.InvariantCulture), "0.00");
            Number("DemandLowValue", double.Parse(Action("settlement", LitigGameDecisions.POffer, 1).Label, CultureInfo.InvariantCulture), "0.00");
            Number("OfferValue", double.Parse(Action("trial", LitigGameDecisions.DOffer, 1).Label, CultureInfo.InvariantCulture), "0.00");
            Pair("NoFilingPayoff", Outcome("no-filing"));
            Pair("NoAnswerPayoff", Outcome("no-answer-deviation"));
            Pair("SettlementPayoff", Outcome("settlement"));
            Pair("LowDeviationPayoff", Outcome("lower-demand-deviation"));
            Pair("HighDeviationPayoff", Outcome("higher-offer-deviation"));
            Pair("TrialWinPayoff", Outcome("trial", 2));
            Pair("TrialLosePayoff", Outcome("trial", 1));
            Number("CourtWinProbability", Outcome("trial", 2).ConditionalProbability);
            Number("CourtLoseProbability", Outcome("trial", 1).ConditionalProbability);
            Number("TrialTransfer", Outcome("trial", 2).TransferToPlaintiff, "0.00");
            Number("TrialPCost", Outcome("trial", 2).PlaintiffNetLitigationExpense, "0.00");
            Number("TrialDCost", Outcome("trial", 2).DefendantNetLitigationExpense, "0.00");
            var latex = new StringBuilder("% Generated numerical bindings. Do not edit these values by hand.\n");
            foreach (var macro in macros)
                latex.AppendLine($@"\newcommand{{\{macro.Key}}}{{{macro.Value}}}");
            return latex.ToString().ReplaceLineEndings("\n");
        }

    }
}

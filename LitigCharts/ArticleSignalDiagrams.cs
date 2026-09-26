using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.DiscreteProbabilities;
using MathNet.Numerics.Integration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LitigCharts;

/// <summary>Probability-flow illustrations, independent of equilibrium solving and legacy chart flags.</summary>
public static class ArticleSignalDiagrams
{
    public static readonly string[] DefaultSpecifications = ["Baseline", "TruthConditionedLatentMerits", "DirectBinaryStateSignals"];
    public sealed record Panel(string Title, string SourceTitle, string DestinationTitle,
        string[] Sources, string[] Destinations, double[][] JointMass, int Highlight)
    {
        public double HighlightedSourceMass => JointMass[Highlight].Sum();
        public double[] ConditionalDestinationProbabilities => JointMass[Highlight]
            .Select(mass => mass / HighlightedSourceMass).ToArray();
    }
    public sealed record Diagram(string FileStem, string Latex, string Description, Panel[] Panels);

    public static string Stem(string specification, bool monochrome) =>
        (specification switch
        {
            "Baseline" => "Continuous merits",
            "CenterWeightedContinuousMerits" => "Center-weighted merits",
            "PolarizedContinuousMerits" => "Polarized merits",
            "TruthConditionedLatentMerits" => "Truth-conditioned merits",
            "DirectBinaryStateSignals" => "Direct binary signals",
            "Damages" => "Damages signals",
            _ => "Continuous merits - " + specification
        }) + (monochrome ? " - bw" : " - color");

    public static string[] Stems(string specification, bool monochrome) =>
        (specification == "Damages" ? new[] { "party" } :
         specification == "DirectBinaryStateSignals" ? new[] { "party", "court" } :
         new[] { "truth", "party", "court" })
        .Select(relationship => Stem(specification, monochrome).Replace(
            monochrome ? " - bw" : " - color", " - " + relationship + (monochrome ? " - bw" : " - color"))).ToArray();

    public static string[] InverseStems(string specification, bool monochrome) =>
        [Stem(specification, monochrome).Replace(monochrome ? " - bw" : " - color",
            " - party inverse" + (monochrome ? " - bw" : " - color"))];

    public static string[] PartyToPartyStems(string specification, bool monochrome) =>
        [Stem(specification, monochrome).Replace(monochrome ? " - bw" : " - color",
            " - party to party" + (monochrome ? " - bw" : " - color"))];

    public static Diagram[] GeneratePartyToParty(string specification, bool monochrome) => GeneratePartyToParty(specification, monochrome, null);

    public static Diagram[] GeneratePartyToParty(string specification, bool monochrome, LitigGameOptions explicitOptions)
    {
        var options = explicitOptions ?? ArticleWorkedPathExtraction.CreateOptions("Specification-" + specification + "__Cost-1__Fee-American");
        var definition = new LitigGameDefinition();
        definition.Setup(options); // Signal beliefs only; no strategies or equilibrium selection.
        var generator = options.LitigGameDisputeGenerator;
        string structure = generator switch
        {
            LitigGameUniformQualityDisputeGenerator continuous =>
                $"Q~{LitigGameUniformQualityDisputeGenerator.GetDistributionLabel(continuous.QualityDistribution)}; T|Q=q~Bernoulli(q). Signals are independent conditional on exact Q. The production belief calculation integrates the product of both signal kernels over Q, not the product of averages within display intervals.",
            LitigGameExogenousDisputeGenerator latent =>
                $"Truth -> discrete merits -> signals; truth prior P(T=1)={latent.ExogenousProbabilityTrulyLiable:G17}; truth-to-merits sigma={latent.StdevNoiseToProduceLiabilityStrength:G17}; {options.NumLiabilityStrengthPoints} midpoint merits levels. Signals are independent conditional on the exact merits level, not merely on truth.",
            LitigGameExogenousDirectSignalDisputeGenerator binary =>
                $"Truth -> signals directly; truth prior P(T=1)={binary.ExogenousProbabilityTrulyLiable:G17}. Signals are independent conditional on binary truth, at source locations 0 and 1. Calibrated noise is read from the production option set. There is no shared intermediate merits variable.",
            _ => throw new NotSupportedException("Unsupported party-to-party signal structure.")
        };
        double[] prior = generator.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
        // The simulation's one-based signal codes are not the displayed midpoint values.
        // Use the full production joint distribution, without coarse Q-bin independence.
        double[][] joint = prior.Select((mass, i) => generator
            .BayesianCalculations_GetDLiabilitySignalProbabilities(checked((byte)(i + 1)))
            .Select(p => mass * p).ToArray()).ToArray();
        string[] Labels(int count) => Enumerable.Range(0, count).Select(i => F((i + .5) / count)).ToArray();
        var panel = new Panel("Other-party signal distribution given one party's signal",
            $"Plaintiff signal (sigma={F(options.PLiabilityNoiseStdev)})",
            $"Defendant signal (sigma={F(options.DLiabilityNoiseStdev)})",
            Labels(prior.Length), Labels(options.DLiabilitySignalParameters.NumSignals), joint, (prior.Length - 1) / 2);
        Validate(panel);
        double[] unconditional = Enumerable.Range(0, panel.Destinations.Length).Select(j => joint.Sum(row => row[j])).ToArray();
        int[] middle = Enumerable.Range(0, panel.Destinations.Length)
            .Where(j => (j + .5) / panel.Destinations.Length >= .4 && (j + .5) / panel.Destinations.Length < .6).ToArray();
        string description = $"Specification={options.Name}; plaintiff sigma={options.PLiabilityNoiseStdev:G17}; defendant sigma={options.DLiabilityNoiseStdev:G17}; boundary={options.PLiabilitySignalParameters.SignalBoundaryMode}. " + structure +
            "\n\nParty-to-party prediction: J[i,j] = P(S_P=i) P(S_D=j | S_P=i), using the production Bayesian signal methods. " +
            "Ribbon widths are joint masses, not normalized conditional probabilities. The reference fan starts at plaintiff signal " +
            panel.Sources[panel.Highlight] + "; labels denote signal-bin midpoints, not exact continuous observations. " +
            "The right column shows possible defendant signals, not an opponent signal observed by the plaintiff. " +
            "This is Bayesian prediction, not a causal effect of one signal on the other. " +
            "The probabilities below are the plaintiff's belief before any procedural selection: NOT conditioned on filing, answering, offers, or trial. " +
            "With equal party noise the same relationship applies in reverse; the adjacent upper-middle signal is the reflected case in these symmetric specifications. " +
            "No solver, equilibrium file, action utility, or strategic mixing is involved. Both palettes have identical data. " + PaletteDescription(monochrome) + "\n\n" +
            DescribeConditionalProbabilities(panel) +
            "\nUnconditional defendant signal probabilities, bottom to top:\n" +
            string.Join("\n", panel.Destinations.Zip(unconditional, (label, p) => $"{label}: {p.ToString("G17", CultureInfo.InvariantCulture)}")) +
            $"\n\nMiddle defendant signal range [0.40,0.60), combining bins labeled {string.Join(", ", middle.Select(j => panel.Destinations[j]))}:\n" +
            $"Unconditional mass: {middle.Sum(j => unconditional[j]).ToString("G17", CultureInfo.InvariantCulture)}\n" +
            $"Conditional on reference plaintiff signal: {middle.Sum(j => panel.ConditionalDestinationProbabilities[j]).ToString("G17", CultureInfo.InvariantCulture)}\n" +
            "These comparisons use each model's production calibration, not identical noise standard deviations. " +
            "In the symmetric, conditionally independent direct-binary benchmark, this symmetric middle range has equal likelihood under both truth states; " +
            "learning one's own signal therefore does not change its probability for the opponent. It can still change which side of the distribution the opponent is likely to occupy. " +
            "This is a limitation of that information structure, not of every model with binary truth.\n";
        return [new(PartyToPartyStems(specification, monochrome)[0], Render([panel], monochrome), description, [panel])];
    }

    public static Diagram[] GenerateInverse(string specification, bool monochrome) => GenerateInverse(specification, monochrome, null);

    public static Diagram[] GenerateInverse(string specification, bool monochrome, LitigGameOptions explicitOptions)
    {
        var forward = Generate(specification, monochrome, explicitOptions).Single(d => d.Panels[0].DestinationTitle.StartsWith("Party signal", StringComparison.Ordinal));
        var inverse = ReversePartyPanel(forward.Panels[0]);
        string modelDescription = forward.Description.Split(" Ribbon width denotes", StringSplitOptions.None)[0];
        string description = modelDescription + "\n\nInverse party-signal diagram: the same joint distribution is transposed, " +
            "not a forward noise kernel read backwards without its prior. Source buckets are party signals; destination buckets are merits " +
            "or true liability. Ribbon widths remain joint probability masses. The reference lower-middle signal bucket is " +
            inverse.Sources[inverse.Highlight] + "; this label denotes a bin midpoint, not an exact continuous observation. " +
            "The companion conditional probabilities are posterior destination probabilities given that one signal bin. " +
            "They are NOT conditioned on filing, answering, trial, or the other party's signal. " +
            "The adjacent upper-middle signal is its mirror image in these symmetric specifications. " +
            "Continuous merits retain the same five display intervals as the forward diagram. For truth-conditioned discrete merits, " +
            "the ten point masses are grouped into those same five intervals for comparison; this does not make the model continuous. " +
            "Direct-binary signals have true liability, not a latent merits variable, on the destination axis. " +
            "No equilibrium or solver is used. Explanations and conditional probabilities are intentionally outside the graphic. " + PaletteDescription(monochrome) + "\n\n" +
            DescribeConditionalProbabilities(inverse);
        return [new(InverseStems(specification, monochrome)[0], Render([inverse], monochrome), description, [inverse])];
    }

    public static Panel ReversePartyPanel(Panel forward)
    {
        Validate(forward);
        if (!forward.DestinationTitle.StartsWith("Party signal", StringComparison.Ordinal))
            throw new ArgumentException("The inverse source must be a party-signal distribution.");
        double[][] joint = forward.JointMass;
        string[] sourceLabels = forward.Sources;
        string sourceTitle = forward.SourceTitle;
        // Use identical display bins across continuous and truth-conditioned discrete merits.
        // Sum point masses before conditioning; never smooth or invent latent merits for binary truth.
        if (sourceTitle.Contains("merits", StringComparison.OrdinalIgnoreCase) &&
            sourceLabels.All(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            double[] merits = sourceLabels.Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (merits.Any(q => q < 0 || q > 1)) throw new InvalidOperationException("Merits lie outside [0,1].");
            joint = Enumerable.Range(0, 5).Select(bin => Enumerable.Range(0, forward.Destinations.Length)
                .Select(j => Enumerable.Range(0, merits.Length).Where(i => Math.Min(4, (int)(merits[i] * 5)) == bin)
                    .Sum(i => forward.JointMass[i][j])).ToArray()).ToArray();
            sourceLabels = Enumerable.Range(0, 5).Select(i => $"{F(i / 5.0)}--{F((i + 1) / 5.0)}").ToArray();
            sourceTitle = "Merits group Q";
        }
        var transpose = Enumerable.Range(0, forward.Destinations.Length)
            .Select(j => joint.Select(row => row[j]).ToArray()).ToArray();
        var inverse = new Panel("Posterior source distribution given a party signal", forward.DestinationTitle, sourceTitle,
            forward.Destinations, sourceLabels, transpose, (forward.Destinations.Length - 1) / 2);
        Validate(inverse);
        return inverse;
    }

    public static Diagram[] Generate(string specification, bool monochrome) => Generate(specification, monochrome, null);

    public static Diagram[] Generate(string specification, bool monochrome, LitigGameOptions explicitOptions)
    {
        Panel[] panels;
        string description;
        if (specification == "Damages")
        {
            double[] values = Enumerable.Range(0, 10).Select(i => (i + .5) / 10).ToArray();
            var kernel = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = 10, NumSignals = 10,
                StdevOfNormalDistribution = .2, SourcePointsIncludeExtremes = false
            };
            panels = [new Panel("Damages extension: a noisy assessment of damages", "Damages strength", "Party signal",
                values.Select(F).ToArray(), values.Select(F).ToArray(),
                values.Select(q => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, kernel).Select(p => p / 10).ToArray()).ToArray(), 4)];
            description = "Illustrative legacy damages extension: uniform prior on ten discrete midpoint damages levels; ten party signal bins; sigma=0.2. Not a current continuous-merits experiment. No equilibrium is asserted.";
        }
        else
        {
            var options = explicitOptions ?? ArticleWorkedPathExtraction.CreateOptions("Specification-" + specification + "__Cost-1__Fee-American");
            var definition = new LitigGameDefinition();
            definition.Setup(options); // Initializes production signal kernels, not strategies or equilibria.
            if (options.PLiabilityNoiseStdev != options.DLiabilityNoiseStdev)
                throw new NotSupportedException("The combined party panel requires equal party noise.");
            var party = options.PLiabilitySignalParameters;
            var court = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = options.NumLiabilityStrengthPoints,
                NumSignals = options.NumCourtLiabilitySignals,
                StdevOfNormalDistribution = options.CourtLiabilityNoiseStdev,
                SignalBoundaryMode = party.SignalBoundaryMode
            };
            string[] signalLabels = Enumerable.Range(0, party.NumSignals).Select(i => F((i + .5) / party.NumSignals)).ToArray();
            string[] courtLabels = ["Not liable", "Liable"];
            if (court.NumSignals != 2) throw new NotSupportedException("Article court panel requires two findings.");
            string partyTitle = $"Party signal (sigma={F(options.PLiabilityNoiseStdev)})";
            string courtTitle = $"Court finding (sigma={F(options.CourtLiabilityNoiseStdev)})";
            if (options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator continuous)
            {
                var p = ContinuousMass(continuous.QualityDistribution, q => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, party));
                var c = ContinuousMass(continuous.QualityDistribution, q => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, court));
                var t = ContinuousMass(continuous.QualityDistribution, q => [1 - q, q]);
                string[] intervals = Enumerable.Range(0, 5).Select(i => $"{F(i / 5.0)}--{F((i + 1) / 5.0)}").ToArray();
                panels =
                [
                    new("Merits and true liability", "Merits interval Q", "True liability T", intervals, courtLabels, t, 3),
                    new("The same merits generate each party's signal", "Merits interval Q", partyTitle, intervals, signalLabels, p, 3),
                    new("A court finding is not true liability", "Merits interval Q", courtTitle, intervals, courtLabels, c, 3)
                ];
                description = $"Specification={options.Name}; Q~{LitigGameUniformQualityDisputeGenerator.GetDistributionLabel(continuous.QualityDistribution)}; T|Q=q~Bernoulli(q). Party sigma={options.PLiabilityNoiseStdev:G17}; court sigma={options.CourtLiabilityNoiseStdev:G17}; boundary={party.SignalBoundaryMode}. Five merits intervals are display groups ONLY, not model states or quadrature nodes. Flows integrate the production signal kernel over each interval with 64-point Gauss-Legendre integration (theta transform for Beta(0.5,0.5)). Party/court signals are independent conditional on exact Q, not generally conditional on a displayed interval. The court signal is not observed by litigants before bargaining. Panels show marginal joint distributions, not a sequence of observed events.";
            }
            else
            {
                double[] values, prior;
                Panel truth = null;
                if (options.LitigGameDisputeGenerator is LitigGameExogenousDisputeGenerator latent)
                {
                    values = Enumerable.Range(0, options.NumLiabilityStrengthPoints).Select(i => (i + .5) / options.NumLiabilityStrengthPoints).ToArray();
                    var truthKernel = new DiscreteValueSignalParameters
                    {
                        NumPointsInSourceUniformDistribution = 2, NumSignals = values.Length,
                        StdevOfNormalDistribution = latent.StdevNoiseToProduceLiabilityStrength, SourcePointsIncludeExtremes = true
                    };
                    double[][] tm = new[] { 0.0, 1.0 }.Select((t, i) =>
                        DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(t, truthKernel)
                        .Select(x => x * (i == 0 ? 1 - latent.ExogenousProbabilityTrulyLiable : latent.ExogenousProbabilityTrulyLiable)).ToArray()).ToArray();
                    prior = Enumerable.Range(0, values.Length).Select(j => tm.Sum(row => row[j])).ToArray();
                    truth = new Panel("Truth-conditioned discrete merits", "True liability T", "Merits Q", courtLabels, values.Select(F).ToArray(), tm, 1);
                }
                else if (options.LitigGameDisputeGenerator is LitigGameExogenousDirectSignalDisputeGenerator binary)
                {
                    values = [0, 1];
                    prior = [1 - binary.ExogenousProbabilityTrulyLiable, binary.ExogenousProbabilityTrulyLiable];
                }
                else throw new NotSupportedException("Unsupported signal structure.");
                string[] labels = truth == null ? courtLabels : values.Select(F).ToArray();
                string sourceTitle = truth == null ? "True liability T" : "Discrete merits Q";
                double[][] Mass(DiscreteValueSignalParameters k) => values.Select((q, i) =>
                    DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, k).Select(p => p * prior[i]).ToArray()).ToArray();
                var list = new List<Panel>();
                if (truth != null) list.Add(truth);
                list.Add(new Panel("Party information", sourceTitle, partyTitle, labels, signalLabels, Mass(party), truth == null ? 1 : 4));
                list.Add(new Panel("Court information", sourceTitle, courtTitle, labels, courtLabels, Mass(court), truth == null ? 1 : 4));
                panels = list.ToArray();
                description = $"Specification={options.Name}; party sigma={options.PLiabilityNoiseStdev:G17}; court sigma={options.CourtLiabilityNoiseStdev:G17}; boundary={party.SignalBoundaryMode}. " +
                    (truth == null ? "Signals are generated directly from binary truth, at source locations 0 and 1; calibrated noise is read from the production option set." :
                    "Truth prior and truth-to-merits sigma are read from the production generator; ten discrete midpoint merits levels. Signals are independent conditional on the exact merits level.") +
                    " Each panel shows a marginal probability flow, not strategic play or a sequence of observed events.";
            }
        }
        foreach (var p in panels) Validate(p);
        description += " Ribbon width denotes joint probability mass, not conditional probability. One reference source group's conditional destination probabilities are listed in this companion file and in JSON, not inside the diagram. They sum to one before display rounding. For continuous merits they condition on the entire reference interval, with the prior integrated over that interval, not on its midpoint or on a single exact Q. Bucket labels for party signals are bin midpoints, not point events. The accompanying JSON also contains unrounded joint masses. Crossings imply no extra probability. Q and T are italic mathematical variables; the Greek symbol sigma denotes the noise standard deviation. Color and grayscale share identical data and ribbon coordinates. No transparency, gradients, equilibrium loading, or solver call. " + PaletteDescription(monochrome);
        string[] stems = Stems(specification, monochrome);
        return panels.Select((panel, i) => new Diagram(stems[i], Render([panel], monochrome),
            description + " This standalone file depicts: " + panel.Title + ".\n\n" + DescribeConditionalProbabilities(panel), [panel])).ToArray();
    }

    private static string DescribeConditionalProbabilities(Panel panel) =>
        $"Reference source (highlighted in both palettes): {panel.Sources[panel.Highlight]}; source probability mass: {panel.HighlightedSourceMass.ToString("G17", CultureInfo.InvariantCulture)}.\n" +
        "Conditional destination probabilities, bottom to top (destination bucket: probability):\n" +
        string.Join("\n", panel.Destinations.Zip(panel.ConditionalDestinationProbabilities,
            (label, probability) => $"{label}: {probability.ToString("G17", CultureInfo.InvariantCulture)}")) +
        "\nEach value equals the corresponding ribbon's joint probability mass divided by the reference source mass.\n";

    private static string PaletteDescription(bool monochrome) => monochrome
        ? "Grayscale: all background ribbons have the same pale-gray fill, regardless of signal strength or source range. The reference fan alone uses darker gray and is drawn last. Gray shade indicates highlighting, not signal strength. No ribbons have outlines; their widths are set by probability mass without an added border stroke. Only endpoint boxes retain black borders. The JSON Highlight index identifies the fan and the companion's reference calculation."
        : "Color: lower-half source buckets are orange and upper-half buckets are blue (a bucket centered exactly at the midpoint joins the upper half). The reference fan retains its source hue at greater saturation and is drawn last. Other ribbons have pale fills. No ribbons have outlines; their widths are set by probability mass without an added border stroke. Only endpoint boxes retain black borders. Colors identify the source bucket, not the destination.";

    public static double[][] ContinuousMass(ContinuousQualityDistribution distribution, Func<double, double[]> kernel, int order = 64)
    {
        var rows = new List<double[]>();
        for (int bin = 0; bin < 5; bin++)
        {
            double low = bin / 5.0, high = (bin + 1) / 5.0;
            bool transformed = distribution == ContinuousQualityDistribution.BetaHalfHalf;
            var rule = new GaussLegendreRule(transformed ? Math.Asin(Math.Sqrt(low)) : low,
                transformed ? Math.Asin(Math.Sqrt(high)) : high, order);
            double[] row = null;
            for (int k = 0; k < rule.Abscissas.Length; k++)
            {
                double x = rule.Abscissas[k], q = transformed ? Math.Pow(Math.Sin(x), 2) : x;
                double density = distribution switch
                {
                    ContinuousQualityDistribution.Uniform => 1,
                    ContinuousQualityDistribution.BetaTwoTwo => 6 * q * (1 - q),
                    ContinuousQualityDistribution.BetaHalfHalf => 2 / Math.PI,
                    _ => throw new NotSupportedException()
                };
                double[] probabilities = kernel(q);
                row ??= new double[probabilities.Length];
                for (int j = 0; j < row.Length; j++) row[j] += rule.Weights[k] * density * probabilities[j];
            }
            rows.Add(row);
        }
        return rows.ToArray();
    }

    public static void Validate(Panel panel)
    {
        if (panel.JointMass.Length != panel.Sources.Length ||
            panel.JointMass.Any(r => r.Length != panel.Destinations.Length || r.Any(v => !double.IsFinite(v) || v < 0)) ||
            panel.Highlight < 0 || panel.Highlight >= panel.JointMass.Length || panel.HighlightedSourceMass <= 0 ||
            Math.Abs(panel.JointMass.Sum(r => r.Sum()) - 1) > 1e-10)
            throw new InvalidOperationException("Invalid probability-flow matrix: " + panel.Title);
    }

    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    private static string MathHeading(string heading)
    {
        string variables = Regex.Replace(heading, @"\b(Q|T)\b", match => "$" + match.Value + "$");
        return Regex.Replace(variables, @"\bsigma=([0-9.]+)", match => @"$\sigma=" + match.Groups[1].Value + "$");
    }
    private static string Render(Panel[] panels, bool bw)
    {
        var b = new StringBuilder("""
            % Generated by ArticleSignalDiagrams. Opaque vector fills only.
            \documentclass[10pt,tikz,border=5pt]{standalone}
            \usepackage{lmodern}
            \usetikzlibrary{fit}
            \begin{document}
            \begin{tikzpicture}[x=1cm,y=1cm,font=\small]
            """);
        for (int page = 0; page < panels.Length; page++)
        {
            Panel p = panels[page];
            double top = -page * 7.4;
            double[] left = p.JointMass.Select(r => r.Sum()).ToArray();
            double[] right = Enumerable.Range(0, p.Destinations.Length).Select(j => p.JointMass.Sum(r => r[j])).ToArray();
            const double massHeight = 4.2, totalHeight = 5.1, x1 = 3.3, x2 = 11.3;
            double[] Bottoms(double[] masses)
            {
                double y = top - totalHeight;
                var positions = new double[masses.Length];
                for (int i = 0; i < masses.Length; i++)
                {
                    positions[i] = y; y += massHeight * masses[i] + (totalHeight - massHeight) / Math.Max(1, masses.Length - 1);
                }
                return positions;
            }
            double[] lb = Bottoms(left), rb = Bottoms(right), la = new double[left.Length], ra = new double[right.Length];
            var ribbons = new List<(bool highlight, string tex)>();
            for (int i = 0; i < left.Length; i++)
                for (int j = 0; j < right.Length; j++)
                {
                    double h = p.JointMass[i][j] * massHeight;
                    double a = lb[i] + la[i], z = rb[j] + ra[j];
                    la[i] += h; ra[j] += h;
                    // Both palettes highlight one fan. Monochrome shade depends only on
                    // highlight status, never on signal strength or the side of the midpoint.
                    bool selected = i == p.Highlight;
                    bool lowerHalf = (i + .5) / left.Length < .5;
                    string hue = lowerHalf ? "orange" : "blue";
                    string fill = bw ? (selected ? "black!55" : "black!12") : selected ? hue + "!50!white" :
                        hue + (lowerHalf ? "!16!white" : "!12!white");
                    // No ribbon strokes: borders inflate thin flows, and white strokes
                    // cut into earlier ribbons at crossings. Emphasize the fan by fill only.
                    string style = $"fill={fill},draw=none";
                    ribbons.Add((selected, $@"\path[{style}] ({N(x1)},{N(a)}) .. controls (6,{N(a)}) and (8.6,{N(z)}) .. ({N(x2)},{N(z)}) -- ({N(x2)},{N(z + h)}) .. controls (8.6,{N(z + h)}) and (6,{N(a + h)}) .. ({N(x1)},{N(a + h)}) -- cycle;"));
                }
            foreach (var ribbon in ribbons.OrderBy(r => r.highlight)) b.AppendLine(ribbon.tex);
            b.AppendLine($@"\coordinate (axis-midline-{page}) at (0,{N(top - totalHeight / 2)});");
            for (int side = 0; side < 2; side++)
            {
                var labels = side == 0 ? p.Sources : p.Destinations;
                var masses = side == 0 ? left : right;
                var bottoms = side == 0 ? lb : rb;
                double x = side == 0 ? x1 : x2;
                for (int i = 0; i < labels.Length; i++)
                {
                    b.AppendLine($@"\path[fill=white,draw=black,line width=0.3pt] ({N(x - .08)},{N(bottoms[i])}) rectangle ({N(x + .08)},{N(bottoms[i] + massHeight * masses[i])});");
                    b.AppendLine($@"\node[anchor={(side == 0 ? "east" : "west")},fill=white,inner sep=2pt] (bucket-{page}-{side}-{i}) at ({N(x + (side == 0 ? -.18 : .18))},{N(bottoms[i] + massHeight * masses[i] / 2)}) {{{labels[i]}}};");
                }
                // Let TeX measure the label column, including unequal-width labels such as
                // Liable/Not liable. Put the vertical axis title just outside that column.
                string columnName = $"{(side == 0 ? "source" : "destination")}-labels-{page}";
                string fit = string.Join(" ", Enumerable.Range(0, labels.Length).Select(i => $"(bucket-{page}-{side}-{i})"));
                b.AppendLine($@"\node[fit={fit},inner sep=0pt] ({columnName}) {{}};");
                b.AppendLine($@"\coordinate (axis-title-{page}-{side}) at ({columnName}.{(side == 0 ? "west" : "east")} |- axis-midline-{page});");
                b.AppendLine($@"\node[rotate=90,anchor={(side == 0 ? "south" : "north")},inner sep=0pt] at ([xshift={(side == 0 ? "-" : "")}0.18cm]axis-title-{page}-{side}) {{{MathHeading(side == 0 ? p.SourceTitle : p.DestinationTitle)}}};");
            }
        }
        b.AppendLine("\\end{tikzpicture}\n\\end{document}\n");
        return b.ToString();
    }
}

using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Explicit, saved-data-only article workflow. Never invokes a solver or the legacy runner.</summary>
public static class ArticleDiagramCommand
{
    public sealed record AdditionalDispositionFigure(string Request, string Output);
    public sealed record Configuration
    {
        public bool UseArticleResultsLayout { get; init; }
        public string GameTreesDirectory { get; init; }
        public string WorkedPathRequest { get; init; }
        public string PublicationFiguresDirectory { get; init; }
        public string PublicationFiguresRequest { get; init; }
        public string WelfareExhibitsRequest { get; init; }
        public AdditionalDispositionFigure[] AdditionalDispositionFigures { get; init; } = [];
        public string IndividualResultsDirectory { get; init; }
        public string MultipleEquilibriaDirectory { get; init; }
        public string AggregateDirectory { get; init; }
        public string AggregateSourceCsv { get; init; }
        public string EndogenousDirectory { get; init; }
        public string SignalsDirectory { get; init; }
        public string DamagesSignalsDirectory { get; init; }
        public string[] SignalSpecifications { get; init; } = ArticleSignalDiagrams.DefaultSpecifications;
        public int? ExpectedIndividualCount { get; init; }
        public int? ExpectedMultipleEquilibriaCount { get; init; }
        public int? ExpectedAggregateCount { get; init; }
        public string LatexExecutable { get; init; } = "lualatex";
        public string PreviewExecutable { get; init; } = "pdftoppm";
        public int MaxParallelCompilers { get; init; } = 4;
        public int ProcessTimeoutSeconds { get; init; } = 180;
    }

    public static readonly string[] TreeStems =
    [
        "game tree 2x2x2", "game tree 2x2x2 beginning", "game tree 2x2x2 end",
        "game tree 2x2x2 simplified", "game tree 2x2x2 end simplified"
    ];
    public static string[] ExpandTarget(string target) => target switch
    {
        "all" => ["game-trees", "worked-path", "signals", "inverse-signals", "party-to-party", "selection-offers", "dispositions", "individual-results", "multiple-equilibria", "aggregates", "welfare-outcomes"],
        "publication" => ["selection-offers", "dispositions", "welfare-outcomes"],
        "results" => ["individual-results", "aggregates", "welfare-outcomes"],
        "game-trees" or "worked-path" or "worked-path-data" or "individual-results"
            or "multiple-equilibria" or "aggregates" or "endogenous" or "signals" or "inverse-signals" or "party-to-party" or "damages-signals"
            or "selection-offers" or "dispositions" or "welfare-outcomes" => [target],
        _ => throw new ArgumentException("Unknown diagram target: " + target)
    };

    public const string Help = """
        LitigCharts diagrams <target> --config <article-diagrams.json> [options]
        Targets: all, results, game-trees, worked-path, worked-path-data,
                 signals, inverse-signals, party-to-party, damages-signals, publication, selection-offers, dispositions, welfare-outcomes,
                 individual-results, multiple-equilibria, aggregates, endogenous
        Options:
          --list             Validate required input paths and show counts; write nothing.
          --sources-only     Generate sources/data without compiling PDFs.
          --compile-only     Compile existing .tex; do not extract or regenerate sources.
          --output-root DIR  Put outputs in separate group subfolders for review.
          --jobs N           Maximum concurrent compilers (default from config: 4).
        Paths inside the JSON configuration are relative to that file.
        'all' excludes endogenous-disputes and damages-extension diagrams.
        Individual/ME diagrams use saved report-generated .tex; aggregates use saved CSV.
        No command solves equilibria, changes production flags, or reorganizes/deletes results.
        No arguments prints this help (the legacy report runner is not invoked).
        """;

    private sealed record Job(string Target, string Source, string Output);

    public static string ResolvePath(string configFile, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("A path required by the selected target is missing from the configuration.");
        return Path.GetFullPath(configuredPath, Path.GetDirectoryName(Path.GetFullPath(configFile)));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0 || args.SequenceEqual(new[] { "--help" }) ||
                args.SequenceEqual(new[] { "diagrams", "--help" }))
            {
                Console.WriteLine(Help);
                return 0;
            }
            if (args.Length < 2 || args[0] != "diagrams")
                throw new ArgumentException("Expected 'diagrams <target>'. Use --help.");
            string[] targets = ExpandTarget(args[1]);
            string configFile = null, outputRoot = null;
            bool list = false, sourcesOnly = false, compileOnly = false;
            int? jobsOverride = null;
            var seen = new HashSet<string>();
            for (int i = 2; i < args.Length; i++)
            {
                string flag = args[i];
                if (!seen.Add(flag)) throw new ArgumentException("Repeated option: " + flag);
                string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                    ? args[i] : throw new ArgumentException("Missing value for " + flag);
                switch (flag)
                {
                    case "--config": configFile = Path.GetFullPath(Value()); break;
                    case "--output-root": outputRoot = Path.GetFullPath(Value()); break;
                    case "--jobs": jobsOverride = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--list": list = true; break;
                    case "--sources-only": sourcesOnly = true; break;
                    case "--compile-only": compileOnly = true; break;
                    default: throw new ArgumentException("Unknown option: " + flag);
                }
            }
            if (sourcesOnly && compileOnly) throw new ArgumentException("Choose sources-only or compile-only, not both.");
            if (compileOnly && targets.Contains("worked-path-data"))
                throw new ArgumentException("worked-path-data has no compilation step.");
            RequireFile(configFile);
            var config = JsonSerializer.Deserialize<Configuration>(await File.ReadAllTextAsync(configFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
                ?? throw new ArgumentException("Empty configuration.");
            int parallelism = jobsOverride ?? config.MaxParallelCompilers;
            if (parallelism < 1 || parallelism > 64 || config.ProcessTimeoutSeconds < 1)
                throw new ArgumentException("Jobs must be 1–64 and process timeout must be positive.");

            string Resolve(string p) => ResolvePath(configFile, p);
            if (config.UseArticleResultsLayout && targets.Any(ArticleResultsCommand.Targets.Contains))
            {
                await ArticleResultsCommand.RunAsync(Resolve(config.WelfareExhibitsRequest), targets, config,
                    parallelism, list, sourcesOnly, compileOnly, outputRoot);
                targets = targets.Where(t => !ArticleResultsCommand.Targets.Contains(t)).ToArray();
                if (targets.Length == 0) return 0;
            }
            string Output(string configured, string group) => outputRoot == null ? Resolve(configured) : Path.Combine(outputRoot, group);
            var planned = new List<Job>();
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string requestFile = null, extractionFile = null;
            WelfareOutcomeExhibits.Generation welfare = null;
            foreach (string target in targets)
            {
                // A configured welfare request replaces the old fixed disposition selection.
                // Both exhibit families are generated together, each in a separate file.
                if (target == "welfare-outcomes" || target == "dispositions" && !string.IsNullOrWhiteSpace(config.WelfareExhibitsRequest))
                {
                    if (target == "welfare-outcomes" && args[1] == "welfare-outcomes" && string.IsNullOrWhiteSpace(config.WelfareExhibitsRequest))
                        throw new ArgumentException("welfare-outcomes requires WelfareExhibitsRequest in the configuration.");
                    continue;
                }
                string inputDir, outputDir;
                string[] files;
                int? expected = null;
                switch (target)
                {
                    case "selection-offers":
                    case "dispositions":
                        inputDir = Resolve(config.PublicationFiguresDirectory);
                        outputDir = Output(config.PublicationFiguresDirectory, "Publication figures");
                        files = [Path.Combine(inputDir, PublicationFigures.Stem(target) + ".tex")];
                        if (!compileOnly)
                        {
                            string publicationRequest = Resolve(config.PublicationFiguresRequest);
                            RequireFile(publicationRequest);
                            var figure = PublicationFigures.Generate(publicationRequest, target);
                            string output = Path.Combine(outputDir, figure.Stem + ".tex");
                            sources.Add(output, figure.Latex);
                            companions.Add(Path.ChangeExtension(output, ".txt"), figure.Caption + "\n");
                            companions.Add(Path.ChangeExtension(output, ".json"),
                                JsonSerializer.Serialize(figure.Data, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                        }
                        break;
                    case "signals":
                    case "inverse-signals":
                    case "party-to-party":
                    case "damages-signals":
                        string configuredSignalDirectory = target == "damages-signals" ? config.DamagesSignalsDirectory : config.SignalsDirectory;
                        inputDir = Resolve(configuredSignalDirectory);
                        outputDir = Output(configuredSignalDirectory, target == "damages-signals" ? "Damages signals" : "Signal diagrams");
                        string[] specifications = target == "damages-signals" ? ["Damages"] : config.SignalSpecifications;
                        if (specifications == null || specifications.Length == 0)
                            throw new ArgumentException("At least one SignalSpecifications entry is required.");
                        files = specifications.SelectMany(s => new[] { false, true }
                            .SelectMany(bw => (target switch
                            {
                                "inverse-signals" => ArticleSignalDiagrams.InverseStems(s, bw),
                                "party-to-party" => ArticleSignalDiagrams.PartyToPartyStems(s, bw),
                                _ => ArticleSignalDiagrams.Stems(s, bw)
                            })
                                .Select(stem => Path.Combine(inputDir, stem + ".tex")))).ToArray();
                        if (!compileOnly)
                            foreach (string specification in specifications)
                                foreach (bool bw in new[] { false, true })
                                foreach (var d in target switch
                                {
                                    "inverse-signals" => ArticleSignalDiagrams.GenerateInverse(specification, bw),
                                    "party-to-party" => ArticleSignalDiagrams.GeneratePartyToParty(specification, bw),
                                    _ => ArticleSignalDiagrams.Generate(specification, bw)
                                })
                                {
                                    string output = Path.Combine(outputDir, d.FileStem + ".tex");
                                    sources.Add(output, d.Latex);
                                    companions.Add(Path.ChangeExtension(output, ".txt"), d.Description + "\n");
                                    companions.Add(Path.ChangeExtension(output, ".json"),
                                        JsonSerializer.Serialize(d.Panels, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                                }
                        break;
                    case "game-trees":
                        inputDir = Resolve(config.GameTreesDirectory);
                        outputDir = Output(config.GameTreesDirectory, "Game trees");
                        files = TreeStems.Select(s => Path.Combine(inputDir, s + ".tex")).ToArray();
                        break;
                    case "worked-path":
                    case "worked-path-data":
                        inputDir = Resolve(config.GameTreesDirectory);
                        outputDir = Output(config.GameTreesDirectory, "Game trees");
                        files = target == "worked-path-data" ? [] : [Path.Combine(inputDir, WorkedPathDiagram.FileStem + ".tex")];
                        if (!compileOnly)
                        {
                            requestFile = Resolve(config.WorkedPathRequest);
                            RequireFile(requestFile);
                            var request = JsonSerializer.Deserialize<ArticleWorkedPathExtraction.Request>(
                                await File.ReadAllTextAsync(requestFile), ArticleWorkedPathExtraction.JsonOptions)
                                ?? throw new ArgumentException("Empty extraction request.");
                            RequireFile(Path.GetFullPath(request.EquilibriumFile, Path.GetDirectoryName(requestFile)));
                            RequireFile(Path.GetFullPath(request.ActionReportFile, Path.GetDirectoryName(requestFile)));
                            extractionFile = Path.Combine(outputDir, "worked equilibrium paths.json");
                            if (string.Equals(extractionFile, requestFile, StringComparison.OrdinalIgnoreCase))
                                throw new ArgumentException("Extraction must not overwrite its request.");
                            foreach (string input in new[] { request.EquilibriumFile, request.ActionReportFile })
                            {
                                string inputDirectory = Path.GetDirectoryName(Path.GetFullPath(input, Path.GetDirectoryName(requestFile)))
                                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                                if (extractionFile.StartsWith(inputDirectory, StringComparison.OrdinalIgnoreCase))
                                    throw new ArgumentException("Write worked-path outputs outside the equilibrium/action-report input directories.");
                            }
                        }
                        break;
                    case "endogenous":
                        inputDir = Resolve(config.EndogenousDirectory);
                        outputDir = Output(config.EndogenousDirectory, "Endogenous");
                        files = [Path.Combine(inputDir, "endogenous disputes beginning.tex")];
                        break;
                    default:
                        (string directory, string group, int? count) = target switch
                        {
                            "individual-results" => (config.IndividualResultsDirectory, "Individual results", config.ExpectedIndividualCount),
                            "multiple-equilibria" => (config.MultipleEquilibriaDirectory, "Multiple equilibria", config.ExpectedMultipleEquilibriaCount),
                            _ => (config.AggregateDirectory, "Aggregates", config.ExpectedAggregateCount)
                        };
                        inputDir = Resolve(directory);
                        outputDir = Output(directory, group);
                        expected = count;
                        if (target == "aggregates" && !compileOnly)
                        {
                            string csv = Resolve(config.AggregateSourceCsv);
                            RequireFile(csv);
                            var aggregateFiles = new List<string>();
                            // Pure callback: no directory creation, compilation, or legacy cleanup.
                            FeeShiftingDataProcessing.ProduceLatexDiagramsAggregatingReports(
                                new LitigGameCorrelatedSignalsArticleLauncher(
                                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits),
                                Runner.DataBeingAnalyzed.CorrelatedSignalsArticle, csv, outputDir,
                                (path, latex) => { sources.Add(path, latex); aggregateFiles.Add(path); });
                            files = aggregateFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                            inputDir = outputDir;
                        }
                        else
                            files = Directory.GetFiles(inputDir, "*.tex", SearchOption.AllDirectories)
                                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                        if (files.Length == 0 || expected.HasValue && files.Length != expected.Value)
                            throw new InvalidDataException($"{target}: found {files.Length} diagrams; expected {expected?.ToString() ?? "at least one"}.");
                        break;
                }
                foreach (string source in files)
                {
                    if (compileOnly || target is "individual-results" or "multiple-equilibria")
                    {
                        RequireFile(source);
                        if (!(await File.ReadAllTextAsync(source)).Contains(@"\documentclass", StringComparison.Ordinal))
                            throw new InvalidDataException("Not a standalone LaTeX diagram: " + source);
                    }
                    planned.Add(new Job(target, source, Path.Combine(outputDir, Path.GetRelativePath(inputDir, source))));
                }
                Console.WriteLine($"{target}: {files.Length} diagrams{(target == "worked-path-data" ? " (JSON extraction only)" : "")} -> {outputDir}");
            }
            if (!string.IsNullOrWhiteSpace(config.WelfareExhibitsRequest) &&
                targets.Any(t => t is "welfare-outcomes" or "dispositions"))
            {
                string welfareRequest = Resolve(config.WelfareExhibitsRequest);
                RequireFile(welfareRequest);
                string originalDirectory = WelfareOutcomeExhibits.OutputDirectory(welfareRequest);
                string destination = outputRoot == null ? originalDirectory : Path.Combine(outputRoot, "Welfare outcomes and dispositions");
                if (compileOnly)
                {
                    foreach (string source in WelfareOutcomeExhibits.ExistingSources(welfareRequest))
                    {
                        RequireFile(source);
                        if (!(await File.ReadAllTextAsync(source)).Contains(@"\documentclass", StringComparison.Ordinal))
                            throw new InvalidDataException("Not a standalone LaTeX exhibit: " + source);
                        planned.Add(new Job("welfare-outcomes", source, Path.Combine(destination, Path.GetRelativePath(originalDirectory, source))));
                    }
                }
                else
                {
                    welfare = WelfareOutcomeExhibits.Prepare(welfareRequest, destination);
                    foreach (var exhibit in welfare.Exhibits)
                        planned.Add(new Job("welfare-outcomes", exhibit.TexFile, exhibit.TexFile));
                    foreach (var file in welfare.Files)
                        (file.Key.EndsWith(".tex", StringComparison.Ordinal) ? sources : companions).Add(file.Key, file.Value);
                }
                Console.WriteLine($"welfare outcomes/dispositions: {planned.Count(j => j.Target == "welfare-outcomes")} separate exhibits -> {destination}");
            }
            // Standalone extension comparisons share the publication renderer, but have independent
            // selections and destinations. Included automatically in dispositions/publication/all.
            if (targets.Contains("dispositions") && string.IsNullOrWhiteSpace(config.WelfareExhibitsRequest))
                foreach (var extra in config.AdditionalDispositionFigures ?? [])
                {
                    string source = Resolve(extra.Output);
                    if (!string.Equals(Path.GetExtension(source), ".tex", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("An additional disposition Output must end in .tex.");
                    string output = outputRoot == null ? source
                        : Path.Combine(outputRoot, "Supplemental disposition figures", Path.GetFileName(source));
                    if (compileOnly)
                    {
                        RequireFile(source);
                        if (!(await File.ReadAllTextAsync(source)).Contains(@"\documentclass", StringComparison.Ordinal))
                            throw new InvalidDataException("Not a standalone LaTeX diagram: " + source);
                    }
                    else
                    {
                        string additionalRequest = Resolve(extra.Request);
                        RequireFile(additionalRequest);
                        var figure = PublicationFigures.Generate(additionalRequest, "dispositions");
                        sources.Add(output, figure.Latex);
                        companions.Add(Path.ChangeExtension(output, ".txt"), figure.Caption + "\n");
                        companions.Add(Path.ChangeExtension(output, ".json"),
                            JsonSerializer.Serialize(figure.Data, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                    }
                    planned.Add(new Job("dispositions", source, output));
                    Console.WriteLine($"dispositions: standalone -> {output}");
                }
            if (planned.Select(j => j.Output).Distinct(StringComparer.OrdinalIgnoreCase).Count() != planned.Count)
                throw new ArgumentException("Selected targets have overlapping output paths.");
            Console.WriteLine($"Total: {planned.Count} diagrams. Mode: {(list ? "list (no writes)" : compileOnly ? "compile only" : sourcesOnly ? "sources only" : "generate and compile")}.");
            if (list) return 0;

            // Finish generation/validation before replacing any diagram sources.
            if (!compileOnly)
            {
                if (targets.Contains("game-trees"))
                    foreach (var d in await ArticleGameTreeDiagrams.GenerateAsync())
                    {
                        string path = planned.Single(j => j.Target == "game-trees" && Path.GetFileNameWithoutExtension(j.Output) == d.FileStem).Output;
                        sources.Add(path, d.Latex);
                        companions.Add(Path.ChangeExtension(path, ".txt"), d.Description + "\n");
                    }
                if (targets.Contains("endogenous"))
                {
                    var job = planned.Single(j => j.Target == "endogenous");
                    sources.Add(job.Output, await EndogenousGameTreeDiagrams.GenerateAsync());
                    companions.Add(Path.ChangeExtension(job.Output, ".txt"),
                        EndogenousGameTreeDiagrams.Description + "\n");
                }
                if (requestFile != null)
                {
                    var request = JsonSerializer.Deserialize<ArticleWorkedPathExtraction.Request>(
                        await File.ReadAllTextAsync(requestFile), ArticleWorkedPathExtraction.JsonOptions);
                    var data = await ArticleWorkedPathExtraction.ExtractAsync(request, Path.GetDirectoryName(requestFile));
                    companions.Add(extractionFile, JsonSerializer.Serialize(data, ArticleWorkedPathExtraction.JsonOptions) + "\n");
                    Console.WriteLine($"Validated {data.ValidatedActionReportRows} action-report rows; extracted {data.Paths.Length} requested histories.");
                    if (targets.Contains("worked-path"))
                        sources.Add(planned.Single(j => j.Target == "worked-path").Output, WorkedPathDiagram.Generate(data));
                }
            }
            foreach (var job in planned)
                if (!sources.ContainsKey(job.Output) && !string.Equals(job.Source, job.Output, StringComparison.OrdinalIgnoreCase))
                    sources.Add(job.Output, await File.ReadAllTextAsync(job.Source));
            foreach (var file in sources.Concat(companions))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
                await File.WriteAllTextAsync(file.Key, file.Value);
            }
            if (!sourcesOnly && planned.Count != 0)
                await DiagramCompiler.CompileAllAsync(planned.Select(j => j.Output).ToArray(), config, parallelism);
            if (welfare != null) WelfareOutcomeExhibits.WriteInventory(welfare, !sourcesOnly);
            Console.WriteLine("Completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    private static void RequireFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Required input file not found: " + (path ?? "(--config is required)"), path);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Generates the routine article collection from freshly aggregated production reports.</summary>
public static class ArticleResultsCommand
{
    public static readonly string[] Targets = ["individual-results", "aggregates", "welfare-outcomes", "dispositions", "selection-offers"];
    public sealed record Artifact(string Kind, string Source);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static async Task RunAsync(string requestPath, string[] targets, ArticleDiagramCommand.Configuration config,
        int jobs, bool list, bool sourcesOnly, bool compileOnly, string outputOverride)
    {
        requestPath = Path.GetFullPath(requestPath);
        var request = JsonSerializer.Deserialize<WelfareOutcomeExhibits.Request>(File.ReadAllText(requestPath), Json);
        string Resolve(string p) => Path.GetFullPath(p, Path.GetDirectoryName(requestPath));
        string aggregate = outputOverride == null ? Resolve(request.OutputDirectory) : Path.Combine(outputOverride, "Aggregated Data");
        if (Path.GetFileName(aggregate) != "Aggregated Data") throw new InvalidDataException("Article results require an Aggregated Data directory.");
        string root = Path.GetDirectoryName(aggregate);
        string inventory = Path.Combine(root, "Run records", "diagram-inventory.json");
        bool individual = targets.Contains("individual-results"), strategies = targets.Any(t => t is "aggregates" or "selection-offers"),
            welfare = targets.Any(t => t is "aggregates" or "welfare-outcomes" or "dispositions");
        bool Selected(Artifact a) => a.Kind switch { "individual-results" => individual, "selection-offers" => strategies, _ => welfare };
        if (compileOnly)
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(inventory));
            string previousRoot = saved.RootElement.TryGetProperty("Root", out var storedRoot) ? storedRoot.GetString() : root;
            var artifacts = saved.RootElement.GetProperty("Artifacts").Deserialize<Artifact[]>(Json).Where(Selected)
                .Select(a => a with { Source = Path.Combine(root, Path.GetRelativePath(previousRoot, a.Source)) }).ToArray();
            Console.WriteLine($"Article results: {artifacts.Length} saved diagrams to compile.");
            if (!list)
            {
                await DiagramCompiler.CompileAllAsync(artifacts.Select(a => a.Source).ToArray(), config, jobs);
                // A complete compile-only pass finishes a preceding sources-only generation.
                if (saved.RootElement.GetProperty("Artifacts").GetArrayLength() == artifacts.Length)
                {
                    var updated = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(inventory));
                    updated["Root"] = root;
                    updated["Compiled"] = true;
                    updated["Artifacts"] = JsonSerializer.SerializeToNode(artifacts, Json);
                    File.WriteAllText(inventory, updated.ToJsonString(Json));
                }
            }
            return;
        }
        // Welfare accounting validates every case before any destination is written.
        var generated = WelfareOutcomeExhibits.Prepare(requestPath, aggregate);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var artifactsToWrite = new List<Artifact>();
        var cases = new List<(Dictionary<string,string> Row, string Directory, string Prefix)>();
        foreach(var input in request.Inputs)
        foreach(var row in PublicationFigures.ReadCsv(Resolve(input.NumericalResultsCsv)).Where(r=>r["Filter"]=="All"&&r["Equilibrium Type"]=="Only Eq"))
            cases.Add((row,Resolve(input.IndividualDirectory),input.ReportPrefix));
        if(welfare)
        {
            foreach(var f in generated.Files) files.Add(f.Key,f.Value);
            artifactsToWrite.AddRange(generated.Exhibits.Select(e=>new Artifact(e.Kind,e.TexFile)));
        }
        if(individual)
        foreach(var c in cases)
        {
            string destination=ArticleResultsLayout.Individual(Path.Combine(root,"Individual simulations"),c.Row);
            string casePrefix=c.Prefix+" "+c.Row["OptionSetName"];
            var sources=Directory.GetFiles(c.Directory,casePrefix+"*").Where(p=>
                Path.GetFileName(p)==casePrefix+".csv"||Path.GetFileName(p).StartsWith(casePrefix+" -",StringComparison.Ordinal))
                .Where(p=>Path.GetExtension(p) is ".csv" or ".tex").ToArray();
            int diagrams=0;
            foreach(string source in sources)
            {
                string suffix=Path.GetFileNameWithoutExtension(source)[casePrefix.Length..].Trim().TrimStart('-');
                string stem=ArticleResultsLayout.Cost(PublicationTables.Number(c.Row,"Costs Multiplier"))+"-"+(suffix==""?"report":suffix);
                string target=ArticleResultsLayout.Source(destination,stem,Path.GetExtension(source));
                string content = File.ReadAllText(source);
                if (Path.GetExtension(source) == ".tex")
                    content = string.Join("\n", content.Replace("\r\n", "\n").Split('\n')
                        .Where(line => !line.Contains(@"node[midway] {\huge Costs:", StringComparison.Ordinal)));
                files.Add(target,content);
                if(Path.GetExtension(source)==".tex") {artifactsToWrite.Add(new("individual-results",target));diagrams++;}
            }
            if(diagrams!=6)throw new InvalidDataException($"Expected six individual diagrams for {casePrefix}; found {diagrams}.");
        }
        if(strategies)
        foreach(var group in cases.GroupBy(c=>(Family:WelfareOutcomeExhibits.Family(c.Row),
            Alpha:PublicationTables.Number(c.Row,"CARA Alpha"),Cost:PublicationTables.Number(c.Row,"Costs Multiplier"))))
        {
            var ordered=group.OrderBy(c=>Array.IndexOf(WelfareOutcomeExhibits.Regimes,WelfareOutcomeExhibits.FeeLabel(c.Row))).ToArray();
            var selections=ordered.Select(c=>new PublicationFigures.StrategyCase(WelfareOutcomeExhibits.FeeLabel(c.Row),c.Row["OptionSetName"],
                Path.Combine(c.Directory,c.Prefix+" "+c.Row["OptionSetName"]+" -InformationSetActions.csv"))).ToArray();
            var figure=ArticleStrategyFigures.Generate(selections);
            string directory=ArticleResultsLayout.Aggregate(aggregate,group.Key.Family,ArticleResultsLayout.Risk(group.Key.Alpha));
            string stem=ArticleResultsLayout.Cost(group.Key.Cost)+"-participation-and-offers";
            string tex=ArticleResultsLayout.Source(directory,stem,".tex");
            files.Add(tex,figure.Latex);
            files.Add(Path.ChangeExtension(tex,".json"),JsonSerializer.Serialize(new {group.Key,figure.Cases,figure.Caption},Json));
            artifactsToWrite.Add(new("selection-offers",tex));
        }
        Console.WriteLine($"Article results: {cases.Count} cases; {artifactsToWrite.Count} standalone figures/tables; {files.Count} sources/data files.");
        if(list)return;
        foreach(var f in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Key));
            File.WriteAllText(f.Key,f.Value);
        }
        if(!sourcesOnly)await DiagramCompiler.CompileAllAsync(artifactsToWrite.Select(a=>a.Source).ToArray(),config,jobs);
        if(welfare)WelfareOutcomeExhibits.WriteInventory(generated,!sourcesOnly);
        Directory.CreateDirectory(Path.GetDirectoryName(inventory));
        // Preserve other generated families when a targeted command refreshes only one family.
        var allArtifacts=artifactsToWrite.AsEnumerable();
        bool compiled=!sourcesOnly;
        if(File.Exists(inventory))
        {
            using var old=JsonDocument.Parse(File.ReadAllText(inventory));
            string previousRoot=old.RootElement.TryGetProperty("Root",out var storedRoot)?storedRoot.GetString():root;
            var retained=old.RootElement.GetProperty("Artifacts").Deserialize<Artifact[]>(Json).Where(a=>!Selected(a))
                .Select(a=>a with {Source=Path.Combine(root,Path.GetRelativePath(previousRoot,a.Source))}).ToArray();
            allArtifacts=allArtifacts.Concat(retained).ToArray();
            if(retained.Length>0)compiled &= old.RootElement.GetProperty("Compiled").GetBoolean();
        }
        File.WriteAllText(inventory,JsonSerializer.Serialize(new {Schema="article-results-v1",Root=root,GeneratedUtc=DateTime.UtcNow,
            Cases=cases.Count,Compiled=compiled,Artifacts=allArtifacts.OrderBy(a=>a.Source).ToArray(),
            GeneratorSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ArticleResultsCommand).Assembly.Location)))},Json));
    }
}

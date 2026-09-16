using ACESim;
using ACESimBase;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Copy only coordinator-complete individual cases; never modify aggregate exhibits or production.</summary>
public static class ArticleCompletedCasesCommand
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public sealed record FileRecord(string Path,string Sha256);
    public sealed record CaseRecord(string OptionSetName,string ReportPrefix,string ProductionCommit,
        FileRecord[] Inputs,FileRecord[] Outputs);
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static FileRecord Record(string path,string root)=>new(Path.GetRelativePath(root,path),Hash(path));

    public static Dictionary<string,string> LayoutRow(LitigGameCorrelatedSignalsArticleLauncher.RoutineCaseDefinition c)=>new()
    {
        ["OptionSetName"]=c.OptionSetName,["Number of Offers"]=c.Offers.ToString(CultureInfo.InvariantCulture),
        ["CARA Alpha"]=c.Risk switch {"Risk Neutral"=>"0","Moderately Risk Averse"=>"2",_=>throw new InvalidDataException("Unknown risk: "+c.Risk)},
        ["Fee Shifting Multiplier"]=c.FeeRule=="American"?"0":"1",["Fee Regime"]=c.FeeRule=="American"?"American":"British",
        ["Fees After Nonanswer"]=c.FeeRule=="Complete Fee-Shifting"?"true":"false",
        ["Fee Shifting Trigger"]=c.FeeRule=="Complete Fee-Shifting"?LitigGameCorrelatedSignalsArticleLauncher.ExitFeeTriggerLabel:"Trial only"
    };

    public static async Task<int> RunAsync(string[] args)
    {
        string variable=FolderFinder.ReportResultsDirectoryEnvironmentVariable,previous=Environment.GetEnvironmentVariable(variable);
        try
        {
            string input=null,output=null;int jobs=Environment.ProcessorCount;bool list=false;
            for(int i=0;i<args.Length;i++)
                switch(args[i])
                {
                    case "--input":input=Path.GetFullPath(args[++i]);break;
                    case "--output":output=Path.GetFullPath(args[++i]);break;
                    case "--jobs":jobs=int.Parse(args[++i],CultureInfo.InvariantCulture);break;
                    case "--list":list=true;break;
                    default:throw new ArgumentException("completed-cases --input <production> --output <Results> [--jobs N] [--list]");
                }
            if(input==null||output==null||jobs<1||!Directory.Exists(input))throw new ArgumentException("Supply input, output and positive jobs.");
            if(input.Equals(output,StringComparison.OrdinalIgnoreCase)||input.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
                output.StartsWith(input+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Incremental output must be separate from the production root.");
            Environment.SetEnvironmentVariable(variable,input);
            string inventory=Path.Combine(output,"Run records","completed-cases.json");
            var records=File.Exists(inventory)?JsonSerializer.Deserialize<CaseRecord[]>(File.ReadAllText(inventory),Json).ToDictionary(c=>c.OptionSetName):new Dictionary<string,CaseRecord>();
            var matrix=LitigGameCorrelatedSignalsArticleLauncher.RoutineCaseMatrix().ToDictionary(c=>c.OptionSetName);
            var selected=new List<(LitigGameCorrelatedSignalsArticleLauncher.RoutineCaseDefinition Case,string Commit)>();
            foreach(var plan in LitigGameCorrelatedSignalsArticleLauncher.RequiredArticleProductionPlans)
            {
                var launcher=new LitigGameCorrelatedSignalsArticleLauncher(plan);
                if(!File.Exists(launcher.GetReportFullPath(null,"Coordinator")))continue;
                var status=launcher.LoadTaskCoordinatorStatus();
                var options=launcher.GetOptionsSets();
                using var manifest=JsonDocument.Parse(File.ReadAllText(launcher.GetReportFullPath("run manifest",".json")));
                string commit=manifest.RootElement.GetProperty("GitCommit").GetString();
                foreach(var task in status.Tasks.Where(t=>t.TaskType=="Optimize"&&t.Complete&&!t.Failed))
                    selected.Add((matrix[options[task.ID].Name],commit));
            }
            var pending=new List<(CaseRecord Record,Dictionary<string,string> Sources)>();
            foreach(var (c,commit) in selected)
            {
                string prefix=c.ReportPrefix+" "+c.OptionSetName;
                string destination=ArticleResultsLayout.Individual(Path.Combine(output,"Individual simulations"),LayoutRow(c));
                var sources=ArticleResultsCommand.IndividualSources(destination,input,prefix,c.Cost);
                foreach(string suffix in new[]{".csv"," -equ.csv"," -InformationSetActions.csv"})
                    if(!File.Exists(Path.Combine(input,prefix+suffix)))throw new InvalidDataException("Incomplete completed case: "+prefix+suffix);
                var inputs=Directory.GetFiles(input,prefix+"*").Where(p=>Path.GetFileName(p)==prefix+".csv"||Path.GetFileName(p).StartsWith(prefix+" -",StringComparison.Ordinal))
                    .Where(p=>Path.GetExtension(p) is ".csv" or ".tex").OrderBy(p=>p).Select(p=>Record(p,input)).ToArray();
                if(records.TryGetValue(c.OptionSetName,out var old)&&old.ProductionCommit==commit&&old.Inputs.SequenceEqual(inputs)&&
                    old.Outputs.All(f=>File.Exists(Path.Combine(output,f.Path))&&Hash(Path.Combine(output,f.Path))==f.Sha256))continue;
                pending.Add((new(c.OptionSetName,c.ReportPrefix,commit,inputs,[]),sources));
            }
            Console.WriteLine($"Coordinator-complete cases: {selected.Count}; new or changed cases to organize: {pending.Count}.");
            if(list||pending.Count==0)return 0;
            foreach(var item in pending)
                foreach(var file in item.Sources){Directory.CreateDirectory(Path.GetDirectoryName(file.Key));File.WriteAllText(file.Key,file.Value);}
            // Reuse only rendered files whose source and output hashes match the earlier verified collection.
            var reusable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string priorHashes=Path.Combine(output,"Run records","final-artifact-hashes.json");
            if(File.Exists(priorHashes))
            {
                using var prior=JsonDocument.Parse(File.ReadAllText(priorHashes));
                foreach(var entry in prior.RootElement.EnumerateArray().Where(e=>e.GetProperty("Kind").GetString()=="individual-results"))
                {
                    string source=Path.Combine(output,entry.GetProperty("Source").GetString());
                    bool matches=File.Exists(source)&&Hash(source).Equals(entry.GetProperty("SourceSha256").GetString(),StringComparison.OrdinalIgnoreCase);
                    foreach(string kind in new[]{"PDF","PNG"})
                    {
                        var file=entry.GetProperty(kind);string path=Path.Combine(output,file.GetProperty("Path").GetString());
                        matches &= File.Exists(path)&&Hash(path).Equals(file.GetProperty("Sha256").GetString(),StringComparison.OrdinalIgnoreCase);
                    }
                    if(matches)reusable.Add(source);
                }
            }
            var tex=pending.SelectMany(p=>p.Sources.Keys).Where(p=>Path.GetExtension(p)==".tex"&&!reusable.Contains(p)).ToArray();
            Console.WriteLine($"Compile {tex.Length} diagrams; reuse verified unchanged renders for the remainder.");
            await DiagramCompiler.CompileAllAsync(tex,new(){ProcessTimeoutSeconds=300},jobs);
            foreach(var item in pending)
            {
                var outputs=item.Sources.Keys.Concat(item.Sources.Keys.Where(p=>Path.GetExtension(p)==".tex")
                    .SelectMany(p=>new[]{ArticleResultsLayout.RenderedArtifact(p,".pdf"),ArticleResultsLayout.RenderedArtifact(p,".png")}));
                records[item.Record.OptionSetName]=item.Record with {Outputs=outputs.OrderBy(p=>p).Select(p=>Record(p,output)).ToArray()};
            }
            Directory.CreateDirectory(Path.GetDirectoryName(inventory));
            File.WriteAllText(inventory,JsonSerializer.Serialize(records.Values.OrderBy(r=>r.OptionSetName),Json).Replace("\r\n","\n")+"\n");
            Console.WriteLine($"Organized {pending.Count} completed cases. Aggregate exhibits and production records were not changed.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Environment.SetEnvironmentVariable(variable,previous);}
    }
}

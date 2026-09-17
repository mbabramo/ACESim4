using ACESim;
using ACESimBase.Util.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Saved multiple-start reports to auditable tables and individual diagrams; never solves a game.</summary>
public static class MultipleEquilibriaExhibits
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static double N(IReadOnlyDictionary<string,string> row, string key) => PublicationTables.Number(row, key);
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }
    private static object Fingerprint(string path) => new { Path = Path.GetFullPath(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    private static string Wrapper(string body) => "\\documentclass[10pt,border=5pt]{standalone}\n" +
        "\\usepackage[T1]{fontenc}\\usepackage{lmodern,booktabs,array}\n\\begin{document}\n" + body + "\n\\end{document}\n";
    private static string Escape(string s) => s.Replace("&", "\\&").Replace("_", "\\_").Replace("%", "\\%");
    private static string Range(Dictionary<string,string> row, string metric) =>
        N(row,"Minimum "+metric).ToString("0.0000",CultureInfo.InvariantCulture) + "--" +
        N(row,"Maximum "+metric).ToString("0.0000",CultureInfo.InvariantCulture);
    public static string RangeTable(Dictionary<string,string>[] rows, string[] metrics, string[] headings)
    {
        var body = new StringBuilder("\\begin{tabular}{ll"+new string('c',metrics.Length)+"}\n\\toprule Risk & Fee rule & "+string.Join(" & ",headings)+" \\\\\n\\midrule\n");
        foreach(var row in rows)
            body.Append(Escape(ArticleResultsLayout.Risk(N(row,"CARA Alpha")))).Append(" & ").Append(Escape(row["Fee Rule"]))
                .Append(" & ").Append(string.Join(" & ",metrics.Select(m=>Range(row,m)))).AppendLine(@" \\");
        return Wrapper(body.AppendLine("\\bottomrule\\end{tabular}").ToString());
    }
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string input=null, output=null; int jobs=Environment.ProcessorCount; bool sourcesOnly=false;
            var seen=new HashSet<string>();
            for(int i=0;i<args.Length;i++)
            {
                string flag=args[i]; if(!seen.Add(flag)) throw new ArgumentException("Repeated option: "+flag);
                string Value()=>++i<args.Length?args[i]:throw new ArgumentException("Missing value: "+flag);
                switch(flag)
                {
                    case "--input": input=Path.GetFullPath(Value()); break;
                    case "--output": output=Path.GetFullPath(Value()); break;
                    case "--jobs": jobs=int.Parse(Value(),CultureInfo.InvariantCulture); break;
                    case "--sources-only": sourcesOnly=true; break;
                    default: throw new ArgumentException("Unknown option: "+flag);
                }
            }
            if(input==null||output==null||jobs<1) throw new ArgumentException("Use --input <completed production> --output <exhibits> [--jobs N] [--sources-only].");
            string raw=Path.Combine(output,"Sources","Production");
            Directory.CreateDirectory(raw);
            var launcher=new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.MultipleEquilibriaRobustness);
            var options=launcher.GetOptionsSets().Cast<LitigGameOptions>().ToArray();
            // Copy the data required to rebuild every outcome and verify recovery counts. The
            // original run manifest remains untouched and identifies the actual solving build.
            string[] inputs=Directory.GetFiles(input,"CS004ME*")
                .Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||p.EndsWith("-log.txt",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(p)=="CS004ME run manifest.json")
                .OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            foreach(string source in inputs)
            {
                string destination=Path.Combine(raw,Path.GetFileName(source));
                if(!Path.GetFullPath(source).Equals(destination,StringComparison.OrdinalIgnoreCase))File.Copy(source,destination,true);
            }
            string summary=Path.Combine(output,"Sources","equilibrium-outcomes.csv"), ranges=Path.Combine(output,"Sources","equilibrium-ranges.csv");
            var audit = sourcesOnly ? null : await MultipleEquilibriaStrategyAudit.RunAsync(options, raw);
            string auditPath=Path.Combine(output,"Sources","strategy-verification.json");
            if(audit!=null)Write(auditPath,JsonSerializer.Serialize(new {
                Method="Reload every saved profile, reproduce its full information-set action report, and compute both players' best responses against that current profile. No equilibrium search is run.",
                Tolerance=1e-7,Profiles=audit,
                Inputs=audit.SelectMany(p=>new[]{p.ProfileFile,p.ActionReport}).Distinct().Select(Fingerprint).ToArray(),
                ModelAssembly=Fingerprint(typeof(LitigGameOptions).Assembly.Location),
                ReportingAssembly=Fingerprint(typeof(MultipleEquilibriaExhibits).Assembly.Location)
            },Json)+"\n");
            string previous=Environment.GetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable);
            CorrelatedSignalsMultipleEquilibriaReport.ValidationSummary validation;
            try
            {
                Environment.SetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable,raw);
                validation=CorrelatedSignalsMultipleEquilibriaReport.BuildAndValidate(launcher,summary,ranges,
                    audit?.ToDictionary(p=>(p.OptionSet,p.Equilibrium),p=>p.MaximumGain));
            }
            finally { Environment.SetEnvironmentVariable(FolderFinder.ReportResultsDirectoryEnvironmentVariable,previous); }
            var rows=PublicationFigures.ReadCsv(ranges).OrderBy(r=>N(r,"CARA Alpha"))
                .ThenBy(r=>Array.IndexOf(WelfareOutcomeExhibits.Regimes,r["Fee Rule"])).ToArray();
            var all=PublicationFigures.ReadCsv(summary);
            var tex=new List<string>();
            string caption="Ranges across distinct recovered equilibrium strategy profiles; each profile receives equal weight. " +
                "Recovery frequencies describe numerical searches, not behavioral equilibrium selection. Each scenario requests fifty starts; failed attempts can leave fewer verified recoveries. " +
                "These ranges are not confidence intervals or guarantees that every equilibrium has been found. Cost multiplier is 1. ";
            void Table(string folder,string stem,string source,string notes,object data)
            {
                string path=Path.Combine(output,folder,"Sources",stem+".tex");Write(path,source);tex.Add(path);
                Write(Path.ChangeExtension(path,".txt"),notes+"\n");
                Write(Path.ChangeExtension(path,".json"),JsonSerializer.Serialize(new {Caption=notes,Data=data,Sources=new[]{Fingerprint(summary),Fingerprint(ranges)}},Json)+"\n");
            }
            string[] welfare=CorrelatedSignalsMultipleEquilibriaReport.WelfareMeasures;
            string[] welfareHead=["\\shortstack{Plaintiff\\\\shortfall}","\\shortstack{Nonliable\\\\defendant}","\\shortstack{Liable\\\\defendant}","\\shortstack{Gross outcome\\\\error}","\\shortstack{Real litigation\\\\costs}"];
            string[] dispositions=["Does Not File","Does Not Answer","Settles","P Abandons (Mutual Give-Up Allocated)","D Defaults (Mutual Give-Up Allocated)","P Loses","P Wins"];
            string[] dispositionHead=["\\shortstack{No\\\\filing}","\\shortstack{No\\\\answer}","Settlement","Abandonment","Default","\\shortstack{Trial\\\\P loses}","\\shortstack{Trial\\\\P wins}"];
            foreach(var view in new[]{(Name:ArticleResultsLayout.RiskComparison,Rows:rows)}.Concat(rows.GroupBy(r=>N(r,"CARA Alpha")).Select(g=>(Name:ArticleResultsLayout.Risk(g.Key),Rows:g.ToArray()))))
            {
                Table(view.Name,"cost-1-welfare-outcome-ranges",RangeTable(view.Rows,welfare,welfareHead),caption+WelfareOutcomeExhibits.ErrorDescription,view.Rows);
                Table(view.Name,"cost-1-disposition-ranges",RangeTable(view.Rows,dispositions,dispositionHead),caption+"Disposition probabilities average over all potential disputes. Mutual give-up is allocated once, half to each exit. Each range is marginal, so minima or maxima across columns need not sum to one.",view.Rows);
            }
            var recovery=new StringBuilder("\\begin{tabular}{llrrrrrr}\n\\toprule Risk & Fee rule & Priors & Attempts & \\shortstack{Exact\\\\attempts} & Recoveries & Profiles & \\shortstack{Maximum\\\\gain} \\\\\n\\midrule\n");
            foreach(var row in rows)
                recovery.Append(Escape(ArticleResultsLayout.Risk(N(row,"CARA Alpha")))).Append(" & ").Append(row["Fee Rule"]).Append(" & ")
                    .Append(string.Join(" & ",new[]{"Requested Priors","Attempted Solves","Exact Attempts","Verified Recoveries","Distinct Reported Strategy Profiles"}.Select(k=>row[k])))
                    .Append(" & ").Append(N(row,"Maximum Exploitability").ToString("0.00E+00",CultureInfo.InvariantCulture)).AppendLine(@" \\");
            Table(ArticleResultsLayout.RiskComparison,"cost-1-equilibrium-recoveries",Wrapper(recovery.AppendLine("\\bottomrule\\end{tabular}").ToString()),caption+"Exact attempts include the initial exact solve and any exact fallback. Attempts can exceed priors. Maximum gain is recomputed for both players against each saved current profile; the original report statistic is retained separately in the full outcome CSV.",rows);
            foreach(var option in options)
            {
                string risk=ArticleResultsLayout.Risk(Convert.ToDouble(option.VariableSettings["CARA Alpha"],CultureInfo.InvariantCulture));
                string fee=LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(option);
                foreach(string source in Directory.GetFiles(input,"CS004ME "+option.Name+" *.tex"))
                {
                    // Averages/correlations across solutions are diagnostic reports, not equilibria.
                    if(!Regex.IsMatch(Path.GetFileName(source),@"-Eq\d+\.tex$"))continue;
                    string destination=Path.Combine(output,"Individual simulations",risk,fee,"Sources",Path.GetFileName(source));
                    Write(destination,string.Join("\n",File.ReadAllLines(source)
                        .Where(line=>!line.Contains(@"node[midway] {\huge Costs:",StringComparison.Ordinal))));tex.Add(destination);
                }
            }
            if(!sourcesOnly)await DiagramCompiler.CompileAllAsync(tex.ToArray(),new(),jobs);
            Write(Path.Combine(output,"multiple-equilibria-exhibits.json"),JsonSerializer.Serialize(new {
                Schema=1,Validation=validation,Compiled=!sourcesOnly,StrategyVerification=sourcesOnly?null:Fingerprint(auditPath),ReportingAssembly=Fingerprint(typeof(MultipleEquilibriaExhibits).Assembly.Location),Summary=Fingerprint(summary),Ranges=Fingerprint(ranges),
                Inputs=inputs.Select(Fingerprint).ToArray(),Artifacts=tex.Select(p=>new{Source=Fingerprint(p),Pdf=sourcesOnly?null:Fingerprint(ArticleResultsLayout.RenderedArtifact(p,".pdf")),Png=sourcesOnly?null:Fingerprint(ArticleResultsLayout.RenderedArtifact(p,".png"))}).ToArray()
            },Json)+"\n");
            Write(Path.Combine(output,"README.md"),$"# Multiple equilibria\n\nSix ordinary-cost scenarios cross three fee rules with risk neutrality and symmetric CARA alpha 2. {validation.EquilibriumCount} distinct profiles were recovered from 50 initializations per case.\n\n"+
                "Risk Comparison and each risk folder contain separate welfare-range and disposition-range tables; recovery diagnostics are in Risk Comparison. Individual simulations contains each equilibrium's generated figures, grouped by risk and fee rule. Sources contains exact data, editable TeX and captions. The production manifest identifies the solving build; the exhibit inventory separately records reporting inputs and output hashes.\n\n"+
                caption+WelfareOutcomeExhibits.ErrorDescription+"\n\nAdditional approximate attempts are capped at 500 pivots and additional exact attempts at 1,000; the initial exact solve is uncapped. A cutoff ends that attempt; failed exact attempts are not replaced with additional starts. Read the actual recovery totals and the saved solve logs together.\n\nSources/strategy-verification.json records a fresh best-response check for every saved profile and reproduction of its action report. The recovery table uses these current-profile gains. The original report statistic is retained separately because older reporting builds measured the running average of profiles instead.\n\nThe legacy truth-specific burden columns in the full CSV remain conditional diagnostics. The five headline columns and all displayed disposition shares are population averages. Conditional offer means describe reached bargaining decisions. Distinctness follows the production recovery catalog; behavioral/outcome differences must be assessed separately.\n\n"+
                "Regenerate with `LitigCharts multiple-equilibria-report --input <completed production directory> --output <this folder> --jobs 32`. The supplemental rebuild script runs this automatically after multiple-start aggregation.\n");
            Console.WriteLine($"Verified {validation.OptionSetCount} scenarios, {validation.EquilibriumCount} profiles; generated {tex.Count} exhibits.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}

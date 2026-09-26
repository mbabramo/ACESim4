using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class Rendering
{
    public static async Task Run(string bundle,BundleManifest manifest,ResolvedArticlePlan plan,
        Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection,string work,int workers)
    {
        var defaults=new CorrelatedSignalsSettings();var s=plan.Settings;
        if(s.IncludeExtensions!=defaults.IncludeExtensions||s.IncludeTrialOnly!=defaults.IncludeTrialOnly||
            !s.NoiseLevels.SequenceEqual(defaults.NoiseLevels)||!s.Grids.SequenceEqual(defaults.Grids)||
            s.StartsPerCore!=defaults.StartsPerCore||s.ApproximatePivotLimit!=defaults.ApproximatePivotLimit)
            throw new NotSupportedException("The remaining cached custom layouts require their original extension/search specification. Regenerate those renderers before changing these settings; no stale custom chart was published. Main cost rows are generated natively.");
        bool NativeSupplement(string path)=>plan.Steps.Contains("StandardReports")&&(path.StartsWith("Supplemental materials/Liability signals diagrams/")||path.StartsWith("Supplemental materials/Game tree diagrams/"));
        var eligible=manifest.Renders.Where(r=>r.CaseIds.All(profiles.ContainsKey)&&!NativeSupplement(r.Output)).ToArray();
        foreach(var f in manifest.Files.Where(f=>f.Path.StartsWith("records/")))
        {
            string rel=f.Path[8..];
            if(MainFigures.Owns(rel)||MainTables.Owns(rel))continue;
            if(NativeSupplement(rel))continue;
            if(rel.StartsWith("Results/Individual simulations/"))continue;
            if(rel.StartsWith("Supplemental materials/Equilibrium strategy changes/"))
            {
                string directory=rel[..rel.IndexOf("/Sources/",StringComparison.Ordinal)];
                if(!eligible.Any(r=>r.Output.StartsWith(directory+"/",StringComparison.Ordinal)))continue;
            }
            if(rel.StartsWith("Figures/Sources/Figure 7 - Welfare outcomes.",StringComparison.Ordinal))continue;
            Files.CopyVerified(Files.Under(bundle,f.Path),Files.Under(collection,rel),f.Sha256);
        }
        WelfareFigure.Generate(plan,profiles,collection);
        MainFigures.Generate(plan,profiles,collection,work);
        await WorkedFigure.Generate(plan,profiles,collection);
        MainTables.Generate(plan,profiles,collection);
        foreach(var f in manifest.Files.Where(f=>f.Path.StartsWith("author/")))
            Files.CopyVerified(Files.Under(bundle,f.Path),Files.Under(collection,"NonGenerated/"+f.Path[7..]),f.Sha256);
        var completed=new System.Collections.Concurrent.ConcurrentBag<object>();
        await Parallel.ForEachAsync(eligible.Select((r,i)=>(r,i)),new ParallelOptions{MaxDegreeOfParallelism=workers},async(item,ct)=>{
            var r=item.r;string source=Files.Under(collection,r.Source[8..]),dest=Files.Under(collection,r.Output);
            string dir=Path.Combine(work,"render",item.i.ToString("D4"));Directory.CreateDirectory(dir);
            string text=File.ReadAllText(source);var parts=new List<string>();
            if(!text.Contains("\\documentclass"))
            {
                foreach(var line in text.Split('\n').Where(l=>l.Trim().StartsWith("% ")&&l.Trim().EndsWith(".tex")))
                    parts.Add(Files.Under(Path.GetDirectoryName(source)!,line.Trim()[2..]));
                if(parts.Count==0)throw new InvalidDataException("Unknown table source format: "+source);
            }
            else parts.Add(source);
            var pdfs=new List<string>();
            for(int n=0;n<parts.Count;n++)
            {
                await Commands.Run(Path.Combine(work,"logs"),$"latex-{item.i:D4}-{n}","lualatex",["-interaction=nonstopmode","-halt-on-error","-output-directory="+dir,parts[n]],Path.GetDirectoryName(parts[n])!);
                pdfs.Add(Path.Combine(dir,Path.GetFileNameWithoutExtension(parts[n])+".pdf"));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if(pdfs.Count==1)Files.CopyVerified(pdfs[0],dest);
            else await Commands.Run(Path.Combine(work,"logs"),$"merge-{item.i:D4}","pdfunite",pdfs.Append(dest),dir);
            bool cached=r.Kind!="CompletePrimaryStrategy"&&!r.Output.StartsWith("Figures/Figure 7 ")&&!MainFigures.Stems.Append(WorkedFigure.Stem).Any(s=>r.Output=="Figures/"+s+".pdf")&&r.Output!="Tables/"+MainTables.Primitives+".pdf"&&r.Output!="Tables/"+MainTables.Summary+".pdf";
            completed.Add(new{r.Output,SourceSha256=Files.Sha(source),PdfSha256=Files.Sha(dest),r.CaseIds,CachedScientificLayout=cached});
        });
        Files.Save(Path.Combine(work,"rendering.json"),new{Passed=true,Artifacts=completed,VisualReviewPending=true,ScientificLayoutRegenerationPending=true});
    }
}

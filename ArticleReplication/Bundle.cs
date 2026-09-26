using ACESim;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public sealed record PrimaryInput(string CaseId,FinalArticleCase Case,JsonObject GameIdentity,
    string ExpectedAudit,string ExpectedProfile,Dictionary<string,FinalArticleExecution.FileIdentity> Inputs);
public sealed record BundleFile(string Path,string Sha256,long Bytes);
public sealed record RenderInput(string Output,string Source,string SourceSha256,string[] CaseIds,string Kind);
public sealed record BundleManifest(string Schema,DateTime CreatedUtc,string SourceCommit,Calibration Calibration,
    BundleFile[] Files,RenderInput[] Renders,string[] PrimaryCaseIds,string[] Notes);

public static class Bundle
{
    public static BundleManifest Open(string root)
    {
        var m=Files.Read<BundleManifest>(Path.Combine(root,"bundle.json"));
        if(m.Schema!="correlated-signals-solutions-v1")throw new InvalidDataException("Unknown solutions bundle.");
        foreach(var f in m.Files)
        {
            string p=Files.Under(root,f.Path);
            if(new FileInfo(p).Length!=f.Bytes||Files.Sha(p)!=f.Sha256)throw new InvalidDataException("Bundle changed: "+f.Path);
        }
        return m;
    }

    public static void Pack(string article,string reproduction,string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output))throw new IOException("Choose a fresh solution-bundle directory.");
        Directory.CreateDirectory(output);
        var distribution=Files.Object(Path.Combine(reproduction,"distribution.json"));
        foreach(var item in distribution["Files"]!.AsArray())
            if(Files.Sha(Files.Under(reproduction,item!["Path"]!.GetValue<string>()))!=item["Sha256"]!.GetValue<string>())
                throw new InvalidDataException("Changed saved-profile distribution.");
        using(var archive=ZipFile.OpenRead(Path.Combine(reproduction,"inputs.zip")))
            foreach(var e in archive.Entries)
            {
                if(e.FullName.EndsWith('/'))continue;
                string p=Files.Under(output,e.FullName);Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                using var input=e.Open();using var target=new FileStream(p,FileMode.CreateNew);input.CopyTo(target);
            }
        var primary=Files.Read<PrimaryInput[]>(Path.Combine(output,"inputs/primary.json"));
        var binary=primary.First(x=>x.Case.Distribution=="direct-binary").Case;
        var calibration=new Calibration(binary.PartySigma,binary.CourtSigma,binary.CalibrationSha256);
        var renders=new List<RenderInput>();
        foreach(string folder in new[]{"Figures","Tables","Results/Individual simulations","Supplemental materials"})
        {
            string dir=Path.Combine(article,folder);
            foreach(var source in Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string rel=Path.GetRelativePath(article,source).Replace('\\','/');string ext=Path.GetExtension(source).ToLowerInvariant();
                if(ext is not (".tex" or ".json" or ".csv" or ".gz" or ".txt"))continue;
                if(rel.StartsWith("Supplemental materials/Risk aversion utility curves/"))continue;
                string dest=Files.Under(output,"records/"+rel);Files.CopyVerified(source,dest);
                if(ext!=".tex")continue;
                string? target=null;string kind="CachedScientificLayout";string[] cases=[];
                if(folder=="Figures"||folder=="Tables")
                {
                    // Per-page table sources are dependencies, not separate numbered exhibits.
                    string stem=Path.GetFileNameWithoutExtension(source);
                    if(File.Exists(Path.Combine(article,folder,stem+".pdf")))target=folder+"/"+stem+".pdf";
                }
                else if(folder=="Results/Individual simulations")
                {
                    var bits=rel.Split('/');cases=[bits[2]];
                    if(Path.GetFileName(source)=="strategy.tex")target=$"Results/Individual simulations/{bits[2]}/strategy.pdf";
                    kind="CompletePrimaryStrategy";
                }
                else
                {
                    string parent=Path.GetDirectoryName(rel)!.Replace('\\','/');
                    if(parent.EndsWith("/Sources"))parent=parent[..^8];
                    else if(parent.Contains("/Sources/"))parent=parent[..parent.IndexOf("/Sources/",StringComparison.Ordinal)];
                    string candidate=parent+"/"+Path.GetFileNameWithoutExtension(source)+".pdf";
                    if(File.Exists(Path.Combine(article,candidate)))target=candidate;
                    if(rel.StartsWith("Supplemental materials/Equilibrium strategy changes/"))
                    {
                        string request=Path.Combine(Path.GetDirectoryName(source)!,"request.json");
                        if(File.Exists(request))
                        {
                            var q=Files.Object(request);var names=q["Sources"]!.AsArray().Select(x=>x!["OptionSetName"]!.GetValue<string>()).ToHashSet();
                            cases=primary.Where(x=>names.Contains(OptionName(x))).Select(x=>x.CaseId).Order().ToArray();
                            if(cases.Length!=2)throw new InvalidDataException("Cannot bind strategic layout to two complete profiles: "+source);
                        }
                    }
                }
                if(target!=null)renders.Add(new(target,"records/"+rel,Files.Sha(source),cases,kind));
            }
        }
        // The two welfare-document sources have a Sources subfolder convention differing from their titles.
        foreach(var (sub,name) in new[]{("welfare-overview","welfare-overview"),("welfare-decompositions","welfare-decompositions")})
        {
            var candidates=Directory.GetFiles(Path.Combine(output,"records/Supplemental materials/Generated pairwise comparisons/Sources",sub),"*.tex");
            if(candidates.Length==1&&!renders.Any(x=>x.Output==$"Supplemental materials/Generated pairwise comparisons/{name}.pdf"))
                renders.Add(new($"Supplemental materials/Generated pairwise comparisons/{name}.pdf",Path.GetRelativePath(output,candidates[0]).Replace('\\','/'),Files.Sha(candidates[0]),[],"CachedWelfareLayout"));
        }
        // Author-owned sources, not generated PDFs or historical build clutter.
        foreach(string f in Directory.EnumerateFiles(Path.Combine(article,"Article and bibliography")))
            if(Path.GetExtension(f).ToLowerInvariant() is ".tex" or ".bib" or ".bst" or ".cls" or ".sty")
                Files.CopyVerified(f,Files.Under(output,"author/Manuscript/"+Path.GetFileName(f)));
        foreach(string f in Directory.EnumerateFiles(Path.Combine(article,"Supplemental materials/Risk aversion utility curves")))
            if(Path.GetExtension(f).ToLowerInvariant() is ".tex" or ".pdf")
                Files.CopyVerified(f,Files.Under(output,"author/Utility curves/"+Path.GetFileName(f)));
        foreach(string name in new[]{"manuscript-exhibits.json","article-diagrams.json"})
            Files.CopyVerified(Path.Combine(article,name),Files.Under(output,"reference/"+name));
        var files=Directory.EnumerateFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f=>new BundleFile(Path.GetRelativePath(output,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
        Files.Save(Path.Combine(output,"bundle.json"),new BundleManifest("correlated-signals-solutions-v1",DateTime.UtcNow,
            "204536f57afe5415e641870129d555c962fd8f68",calibration,files,renders.OrderBy(r=>r.Output,StringComparer.Ordinal).ToArray(),
            primary.Select(p=>p.CaseId).Order().ToArray(),["No PDFs are used for generated scientific exhibits; cached, data-bound TeX is recompiled.",
                "Historical metadata paths are provenance only. Runtime input paths are bundle-relative.",
                "Authored manuscript and utility illustrations are protected inputs."]));
        Console.WriteLine($"Packaged {primary.Length} primary inputs, {renders.Count} render sources, {files.Length} files.");
    }
    private static string OptionName(PrimaryInput p)=>p.Case.OriginalOptionName??FinalArticleCaseFactory.NamePrefix+p.Case.Id;
}

namespace ArticleReplication;

public sealed record StrategicInput(string Directory,string[] CaseIds);

/// <summary>Migrates existing computation evidence; no rendered artifact or data-bearing template is admitted.</summary>
public static class ComputationBundle
{
    public static void Pack(string legacy,string output,string? approximate=null,string? histories=null)
    {
        var old=Bundle.Open(legacy);if(Directory.Exists(output))throw new IOException("Fresh computational input directory required.");Directory.CreateDirectory(output);
        foreach(var file in old.Files.Where(f=>f.Path.StartsWith("inputs/")))Files.CopyVerified(Files.Under(legacy,file.Path),Files.Under(output,file.Path),file.Sha256);
        var inputs=new List<StrategicInput>();
        foreach(var group in old.Renders.Where(r=>r.Output.StartsWith("Supplemental materials/Equilibrium strategy changes/")).GroupBy(r=>Path.GetDirectoryName(r.Source)!))
        {
            var record=group.First();string id=new DirectoryInfo(Path.GetDirectoryName(Files.Under(legacy,group.Key))!).Name,dir="computations/strategic/"+id;
            foreach(string path in Directory.GetFiles(Files.Under(legacy,group.Key)).Where(p=>p.EndsWith(".json.gz")||Path.GetFileName(p) is "request.json" or "validation.json"))
                Files.CopyVerified(path,Files.Under(output,dir+"/"+Path.GetFileName(path)));
            inputs.Add(new(dir,record.CaseIds));
        }
        void CopyPack(string root,string sub,string manifestFile)
        {
            var m=Files.Object(Path.Combine(root,manifestFile));
            foreach(var f in m["Files"]!.AsArray())Files.CopyVerified(Files.Under(root,f!["Path"]!.GetValue<string>()),Files.Under(output,sub+"/"+f["Path"]!.GetValue<string>()),f["Sha256"]!.GetValue<string>());
            Files.CopyVerified(Path.Combine(root,manifestFile),Files.Under(output,sub+"/"+manifestFile));
        }
        if(approximate!=null)CopyPack(approximate,"computations/approximate","approximate-inputs.json");
        if(histories!=null)CopyPack(histories,"computations/histories","histories.json");
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f=>new BundleFile(Path.GetRelativePath(output,f).Replace('\\','/'),Files.Sha(f),new FileInfo(f).Length)).ToArray();
        if(files.Any(f=>Path.GetExtension(f.Path) is ".tex" or ".pdf" or ".png" or ".html"))throw new InvalidDataException("Rendered artifacts are forbidden in computational bundles.");
        Files.Save(Path.Combine(output,"bundle.json"),new BundleManifest("correlated-signals-computations-v2",DateTime.UtcNow,old.SourceCommit,old.Calibration,files,[],old.PrimaryCaseIds,["Computational inputs and validation evidence only. No figure/table sources, PDFs, chart coordinates or manuscript.","Historical file paths are provenance, never executable instructions."]){Strategic=inputs.ToArray()});
        Files.Save(Path.Combine(output,"pack-validation.json"),new{Passed=true,Files=files.Length,NoRenderedArtifacts=true,StrategicPairs=inputs.Count});
    }
}

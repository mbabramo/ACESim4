using System.IO.Compression;

namespace ArticleReplication;

/// <summary>Snapshot actual source, rebuild dependencies and execute selected stages; Git is optional.</summary>
public static class Rebuild
{
    public static async Task Run(Dictionary<string,string> options)
    {
        string source=Path.GetFullPath(options["source"]),output=Path.GetFullPath(options["output"]);
        if(Directory.Exists(output))throw new IOException("Rebuild requires a fresh output directory.");
        SourceSnapshot.Create(source,output);string logs=Path.Combine(output,"logs");
        string archive=Path.Combine(output,"source.zip"),checkout=Path.Combine(output,"source");
        await Prerequisites.Run(Path.Combine(output,"prerequisites"));
        string project=Path.Combine(checkout,"ArticleReplication/ArticleReplication.csproj"),runtime=Path.Combine(output,"runtime");
        await Commands.Run(logs,"sdk","dotnet",["--info"],checkout);
        await Commands.Run(logs,"restore","dotnet",["restore",project,"--locked-mode","--disable-parallel"],checkout);
        await Commands.Run(logs,"build","dotnet",["build",project,"-c","Release","--no-restore","-m:1","/p:UseSharedCompilation=false","-o",runtime],checkout);
        Files.Save(Path.Combine(output,"build.json"),new{Passed=true,SourceManifestSha256=Files.Sha(Path.Combine(output,"source-manifest.json")),SourceArchiveSha256=Files.Sha(archive),BuiltUtc=DateTime.UtcNow,Runtime=Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Order().Select(f=>new BundleFile(Path.GetRelativePath(runtime,f),Files.Sha(f),new FileInfo(f).Length))});
        var args=new List<string>{Path.Combine(runtime,"ArticleReplication.dll"),"reproduce"};
        foreach(var (key,value) in options.Where(x=>x.Key!="source"&&x.Key!="output")){args.Add("--"+key);args.Add(key is "solutions" or "histories" or "settings" or "external-jobs"?Path.GetFullPath(value):value);}
        args.AddRange(["--output",Path.Combine(output,"run")]);
        await Commands.Run(logs,"reproduce","dotnet",args,output);
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,FinishedUtc=DateTime.UtcNow,BuildSha256=Files.Sha(Path.Combine(output,"build.json")),ReproductionSha256=Files.Sha(Path.Combine(output,"run/completed.json")),WholeArticleRelease=false});
    }
}

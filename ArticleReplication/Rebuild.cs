using System.IO.Compression;

namespace ArticleReplication;

/// <summary>One C# entry point snapshots committed source, rebuilds all dependencies and executes selected stages.</summary>
public static class Rebuild
{
    public static async Task Run(Dictionary<string,string> options)
    {
        string source=Path.GetFullPath(options["source"]),output=Path.GetFullPath(options["output"]);
        if(Directory.Exists(output))throw new IOException("Rebuild requires a fresh output directory.");
        if(output.StartsWith(source.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(output.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.Equals(output,StringComparison.OrdinalIgnoreCase))throw new IOException("Source and rebuild directory must be disjoint.");
        Directory.CreateDirectory(output);string logs=Path.Combine(output,"logs");
        await Commands.Run(logs,"git-status","git",["-C",source,"status","--porcelain","--untracked-files=all"],output);
        if(!string.IsNullOrWhiteSpace(File.ReadAllText(Path.Combine(logs,"git-status.stdout.log"))))throw new InvalidDataException("Commit task source before reproducible snapshot; unrelated repositories are never staged by this command.");
        await Commands.Run(logs,"git-head","git",["-C",source,"rev-parse","HEAD"],output);
        string commit=File.ReadAllText(Path.Combine(logs,"git-head.stdout.log")).Trim();
        string archive=Path.Combine(output,"source.zip");await Commands.Run(logs,"source-archive","git",["-C",source,"archive","--format=zip","--output="+archive,commit],output);
        string checkout=Path.Combine(output,"source");Directory.CreateDirectory(checkout);
        using(var zip=ZipFile.OpenRead(archive))foreach(var entry in zip.Entries)
        {
            if(entry.FullName.EndsWith('/'))continue;string file=Files.Under(checkout,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var input=entry.Open();using var target=new FileStream(file,FileMode.CreateNew);input.CopyTo(target);
        }
        string project=Path.Combine(checkout,"ArticleReplication/ArticleReplication.csproj"),runtime=Path.Combine(output,"runtime");
        await Commands.Run(logs,"sdk","dotnet",["--info"],checkout);
        await Commands.Run(logs,"restore","dotnet",["restore",project,"--locked-mode","--disable-parallel"],checkout);
        await Commands.Run(logs,"build","dotnet",["build",project,"-c","Release","--no-restore","-m:1","/p:UseSharedCompilation=false","-o",runtime],checkout);
        Files.Save(Path.Combine(output,"build.json"),new{Passed=true,SourceCommit=commit,SourceArchiveSha256=Files.Sha(archive),BuiltUtc=DateTime.UtcNow,Runtime=Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Order().Select(f=>new BundleFile(Path.GetRelativePath(runtime,f),Files.Sha(f),new FileInfo(f).Length))});
        var args=new List<string>{Path.Combine(runtime,"ArticleReplication.dll"),"reproduce"};
        foreach(var (key,value) in options.Where(x=>x.Key!="source"&&x.Key!="output")){args.Add("--"+key);args.Add(key is "solutions" or "histories" or "settings" or "external-jobs"?Path.GetFullPath(value):value);}
        args.AddRange(["--output",Path.Combine(output,"run")]);
        await Commands.Run(logs,"reproduce","dotnet",args,output);
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,FinishedUtc=DateTime.UtcNow,BuildSha256=Files.Sha(Path.Combine(output,"build.json")),ReproductionSha256=Files.Sha(Path.Combine(output,"run/completed.json")),WholeArticleRelease=false});
    }
}

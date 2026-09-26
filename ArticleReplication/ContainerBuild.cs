namespace ArticleReplication;

public static class ContainerBuild
{
    public static async Task Run(Dictionary<string,string> args)
    {
        string source=Path.GetFullPath(args["source"]),output=Path.GetFullPath(args["output"]),tag=args.GetValueOrDefault("tag","acesim-correlated-signals:local");
        if(Directory.Exists(output))throw new IOException("Container build records need a fresh directory.");
        string docker=Prerequisites.Find("docker")??throw new FileNotFoundException("Install and start a Docker engine with Linux containers; see ArticleReplication/INSTALL.md.");
        Directory.CreateDirectory(output);string logs=Path.Combine(output,"logs");
        SourceSnapshot.Create(source,Path.Combine(output,"snapshot"));
        string context=Path.Combine(output,"snapshot/source");
        await Commands.Run(logs,"docker-version",docker,["version"],source);
        await Commands.Run(logs,"container-build",docker,["build","--progress=plain","--tag",tag,"--file",Path.Combine(context,"ArticleReplication/Containerfile"),context],source);
        await Commands.Run(logs,"container-inspect",docker,["image","inspect",tag],source);
        string inspect=Path.Combine(logs,"container-inspect.stdout.log");
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,FinishedUtc=DateTime.UtcNow,Image=tag,ImageInspect=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(inspect)),ContainerfileSha256=Files.Sha(Path.Combine(context,"ArticleReplication/Containerfile")),SourceManifestSha256=Files.Sha(Path.Combine(output,"snapshot/source-manifest.json")),InstalledIntoArticleDirectory=false});
    }
}

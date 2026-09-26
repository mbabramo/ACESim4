using System.IO.Compression;

namespace ArticleReplication;

/// <summary>Hash the actual source, including downloaded archives without Git metadata.</summary>
public static class SourceSnapshot
{
    static readonly HashSet<string> Excluded=new(StringComparer.OrdinalIgnoreCase){"bin","obj",".git",".vs","TestResults"};
    public static BundleFile[] Create(string source,string output)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(Files.Nested(output,source)||Files.Nested(source,output))throw new IOException("Source and snapshot must be disjoint.");
        if(Directory.Exists(output))throw new IOException("Snapshot requires a fresh directory.");
        Directory.CreateDirectory(output);string tree=Path.Combine(output,"source");
        var files=new List<BundleFile>();
        foreach(string name in new[]{"global.json",".dockerignore"})Copy(Files.Under(source,name));
        foreach(string name in new[]{"ACESimBase","LitigCharts","ArticleReplication"})Walk(Files.Under(source,name));
        void Walk(string directory)
        {
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked source directory: "+directory);
            foreach(string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked source file: "+path);
                if(Directory.Exists(path)){if(!Excluded.Contains(Path.GetFileName(path)))Walk(path);}
                else if(!path.EndsWith(".user")&&!path.EndsWith(".suo")&&Path.GetFileName(path)!=".env")Copy(path);
            }
        }
        void Copy(string path)
        {
            string relative=Path.GetRelativePath(source,path).Replace('\\','/'),hash=Files.Sha(path);
            Files.CopyVerified(path,Files.Under(tree,relative),hash);files.Add(new(relative,hash,new FileInfo(path).Length));
        }
        var ordered=files.OrderBy(f=>f.Path,StringComparer.Ordinal).ToArray();
        Files.Save(Path.Combine(output,"source-manifest.json"),new{Schema="article-source-snapshot-v1",Files=ordered,ContentSha256=Files.Digest(ordered),ExternalToolchainsIncluded=false});
        using(var archive=ZipFile.Open(Path.Combine(output,"source.zip"),ZipArchiveMode.Create))
        foreach(var file in ordered)
        {
            var entry=archive.CreateEntry(file.Path,CompressionLevel.Optimal);entry.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);
            using var input=File.OpenRead(Files.Under(tree,file.Path));using var target=entry.Open();input.CopyTo(target);
        }
        return ordered;
    }
}

namespace ArticleReplication;

/// <summary>Read-only, byte-for-byte accounting of a generated collection and its published copy.</summary>
public static class DeliveryVerification
{
    public sealed record FileIdentity(long Bytes,string Sha256);
    public sealed record Tree(SortedDictionary<string,FileIdentity> Files,string[] Directories);
    public static Tree Read(string root)
    {
        root=Path.GetFullPath(root);
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        var files=new SortedDictionary<string,FileIdentity>(StringComparer.Ordinal);
        var directories=new List<string>();
        void Walk(string directory)
        {
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked directory: "+directory);
            foreach(string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                string relative=Path.GetRelativePath(root,path).Replace('\\','/');
                if(relative is ".git" or "Article and bibliography")continue;
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked collection entry: "+path);
                if(Directory.Exists(path)){directories.Add(relative);Walk(path);}
                else files.Add(relative,new(new FileInfo(path).Length,Files.Sha(path)));
            }
        }
        Walk(root);return new(files,directories.Order(StringComparer.Ordinal).ToArray());
    }

    public static void Compare(string generated,string published,string output)
    {
        generated=Path.GetFullPath(generated);published=Path.GetFullPath(published);output=Path.GetFullPath(output);
        if(Files.Nested(generated,published)||Files.Nested(published,generated))throw new IOException("Compare separate collection directories.");
        if(Files.Nested(output,generated)||Files.Nested(output,published))throw new IOException("Keep verification output outside both collections.");
        var a=Read(generated);var b=Read(published);
        var rows=a.Files.Keys.Union(b.Files.Keys,StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path=>
        {
            a.Files.TryGetValue(path,out var x);b.Files.TryGetValue(path,out var y);
            return new{Path=path,Status=x==null?"Extra":y==null?"Missing":x==y?"Identical":"Different",Generated=x,Published=y};
        }).ToArray();
        string[] missing=a.Directories.Except(b.Directories,StringComparer.Ordinal).ToArray(),extra=b.Directories.Except(a.Directories,StringComparer.Ordinal).ToArray();
        bool passed=rows.All(r=>r.Status=="Identical")&&missing.Length==0&&extra.Length==0;
        Files.Save(output,new{Passed=passed,Generated=generated,Published=published,ExcludedTopLevelEntries=new[]{".git","Article and bibliography"},GeneratedFiles=a.Files.Count,PublishedFiles=b.Files.Count,Files=rows,GeneratedDirectories=a.Directories,PublishedDirectories=b.Directories,MissingDirectories=missing,ExtraDirectories=extra,ExactBytesRequired=true,NoContentNormalizations=true});
        if(!passed)throw new InvalidDataException($"Delivery mismatch: {rows.Count(r=>r.Status!="Identical")} files; {missing.Length} missing and {extra.Length} extra directories. See {output}");
    }

    public static void Test(string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh test output required.");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(string name,Action<string,string> mutate,bool expected)
        {
            string root=Path.Combine(output,name),a=Path.Combine(root,"generated"),b=Path.Combine(root,"published"),proof=Path.Combine(root,"proof.json");
            foreach(string path in new[]{a,b}){Directory.CreateDirectory(Path.Combine(path,"Results"));File.WriteAllText(Path.Combine(path,"Results/value.csv"),"value\n1\n");}
            mutate(a,b);bool result=true;
            try{Compare(a,b,proof);}catch(InvalidDataException){result=false;}
            if(result!=expected||Files.Object(proof)["Passed"]!.GetValue<bool>()!=expected)throw new InvalidDataException("Unexpected delivery result: "+name);
            checks.Add(name);
        }
        Check("identical",(_,_)=>{},true);
        Check("extra-empty-directory",(_,b)=>Directory.CreateDirectory(Path.Combine(b,"old/Sources")),false);
        Check("missing-directory",(a,_)=>Directory.CreateDirectory(Path.Combine(a,"required")),false);
        Check("extra-file",(_,b)=>File.WriteAllText(Path.Combine(b,"stale.pdf"),"old"),false);
        Check("missing-file",(a,_)=>File.WriteAllText(Path.Combine(a,"required.txt"),"new"),false);
        Check("changed-bytes",(_,b)=>File.WriteAllText(Path.Combine(b,"Results/value.csv"),"value\r\n1\r\n"),false);
        Check("author-and-git-excluded",(_,b)=>{Directory.CreateDirectory(Path.Combine(b,"Article and bibliography"));File.WriteAllText(Path.Combine(b,"Article and bibliography/current.tex"),"author edits");Directory.CreateDirectory(Path.Combine(b,".git"));},true);
        Files.Save(Path.Combine(output,"passed.json"),new{Passed=true,Checks=checks.Count,Tests=checks});
    }
}

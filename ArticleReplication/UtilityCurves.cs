namespace ArticleReplication;

/// <summary>Supplemental utility artwork is independent of the optional manuscript build.</summary>
public static class UtilityCurves
{
    public static async Task Run(string collection,string work)
    {
        string directory=Path.Combine(collection,"Supplemental materials/Risk aversion utility curves");
        Directory.CreateDirectory(directory);
        var assembly=typeof(UtilityCurves).Assembly;const string prefix="ArticleReplication.Authored/";
        var sources=new List<object>();
        foreach(string name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith(prefix)))
        {
            string relative=name[prefix.Length..].Replace('\\','/');
            if(!relative.StartsWith("Utility curves/"))continue;
            string target=Files.Under(directory,relative[15..]);
            using var input=assembly.GetManifestResourceStream(name)!;
            using(var output=new FileStream(target,FileMode.CreateNew))input.CopyTo(output);
            sources.Add(new{Resource=name,Sha256=Files.Sha(target)});
        }
        await Commands.Run(Path.Combine(work,"logs"),"utility-curves","lualatex",["-interaction=nonstopmode","-halt-on-error","risk aversion v2.tex"],directory);
        string diagnostics=Path.Combine(work,"ReportResults/Utility-curves-build");Directory.CreateDirectory(diagnostics);
        foreach(string file in Directory.GetFiles(directory).Where(f=>Path.GetExtension(f) is ".aux" or ".log" or ".out"))
            File.Move(file,Path.Combine(diagnostics,Path.GetFileName(file)));
        Files.Save(Path.Combine(work,"utility-curves-validation.json"),new{Passed=true,SourceAssets=sources,PdfSha256=Files.Sha(Path.Combine(directory,"risk aversion v2.pdf")),ManuscriptRequired=false});
    }
}

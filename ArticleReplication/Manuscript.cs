namespace ArticleReplication;

public static class Manuscript
{
    public static async Task Run(string bundle,BundleManifest manifest,string collection,string work)
    {
        string directory=Path.Combine(collection,"Article and bibliography");Directory.CreateDirectory(directory);
        var assembly=typeof(Manuscript).Assembly;const string prefix="ArticleReplication.Authored/";
        var sources=new List<object>();
        foreach(string name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith(prefix)))
        {
            string relative=name[prefix.Length..].Replace('\\','/');string target=relative.StartsWith("Manuscript/")?Files.Under(directory,relative[11..]):relative.StartsWith("Utility curves/")?Files.Under(collection,"Supplemental materials/Risk aversion utility curves/"+relative[15..]):throw new InvalidDataException("Unknown authored asset.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);using var input=assembly.GetManifestResourceStream(name)!;using(var output=new FileStream(target,FileMode.CreateNew))input.CopyTo(output);sources.Add(new{Resource=name,Sha256=Files.Sha(target)});
        }
        ManuscriptValues.Generate(collection,work);
        string utility=Path.Combine(collection,"Supplemental materials/Risk aversion utility curves"),utilitySource=Path.Combine(utility,"risk aversion v2.tex");
        string logs=Path.Combine(work,"logs");
        await Commands.Run(logs,"utility-curves","lualatex",["-interaction=nonstopmode","-halt-on-error","risk aversion v2.tex"],utility);
        await Commands.Run(logs,"manuscript-latex-1","lualatex",["-interaction=nonstopmode","-halt-on-error","corr_signals.tex"],directory);
        await Commands.Run(logs,"manuscript-bibliography","bibtex",["corr_signals"],directory);
        for(int pass=2;pass<=3;pass++)await Commands.Run(logs,"manuscript-latex-"+pass,"lualatex",["-interaction=nonstopmode","-halt-on-error","corr_signals.tex"],directory);
        string log=File.ReadAllText(Path.Combine(directory,"corr_signals.log"));
        if(log.Contains("There were undefined references")||log.Contains("There were undefined citations"))throw new InvalidDataException("Unresolved manuscript references.");
        string diagnostics=Path.Combine(work,"ReportResults/Manuscript-build");Directory.CreateDirectory(diagnostics);
        foreach(string dir in new[]{directory,utility})foreach(string file in Directory.GetFiles(dir).Where(f=>Path.GetExtension(f) is ".aux" or ".log" or ".out" or ".bbl" or ".blg" or ".bcf"||f.EndsWith(".run.xml")||f.EndsWith("-blx.bib")))
            File.Move(file,Path.Combine(diagnostics,Path.GetFileName(file)));
        Files.Save(Path.Combine(work,"manuscript-validation.json"),new{Passed=true,AuthoredTemplatePreserved=true,DataBindingsGenerated=true,SourceAssets=sources,PdfSha256=Files.Sha(Path.Combine(directory,"corr_signals.pdf")),SourceSha256=Files.Sha(Path.Combine(directory,"corr_signals.tex")),BindingsSha256=Files.Sha(Path.Combine(directory,"generated-values.tex")),VisualReviewPending=true});
    }
}

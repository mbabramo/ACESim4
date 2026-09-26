namespace ArticleReplication;

public static class Manuscript
{
    public static async Task Run(string bundle,BundleManifest manifest,string collection,string work)
    {
        string directory=Path.Combine(collection,"Article and bibliography");Directory.CreateDirectory(directory);
        foreach(var item in manifest.Files.Where(f=>f.Path.StartsWith("author/Manuscript/")))
            Files.CopyVerified(Files.Under(bundle,item.Path),Path.Combine(directory,Path.GetFileName(item.Path)),item.Sha256);
        foreach(var item in manifest.Files.Where(f=>f.Path.StartsWith("author/Utility curves/")))
            Files.CopyVerified(Files.Under(bundle,item.Path),Files.Under(collection,"Supplemental materials/Risk aversion utility curves/"+Path.GetFileName(item.Path)),item.Sha256);
        string logs=Path.Combine(work,"logs");
        await Commands.Run(logs,"manuscript-latex-1","lualatex",["-interaction=nonstopmode","-halt-on-error","corr_signals.tex"],directory);
        await Commands.Run(logs,"manuscript-bibliography","bibtex",["corr_signals"],directory);
        for(int pass=2;pass<=3;pass++)await Commands.Run(logs,"manuscript-latex-"+pass,"lualatex",["-interaction=nonstopmode","-halt-on-error","corr_signals.tex"],directory);
        string log=File.ReadAllText(Path.Combine(directory,"corr_signals.log"));
        if(log.Contains("There were undefined references")||log.Contains("There were undefined citations"))throw new InvalidDataException("Unresolved manuscript references.");
        foreach(var item in manifest.Files.Where(f=>f.Path.StartsWith("author/Manuscript/")))
            if(Files.Sha(Path.Combine(directory,Path.GetFileName(item.Path)))!=item.Sha256)throw new InvalidDataException("Authored manuscript was modified.");
        string diagnostics=Path.Combine(work,"ReportResults/Manuscript-build");Directory.CreateDirectory(diagnostics);
        foreach(string file in Directory.GetFiles(directory).Where(f=>Path.GetExtension(f) is ".aux" or ".log" or ".out" or ".bbl" or ".blg" or ".bcf"||f.EndsWith(".run.xml")||f.EndsWith("-blx.bib")))
            File.Move(file,Path.Combine(diagnostics,Path.GetFileName(file)));
        Files.Save(Path.Combine(work,"manuscript-validation.json"),new{Passed=true,AuthoredTextUnchanged=true,PdfSha256=Files.Sha(Path.Combine(directory,"corr_signals.pdf")),SourceSha256=Files.Sha(Path.Combine(directory,"corr_signals.tex")),VisualReviewPending=true});
    }
}

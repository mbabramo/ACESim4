using System.Runtime.InteropServices;

namespace ArticleReplication;

public static class Prerequisites
{
    public static string? Find(string name)
    {
        if(Path.IsPathRooted(name))return File.Exists(name)?name:null;
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        foreach(string suffix in OperatingSystem.IsWindows()?new[]{".exe",".cmd",".bat",""}:new[]{""})
        {
            string file=Path.Combine(directory.Trim('"'),name+suffix);
            if(File.Exists(file))return Path.GetFullPath(file);
        }
        return null;
    }
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output))throw new IOException("Prerequisite checks require a fresh output directory.");
        Directory.CreateDirectory(output);string logs=Path.Combine(output,"logs");
        var tools=new Dictionary<string,string>();
        foreach(string tool in new[]{"dotnet","lualatex","bibtex","kpsewhich","pdftoppm","pdfunite"})
            tools.Add(tool,Find(tool)??throw new FileNotFoundException($"Missing {tool}. Follow ArticleReplication/INSTALL.md; tools are installed separately, never copied into results."));
        foreach(var (tool,path) in tools)
            await Commands.Run(logs,tool+"-version",path,[tool=="dotnet"?"--info":tool is "pdftoppm" or "pdfunite"?"-v":"--version"],output);
        var fonts=new List<object>();
        foreach(string font in new[]{"lmodern.sty","ec-lmr10.tfm","lmroman10-regular.otf","ClearSans.sty"})
        {
            string name="font-"+Path.GetFileNameWithoutExtension(font);
            await Commands.Run(logs,name,tools["kpsewhich"],[font],output);
            string file=File.ReadAllText(Path.Combine(logs,name+".stdout.log")).Trim();
            if(!File.Exists(file))throw new InvalidDataException("Required Latin Modern font/package missing: "+font);
            fonts.Add(new{Name=font,Sha256=Files.Sha(file)});
        }
        string tex=Path.Combine(output,"rendering-check.tex");
        File.WriteAllText(tex,"""
            \documentclass{standalone}
            \usepackage[T1]{fontenc}
            \usepackage{ClearSans}
            \usepackage{lmodern,booktabs,array,tabularx,microtype,pgfplots}
            \usetikzlibrary{arrows.meta,patterns,calc,positioning,matrix,shapes.geometric}
            \pgfplotsset{compat=1.18}
            \begin{document}
            \begin{tikzpicture}\node{Replication: Latin Modern $\alpha=2$};\node at (0,-2) {\sffamily Standard reports: Clear Sans};\draw[pattern=north east lines] (0,-1) rectangle (2,-.5);\end{tikzpicture}
            \end{document}
            """);
        await Commands.Run(logs,"render",tools["lualatex"],["-interaction=nonstopmode","-halt-on-error","rendering-check.tex"],output);
        await Commands.Run(logs,"preview",tools["pdftoppm"],["-png","-singlefile","-r","100","rendering-check.pdf","rendering-check"],output);
        await Commands.Run(logs,"merge",tools["pdfunite"],["rendering-check.pdf","rendering-check.pdf","merge-check.pdf"],output);
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,CheckedUtc=DateTime.UtcNow,OperatingSystem=RuntimeInformation.OSDescription,Architecture=RuntimeInformation.ProcessArchitecture.ToString(),Runtime=RuntimeInformation.FrameworkDescription,Tools=tools.Select(t=>new{Name=t.Key,InstalledPath=t.Value,ExecutableSha256=Files.Sha(t.Value)}),Fonts=fonts,GeneratedFiles=new[]{"rendering-check.pdf","rendering-check.png","merge-check.pdf"}.Select(f=>new{File=f,Sha256=Files.Sha(Path.Combine(output,f))}),ToolInstallationsCopied=false});
        Console.WriteLine("Prerequisites and rendering check passed; no tool installation copied.");
    }
}

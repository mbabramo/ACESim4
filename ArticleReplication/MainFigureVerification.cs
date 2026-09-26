using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ArticleReplication;

public static class MainFigureVerification
{
    public static void Run(string generated,string reference,string output)
    {
        var checks=new List<object>();
        foreach(string stem in MainFigures.Stems.Append(WorkedFigure.Stem))
        {
            string actualFile=Path.Combine(generated,"Figures/Sources",stem+".generated-data.json"),expectedFile=Path.Combine(reference,stem+".json");
            var a=Files.Object(actualFile);var b=Files.Object(expectedFile);int values=0;
            if(stem.StartsWith("Figure 1 "))Files.EqualScience(a["Panels"],b["Panels"],stem+" signal joint masses");
            else if(stem.StartsWith("Figure 2 "))
            {
                var currentPaths=a["Extraction"]!["Paths"]!.DeepClone().AsArray();var oldPaths=b["Paths"]!["Paths"]!.DeepClone().AsArray();
                foreach(var p in currentPaths.Concat(oldPaths))p!.AsObject().Remove("Purpose"); // Descriptive provenance is not scientific data.
                Files.EqualScience(currentPaths,oldPaths,"Every extracted history, action, conditional utility, reach and outcome");
                static Dictionary<string,string> Bindings(string file)=>Regex.Matches(File.ReadAllText(file),@"(?m)^\\newcommand\{\\([^}]+)\}\{([^\r\n]*)\}").ToDictionary(m=>m.Groups[1].Value,m=>m.Groups[2].Value);
                var oldBindings=Bindings(Path.Combine(reference,stem+".tex"));var current=Bindings(Path.Combine(generated,"Figures/Sources",stem+".tex"));
                foreach(var (name,value) in oldBindings)if(!current.TryGetValue(name,out var x)||x!=value)throw new InvalidDataException("Changed displayed worked-path binding: "+name);
                values=oldBindings.Count;
            }
            else if(a.ContainsKey("DisplayedPoints"))
            {
                var points=a["DisplayedPoints"]!.AsArray();
                foreach(var point in points)point!.AsObject().Remove("MarkerAreaPt2");
                Files.EqualScience(points,b["DisplayedPoints"],stem+" all reached positive support and coordinates");
                Files.EqualScience(a["OmittedZeroReach"],b["OmittedZeroReach"],stem+" zero-reach omissions");values=points.Count;
            }
            else
            {
                var series=new JsonArray(a["Dispositions"]!.AsArray().Select(x=>x!["Values"]!.DeepClone()).ToArray());
                Files.EqualScience(series,b["Dispositions"],stem+" seven terminal dispositions per rule");
                Files.EqualScience(a["PatternStyles"],b["PatternStyles"],stem+" pro-party pattern styles");values=14;
            }
            checks.Add(new{Figure=stem,Passed=true,Values=values,GeneratedDataSha256=Files.Sha(actualFile),ReferenceDataSha256=Files.Sha(expectedFile),GeneratedTexSha256=Files.Sha(Path.Combine(generated,"Figures/Sources",stem+".tex"))});
        }
        Files.Save(output,new{Passed=true,ExactScientificEquality=true,Checks=checks,VisualInspectionSeparate=true});
    }
}

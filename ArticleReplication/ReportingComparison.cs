using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ACESimBase.Util.Reporting;

namespace ArticleReplication;

/// <summary>Release-only comparison of derived floating-point reports; never solver acceptance.</summary>
public sealed class ReportingComparison
{
    public const double AbsoluteTolerance = 1e-14;
    public sealed record Difference(string Field,double Generated,double Reference,double AbsoluteDifference);
    public List<Difference> Differences { get; } = [];
    public sealed record DisplayDifference(string Field,string Generated,string Reference,double GeneratedUnrounded,double ReferenceUnrounded);
    public List<DisplayDifference> DisplayDifferences { get; } = [];

    public void Csv(Dictionary<string,string>[] actual,Dictionary<string,string>[] expected,
        Dictionary<string,string>[] actualFull,Dictionary<string,string>[] expectedFull,string context)
    {
        foreach(var row in actual.Concat(expected).Concat(actualFull).Concat(expectedFull))row.Remove("Seconds");
        Compare(JsonSerializer.SerializeToNode(actualFull,Files.Json),JsonSerializer.SerializeToNode(expectedFull,Files.Json),context+" full precision",true);
        if(actual.Length!=expected.Length||actual.Length!=actualFull.Length||actual.Length!=expectedFull.Length)
            throw new InvalidDataException(context+": row counts differ");
        bool Number(string s,out double d)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out d)&&double.IsFinite(d);
        bool Formatted(double full,double displayed)=>Number(full.ToSignificantFigures(6),out double rounded)&&rounded==displayed;
        for(int i=0;i<actual.Length;i++)
        {
            var a=actual[i];var b=expected[i];
            if(!a.Keys.ToHashSet().SetEquals(b.Keys)||!a.Keys.ToHashSet().SetEquals(actualFull[i].Keys)||!a.Keys.ToHashSet().SetEquals(expectedFull[i].Keys))
                throw new InvalidDataException(context+": columns differ");
            foreach(var cell in a)
            {
                string value=b[cell.Key];if(cell.Value==value)continue;
                if(!Number(cell.Value,out double displayedA)||!Number(value,out double displayedB)||
                    !Number(actualFull[i][cell.Key],out double fullA)||!Number(expectedFull[i][cell.Key],out double fullB)||
                    !Formatted(fullA,displayedA)||!Formatted(fullB,displayedB))
                    throw new InvalidDataException(context+$"/{i}/{cell.Key}: displayed difference is not explained by the validated full-precision values");
                DisplayDifferences.Add(new(context+$"/{i}/{cell.Key}",cell.Value,value,fullA,fullB));
            }
        }
    }

    public void Compare(JsonNode? actual,JsonNode? expected,string context,bool numericStrings=false)
    {
        if(actual is null||expected is null)
        {if(actual!=null||expected!=null)throw new InvalidDataException(context+": null mismatch");return;}
        if(actual is JsonObject a&&expected is JsonObject b)
        {
            if(a.Count!=b.Count||a.Any(k=>!b.ContainsKey(k.Key)))throw new InvalidDataException(context+": fields differ");
            foreach(var k in a)Compare(k.Value,b[k.Key],context+"/"+k.Key,numericStrings);return;
        }
        if(actual is JsonArray x&&expected is JsonArray y)
        {
            if(x.Count!=y.Count)throw new InvalidDataException(context+": length differs");
            for(int i=0;i<x.Count;i++)Compare(x[i],y[i],context+"/"+i,numericStrings);return;
        }
        bool Number(JsonNode n,out double value)
        {
            value=0;
            if(n.GetValueKind()==JsonValueKind.Number){value=n.GetValue<double>();return true;}
            return numericStrings&&n.GetValueKind()==JsonValueKind.String&&double.TryParse(n.GetValue<string>(),NumberStyles.Float,CultureInfo.InvariantCulture,out value);
        }
        if(Number(actual,out double left)&&Number(expected,out double right))
        {
            if(!double.IsFinite(left)||!double.IsFinite(right))throw new InvalidDataException(context+": nonfinite report value");
            if(left==right)return;
            double difference=Math.Abs(left-right);
            if(difference>AbsoluteTolerance)throw new InvalidDataException(context+$": reporting difference {difference:R} exceeds {AbsoluteTolerance:R} ({left:R} versus {right:R})");
            Differences.Add(new(context,left,right,difference));return;
        }
        if(!JsonNode.DeepEquals(actual,expected))throw new InvalidDataException(context+": value differs");
    }

    public void Profile(JsonObject actual,JsonObject expected,string context)
    {
        var a=(JsonObject)actual.DeepClone();var b=(JsonObject)expected.DeepClone();
        foreach(string key in new[]{"Profile","ActionReport","ReplayReport"}){a.Remove(key);b.Remove(key);}
        // These are calculated reports. Every complete strategy, action label, off-path
        // completion, normalization residual and game parameter outside them stays exact.
        foreach(string key in new[]{"Metrics","BySignal","ReachedHistories","WorkedRefusalPath"})
        {
            if(!a.ContainsKey(key)||!b.ContainsKey(key))throw new InvalidDataException(context+": missing report "+key);
            Compare(a[key],b[key],context+"/"+key);a.Remove(key);b.Remove(key);
        }
        Files.EqualScience(a,b,context+" exact strategy and model fields");
    }

    public static void Test(string output)
    {
        var tests=new List<string>();
        void Check(string name,Action action,bool accepted)
        {
            bool passed=true;try{action();}catch(InvalidDataException){passed=false;}
            if(passed!=accepted)throw new InvalidDataException("Unexpected comparison result: "+name);tests.Add(name);
        }
        JsonNode N(double d)=>JsonValue.Create(d)!;
        Check("Observed Linux-Windows last-bit difference accepted and recorded",()=>
        {
            var comparison=new ReportingComparison();comparison.Compare(N(.16867109314357012),N(.16867109314357015),"Welfare");
            if(comparison.Differences.Count!=1)throw new InvalidDataException("Missing difference record.");
        },true);
        Check("Meaningful reporting difference rejected",()=>new ReportingComparison().Compare(N(.2),N(.200000001),"Welfare"),false);
        Check("Tolerance boundary accepted",()=>new ReportingComparison().Compare(N(0),N(AbsoluteTolerance),"Welfare"),true);
        Check("Just outside tolerance rejected",()=>new ReportingComparison().Compare(N(0),N(Math.BitIncrement(AbsoluteTolerance)),"Welfare"),false);
        Check("Nonfinite rejected",()=>new ReportingComparison().Compare(N(double.NaN),N(0),"Welfare"),false);
        Check("Missing field rejected",()=>new ReportingComparison().Compare(JsonNode.Parse("{\"a\":1}"),JsonNode.Parse("{}"),"report"),false);
        Check("Changed row count rejected",()=>new ReportingComparison().Compare(JsonNode.Parse("[1]"),JsonNode.Parse("[1,2]"),"report"),false);
        Check("Changed label rejected",()=>new ReportingComparison().Compare(JsonValue.Create("American"),JsonValue.Create("British"),"label"),false);
        Check("CSV last-bit difference accepted",()=>new ReportingComparison().Compare(JsonValue.Create("0.16867109314357012"),JsonValue.Create("0.16867109314357015"),"csv",true),true);
        Check("Changed strategy probability rejected even by one bit",()=>
        {
            var a=JsonNode.Parse("{\"Strategies\":[{\"Probabilities\":[0.5,0.5]}],\"Metrics\":{},\"BySignal\":[],\"ReachedHistories\":[],\"WorkedRefusalPath\":{}}")!.AsObject();
            var b=(JsonObject)a.DeepClone();b["Strategies"]![0]!["Probabilities"]![0]=Math.BitIncrement(.5);
            new ReportingComparison().Profile(a,b,"profile");
        },false);
        Check("Existing exact arithmetic comparison still rejects last-bit change",()=>Files.EqualScience(N(.5),N(Math.BitIncrement(.5)),"exact"),false);
        Dictionary<string,string>[] Cell(string value)=>[new(){{"Value",value}}];
        double low=Math.BitDecrement(.3157305),high=Math.BitIncrement(.3157305);
        Check("Rounding-boundary display difference requires matching unrounded values",()=>
        {
            var c=new ReportingComparison();c.Csv(Cell(low.ToSignificantFigures(6)),Cell(high.ToSignificantFigures(6)),Cell(low.ToString("R")),Cell(high.ToString("R")),"csv");
            if(c.DisplayDifferences.Count!=1)throw new InvalidDataException("Display difference not retained.");
        },true);
        Check("Altered displayed value rejected despite matching raw values",()=>new ReportingComparison().Csv(Cell("0.315732"),Cell("0.315731"),Cell(high.ToString("R")),Cell(high.ToString("R")),"csv"),false);
        Check("Adjacent displayed values do not excuse a larger raw discrepancy",()=>new ReportingComparison().Csv(Cell("0.31573"),Cell("0.315731"),Cell("0.3157301"),Cell("0.3157306"),"csv"),false);
        Files.Save(output,new{Passed=true,Tests=tests,AbsoluteReportingTolerance=AbsoluteTolerance,ExactComparisonUnchanged=true});
    }
}

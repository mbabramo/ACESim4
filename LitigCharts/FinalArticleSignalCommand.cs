using ACESim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileIdentity = ACESim.FinalArticleExecution.FileIdentity;

namespace LitigCharts;

/// <summary>Signal illustrations bound to the prepared final cases; no solve or calibration fit.</summary>
public static class FinalArticleSignalCommand
{
    public sealed record CaseReference(string CaseId, FileIdentity PreparedInventory);
    public sealed record Request(CaseReference[] Cases, FileIdentity Calibration);
    public static string Specification(FinalArticleCase c) => c.Distribution switch
    {
        "uniform" => "Baseline", "beta-2-2" => "CenterWeightedContinuousMerits",
        "beta-half-half" => "PolarizedContinuousMerits", "direct-binary" => "DirectBinaryStateSignals",
        _ => throw new InvalidDataException("Unknown final signal distribution: " + c.Distribution)
    };

    public static void ValidateCalibration(FinalArticleCase c, FileIdentity file, JsonElement calibration)
    {
        if (c.Distribution != "direct-binary") return;
        if (!string.Equals(c.CalibrationSha256, file.Sha256, StringComparison.OrdinalIgnoreCase) ||
            c.Signals != calibration.GetProperty("Signals").GetByte() ||
            c.PartySigma != calibration.GetProperty("PartyFit").GetProperty("Sigma").GetDouble() ||
            c.CourtSigma != calibration.GetProperty("CourtFit").GetProperty("Sigma").GetDouble())
            throw new InvalidDataException("Signal diagram differs from the executed uniform-merits calibration.");
    }

    public static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--request" || args[2] != "--output")
            throw new ArgumentException("Use final-signal-sources --request FILE --output NEW_DIRECTORY.");
        string requestPath=Path.GetFullPath(args[1]), output=Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Signal output already exists.");
        var request=JsonSerializer.Deserialize<Request>(File.ReadAllBytes(requestPath), FinalArticleExecution.Json)
            ?? throw new InvalidDataException("Missing request.");
        if (request.Cases == null || request.Cases.Length == 0 ||
            request.Cases.Select(c=>c.CaseId).Distinct().Count()!=request.Cases.Length)
            throw new InvalidDataException("Declare unique prepared cases.");
        FinalArticleExecution.Verify(request.Calibration);
        using var calibrationFile=JsonDocument.Parse(File.ReadAllBytes(request.Calibration.Path));
        var calibration=calibrationFile.RootElement.GetProperty("Calibration");
        var prepared=new List<FinalArticleInventoryCommand.PreparedCase>();
        foreach (var item in request.Cases)
        {
            FinalArticleExecution.Verify(item.PreparedInventory);
            using var file=JsonDocument.Parse(File.ReadAllBytes(item.PreparedInventory.Path));
            if (file.RootElement.GetProperty("Schema").GetString()!="prepared-final-agreement-inventory-v1")
                throw new InvalidDataException("Expected prepared final inventory.");
            var row=file.RootElement.GetProperty("Cases").EnumerateArray()
                .Single(x=>x.GetProperty("Case").GetProperty("Id").GetString()==item.CaseId)
                .Deserialize<FinalArticleInventoryCommand.PreparedCase>(FinalArticleExecution.Json);
            _=Specification(row.Case); _=FinalArticleCaseFactory.Create(row.Case);
            ValidateCalibration(row.Case,request.Calibration,calibration); prepared.Add(row);
        }
        Directory.CreateDirectory(output); var artifacts=new List<object>();
        static FileIdentity Identity(string path)=>new(Path.GetFullPath(path),FinalArticleExecution.Hash(path));
        foreach (var row in prepared)
        {
            var c=row.Case; string spec=Specification(c); string directory=Path.Combine(output,c.Id);
            Directory.CreateDirectory(directory);
            foreach (bool monochrome in new[]{false,true})
            {
                var forward=ArticleSignalDiagrams.Generate(spec,monochrome,FinalArticleCaseFactory.Create(c));
                var inverse=ArticleSignalDiagrams.GenerateInverse(spec,monochrome,FinalArticleCaseFactory.Create(c));
                var predictive=ArticleSignalDiagrams.GeneratePartyToParty(spec,monochrome,FinalArticleCaseFactory.Create(c));
                var joint=predictive.Single().Panels.Single().JointMass;
                var party=forward.SelectMany(x=>x.Panels).Single(x=>x.DestinationTitle.StartsWith("Party signal",StringComparison.Ordinal));
                for(int i=0;i<c.Signals;i++)
                    if(Math.Abs(joint[i].Sum()-party.JointMass.Sum(x=>x[i]))>1E-7)
                        throw new InvalidDataException("Displayed signal marginal differs from production beliefs.");
                foreach(var d in forward.Concat(inverse).Concat(predictive))
                {
                    string stem=Path.Combine(directory,d.FileStem); string description=
                        $"Final prepared case {c.Id}; agreement-enabled. Merits distribution={c.Distribution}; party sigma={c.PartySigma:G17}; court sigma={c.CourtSigma:G17}; signal bins={c.Signals}.\n"+d.Description;
                    File.WriteAllText(stem+".tex",d.Latex);
                    File.WriteAllText(stem+".txt",description);
                    File.WriteAllText(stem+".json",JsonSerializer.Serialize(new{Case=c,row.Identity,Panels=d.Panels,Description=description},FinalArticleExecution.Json));
                    artifacts.Add(new{CaseId=c.Id,d.FileStem,Monochrome=monochrome,
                        TeX=Identity(stem+".tex"),Data=Identity(stem+".json"),Caption=Identity(stem+".txt")});
                }
            }
        }
        foreach(var item in request.Cases)FinalArticleExecution.Verify(item.PreparedInventory);
        FinalArticleExecution.Verify(request.Calibration);
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
            Schema="final-signal-sources-v1",CreatedUtc=DateTime.UtcNow,Passed=true,Request=Identity(requestPath),
            request.Calibration,Cases=prepared.Select(x=>new{x.Case,x.Identity}),Artifacts=artifacts,
            GameAssembly=Identity(typeof(LitigGame).Assembly.Location),ReportingAssembly=Identity(typeof(FinalArticleSignalCommand).Assembly.Location),
            SolvesStarted=0,CalibrationFitsStarted=0,TruthMapping="identity",RenderingAndVisualQAPending=true
        },FinalArticleExecution.Json));
        Console.WriteLine($"Generated {artifacts.Count} parameter-bound signal sources; no solve or new calibration.");
        return 0;
    }
}

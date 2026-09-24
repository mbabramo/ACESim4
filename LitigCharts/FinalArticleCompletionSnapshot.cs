using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.TaskManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileIdentity = ACESim.FinalArticleExecution.FileIdentity;

namespace LitigCharts;

/// <summary>One scheduled read of the new queue; freeze only coordinator-complete cases.</summary>
public static class FinalArticleCompletionSnapshot
{
    private sealed class ReadOnlyLauncher(FinalArticleExecution.Manifest manifest)
        : LitigGameCorrelatedSignalsArticleLauncher(ProductionRunPlan.AgreementToBargain)
    {
        public override string MasterReportNameForDistributedProcessing => "FinalAgreement";
        public override List<GameOptions> GetOptionsSets() => manifest.Cases.Where(c => !c.Parameters.IsExternalImport)
            .Select(c => (GameOptions)FinalArticleCaseFactory.Create(c.Parameters)).ToList();
    }
    private static FileIdentity Identity(string path) => new(Path.GetFullPath(path),FinalArticleExecution.Hash(path));
    public static FinalArticleExecution.Manifest ReadContract(string path)
    {
        var manifest=JsonSerializer.Deserialize<FinalArticleExecution.Manifest>(File.ReadAllBytes(path),FinalArticleExecution.Json);
        if(manifest?.Schema!="executable-final-agreement-single-v1" || manifest.Cases.Count(c=>c.Parameters.IsExternalImport)!=30 ||
            !FinalArticleExecution.Inside(manifest.ResultsDirectory,Path.Combine(manifest.ExecutionRoot,"production")))
            throw new InvalidDataException("Not an isolated final primary queue.");
        FinalArticleExecution.ValidateAuthorization(manifest.Authorization);
        FinalArticleExecution.Verify(manifest.PreparedInventory);
        foreach(var file in manifest.BuildFiles) FinalArticleExecution.Verify(file);
        return manifest;
    }
    public static TaskCoordinator ParseCoordinator(FinalArticleExecution.Manifest manifest, byte[] bytes)
    {
        var coordinator=new ReadOnlyLauncher(manifest).GetUninitializedTaskList();
        coordinator.StatusFromByteArray(bytes); // Includes task-plan fingerprint equality.
        return coordinator;
    }
    public static void ValidateCertificate(JsonElement certificate, FinalArticleInventoryCommand.PreparedCase prepared)
    {
        FileIdentity File(string key) => certificate.GetProperty(key).Deserialize<FileIdentity>();
        var manifest=ReadContract(FinalArticleExecution.Verify(File("OriginManifest")));
        var cases=manifest.Cases.Where(c=>!c.Parameters.IsExternalImport).ToArray();
        int id=certificate.GetProperty("TaskId").GetInt32();
        if(id<0 || id>=cases.Length || cases[id].Parameters.Id!=prepared.Case.Id || cases[id].Identity!=prepared.Identity ||
            JsonSerializer.Serialize(cases[id].Parameters)!=JsonSerializer.Serialize(prepared.Case))
            throw new InvalidDataException("Completed task is mapped to a different case.");
        var task=ParseCoordinator(manifest,System.IO.File.ReadAllBytes(FinalArticleExecution.Verify(File("Coordinator")))).Tasks
            .Single(t=>t.TaskType=="Optimize" && t.ID==id && t.Repetition==0 && t.RestrictToScenarioIndex==null);
        if(!task.Complete || task.Failed) throw new InvalidDataException("Frozen coordinator has no successful completion for this case.");
        using var dispatch=JsonDocument.Parse(System.IO.File.ReadAllBytes(FinalArticleExecution.Verify(File("Dispatch"))));
        var d=dispatch.RootElement;
        if(d.GetProperty("Id").GetString()!=prepared.Case.Id || d.GetProperty("Seed").GetInt32()!=0 ||
            d.GetProperty("ManifestSha256").GetString()!=File("OriginManifest").Sha256 ||
            d.GetProperty("Identity").Deserialize<StrategicGameFingerprint.Snapshot>()!=prepared.Identity)
            throw new InvalidDataException("Completed dispatch differs from the frozen original task.");
    }

    public static int Run(string[] args)
    {
        if(args.Length!=4 || args[0]!="--manifest" || args[2]!="--output")
            throw new ArgumentException("Use final-completion-snapshot --manifest FILE --output NEW_DIRECTORY; respect the queue's hourly gate.");
        string file=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[3]);
        var manifest=ReadContract(file);
        if(Directory.Exists(output) || !FinalArticleExecution.Inside(output,Path.Combine(manifest.ExecutionRoot,"imports")))
            throw new IOException("Choose a fresh snapshot directory inside this workspace's imports.");
        var launcher=new ReadOnlyLauncher(manifest);
        string variable=FolderFinder.ReportResultsDirectoryEnvironmentVariable, previous=Environment.GetEnvironmentVariable(variable);
        TaskCoordinator coordinator;
        try
        {
            Environment.SetEnvironmentVariable(variable,manifest.ResultsDirectory);
            coordinator=launcher.LoadTaskCoordinatorStatus();
        }
        finally { Environment.SetEnvironmentVariable(variable,previous); }
        Directory.CreateDirectory(output);
        string frozenManifest=Path.Combine(output,"origin-manifest.json"),snapshot=Path.Combine(output,"coordinator.snapshot");
        File.Copy(file,frozenManifest,false); File.WriteAllBytes(snapshot,coordinator.StatusAsByteArray());
        if(FinalArticleExecution.Hash(file)!=FinalArticleExecution.Hash(frozenManifest)) throw new IOException("Manifest changed during copy.");
        using var inventory=JsonDocument.Parse(File.ReadAllBytes(manifest.PreparedInventory.Path));
        var prepared=inventory.RootElement.GetProperty("Cases").EnumerateArray().Select(x=>x.Deserialize<FinalArticleInventoryCommand.PreparedCase>(FinalArticleExecution.Json))
            .ToDictionary(c=>c.Case.Id);
        var cases=manifest.Cases.Where(c=>!c.Parameters.IsExternalImport).ToArray();
        var certificates=new List<FileIdentity>();
        foreach(var task in coordinator.Tasks.Where(t=>t.TaskType=="Optimize" && t.Complete && !t.Failed))
        {
            if(task.Repetition!=0 || task.RestrictToScenarioIndex!=null) throw new InvalidDataException("Unexpected primary task identity.");
            var c=cases[task.ID]; var p=prepared[c.Parameters.Id];
            string directory=Path.Combine(output,c.Parameters.Id); Directory.CreateDirectory(directory);
            var copies=new List<object>();
            string logName=Launcher.ReportFilename("FinalAgreement",p.OptionSetName,$" task-Optimize-id{task.ID}-rep0-scenarionone-log.txt");
            foreach(string name in new[] { p.ProfileFileName,p.ActionFileName,p.NumericFileName,logName })
            {
                string source=Path.Combine(manifest.ResultsDirectory,name),destination=Path.Combine(directory,name);
                string before=FinalArticleExecution.Hash(source); File.Copy(source,destination,false);
                if(before!=FinalArticleExecution.Hash(source) || before!=FinalArticleExecution.Hash(destination)) throw new IOException("Completed source changed while freezing: "+source);
                copies.Add(new { Source=source,Destination=destination,Sha256=before });
            }
            string dispatch=Path.Combine(directory,"dispatch.json");
            File.Copy(Path.Combine(manifest.ResultsDirectory,c.Parameters.Id+".dispatch.json"),dispatch,false);
            var record=new { Schema="completed-final-primary-case-v1",CaseId=c.Parameters.Id,TaskId=task.ID,Complete=true,Failed=false,Seed=0,
                Identity=c.Identity,CreatedUtc=DateTime.UtcNow,Files=copies,OriginManifest=Identity(frozenManifest),Coordinator=Identity(snapshot),Dispatch=Identity(dispatch),
                Provenance=new[] { Identity(frozenManifest),Identity(snapshot),Identity(dispatch),manifest.PreparedInventory },SolvesStarted=0 };
            string certificate=Path.Combine(directory,"completion.json");
            File.WriteAllText(certificate,JsonSerializer.Serialize(record,FinalArticleExecution.Json));
            using var check=JsonDocument.Parse(File.ReadAllBytes(certificate)); ValidateCertificate(check.RootElement,p);
            certificates.Add(Identity(certificate));
        }
        File.WriteAllText(Path.Combine(output,"snapshot.json"),JsonSerializer.Serialize(new {
            Schema="final-primary-completion-snapshot-v1",CheckedUtc=DateTime.UtcNow,OriginManifest=Identity(frozenManifest),Coordinator=Identity(snapshot),
            Tasks=coordinator.Tasks.Select(t=>new {t.TaskType,t.ID,t.PlanLabel,t.Started,t.Complete,t.Failed}),
            CompletedCertificates=certificates,SolvesStarted=0,OriginalStudyPolled=false
        },FinalArticleExecution.Json));
        Console.WriteLine($"Frozen {certificates.Count} completed new cases; no solves, retries or original-study access.");
        return 0;
    }
}

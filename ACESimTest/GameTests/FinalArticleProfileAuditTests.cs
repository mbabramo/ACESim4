using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.Settings;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ACESimTest.GameTests;

[TestClass]
public class FinalArticleProfileAuditTests
{
    private sealed class FixtureLauncher(FinalArticleCase[] cases) : LitigGameCorrelatedSignalsArticleLauncher(ProductionRunPlan.AgreementToBargain)
    {
        public override System.Collections.Generic.List<GameOptions> GetOptionsSets() => cases.Select(c => (GameOptions)FinalArticleCaseFactory.Create(c)).ToList();
    }
    [TestMethod]
    public void FrozenCoordinatorRetainsCompletionFailureAndExactTaskMapping()
    {
        var original = new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
            .GetOptionsSets().Cast<LitigGameOptions>().First();
        var template = new FinalArticleCase { Id="new-one", Family="merits", Variant="center", FeeRule="american", AlphaP=0,AlphaD=0,
            CostMultiplier=1,Signals=10,Offers=original.GetOfferValues(),Distribution="uniform",PartySigma=.2,CourtSigma=.2,EntryCost=.15,TrialCost=.15 };
        // A recognized continuous specification; its labels are part of the coordinator fingerprint.
        var cases=new[] {template,template with { Id="new-two" }};
        var source=new FixtureLauncher(cases).GetUninitializedTaskList();
        var first=source.Tasks.Single(t=>t.TaskType=="Optimize" && t.ID==0);
        first.Complete=true; first.Started=DateTime.UtcNow;
        var second=source.Tasks.Single(t=>t.TaskType=="Optimize" && t.ID==1);
        second.Failed=true; second.Started=DateTime.UtcNow;
        var manifest=new FinalArticleExecution.Manifest("test","unused","unused","unused",null,null,null,null,[],new(32,3,1,2,2,8),
            cases.Select(c=>new FinalArticleExecution.Case(c,null)).ToArray());
        var snapshot=FinalArticleCompletionSnapshot.ParseCoordinator(manifest,source.StatusAsByteArray());
        Assert.IsTrue(snapshot.Tasks.Single(t=>t.TaskType=="Optimize" && t.ID==0).Complete);
        Assert.IsTrue(snapshot.Tasks.Single(t=>t.TaskType=="Optimize" && t.ID==1).Failed);
        Assert.IsFalse(snapshot.AllComplete);
        Assert.AreEqual(1,FinalArticleExecution.CountConcurrentWorkers(manifest,source.StatusAsByteArray()));
        foreach(var task in source.Tasks) { task.Complete=true; task.Failed=false; }
        Assert.AreEqual(0,FinalArticleExecution.CountConcurrentWorkers(manifest,source.StatusAsByteArray()));
        var renamed=manifest with { Cases=manifest.Cases.Reverse().ToArray() };
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleCompletionSnapshot.ParseCoordinator(renamed,source.StatusAsByteArray()));
    }

    [TestMethod]
    public void CompletedImportBindsExactGameAndEveryFrozenFile()
    {
        WithFixture((request, directory, identity) => {
            string evidence = request.CompletionEvidence.Path;
            void Write(string status, StrategicGameFingerprint.Snapshot game, bool includeNumeric) => File.WriteAllText(evidence, JsonSerializer.Serialize(new {
                Schema = "validated-production-imports-v1", Cases = new[] { new {
                    CaseId = "fixture", Status = status, ProductionBuildValidationPending = status != "validated-production-import", GameIdentity = game,
                    Files = new[] { request.Equilibrium, request.Actions, request.Numeric }.Take(includeNumeric ? 3 : 2)
                        .Select(f => new { Destination = f.Path, f.Sha256 }).ToArray() } } }));
            FinalArticleProfileAudit.Request Updated() => request with { CompletionEvidence = new(evidence, FinalArticleExecution.Hash(evidence)) };
            Write("validated-production-import", identity, true);
            Assert.AreEqual("fixture", FinalArticleProfileAudit.ValidateInputs(Updated()).Case.Id);
            Write("awaiting-original", identity, true);
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleProfileAudit.ValidateInputs(Updated()));
            Write("validated-production-import", identity with { CoordinatesSha256 = new string('c',64) }, true);
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleProfileAudit.ValidateInputs(Updated()));
            Write("validated-production-import", identity, false);
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleProfileAudit.ValidateInputs(Updated()));
            Write("validated-production-import", identity, true);
            File.AppendAllText(request.Equilibrium.Path, "changed");
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleProfileAudit.ValidateInputs(Updated()));
        });
    }

    private static void WithFixture(Action<FinalArticleProfileAudit.Request,string,StrategicGameFingerprint.Snapshot> action)
    {
        string allowed = Path.Combine(AppContext.BaseDirectory, "FinalProfileAuditTests");
        string directory = Path.Combine(allowed, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var spec = new FinalArticleCase { Id="fixture", Family="baseline", Variant="standard", FeeRule="american",
                AlphaP=0, AlphaD=0, CostMultiplier=1, Signals=10, Offers=new[] { .05,.95 }, Distribution="uniform",
                PartySigma=.2, CourtSigma=.2, EntryCost=.15, TrialCost=.15, OriginalOptionName="original" };
            string hash = new('a',64);
            var identity = new StrategicGameFingerprint.Snapshot("strategic-tree-v1",hash,hash,hash,hash,47611,41400,120,560);
            var prepared = new FinalArticleInventoryCommand.PreparedCase(spec,"original",identity,684,new {},new {},"profile.csv","actions.csv","numeric.csv");
            FinalArticleExecution.FileIdentity Write(string name, string text)
            {
                string path=Path.Combine(directory,name); File.WriteAllText(path,text); return new(path,FinalArticleExecution.Hash(path));
            }
            var inventory=Write("inventory.json",JsonSerializer.Serialize(new { Schema="prepared-final-agreement-inventory-v1",SolvesStarted=0,Cases=new[] {prepared} }));
            var request=new FinalArticleProfileAudit.Request("fixture",inventory,Write("evidence.json","{}"),
                Write("profile.csv","1,0"),Write("actions.csv","actions"),Write("numeric.csv","numeric"),false);
            action(request,directory,identity);
        }
        finally
        {
            if(!FinalArticleExecution.Inside(directory,allowed)) throw new InvalidOperationException("Test cleanup escaped scratch directory.");
            Directory.Delete(directory,true);
        }
    }
}

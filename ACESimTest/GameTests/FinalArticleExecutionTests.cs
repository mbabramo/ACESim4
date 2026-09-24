using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ACESimTest.GameTests;

[TestClass]
public class FinalArticleExecutionTests
{
    [TestMethod]
    public void WorkerBudgetIncludesReservedExternalAndOtherComputations()
    {
        var budget=new FinalArticleExecution.ResourceBudget(30,3,1,26,2,12);
        FinalArticleExecution.ValidateBudget(budget,26);
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateBudget(budget,27));
        FinalArticleExecution.ValidateBudget(budget with { GlobalCeiling=32,MaximumNewWorkers=28 },28);
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateBudget(budget with { GlobalCeiling=33 },26));
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateBudget(budget with { EstimatedPeakWorkerGiB=double.NaN },1));
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateBudget(budget with { MemoryHeadroomGiB=0 },1));
    }

    [TestMethod]
    public void MissingVerificationAndUnresolvedSpecificationsPreventExecution()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string exact=Path.Combine(root,"exact.json"), decisions=Path.Combine(root,"decisions.json");
        void WriteExact(bool complete,int cases) => File.WriteAllText(exact,JsonSerializer.Serialize(new {
            AcceptanceComplete=complete,InstrumentedAndOrdinaryOutputsEqual=true,
            Cases=Enumerable.Range(0,cases).Select(i=>new { CompletedEquivalence=true }).ToArray(),
            ComparedTimingPairs=Enumerable.Range(0,5).Select(i=>new { ExactOutputsEqual=true }).ToArray() }));
        FinalArticleExecution.Authorization Auth() => new(new(exact,FinalArticleExecution.Hash(exact)),new(decisions,FinalArticleExecution.Hash(decisions)));
        try
        {
            File.WriteAllText(decisions,"{\"Schema\":\"resolved-final-article-specifications-v1\",\"Resolved\":false,\"UserDecisionRecord\":\"pending\"}");
            WriteExact(false,3);
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
            WriteExact(true,2);
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
            WriteExact(true,3);
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
            var frozen=Auth();
            File.AppendAllText(exact," ");
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(frozen));
        }
        finally
        {
            string allowed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests"));
            if(!FinalArticleExecution.Inside(root,allowed)) throw new InvalidOperationException("Test cleanup escaped scratch root.");
            Directory.Delete(root,true);
        }
    }

    [TestMethod]
    public void UserReducedScopeStillRequiresEveryExactResultAndTimingPair()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string exact=Path.Combine(root,"exact.json"), scope=Path.Combine(root,"scope.json"), decisions=Path.Combine(root,"decisions.json");
        try
        {
            File.WriteAllText(scope,JsonSerializer.Serialize(new { Schema="exact-verification-scope-v2",RequiredCases=new[] {"rn","ra"},
                RequiredTimingPairs=4,ExactEqualityRequirementsUnchanged=true,UserSteering="Omit optional long replay" }));
            var frozenScope=new FinalArticleExecution.FileIdentity(scope,FinalArticleExecution.Hash(scope));
            File.WriteAllText(decisions,"{\"Schema\":\"resolved-final-article-specifications-v1\",\"Resolved\":true,\"UserDecisionRecord\":\"test-only decision\"}");
            void WriteExact(int pairs=4,bool strategyEqual=true) => File.WriteAllText(exact,JsonSerializer.Serialize(new {
                AcceptanceComplete=true,InstrumentedAndOrdinaryOutputsEqual=true,VerificationScope=frozenScope,
                Cases=new[] {"rn","ra"}.Select(id=>new {CaseId=id,CompletedEquivalence=true,EquivalenceResult=new {
                    PivotCount=id=="rn" ? 315 : 413,Passed=true,InitialEqual=true,ExactComparison=true,
                    CompleteStrategyEqual=strategyEqual,SavedReloadedEqual=true,FrozenProductionStrategyEqual=true}}).ToArray(),
                ComparedTimingPairs=Enumerable.Range(0,pairs).Select(i=>new {CaseId=i<2 ? "rn":"ra",Repetition=i%2+1,ExactOutputsEqual=true}).ToArray()}));
            FinalArticleExecution.Authorization Auth()=>new(new(exact,FinalArticleExecution.Hash(exact)),new(decisions,FinalArticleExecution.Hash(decisions)));
            WriteExact(); FinalArticleExecution.ValidateAuthorization(Auth());
            WriteExact(pairs:3); Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
            WriteExact(strategyEqual:false); Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
            WriteExact(); File.AppendAllText(scope," ");
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(Auth()));
        }
        finally
        {
            string allowed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests"));
            if(!FinalArticleExecution.Inside(root,allowed)) throw new InvalidOperationException("Test cleanup escaped scratch root.");
            Directory.Delete(root,true);
        }
    }

    [TestMethod]
    public void AdoptionCertificateRequiresExactReportsEvenWhenExtraTimingsAreOptional()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FinalArticleExecution.FileIdentity Save(string name,object value)
        {
            string path=Path.Combine(root,name+".json"); File.WriteAllText(path,JsonSerializer.Serialize(value));
            return new(path,FinalArticleExecution.Hash(path));
        }
        try
        {
            const string commit="56053d69fb4b9762f15538d897bf25d56151601a";
            var scope=Save("scope",new {Schema="exact-verification-scope-v2",RequiredCases=new[]{"rn","ra"}});
            var decisions=Save("decisions",new {Schema="resolved-final-article-specifications-v1",Resolved=true,UserDecisionRecord="test-only"});
            var output=Save("output",new {Passed=true,Checks=Enumerable.Range(0,5).Select(i=>new {exactEqual=true}).ToArray()});
            var cases=new[]{"rn","ra"}.Select(id=>new {
                CaseId=id,ExpectedPivots=id=="rn"?315:413,
                FullExactResult=Save(id,new {Passed=true,PivotCount=id=="rn"?315:413,InitialEqual=true,ExactComparison=true,
                    CompleteStrategyEqual=true,SavedReloadedEqual=true,FrozenProductionStrategyEqual=true}),
                ReferenceResult=Save(id+"-ref",new {Passed=true,PivotCount=id=="rn"?315:413}),ExactOutputComparison=output}).ToArray();
            var certificate=Save("certificate",new {Schema="exact-ecta-adoption-v1",Passed=true,ExactEqualityRequirementsUnchanged=true,
                AdditionalTimingPairsRequiredBeforeProduction=false,UserInstruction="Start production",CandidateCommit=commit,Scope=scope,Cases=cases,
                OrdinaryTimingEvidence=new[]{Save("timing1",new{Passed=true}),Save("timing2",new{Passed=true})},
                FocusedFixtureEvidence=new[]{Save("fixture1",new{Passed=true}),Save("fixture2",new{Passed=true}),
                    Save("fixture-run1",new{exitCode=0}),Save("fixture-run2",new{exitCode=0})},
                IntegralityProof=Save("proof",new{TestOnly=true}),SourceIdentities=Save("sources",new{CandidateCommit=commit})});
            var auth=new FinalArticleExecution.Authorization(certificate,decisions);
            FinalArticleExecution.ValidateAuthorization(auth);
            // Corrupt an exact-report artifact without changing the certificate:
            // extra timing freedom never permits changed mathematical evidence.
            File.AppendAllText(output.Path," ");
            Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateAuthorization(auth));
        }
        finally
        {
            string allowed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"FinalArticleGateTests"));
            if(!FinalArticleExecution.Inside(root,allowed)) throw new InvalidOperationException("Test cleanup escaped scratch root.");
            Directory.Delete(root,true);
        }
    }

    [TestMethod]
    public void OutputContainmentRejectsSiblingAndParentTraversal()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"isolated","production"));
        Assert.IsTrue(FinalArticleExecution.Inside(Path.Combine(root,"queue"),root));
        Assert.IsFalse(FinalArticleExecution.Inside(root,root));
        Assert.IsFalse(FinalArticleExecution.Inside(root+"-other",root));
        Assert.IsFalse(FinalArticleExecution.Inside(Path.Combine(root,"..","source"),root));
    }
}

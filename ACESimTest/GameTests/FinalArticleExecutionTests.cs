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
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleExecution.ValidateBudget(budget with { GlobalCeiling=31 },26));
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
    public void OutputContainmentRejectsSiblingAndParentTraversal()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"isolated","production"));
        Assert.IsTrue(FinalArticleExecution.Inside(Path.Combine(root,"queue"),root));
        Assert.IsFalse(FinalArticleExecution.Inside(root,root));
        Assert.IsFalse(FinalArticleExecution.Inside(root+"-other",root));
        Assert.IsFalse(FinalArticleExecution.Inside(Path.Combine(root,"..","source"),root));
    }
}

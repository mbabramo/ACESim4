using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingAlgorithms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass,DoNotParallelize]
public class ArticleApproximateSearchTests
{
    [TestMethod,Timeout(120000)]
    public async Task FloatingFixtureAuditsEveryPivotAndRetainsAllAgreementInformationSets()
    {
        // Bounded algorithm fixture only; this is not an article simulation or an
        // alternative representation used by exact optimization or production.
        var options=new LitigGameCorrelatedSignalsArticleLauncher(LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
            .GetOptionsSets().Cast<LitigGameOptions>().Single(o=>o.CostsMultiplier==1 && o.LoserPaysMultiple==0 && Convert.ToDouble(o.VariableSettings["CARA Alpha"])==0);
        options.NumLiabilitySignals=2;options.NumOffers=2;
        var developer=(SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(options);
        int entries=developer.InformationSets.Sum(n=>n.NumPossibleActions);
        Assert.AreEqual(8,developer.InformationSets.Count(n=>(LitigGameDecisions)n.DecisionByteCode is LitigGameDecisions.PAgreeToBargain or LitigGameDecisions.DAgreeToBargain));
        var audits=new List<ArticleApproximateSearch.PivotAudit>(); double[] prior=null;
        var policy=new ArticleApproximatePolicy(0,ArticleApproximateGainUnits.FullTerminalUtilityRange);
        var result=ArticleApproximateSearch.Run(developer,options,0,policy,audits.Add,p=>prior=p);
        Assert.AreEqual(1_000_000,result.ActualPriorSeed);
        Assert.AreEqual("InexactValue (double); no exact fallback",result.Arithmetic);
        Assert.AreEqual(entries,prior.Length);
        Assert.IsTrue(audits.Count>1);
        CollectionAssert.AreEqual(Enumerable.Range(1,audits.Count).ToArray(),audits.Select(a=>a.Pivot).ToArray());
        Assert.AreEqual(audits.Count,result.EvaluatedPivots);
        Assert.IsNotNull(result.Decision);
        Assert.IsTrue(audits.Any(a=>a.Valid));
        if(result.BestCandidate is {} best)
        {
            Assert.AreEqual(entries,best.Probabilities.Length);
            ArticleWorkedPathExtraction.LoadProfile(developer,best.Probabilities);
            developer.CalculateBestResponse(false);
            double average=policy.Scale(developer.Status.BestResponseImprovement.ToArray(),result.TerminalUtilityRanges).Average();
            Assert.AreEqual(best.AverageGain,average);
            Assert.AreEqual(audits.Where(a=>a.Valid).Min(a=>a.AverageGain.Value),best.AverageGain);
        }
        if(result.Decision.Accepted is {} accepted)
            Assert.IsTrue(accepted.AverageGain<(result.Decision.Reason=="cap-accepted" ? 0.0025 : 0.001));
    }
}

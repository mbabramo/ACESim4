using ACESim;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class FinalArticleSignalTests
{
    private static FinalArticleCase Case(string distribution, double party, double court) => new()
    {
        Id="signal-check",Family="signals",Variant="explicit",FeeRule="american",AlphaP=0,AlphaD=0,
        CostMultiplier=1,Signals=10,Offers=LitigGameOptions.CreateFixedSupportOffers(10,.05,.95),
        Distribution=distribution,PartySigma=party,CourtSigma=court,EntryCost=.15,TrialCost=.15,
        CalibrationSha256=distribution=="direct-binary" ? new string('a',64) : null
    };

    [TestMethod]
    public void ExplicitNewSignalOptionsControlAllThreeViewsAndBothPalettes()
    {
        foreach(var c in new[]{Case("uniform",.1,.2),Case("uniform",.2,.4),Case("beta-2-2",.2,.2),
            Case("beta-half-half",.2,.2),Case("direct-binary",.36495,.312719)})
        {
            string spec=FinalArticleSignalCommand.Specification(c);
            var forward=ArticleSignalDiagrams.Generate(spec,false,FinalArticleCaseFactory.Create(c));
            var party=forward.SelectMany(d=>d.Panels).Single(p=>p.DestinationTitle.StartsWith("Party signal"));
            var inverse=ArticleSignalDiagrams.GenerateInverse(spec,false,FinalArticleCaseFactory.Create(c)).Single().Panels.Single();
            var prediction=ArticleSignalDiagrams.GeneratePartyToParty(spec,false,FinalArticleCaseFactory.Create(c)).Single().Panels.Single();
            for(int i=0;i<c.Signals;i++)
            {
                Assert.AreEqual(party.JointMass.Sum(r=>r[i]),prediction.JointMass[i].Sum(),1E-7);
                for(int q=0;q<party.JointMass.Length;q++)Assert.AreEqual(party.JointMass[q][i],inverse.JointMass[i][q]);
            }
            var bw=ArticleSignalDiagrams.Generate(spec,true,FinalArticleCaseFactory.Create(c));
            Assert.AreEqual(JsonSerializer.Serialize(forward.SelectMany(x=>x.Panels)),JsonSerializer.Serialize(bw.SelectMany(x=>x.Panels)));
            Assert.IsTrue(forward.All(d=>d.Description.Contains("Final-Agreement__signal-check")));
        }
    }

    [TestMethod]
    public void NewDirectBinaryCannotSilentlyUseHistoricalCalibratedNoise()
    {
        var c=Case("direct-binary",.36495,.312719);
        var current=ArticleSignalDiagrams.GeneratePartyToParty("DirectBinaryStateSignals",false,FinalArticleCaseFactory.Create(c)).Single().Panels.Single();
        var historical=ArticleSignalDiagrams.GeneratePartyToParty("DirectBinaryStateSignals",false).Single().Panels.Single();
        Assert.IsTrue(current.JointMass.SelectMany(x=>x).Zip(historical.JointMass.SelectMany(x=>x),(a,b)=>Math.Abs(a-b)).Max()>1E-6);
        using var json=JsonDocument.Parse("{\"Signals\":10,\"PartyFit\":{\"Sigma\":0.36495},\"CourtFit\":{\"Sigma\":0.312719}}");
        var file=new FinalArticleExecution.FileIdentity("unused",new string('a',64));
        FinalArticleSignalCommand.ValidateCalibration(c,file,json.RootElement);
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleSignalCommand.ValidateCalibration(c with{PartySigma=.2},file,json.RootElement));
        Assert.ThrowsException<InvalidDataException>(()=>FinalArticleSignalCommand.ValidateCalibration(c,file with{Sha256=new string('b',64)},json.RootElement));
    }
}

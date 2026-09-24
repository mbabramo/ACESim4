using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.GameTree;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass,DoNotParallelize]
public class FinalArticleStructureTests
{
    [TestMethod]
    public async Task IllustratedPathsKeepCommitmentsPrivateAndOffersBehindMutualAgreement()
    {
        var developer=await ArticleGameTreeDiagrams.InitializeAsync(false,true);
        var recorder=new RecordGamePathsProcessor();developer.TreeWalk_Tree(recorder);
        var paths=recorder.Paths.Select(p=>p.Steps.Where(s=>s.FromNode is InformationSetNode)
            .Select(s=>(Node:(InformationSetNode)s.FromNode,Action:s.ActionIndex)).ToArray()).ToArray();
        foreach(var path in paths.Where(p=>p.Any(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.PAgreeToBargain)))
        {
            var codes=path.Select(s=>s.Node.DecisionByteCode).ToList();
            codes.IndexOf((byte)LitigGameDecisions.PAbandon).Should().BeLessThan(codes.IndexOf((byte)LitigGameDecisions.PAgreeToBargain));
            codes.IndexOf((byte)LitigGameDecisions.DDefault).Should().BeLessThan(codes.IndexOf((byte)LitigGameDecisions.PAgreeToBargain));
            bool both=path.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.PAgreeToBargain).Action==1 &&
                path.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.DAgreeToBargain).Action==1;
            codes.Contains((byte)LitigGameDecisions.POffer).Should().Be(both);
            codes.Contains((byte)LitigGameDecisions.DOffer).Should().Be(both);
        }
        var grouped=paths.Where(p=>p.Any(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.DAgreeToBargain))
            .GroupBy(p=>p.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.DAgreeToBargain).Node.InformationSetNodeNumber);
        grouped.Should().HaveCount(4,"D has two signals and two own commitments");
        foreach(var group in grouped)
        {
            group.Select(p=>p.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.PAgreeToBargain).Action).Distinct().Should().HaveCount(2);
            group.Select(p=>p.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.PAbandon).Action).Distinct().Should().HaveCount(2);
            group.Select(p=>p.Single(s=>s.Node.DecisionByteCode==(byte)LitigGameDecisions.DDefault).Action).Distinct().Should().HaveCount(1);
        }
        var full=developer.TreeWalk_Tree(new CalculateUtilitiesAtEachInformationSet());
        var simple=await ArticleGameTreeDiagrams.InitializeAsync(true,true);
        var integrated=simple.TreeWalk_Tree(new CalculateUtilitiesAtEachInformationSet());
        for(int player=0;player<full.Length;player++)full[player].Should().BeApproximately(integrated[player],1e-10);
    }
}

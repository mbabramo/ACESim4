using System;
using System.IO;
using System.Linq;
using ACESim;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.GameTests;

[TestClass]
public class ArticleCompletedCasesTests
{
    [TestMethod]
    public void EveryRoutineCaseUsesTheExistingFigureAndTableVocabulary()
    {
        var cases=LitigGameCorrelatedSignalsArticleLauncher.RoutineCaseMatrix();
        var paths=cases.Select(c=>Path.Combine(ArticleResultsLayout.Individual("root",ArticleCompletedCasesCommand.LayoutRow(c)),
            ArticleResultsLayout.Cost(c.Cost))).ToArray();
        Assert.AreEqual(cases.Count,paths.Distinct().Count());
        foreach(var c in cases)
        {
            string path=ArticleResultsLayout.Individual("root",ArticleCompletedCasesCommand.LayoutRow(c));
            StringAssert.Contains(path,c.FeeRule);
            StringAssert.Contains(path,c.Risk=="Risk Neutral"?"Risk Neutral":"Risk Averse");
        }
    }

    [TestMethod]
    public void IndividualSourcesRejectIncompleteCasesAndDoNotMixSimilarNames()
    {
        var temp=Directory.CreateTempSubdirectory("article-case-test-");
        try
        {
            foreach(string suffix in new[]{"offers","fileans","costbreakdowndark","costbreakdownlight","stagecostdark","stagecostlight"})
                File.WriteAllText(Path.Combine(temp.FullName,"CS004 example -"+suffix+".tex"),"diagram");
            File.WriteAllText(Path.Combine(temp.FullName,"CS004 example-longer -offers.tex"),"wrong case");
            var files=ArticleResultsCommand.IndividualSources("display",temp.FullName,"CS004 example",.5);
            Assert.AreEqual(6,files.Count);
            Assert.IsTrue(files.Keys.All(p=>Path.GetFileName(p).StartsWith("cost-0.5-")));
            Assert.IsTrue(files.Values.All(v=>v=="diagram"));
            File.Delete(Path.Combine(temp.FullName,"CS004 example -offers.tex"));
            Assert.ThrowsException<InvalidDataException>(()=>ArticleResultsCommand.IndividualSources("display",temp.FullName,"CS004 example",.5));
        }
        finally{temp.Delete(true);}
    }
}

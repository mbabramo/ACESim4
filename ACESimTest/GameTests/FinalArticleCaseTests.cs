using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Mathematics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.GameTests
{
    [TestClass, DoNotParallelize]
    public class FinalArticleCaseTests
    {
        private static FinalArticleCase Baseline() => new()
        {
            Id = "baseline__standard__american__rn__cost-1", Family = "baseline", Variant = "standard", FeeRule = "american",
            AlphaP = 0, AlphaD = 0, CostMultiplier = 1, Signals = 10,
            Offers = LitigGameOptions.CreateFixedSupportOffers(10, 0.05, 0.95), Distribution = "uniform",
            PartySigma = 0.2, CourtSigma = 0.2, EntryCost = 0.15, TrialCost = 0.15,
            OriginalOptionName = "Agreement-Enabled__Specification-Baseline__Cost-1__Fee-American"
        };

        [TestMethod]
        public void FinalPressureHeadingsHandleNewFamiliesAndAsymmetricRisk()
        {
            var source=Baseline() with { OriginalOptionName=null, Id="asymmetric-american",Family="asymmetric-risk",Variant="p-only-ra",AlphaP=2 };
            var target=source with { Id="asymmetric-complete",FeeRule="complete" };
            var heading=LitigCharts.EquilibriumChangeTables.Heading(source,target);
            StringAssert.Contains(heading.HeldFixed,"plaintiff only risk averse");
            Assert.IsFalse(heading.HeldFixed.Contains("Both players"));
            var noise=Baseline() with { OriginalOptionName=null,Id="private-noise-rn",Family="private-noise",Variant="low",PartySigma=0.1 };
            var risk=noise with { Id="private-noise-ra",AlphaP=2,AlphaD=2 };
            StringAssert.Contains(LitigCharts.EquilibriumChangeTables.Heading(noise,risk).Title,"Preferences:");
            Assert.ThrowsException<InvalidDataException>(()=>LitigCharts.EquilibriumChangeTables.Heading(noise,risk with { CourtSigma=0.4 }));
        }

        [TestMethod]
        public void ImportsCannotChangeGameAndAsymmetricRiskHasExplicitMetadata()
        {
            var baseline = Baseline();
            var original = FinalArticleCaseFactory.Create(baseline);
            Assert.AreEqual(baseline.OriginalOptionName, original.Name);
            Assert.IsNull(original.ExplicitOfferValues);
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleCaseFactory.Create(baseline with { Signals = 8 }));
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleCaseFactory.Create(baseline with { CourtSigma = 0.1 }));
            var asym = FinalArticleCaseFactory.Create(baseline with { OriginalOptionName = null, Family = "asymmetric-risk", Variant = "p-only-ra", AlphaP = 2 });
            Assert.IsInstanceOfType<CARARiskAverseUtilityCalculator>(asym.PUtilityCalculator);
            Assert.IsInstanceOfType<RiskNeutralUtilityCalculator>(asym.DUtilityCalculator);
            Assert.AreEqual("Asymmetric", asym.VariableSettings["CARA Alpha"]);
            Assert.IsTrue(asym.IncludeAgreementToBargainDecisions);
            Assert.IsTrue(asym.PredeterminedAbandonAndDefaults);
            Assert.ThrowsException<InvalidDataException>(() => FinalArticleCaseFactory.Create(baseline with { OriginalOptionName = null, Distribution = "direct-binary" }));
        }

        [TestMethod]
        public async Task FingerprintIgnoresNamesButDetectsInformationAndActionChanges()
        {
            var options = FinalArticleCaseFactory.Create(Baseline());
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
            var original = StrategicGameFingerprint.Capture(developer);
            Assert.AreEqual(47611, original.TreeNodes);
            Assert.AreEqual(120, original.InformationSets);
            Assert.AreEqual(560, original.StrategyEntries);
            var chanceBefore = developer.ChanceNodes.SelectMany(c => c.GetActionProbabilities()).ToArray();
            options.Name = "Only a display-name change";
            var renamed = StrategicGameFingerprint.Capture(developer);
            Assert.AreEqual(original.CompleteSha256, renamed.CompleteSha256);
            CollectionAssert.AreEqual(chanceBefore, developer.ChanceNodes.SelectMany(c => c.GetActionProbabilities()).ToArray());
            var info = developer.InformationSets.First();
            string originalCoordinateHash = original.CoordinatesSha256;
            byte saved = info.InformationSetContents[0];
            info.InformationSetContents[0] ^= 1;
            Assert.AreNotEqual(originalCoordinateHash, StrategicGameFingerprint.Capture(developer).CoordinatesSha256);
            info.InformationSetContents[0] = saved;
            Assert.AreEqual(original.CompleteSha256, StrategicGameFingerprint.Capture(developer).CompleteSha256);
            var changedOffers = options.GetOfferValues(); changedOffers[1] = 0.16;
            options.ExplicitOfferValues = changedOffers;
            Assert.AreNotEqual(original.CompleteSha256, StrategicGameFingerprint.Capture(developer).CompleteSha256);
            options.ExplicitOfferValues = null;
            var disabledOptions = FinalArticleCaseFactory.Create(Baseline());
            disabledOptions.IncludeAgreementToBargainDecisions = false;
            var disabled = await ArticleWorkedPathExtraction.InitializeAsync(disabledOptions);
            Assert.AreNotEqual(original.CompleteSha256, StrategicGameFingerprint.Capture(disabled).CompleteSha256);
        }

        [DataTestMethod]
        [DataRow(8, 15, 63753, 96, 708)]
        [DataRow(12, 8, 46669, 144, 724)]
        [DataRow(8, 8, 20745, 96, 484)]
        public async Task FullDeclaredGridHasExpectedUnreducedTree(int signals, int offers, int nodes, int informationSets, int lcp)
        {
            var spec = Baseline() with { OriginalOptionName = null, Family = "grid", Variant = $"s{signals}-o{offers}",
                Signals = checked((byte)signals), Offers = LitigGameOptions.CreateFixedSupportOffers(offers, 0.05, 0.95) };
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(FinalArticleCaseFactory.Create(spec));
            var identity = StrategicGameFingerprint.Capture(developer);
            Assert.AreEqual(nodes, identity.TreeNodes);
            Assert.AreEqual(informationSets, identity.InformationSets);
            Assert.AreEqual(lcp, identity.StrategyEntries+identity.InformationSets+4);
        }
    }
}

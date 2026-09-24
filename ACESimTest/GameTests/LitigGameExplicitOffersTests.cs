using ACESim;
using ACESimBase.Games.LitigGame;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass, DoNotParallelize]
    public class LitigGameExplicitOffersTests
    {
        [TestMethod]
        public void LegacyAndExplicitBaselineRetainEveryBit()
        {
            foreach (bool endpoints in new[] { false, true })
            foreach (byte count in new byte[] { 1, 2, 8, 10, 15 })
            {
                var options = new LitigGameOptions { NumOffers = count, IncludeEndpointsForOffers = endpoints };
                options.ValidateOfferValues();
                for (int action = 1; action <= count; action++)
                    Assert.AreEqual(BitConverter.DoubleToInt64Bits(Game.ConvertActionToUniformDistributionDraw(action, count, endpoints)),
                        BitConverter.DoubleToInt64Bits(options.GetOfferValue(action)));
            }
            var baseline = new LitigGameOptions { NumOffers = 10, ExplicitOfferValues = LitigGameOptions.CreateFixedSupportOffers(10, 0.05, 0.95) };
            baseline.ValidateOfferValues();
            for (int action = 1; action <= 10; action++)
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(Game.ConvertActionToUniformDistributionDraw(action, 10, false)),
                    BitConverter.DoubleToInt64Bits(baseline.GetOfferValue(action)));
        }

        [TestMethod]
        public void GridDoesNotChangeSupportAndCannotBeMutatedThroughAliases()
        {
            foreach (byte count in new byte[] { 8, 12, 15 })
            {
                var input = LitigGameOptions.CreateFixedSupportOffers(count, 0.05, 0.95);
                var options = new LitigGameOptions { NumOffers = count, ExplicitOfferValues = input };
                options.ValidateOfferValues();
                input[0] = 0;
                var output = options.ExplicitOfferValues;
                output[count - 1] = 1;
                Assert.AreEqual(0.05, options.GetOfferValue(1));
                Assert.AreEqual(0.95, options.GetOfferValue(count));
                var values = options.GetOfferValues();
                Assert.IsTrue(values.Zip(values.Skip(1), (a, b) => b > a).All(x => x));
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.GetOfferValue(0));
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.GetOfferValue(count + 1));
            }
        }

        [TestMethod]
        public void InvalidOrAmbiguousOfferSpecificationsAreRejectedBeforeTreeConstruction()
        {
            foreach (var values in new[] {
                new[] { 0.5 }, new[] { 0.9, 0.1 }, new[] { 0.5, 0.5 },
                new[] { double.NaN, 0.9 }, new[] { 0.1, double.PositiveInfinity },
                new[] { -0.1, 0.9 }, new[] { 0.1, 1.1 } })
            {
                var options = new LitigGameOptions { NumOffers = 2, ExplicitOfferValues = values };
                Assert.ThrowsException<ArgumentException>(() => options.ValidateOfferValues());
            }
            var ambiguous = new LitigGameOptions { NumOffers = 2, ExplicitOfferValues = new[] { 0.1, 0.9 },
                DeltaOffersOptions = new DeltaOffersOptions { SubsequentOffersAreDeltas = true } };
            Assert.ThrowsException<ArgumentException>(() => ambiguous.ValidateOfferValues());
        }

        [TestMethod]
        public void EveryFixedSupportOfferAgreesInOrdinaryAndCollapsedSettlementPlay()
        {
            foreach (byte offers in new byte[] { 8, 10, 15 })
            foreach (bool risk in new[] { false, true })
            {
                var options = new LitigGameCorrelatedSignalsArticleLauncher(
                    LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                    .GetOptionsSets().Cast<LitigGameOptions>().Single(o => o.CostsMultiplier == 1 && o.LoserPaysAfterAbandonment &&
                        (Convert.ToDouble(o.VariableSettings["CARA Alpha"]) == 2) == risk);
                options.NumOffers = offers;
                options.ExplicitOfferValues = LitigGameOptions.CreateFixedSupportOffers(offers, 0.05, 0.95);
                LitigGameProgress Play(byte offer, bool collapse)
                {
                    options.CollapseAlternativeEndings = collapse;
                    return LitigGameLauncherBase.PlayLitigGameOnce(options, (decision, progress) =>
                        (LitigGameDecisions)decision.DecisionByteCode switch {
                            LitigGameDecisions.PLiabilitySignal or LitigGameDecisions.DLiabilitySignal => 5,
                            LitigGameDecisions.POffer or LitigGameDecisions.DOffer => offer,
                            _ => 1
                        });
                }
                for (byte action = 1; action <= offers; action++)
                {
                    var ordinary = Play(action, false);
                    var collapsed = Play(action, true);
                    Assert.IsTrue(ordinary.CaseSettles && collapsed.CaseSettles);
                    Assert.AreEqual(options.GetOfferValue(action), ordinary.SettlementValue.Value);
                    Assert.AreEqual(ordinary.SettlementValue, collapsed.SettlementValue);
                    Assert.AreEqual(ordinary.PFinalWealth, collapsed.PFinalWealth, 1e-12);
                    Assert.AreEqual(ordinary.DFinalWealth, collapsed.DFinalWealth, 1e-12);
                    Assert.AreEqual(ordinary.PWelfare, collapsed.PWelfare, 1e-10);
                    Assert.AreEqual(ordinary.DWelfare, collapsed.DWelfare, 1e-10);
                }
            }
        }
    }
}

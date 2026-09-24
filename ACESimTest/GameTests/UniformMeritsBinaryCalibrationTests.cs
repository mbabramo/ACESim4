using ACESimBase.Games.LitigGame.ManualReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class UniformMeritsBinaryCalibrationTests
    {
        [TestMethod]
        public void TwoStageFitRecoversKnownSignalKernelWithoutUsingEquilibriumOutcomes()
        {
            var party = UniformMeritsBinaryCalibration.BinaryJoint(10, 0.35, 0.30, false);
            var three = UniformMeritsBinaryCalibration.BinaryJoint(10, 0.35, 0.30, true);
            var partyFit = UniformMeritsBinaryCalibration.Minimize(s => UniformMeritsBinaryCalibration.KL(party,
                UniformMeritsBinaryCalibration.BinaryJoint(10, s, s, false)));
            var courtFit = UniformMeritsBinaryCalibration.Minimize(s => UniformMeritsBinaryCalibration.KL(three,
                UniformMeritsBinaryCalibration.BinaryJoint(10, partyFit.Sigma, s, true)));
            Assert.AreEqual(0.35, partyFit.Sigma, 1e-7);
            Assert.AreEqual(0.30, courtFit.Sigma, 1e-7);
            Assert.AreEqual(1, party.Sum(), 1e-12); Assert.AreEqual(1, three.Sum(), 1e-12);
            Assert.IsTrue(Math.Abs(partyFit.Divergence) < 1e-12);
            Assert.IsTrue(Math.Abs(courtFit.Divergence) < 1e-12);
            Assert.AreEqual(100 + 2, partyFit.Evaluations.Count(e => e.Stage == "refine"));
        }
    }
}

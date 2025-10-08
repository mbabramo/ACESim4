using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.GameSolvingSupport.FastCFR;

namespace ACESimBase.GameSolvingSupport.FastCFR.Tests
{
    public static class TestTrees
    {
        public static void InitializeVectorPolicies(
            FastCFRInformationSetVec node,
            double[][] ownerPolicyByLane,
            double[][] oppPolicyByLane)
        {
            node.InitializeIterationVec(ownerPolicyByLane, oppPolicyByLane);
        }
    }

    [TestClass]
    public class FastCFRVectorTests
    {
        [TestMethod]
        public void DotPerLane_Works()
        {
            double[] weights = { 0.2, 0.3, 0.5 };

            double[] action0LaneValues = { 1, 2, 3, 4 };
            double[] action1LaneValues = { 10, 20, 30, 40 };
            double[] action2LaneValues = { -1, 0, 1, 2 };

            double[][] perActionLaneValues = { action0LaneValues, action1LaneValues, action2LaneValues };

            byte[] activeMask = { 1, 1, 0, 1 };

            double[] resultPerLane = new double[4];

            FastCFRVecMath.DotPerLane(weights, perActionLaneValues, activeMask, resultPerLane);

            resultPerLane[0].Should().BeApproximately(2.7, 1e-12);
            resultPerLane[1].Should().BeApproximately(6.4, 1e-12);
            resultPerLane[2].Should().BeApproximately(0.0, 1e-12);
            resultPerLane[3].Should().BeApproximately(13.8, 1e-12);
        }
    }
}

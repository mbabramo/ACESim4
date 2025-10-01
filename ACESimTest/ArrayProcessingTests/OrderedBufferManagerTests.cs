// -----------------------------------------------------------------------------
//  OrderedBufferManagerTests.cs  (MSTest + FluentAssertions)
// -----------------------------------------------------------------------------
//  Direct unit‑tests for OrderedBufferManager covering serial and parallel paths.
// -----------------------------------------------------------------------------

using System;
using System.Linq;
using ACESimBase.Util.ArrayProcessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public sealed class OrderedBufferManagerTests
    {
        /// <summary>
        /// Helper: creates a buffer of length 10 containing 0,1,2,…,9.
        /// </summary>
        private static double[] MakeSequentialArray(int len = 10)
            => Enumerable.Range(0, len).Select(i => (double)i).ToArray();

        [TestMethod]
        public void PrepareBuffers_CopiesSources()
        {
            // Arrange
            var obm = new OrderedBufferManager();
            obm.SourceIndices.AddRange(new OsIndex[] { 2.Os(), 4.Os() });

            double[] data = MakeSequentialArray();

            // Act
            obm.PrepareBuffers(data, parallel: false);

            // Assert
            obm.Sources.Should().Equal(new[] { 2.0, 4.0 });
        }

        [TestMethod]
        public void ApplyDestinations_AppliesIncrementsAndAccumulates()
        {
            var mgr = new OrderedBufferManager
            {
                DestinationIndices = { 1, 1, 3 }
            };

            double[] data = { 10.0, 20.0, 30.0, 40.0 };
            mgr.PrepareBuffers(data); // initializes Destinations = [0,0,0]

            // simulate executor writes
            mgr.Destinations[0] = 1.5;  // to index 1
            mgr.Destinations[1] = 2.5;  // also to index 1
            mgr.Destinations[2] = 5.0;  // to index 3

            mgr.ApplyDestinations(data, parallel: false);

            Assert.AreEqual(10.0, data[0], 1e-12);
            Assert.AreEqual(20.0 + 1.5 + 2.5, data[1], 1e-12, "increments to same index should accumulate");
            Assert.AreEqual(30.0, data[2], 1e-12);
            Assert.AreEqual(40.0 + 5.0, data[3], 1e-12);
        }

        [TestMethod]
        public void ApplyDestinations_SerialAndParallelProduceSameResults()
        {
            var mgr = new OrderedBufferManager
            {
                DestinationIndices = { 0, 1, 2, 3 }
            };

            double[] baseline = { 1, 2, 3, 4 };
            double[] serial = (double[])baseline.Clone();
            double[] parallel = (double[])baseline.Clone();

            // fill Destinations with known pattern
            mgr.PrepareBuffers(baseline);
            for (int i = 0; i < mgr.Destinations.Length; i++)
                mgr.Destinations[i] = i + 0.5;

            // serial apply
            mgr.ApplyDestinations(serial, parallel: false);

            // reset, fill again
            mgr.PrepareBuffers(baseline);
            for (int i = 0; i < mgr.Destinations.Length; i++)
                mgr.Destinations[i] = i + 0.5;

            // parallel apply
            mgr.ApplyDestinations(parallel, parallel: true);

            CollectionAssert.AreEqual(serial, parallel, "serial and parallel must yield identical results");
        }

        [TestMethod]
        public void ApplyDestinations_NoEffectWhenAllZeros()
        {
            var mgr = new OrderedBufferManager
            {
                DestinationIndices = { 0, 2 }
            };

            double[] data = { 5, 6, 7 };
            mgr.PrepareBuffers(data);

            // leave Destinations zeroed
            mgr.ApplyDestinations(data);

            CollectionAssert.AreEqual(new[] { 5.0, 6.0, 7.0 }, data, "zeros in Destinations must not change original data");
        }

    }
}

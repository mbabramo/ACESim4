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
        public void PrepareBuffers_CopiesSourcesAndClearsDestinations()
        {
            // Arrange
            var obm = new OrderedBufferManager();
            obm.SourceIndices.AddRange(new[] { 2, 4 });
            obm.DestinationIndices.AddRange(new[] { 5 });

            double[] data = MakeSequentialArray();

            // Act
            obm.PrepareBuffers(data, parallel: false);

            // Assert
            obm.Sources.Should().Equal(new[] { 2.0, 4.0 });
            obm.Destinations.Should().AllBeEquivalentTo(0.0);
        }

        [TestMethod]
        public void FlushDestinations_Serial_AggregatesDuplicateIndices()
        {
            // Arrange
            var obm = new OrderedBufferManager();
            obm.DestinationIndices.AddRange(new[] { 5, 5, 7 });
            double[] data = new double[10];

            // Stage some results (simulate executor writes)
            obm.Destinations = new[] { 10.0, 3.0, 4.0 };

            // Act
            obm.FlushDestinations(data, parallel: false);

            // Assert
            data[5].Should().Be(13.0);
            data[7].Should().Be(4.0);
            data.Where((v, idx) => idx != 5 && idx != 7)
                .Should().AllBeEquivalentTo(0.0);
        }

        [TestMethod]
        public void FlushDestinations_Parallel_AggregatesDuplicateIndices()
        {
            // Arrange (same set‑up as previous test)
            var obm = new OrderedBufferManager();
            obm.DestinationIndices.AddRange(new[] { 5, 5, 7 });
            double[] data = new double[10];
            obm.Destinations = new[] { 2.0, 8.0, 6.0 }; // totals will be 10 + 6

            // Act – parallel path
            obm.FlushDestinations(data, parallel: true);

            // Assert
            data[5].Should().Be(10.0); // 2 + 8
            data[7].Should().Be(6.0);
        }
    }
}

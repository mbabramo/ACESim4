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
            obm.SourceIndices.AddRange(new[] { 2, 4 });

            double[] data = MakeSequentialArray();

            // Act
            obm.PrepareBuffers(data, parallel: false);

            // Assert
            obm.Sources.Should().Equal(new[] { 2.0, 4.0 });
        }
    }
}

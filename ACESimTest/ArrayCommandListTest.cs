using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ACESim;
using ACESim.Util;
using ACESimBase.Util.ArrayProcessing;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class ArrayCommandListTest
    {
        [TestMethod]
        public void ArrayCommandList_CopyFromSourceAndToDestination()
        {
            bool parallel = true;
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] values = new double[20] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallel);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(parallel, null, "Chunk");

            int source = sourceIndicesStart + 2;
            int result = cl.CopyToNew(source, true);
            cl.Increment(destinationIndicesStart + 1, true, result);

            cl.EndCommandChunk();


            cl.CompleteCommandList();
            cl.ExecuteAll(values, false);
            values[destinationIndicesStart + 1].Should().BeApproximately(20, 0.001);
        }

        [TestMethod]
        public void ArrayCommandListBasic()
        {
            bool parallel = true;
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;
            
            double[] values = new double[20] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, parallel);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(parallel, null, "Chunk");

            int v0_10 = cl.CopyToNew(sourceIndicesStart + 1 /* 10 */, fromOriginalSources: true); // example of copying from source
            int v1_10 = cl.CopyToNew(v0_10, fromOriginalSources: false);
            int v2_50 = cl.CopyToNew(sourceIndicesStart + 5 /* 50 */, fromOriginalSources: true);
            int v3_60 = cl.AddToNew(v0_10, false, v2_50);
            int v4_80 = cl.AddToNew(sourceIndicesStart + 2 /* 20 */, true, v3_60);
            int v5_4800 = cl.MultiplyToNew(v4_80, false, v3_60);
            cl.Increment(destinationIndicesStart + 0, true, v5_4800); // example of incrementing to destination --> destination 0 is 4800

            int[] sources = new int[] { sourceIndicesStart + 2 /* 20 */, sourceIndicesStart + 3 /* 30 */, sourceIndicesStart + 4 /* 40 */};
            int[] sourcesCopied = cl.CopyToNew(sources, true);
            cl.MultiplyArrayBy(sourcesCopied, sourcesCopied); // 40, 900, 1600
            cl.MultiplyBy(sourcesCopied[1] /* 900 */, sourcesCopied[2] /* 1600 */); // 900 * 1600 = 1_440_000 in sourcesCopied[1]
            int v6 = cl.CopyToNew(sources[0], true); // 20
            cl.DecrementArrayBy(sourcesCopied, v6); // 1_439_980 in sourcesCopied[1]
            cl.IncrementByProduct(sourcesCopied[1], false, v6, v6); // + 400 = 1_440_380
            cl.Increment(destinationIndicesStart + 1, true, sourcesCopied[1]); // copied to target

            cl.EndCommandChunk();
            cl.CompleteCommandList();
            cl.ExecuteAll(values, false);

            values[destinationIndicesStart + 0].Should().BeApproximately(4800, 0.001);
            values[destinationIndicesStart + 1].Should().BeApproximately(1_440_380, 0.001);
        }


    }
}

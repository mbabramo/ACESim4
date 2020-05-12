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
            cl.StartCommandChunk(false, null, "Chunk");

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

        [TestMethod]
        public void ArrayCommandList_Conditional()
        {
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] sourceValues = new double[20] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; 
            int[] sourceIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 100;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, false);
            cl.MinNumCommandsToCompile = 1;
            cl.StartCommandChunk(false, null, "Chunk");

            int[] copiedValues = cl.CopyToNew(sourceIndices, true);
            int v1 = cl.NewZero();

            cl.InsertEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[0]); // since true, add 1 => 1
            cl.InsertEndIfCommand();

            cl.InsertEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[1]); // since false => 1
            cl.InsertEndIfCommand();

            cl.InsertNotEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[2]); // since false => 1
            cl.InsertEndIfCommand();

            cl.InsertNotEqualsOtherArrayIndexCommand(copiedValues[2], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[3]); // since false => 1 + 8 => 9
            cl.InsertEndIfCommand();

            cl.InsertGreaterThanOtherArrayIndexCommand(copiedValues[2], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[4]); // since false => 9
            cl.InsertEndIfCommand();

            cl.InsertGreaterThanOtherArrayIndexCommand(copiedValues[4], copiedValues[3]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[5]); // since true => 9 + 32 => 41
            cl.InsertEndIfCommand();

            cl.InsertLessThanOtherArrayIndexCommand(copiedValues[3], copiedValues[2]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[6]); // since false => 41
            cl.InsertEndIfCommand();

            cl.InsertLessThanOtherArrayIndexCommand(copiedValues[4], copiedValues[5]);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[7]); // since true => 41 + 128 = 169
            cl.InsertEndIfCommand();

            cl.InsertEqualsValueCommand(copiedValues[4], 999);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[8]); // since false => 169
            cl.InsertEndIfCommand();

            cl.InsertEqualsValueCommand(copiedValues[5], 32);
            cl.InsertIfCommand();
            cl.Increment(v1, false, copiedValues[9]); // since true => 169 + 512 = 681
            cl.InsertEndIfCommand();

            cl.Increment(destinationIndicesStart + 1, true, v1);

            cl.EndCommandChunk();


            cl.CompleteCommandList();
            cl.ExecuteAll(sourceValues, false);
            sourceValues[destinationIndicesStart + 1].Should().BeApproximately(681, 0.001);
        }

        [TestMethod]
        public void ArrayCommandList_ParallelChunk()
        {
            const int sourceIndicesStart = 0;
            const int totalSourceIndices = 10;
            const int totalDestinationIndices = 10;
            const int destinationIndicesStart = sourceIndicesStart + totalSourceIndices;
            const int totalIndices = sourceIndicesStart + totalSourceIndices + totalDestinationIndices;

            double[] sourceValues = new double[20] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int[] sourceIndices = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] destinationIndices = new int[] { 10, 11 };

            const int initialArrayIndex = totalIndices;
            const int maxNumCommands = 1000;
            var cl = new ArrayCommandList(maxNumCommands, initialArrayIndex, false);
            cl.MinNumCommandsToCompile = 1;
            cl.IncrementDepth();
            cl.StartCommandChunk(false, null, "Chunk");
            int[] copiedValues = cl.CopyToNew(sourceIndices, true);
            int anotherValue = cl.CopyToNew(sourceIndices[5], true);
            int yetAnother = cl.CopyToNew(anotherValue, false);

            const int numParallelChunks = 50;
            const int numPotentialIncrementsWithin = 10;
            const int excludeIndexFromIncrement = 3;

            cl.StartCommandChunk(true, null); // parallel within this chunk
            for (int i = 0; i < numParallelChunks; i++)
            {
                cl.StartCommandChunk(false, null);
                cl.IncrementDepth();
                for (int j = numPotentialIncrementsWithin - 1; j >= 0; j--) // go backward to make it easier to follow algorithm
                    if (j != excludeIndexFromIncrement)
                        cl.Increment(destinationIndices[0], true /* target the original, even though ... */, copiedValues[j]);
                cl.DecrementDepth();
                cl.EndCommandChunk(destinationIndices); // ... we will ultimately need to copy the destination increments recorded here to the outer command chunk so that these increments are not lost.
            }
            cl.EndCommandChunk();    // end parallel within chunk

            cl.EndCommandChunk();
            cl.DecrementDepth();
            cl.CompleteCommandList();
            for (int i = 0; i <= 1; i++)
            {
                if (i == 0)
                    cl.Parallelize = true;
                else
                    cl.Parallelize = false;
                cl.ExecuteAll(sourceValues, false);
                sourceValues[destinationIndicesStart].Should().BeApproximately(numParallelChunks * (1023 - 2 * 2 * 2), 0.001);
                sourceValues[destinationIndicesStart] = 0;
            }
        }
    }
}

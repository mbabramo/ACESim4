using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using static ACESimBase.Util.ArrayProcessing.ArrayCommandList;
using System.Linq;

namespace ACESimTest
{
    /// <summary>
    /// Direct, chunk‑level tests for IChunkExecutor implementations.
    /// Uses Arrange/Act/Assert to validate each command type without ArrayCommandList.
    /// </summary>
    public abstract class ChunkExecutorTestBase
    {
        /// <summary>
        /// Construct the executor under test.
        /// </summary>
        protected abstract IChunkExecutor CreateExecutor();

        /// <summary>
        /// Prepare a chunk containing exactly the given commands.
        /// </summary>
        protected ArrayCommandChunk ArrangeChunk(params ArrayCommand[] commands)
        {
            var chunk = new ArrayCommandChunk();
            chunk.StartCommandRange = 0;
            chunk.EndCommandRangeExclusive = commands.Length;
            // Attach underlying commands buffer (executor implementations must reference this)
            UnderlyingCommands = commands;
            // Prepare a default virtual stack of adequate size
            chunk.VirtualStack = new double[commands.Max(c => Math.Max(c.GetTargetIndexIfUsed(), c.GetSourceIndexIfUsed()) + 1) + 1];
            return chunk;
        }

        /// <summary>
        /// UnderlyingCommands buffer for executors to read from.
        /// Implementations should read from this static field.
        /// </summary>
        protected ArrayCommand[] UnderlyingCommands;

        /// <summary>
        /// Execute the chunk with the given executor.
        /// </summary>
        protected double[] ActExecute(ArrayCommandChunk chunk,
                                      double[] orderedSources,
                                      double[] orderedDestinations)
        {
            var executor = CreateExecutor();
            executor.AddToGeneration(chunk);
            executor.PerformGeneration();

            int cosi = 0, codi = 0;
            bool condition = true;
            executor.Execute(
                chunk,
                chunk.VirtualStack,
                orderedSources,
                orderedDestinations,
                ref cosi,
                ref codi,
                ref condition);

            return chunk.VirtualStack;
        }

        [TestMethod]
        public void TestZeroCommand()
        {
            // Arrange a single Zero command at index 2
            var cmd = new ArrayCommand(ArrayCommandType.Zero, 2, -1);
            var chunk = ArrangeChunk(cmd);
            var os = Array.Empty<double>();
            var od = Array.Empty<double>();

            // Act
            var vs = ActExecute(chunk, os, od);

            // Assert
            Assert.AreEqual(0.0, vs[2], 1e-9);
        }

        // Additional tests for CopyTo, IncrementBy, MultiplyBy, If/EndIf, etc.
    }

    [TestClass]
    public class ChunkExecutorTests_Interpreter : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() => new InterpreterChunkExecutor(UnderlyingCommands);
    }

    [TestClass]
    public class ChunkExecutorTests_IL : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() => new ILChunkExecutor(UnderlyingCommands);
    }

    [TestClass]
    public class ChunkExecutorTests_Roslyn : ChunkExecutorTestBase
    {
        protected override IChunkExecutor CreateExecutor() => new RoslynChunkExecutor(UnderlyingCommands);
    }
}

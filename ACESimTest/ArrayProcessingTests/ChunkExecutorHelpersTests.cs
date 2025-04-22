using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class ChunkExecutorHelpersTests
    {
        [TestMethod]
        public void LocalBindingState_FlushBeforeReuse()
        {
            var lbs = new LocalBindingState(maxLocals: 2);
            lbs.StartInterval(slot: 2, local: 0, bindDepth: 0);
            lbs.MarkWritten(0);

            Assert.IsTrue(lbs.NeedsFlushBeforeReuse(0, out int oldSlot));
            Assert.AreEqual(2, oldSlot);

            lbs.FlushLocal(0);                         // mark clean
            Assert.IsFalse(lbs.NeedsFlushBeforeReuse(0, out _));
        }

        [TestMethod]
        public void DepthMap_ComputesDepths()
        {
            var cmds = new[]
            {
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.If,   -1, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1),
                new ArrayCommand(ArrayCommandType.EndIf,-1, -1),
            };
            var dm = new DepthMap(cmds, 0, cmds.Length);

            Assert.AreEqual(0, dm.GetDepth(0));   // outside if
            Assert.AreEqual(1, dm.GetDepth(2));   // inside if
        }

        [TestMethod]
        public void IntervalIndex_LookupStartsAndEnds()
        {
            var ii = new IntervalIndex(capacity: 3);
            ii.AddStart(cmdIndex: 5, slot: 10);
            ii.AddEnd(cmdIndex: 8, slot: 10);

            Assert.IsTrue(ii.TryStart(5, out int s) && s == 10);
            Assert.IsTrue(ii.TryEnd(8, out int e) && e == 10);
            Assert.IsFalse(ii.TryStart(6, out _));
        }

        [TestMethod]
        public void LocalBindingState_ReuseDisallowedAcrossDeeperDepth()
        {
            var lbs = new LocalBindingState(maxLocals: 1);
            lbs.StartInterval(slot: 0, local: 0, bindDepth: 0);

            bool ok = lbs.TryReuse(local: 0, newSlot: 1, bindDepth: 1, out int flush);
            Assert.IsFalse(ok, "reuse must fail at deeper depth");
            Assert.AreEqual(-1, flush);
        }

        [TestMethod]
        public void LocalBindingState_FlushDirtyAtEndIf()
        {
            var lbs = new LocalBindingState(maxLocals: 2);
            lbs.StartInterval(2, 0, 1); lbs.MarkWritten(0);
            lbs.StartInterval(3, 1, 1); lbs.MarkWritten(1);

            var flushed = lbs.FlushDirtyForDepth(1);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, flushed);

            Assert.IsFalse(lbs.NeedsFlushBeforeReuse(0, out _));
            Assert.IsFalse(lbs.NeedsFlushBeforeReuse(1, out _));
        }
    }
}

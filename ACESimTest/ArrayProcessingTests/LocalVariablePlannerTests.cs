using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest.ArrayProcessingTests
{
    [TestClass]
    public class LocalVariablePlannerTests
    {
        private static ArrayCommand[] Cmds(params ArrayCommand[] cmds) => cmds;

        [TestMethod]
        public void PlanLocals_NoCommands_ReturnsEmpty()
        {
            var commands = Array.Empty<ArrayCommand>();
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 0);

            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
            Assert.AreEqual(0, plan.Intervals.Count);
        }

        [TestMethod]
        public void PlanLocals_SingleCommand_MeetsMinUses()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 2, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1, minUses: 1);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(2));
            Assert.AreEqual(1, plan.Intervals.Count);
            var iv = plan.Intervals.Single();
            Assert.AreEqual(2, iv.Slot);
            Assert.AreEqual(0, iv.First);
            Assert.AreEqual(0, iv.Last);
        }

        [TestMethod]
        public void PlanLocals_SingleCommand_BelowDefaultThreshold()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 5, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1);

            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_MultiplyBy_UsesThreshold()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.MultiplyBy, 3, 1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1, minUses: 2);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(3));
            Assert.IsFalse(plan.SlotToLocal.ContainsKey(1));
        }

        [TestMethod]
        public void PlanLocals_OverlappingIntervals_NeedsSeparateLocals()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                                new ArrayCommand(ArrayCommandType.CopyTo, 1, 0));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 2, minUses: 1);

            Assert.AreEqual(2, plan.LocalCount);
            Assert.AreNotEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_NonOverlappingIntervals_ReusesLocal()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 0, -1),  // depth 0
                                new ArrayCommand(ArrayCommandType.Comment, -1, -1),
                                new ArrayCommand(ArrayCommandType.Zero, 1, -1));   // still depth 0
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 3, minUses: 1);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.AreEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_IfScope_PreventsReuseAcrossDeeperDepth()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 0, -1),  // depth 0
                                new ArrayCommand(ArrayCommandType.If, -1, -1),   // depth 1
                                new ArrayCommand(ArrayCommandType.Zero, 1, -1),  // depth 1
                                new ArrayCommand(ArrayCommandType.EndIf, -1, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 1);

            Assert.AreEqual(2, plan.LocalCount);
            Assert.AreNotEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_MaxLocalsCap_Respected()
        {
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1),
                new ArrayCommand(ArrayCommandType.Zero, 2, -1),
                new ArrayCommand(ArrayCommandType.Zero, 3, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length,
                                                       minUses: 1, maxLocals: 2);

            Assert.IsTrue(plan.LocalCount <= 2);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, plan.SlotToLocal.Keys.ToArray());
        }

        [TestMethod]
        public void PlanLocals_ExactThresholdBoundary()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.CopyTo, 0, 1),
                                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1));

            var plan3 = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 3);
            Assert.AreEqual(1, plan3.LocalCount);
            Assert.IsTrue(plan3.SlotToLocal.ContainsKey(0));

            var plan4 = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 4);
            Assert.AreEqual(0, plan4.LocalCount);
        }

        [TestMethod]
        public void PlanLocals_SubSliceHonoursMinUses()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 5, -1),
                                new ArrayCommand(ArrayCommandType.Zero, 6, -1),
                                new ArrayCommand(ArrayCommandType.Zero, 5, -1));

            var full = LocalVariablePlanner.PlanLocals(commands, 0, 3, minUses: 2);
            Assert.AreEqual(1, full.LocalCount);
            Assert.IsTrue(full.SlotToLocal.ContainsKey(5));

            var sub = LocalVariablePlanner.PlanLocals(commands, 0, 2, minUses: 2);
            Assert.AreEqual(0, sub.LocalCount);
        }

        [TestMethod]
        public void PlanLocals_MinUsesZero_SelectsAllSlots()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 2, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 0);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(2));
        }

        [TestMethod]
        public void PlanLocals_StartGreaterThanEnd_ReturnsEmpty()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 0, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, start: 1, end: 0);

            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_IntervalsAdjacent_ReusesLocal()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                                new ArrayCommand(ArrayCommandType.Comment, -1, -1),
                                new ArrayCommand(ArrayCommandType.Zero, 1, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 1);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.AreEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_SingleMultiplyBy_RequiresTwoLocals()
        {
            var commands = Cmds(new ArrayCommand(ArrayCommandType.MultiplyBy, 0, 1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 1);

            Assert.AreEqual(2, plan.LocalCount);
            Assert.AreNotEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }
    }
}

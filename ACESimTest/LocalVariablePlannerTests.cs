// File: ChunkExecutors/LocalVariablePlannerTests.cs

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;

namespace ACESimTest
{
    [TestClass]
    public class LocalVariablePlannerTests
    {
        /// <summary>
        /// Helper to build a single‐command ArrayCommand array.
        /// </summary>
        private static ArrayCommand[] Cmds(params ArrayCommand[] cmds) => cmds;

        [TestMethod]
        public void PlanLocals_NoCommands()
        {
            var commands = Array.Empty<ArrayCommand>();
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 0);

            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
            Assert.AreEqual(0, plan.Intervals.Count);
        }

        [TestMethod]
        public void PlanLocals_SingleZero_MinUses1()
        {
            // Zero writes slot 2 once; with minUses=1 it qualifies
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 2, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1, minUses: 1);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(2));
            Assert.AreEqual((slot: 2, first: 0, last: 0), plan.Intervals.Single());
        }

        [TestMethod]
        public void PlanLocals_SingleZero_DefaultMinUses()
        {
            // Default minUses=3, this single use shouldn't qualify
            var commands = Cmds(new ArrayCommand(ArrayCommandType.Zero, 5, -1));
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1);

            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_MultiplyBy_ThresholdBehavior()
        {
            // MultiplyBy touches index(3) twice (read+write) and sourceIndex(1) once.
            var commands = Cmds(new ArrayCommand(ArrayCommandType.MultiplyBy, 3, 1));
            // With minUses=2, only slot 3 qualifies.
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 1, minUses: 2);

            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(3));
            Assert.IsFalse(plan.SlotToLocal.ContainsKey(1));
        }

        [TestMethod]
        public void PlanLocals_OverlappingIntervals()
        {
            // Two slots both used at command 0 → intervals overlap → need 2 locals
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.CopyTo, 1, 0)
            );
            // minUses=1 so both qualify
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 2, minUses: 1);

            Assert.AreEqual(2, plan.LocalCount);
            // both 0 and 1 should be mapped to distinct locals
            Assert.IsTrue(plan.SlotToLocal[0] != plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_NonOverlappingIntervals_ReuseLocal()
        {
            // Slot 0 used at index 0; slot 1 used at index 2 → intervals non-overlapping
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),  // interval [0,0]
                new ArrayCommand(ArrayCommandType.Comment, -1, -1), // no VS
                new ArrayCommand(ArrayCommandType.Zero, 1, -1)   // interval [2,2]
            );
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 3, minUses: 1);

            // Only one local needed (reuse)
            Assert.AreEqual(1, plan.LocalCount);
            Assert.AreEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_MaxLocalsCapping()
        {
            // Four different slots, each used once; minUses=1 so all qualify
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1),
                new ArrayCommand(ArrayCommandType.Zero, 2, -1),
                new ArrayCommand(ArrayCommandType.Zero, 3, -1)
            );

            // Cap to maxLocals=2 → pick the first two slots (0 and 1)
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, 4, minUses: 1, maxLocals: 2);

            // We selected exactly two slots (0 and 1), but their live intervals [0..0] and [1..1]
            // do not overlap, so they share a single local. We must never exceed maxLocals.
            Assert.IsTrue(
                plan.LocalCount <= 2,
                $"LocalCount ({plan.LocalCount}) must be ≤ maxLocals (2)");

            // Confirm exactly the two hottest slots were chosen
            CollectionAssert.AreEquivalent(
                new[] { 0, 1 },
                plan.SlotToLocal.Keys.ToArray(),
                "Only the first two slots should be mapped");
        }


        [TestMethod]
        public void PlanLocals_ComplexUseCounts_SelectHottest()
        {
            // slot0 used 5 times, slot1 used 5 times, slot2 used 2 times
            var cmds = Enumerable.Range(0, 5)
                                 .Select(i => new ArrayCommand(ArrayCommandType.CopyTo, 0, 0))
                                 .Concat(Enumerable.Range(0, 5)
                                                   .Select(i => new ArrayCommand(ArrayCommandType.CopyTo, 1, 1)))
                                 .Concat(Enumerable.Range(0, 2)
                                                   .Select(i => new ArrayCommand(ArrayCommandType.CopyTo, 2, 2)))
                                 .ToArray();

            var plan = LocalVariablePlanner.PlanLocals(
                cmds,
                start: 0,
                end: cmds.Length,
                minUses: 1,
                maxLocals: 2);

            // Slots 0 and 1 are the hottest but their live intervals [0..4] and [5..9] do not overlap,
            // so they share a single local.
            Assert.AreEqual(
                1,
                plan.LocalCount,
                "Non‑overlapping hot intervals should reuse one local");

            // Both hot slots should be included in the mapping
            CollectionAssert.AreEquivalent(
                new[] { 0, 1 },
                plan.SlotToLocal.Keys.ToArray(),
                "Both hot slots should appear in the mapping");
        }

        [TestMethod]
        public void PlanLocals_ExactThresholdBoundary()
        {
            // slot0 is used 3 times (write + read + write), slot1 only 2 times
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.CopyTo, 0, 1),  // read VS[1], write VS[0]
                new ArrayCommand(ArrayCommandType.IncrementBy, 0, 1)   // read VS[0], read VS[1], write VS[0]
            );

            // minUses = 3 ⇒ only slot0 qualifies
            var plan3 = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 3);
            Assert.AreEqual(1, plan3.LocalCount);
            Assert.IsTrue(plan3.SlotToLocal.ContainsKey(0), "Slot 0 should qualify at threshold 3");

            // minUses = 4 ⇒ no slot reaches 4 uses
            var plan4 = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 4);
            Assert.AreEqual(0, plan4.LocalCount);
            Assert.AreEqual(0, plan4.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_SliceSubset()
        {
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 5, -1),   // idx 0
                new ArrayCommand(ArrayCommandType.Zero, 6, -1),   // idx 1
                new ArrayCommand(ArrayCommandType.Zero, 5, -1)    // idx 2
            );

            // Full slice: VS[5] used twice → qualifies for minUses=2
            var full = LocalVariablePlanner.PlanLocals(commands, 0, 3, minUses: 2);
            Assert.AreEqual(1, full.LocalCount);
            Assert.IsTrue(full.SlotToLocal.ContainsKey(5));

            // Sub‑slice [0,2): VS[5] used only once → does NOT qualify for minUses=2
            var sub = LocalVariablePlanner.PlanLocals(commands, 0, 2, minUses: 2);
            Assert.AreEqual(0, sub.LocalCount);
            Assert.AreEqual(0, sub.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_MinUsesZeroSelectsAll()
        {
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 2, -1)
            );

            // minUses = 0 ⇒ all used slots (slot2 used once) qualify
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 0);
            Assert.AreEqual(1, plan.LocalCount);
            Assert.IsTrue(plan.SlotToLocal.ContainsKey(2));
        }

        [TestMethod]
        public void PlanLocals_StartGreaterThanEnd_ReturnsEmpty()
        {
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1)
            );

            // start > end should yield no intervals
            var plan = LocalVariablePlanner.PlanLocals(commands, start: 1, end: 0);
            Assert.AreEqual(0, plan.LocalCount);
            Assert.AreEqual(0, plan.SlotToLocal.Count);
        }

        [TestMethod]
        public void PlanLocals_MaxLocalsGreaterThanQualifiers()
        {
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1)
            );

            // only slots 0 and 1 qualify (each used once); maxLocals=10 should not over‑allocate
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 1, maxLocals: 10);
            Assert.AreEqual(2, plan.SlotToLocal.Count);
            Assert.IsTrue(plan.LocalCount <= 10);
        }

        [TestMethod]
        public void PlanLocals_IntervalAdjacency_ReusesLocal()
        {
            // slot0 lives [0..0], slot1 lives [1..1] → non‑overlapping, should share
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.Zero, 0, -1),
                new ArrayCommand(ArrayCommandType.Comment, -1, -1),
                new ArrayCommand(ArrayCommandType.Zero, 1, -1)
            );
            var plan = LocalVariablePlanner.PlanLocals(commands, 0, commands.Length, minUses: 1);
            Assert.AreEqual(1, plan.LocalCount);
            Assert.AreEqual(plan.SlotToLocal[0], plan.SlotToLocal[1]);
        }

        [TestMethod]
        public void PlanLocals_OverlapRequiresSeparateLocals()
        {
            // A single MultiplyBy at command index 0 reads and writes both VS[0] and VS[1],
            // so their live intervals both cover [0..0] and must get separate locals.
            var commands = Cmds(
                new ArrayCommand(ArrayCommandType.MultiplyBy, 0, 1)
            );

            var plan = LocalVariablePlanner.PlanLocals(
                commands,
                start: 0,
                end: commands.Length,
                minUses: 1);

            // We need two distinct locals for slot 0 and slot 1
            Assert.AreEqual(2, plan.LocalCount, "Overlapping intervals should require two locals");
            Assert.AreNotEqual(
                plan.SlotToLocal[0],
                plan.SlotToLocal[1],
                "Slots 0 and 1 share the same interval and must not reuse the same local");
        }

        [TestMethod]
        public void PlanLocals_RandomStress_NoOverlappingShare()
        {
            var rand = new Random(123);
            var list = new System.Collections.Generic.List<ArrayCommand>();
            for (int i = 0; i < 100; i++)
            {
                int slot = rand.Next(5);
                list.Add(new ArrayCommand(ArrayCommandType.CopyTo, slot, slot));
            }
            var cmds = list.ToArray();

            var plan = LocalVariablePlanner.PlanLocals(
                cmds, 0, cmds.Length, minUses: 1, maxLocals: null);

            // For any two slots with overlapping intervals, ensure they do not share a local.
            var ivMap = plan.Intervals.ToDictionary(iv => iv.slot, iv => (iv.first, iv.last));
            foreach (var a in plan.SlotToLocal)
            {
                foreach (var b in plan.SlotToLocal)
                {
                    if (a.Key >= b.Key) continue;
                    var (fa, la) = ivMap[a.Key];
                    var (fb, lb) = ivMap[b.Key];
                    bool overlap = fa <= lb && fb <= la;
                    if (overlap)
                        Assert.AreNotEqual(
                            a.Value, b.Value,
                            $"Slots {a.Key} and {b.Key} overlap but share a local");
                }
            }
        }

    }
}

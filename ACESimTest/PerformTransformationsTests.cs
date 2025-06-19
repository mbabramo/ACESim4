using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.GameSolvingSupport.Settings;
using ACESim;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    /// <summary>
    /// Unit‑tests for <see cref="PermutationalLauncher.PerformTransformations{T}"/>
    /// updated to reflect the new “criticalPermutations ∪ (noncritical × supercritical)”
    /// design.
    /// </summary>
    [TestClass]
    public class PerformTransformationsTests
    {
        // ---------------------------------------------------------------------
        //  Dummy plumbing (unchanged)
        // ---------------------------------------------------------------------
        private sealed class DummyOptions : GameOptions
        {
            public DummyOptions()
            {
                VariableSettings = new Dictionary<string, object>();
                Name             = string.Empty;
            }
        }

        private sealed class DummyLauncher : PermutationalLauncher
        {
            public override List<string> NamesOfVariationSets =>
                new() { "CriticalPerms", "A‑BaselineAgain", "B‑Sweep", "C‑Sweep" };

            public override List<(string, string)> DefaultVariableValues => new()
            {
                ("A", "1"),
                ("B", "10"),
                ("C", "100"),
            };

            public override List<(string, string[])> CriticalVariableValues => new()
            {
                ("A", new[] { "1", "2" }),
                ("B", new[] { "10", "20" }),
            };

            public override string ReportPrefix => "TEST";

            // not used in these tests
            public override List<List<GameOptions>> GetVariationSets(bool _, bool __) => throw new NotImplementedException();
            public override List<GameOptions>       GetOptionsSets()                    => throw new NotImplementedException();
            public override GameDefinition          GetGameDefinition()                 => null;
            public override GameOptions             GetDefaultSingleGameOptions()       => new DummyOptions();

            // Helper – wraps PerformTransformations
            public List<List<GameOptions>> BuildVariationSets(
                bool includeBaselineValueForNoncritical,
                int  numSupercriticals = 2)
            {
                // critical
                List<Func<DummyOptions, DummyOptions>> critA = new()
                {
                    g => GetAndTransform(g, " _A1", o => o.VariableSettings["A"] = 1),
                    g => GetAndTransform(g, " _A2", o => o.VariableSettings["A"] = 2),
                };
                List<Func<DummyOptions, DummyOptions>> critB = new()
                {
                    g => GetAndTransform(g, " _B10", o => o.VariableSettings["B"] = 10),
                    g => GetAndTransform(g, " _B20", o => o.VariableSettings["B"] = 20),
                };

                // partner non‑critical
                List<Func<DummyOptions, DummyOptions>> noncritA = new()
                {
                    g => GetAndTransform(g, " _A1baseline", o => o.VariableSettings["A"] = 1),
                };
                List<Func<DummyOptions, DummyOptions>> noncritB = new()
                {
                    g => GetAndTransform(g, " _B10baseline", o => o.VariableSettings["B"] = 10),
                    g => GetAndTransform(g, " _B30",         o => o.VariableSettings["B"] = 30),
                };

                // independent non‑critical
                List<Func<DummyOptions, DummyOptions>> noncritC = new()
                {
                    g => GetAndTransform(g, " _C100baseline", o => o.VariableSettings["C"] = 100),
                    g => GetAndTransform(g, " _C200",         o => o.VariableSettings["C"] = 200),
                };

                var allTransformations = new List<List<Func<DummyOptions, DummyOptions>>>
                {
                    critA, critB,
                    noncritA, noncritB, noncritC
                };

                const int numCritical = 2;

                return PerformTransformations(
                    allTransformations,
                    numCritical,                  // critical count
                    numSupercriticals,            // super‑critical count
                    useAllPermutationsOfTransformations: false,
                    includeBaselineValueForNoncritical,
                    optionsFactory: () => new DummyOptions());
            }
        }

        // -----------------------------------------------------------------
        //  All criticals are super‑critical
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_DuplicatesSkipped_AllCriticalsSupercritical()
        {
            var sets = new DummyLauncher()
                .BuildVariationSets(includeBaselineValueForNoncritical: true,
                                    numSupercriticals:                2);

            sets.Should().HaveCount(3, "criticalPermutations + B‑sweep + C‑sweep");
            sets.Select(s => s.Count)
                .Should().BeEquivalentTo(new[] { 4, 4, 8 });
        }

        // -----------------------------------------------------------------
        //  Exactly ONE super‑critical variable (A)
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_OneSupercritical_PermutationsRestricted()
        {
            var sets = new DummyLauncher()
                .BuildVariationSets(includeBaselineValueForNoncritical: true,
                                    numSupercriticals:                1);

            // Expected:
            //   0. criticalPermutations   – A×B (2×2)                     => 4
            //   1. B‑sweep (10,30)        – A varies (2) × B list (2)     => 4
            //   2. C‑sweep (100,200)      – A varies (2) × C list (2)     => 4
            sets.Should().HaveCount(3);
            sets.Select(s => s.Count)
                .Should().BeEquivalentTo(new[] { 4, 4, 4 });
        }

        // -----------------------------------------------------------------
        //  No super‑critical variables
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_NoSupercriticals_OnlyBaselinesVary()
        {
            var sets = new DummyLauncher()
                .BuildVariationSets(includeBaselineValueForNoncritical: true,
                                    numSupercriticals:                0);

            // Expected:
            //   0. criticalPermutations   – A×B (2×2)                     => 4
            //   1. B‑sweep (10,30)        – B list (2)                    => 2
            //   2. C‑sweep (100,200)      – C list (2)                    => 2
            sets.Should().HaveCount(3);
            sets.Select(s => s.Count)
                .Should().BeEquivalentTo(new[] { 4, 2, 2 });
        }
    }
}

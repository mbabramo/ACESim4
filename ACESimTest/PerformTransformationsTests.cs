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
    /// Expanded unit-test suite for <see cref="PermutationalLauncher.PerformTransformations{T}"/>,
    /// now exercising the new <c>numSupercriticals</c> parameter while keeping the
    /// original behavioural guarantees intact.
    /// </summary>
    [TestClass]
    public class PerformTransformationsTests
    {
        // ---------------------------------------------------------------------
        //  Dummy types
        // ---------------------------------------------------------------------
        private sealed class DummyOptions : GameOptions
        {
            public DummyOptions()
            {
                VariableSettings = new Dictionary<string, object>();
                Name = string.Empty;
            }
        }

        /// <summary>Concrete launcher exposing the protected helper.</summary>
        private sealed class DummyLauncher : PermutationalLauncher
        {
            public override List<string> NamesOfVariationSets =>
                new() { "CriticalOnly", "A-BaselineAgain", "B-Sweep", "C-Sweep" };

            public override List<(string, string)> DefaultVariableValues => new()
            {
                ("A", "1"),
                ("B", "10"),
                ("C", "100"),
            };

            public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues =>
                new()
                {
                    ("A", new[] { "1", "2" }),
                    ("B", new[] { "10", "20" }),
                };

            public override string ReportPrefix => "TEST";

            // --------------------------------------------------------------
            //  Minimal stubs for other abstract members of Launcher
            // --------------------------------------------------------------
            public override List<List<GameOptions>> GetVariationSets(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical) =>
                throw new NotImplementedException();

            public override List<GameOptions> GetOptionsSets() =>
                throw new NotImplementedException();

            public override GameDefinition GetGameDefinition() => null;

            public override GameOptions GetDefaultSingleGameOptions() => new DummyOptions();

            // --------------------------------------------------------------
            //  Helper used by the tests – wraps the new PerformTransformations overload
            // --------------------------------------------------------------
            public List<List<GameOptions>> BuildVariationSets(bool includeBaselineValueForNoncritical, int numSupercriticals = 2)
            {
                // ---------------- critical ----------------
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

                // ---------------- non-critical (partner of A) ----------------
                List<Func<DummyOptions, DummyOptions>> noncritA = new()
                {
                    g => GetAndTransform(g, " _A1baseline", o => o.VariableSettings["A"] = 1),
                }; // redundant baseline only

                // ---------------- non-critical (partner of B) ----------------
                List<Func<DummyOptions, DummyOptions>> noncritB = new()
                {
                    g => GetAndTransform(g, " _B10baseline", o => o.VariableSettings["B"] = 10),
                    g => GetAndTransform(g, " _B30",        o => o.VariableSettings["B"] = 30),
                };

                // ---------------- independent non-critical C ----------------
                List<Func<DummyOptions, DummyOptions>> noncritC = new()
                {
                    g => GetAndTransform(g, " _C100baseline", o => o.VariableSettings["C"] = 100),
                    g => GetAndTransform(g, " _C200",         o => o.VariableSettings["C"] = 200),
                };

                var allTransformations = new List<List<Func<DummyOptions, DummyOptions>>>
                {
                    critA,      // index 0 — critical
                    critB,      // index 1 — critical
                    noncritA,   // index 2 — partner of critA
                    noncritB,   // index 3 — partner of critB
                    noncritC,   // index 4 — independent non-critical
                };

                const int numCritical = 2;

                return PerformTransformations(
                    allTransformations,
                    numCritical:             numCritical,
                    numSupercriticals:       numSupercriticals,
                    useAllPermutationsOfTransformations: false,
                    includeBaselineValueForNoncritical:  includeBaselineValueForNoncritical,
                    optionsFn: () => new DummyOptions());
            }
        }

        // -----------------------------------------------------------------
        //  Original behaviour – every critical is also super-critical
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_DuplicatesSkipped_AllCriticalsSupercritical()
        {
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: true, numSupercriticals: 2);

            sets.Should().HaveCount(3, "baseline + B-sweep + C-sweep");
            sets.Select(s => s.Count).Should().BeEquivalentTo(new[] { 4, 4, 8 });
        }

        // -----------------------------------------------------------------
        //  Exactly ONE super-critical variable (A)
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_OneSupercritical_PermutationsRestricted()
        {
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: true, numSupercriticals: 1);

            // Expected batches:
            //   0. Critical-only       – A varies (2), B fixed             => 2
            //   1. B-sweep (10,30)     – A varies (2) × B list (2)        => 4
            //   2. C-sweep (100,200)   – A varies (2) × C list (2)        => 4
            sets.Should().HaveCount(3);
            sets.Select(s => s.Count).Should().BeEquivalentTo(new[] { 2, 4, 4 });
        }

        // -----------------------------------------------------------------
        //  No super-critical variables – non-critical sweeps are 1-at-a-time
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_NoSupercriticals_OnlyBaselinesVary()
        {
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: true, numSupercriticals: 0);

            // Expected batches:
            //   0. Critical-only       – A&B at baseline                   => 1
            //   1. B-sweep (10,30)     – B list (2)                        => 2
            //   2. C-sweep (100,200)   – C list (2)                        => 2
            sets.Should().HaveCount(3);
            sets.Select(s => s.Count).Should().BeEquivalentTo(new[] { 1, 2, 2 });
        }
    }
}
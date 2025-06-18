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
    /// Unit‑test suite exercising the behaviour of
    /// <see cref="PermutationalLauncher.PerformTransformations{T}"/>.
    ///
    /// We spin up a minimal concrete launcher (<see cref="DummyLauncher"/>) that
    /// wires together:
    ///   • TWO critical variables, each with two values (baseline + alt).
    ///   • A non‑critical partner list for <c>A</c> (only baseline) so we can test the
    ///     "redundant baseline" skip logic.
    ///   • A non‑critical partner list for <c>B</c> (baseline + alt).
    ///   • An independent non‑critical variable <c>C</c> (baseline + alt) that is
    ///     not paired with any critical, to show that unpaired non‑criticals get
    ///     appended rather than replacing.
    ///
    /// Expected batches (outer lists) when
    /// <c>includeBaselineValueForNoncritical == false</c> (duplicates kept):
    ///   0. Critical‑only                  – 4 permutations (A×B)
    ///   1. A‑baseline‑again               – 2 permutations (A=1 × B)
    ///   2. B‑sweep (10 + 30)              – 4 permutations (A × Blist)
    ///   3. C‑sweep (100 + 200)            – 8 permutations (A × B × Clist)
    ///
    /// When the flag is <c>true</c> the single‑entry A‑baseline list is skipped,
    /// so we get batches 0, 2, 3 only.
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
                new() { "CriticalOnly", "A‑BaselineAgain", "B‑Sweep", "C‑Sweep" };

            public override List<(string, string)> DefaultVariableValues => new()
            {
                ("A", "1"),
                ("B", "10"),
                ("C", "100")
            };

            public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues =>
                new()
                {
                    ("A", new[] { "1", "2" }),
                    ("B", new[] { "10", "20" })
                };

            public override string ReportPrefix => "TEST";

            // --------------------------------------------------------------
            //  Minimal stubs for other abstract members of Launcher
            // --------------------------------------------------------------
            public override List<List<GameOptions>> GetVariationSets(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical) =>
                throw new NotImplementedException();

            public override List<GameOptions> GetOptionsSets() =>
                throw new NotImplementedException();

            public override GameDefinition GetGameDefinition() =>
                null;

            public override GameOptions GetDefaultSingleGameOptions() =>
                new DummyOptions();

            // --------------------------------------------------------------
            //  Expose helper for tests
            // --------------------------------------------------------------
            public List<List<GameOptions>> BuildVariationSets(bool includeBaselineValueForNoncritical)
            {
                // ---------------- critical ----------------
                List<Func<DummyOptions, DummyOptions>> critA = new()
                {
                    g => GetAndTransform(g, " _A1", o => o.VariableSettings["A"] = 1),
                    g => GetAndTransform(g, " _A2", o => o.VariableSettings["A"] = 2)
                };

                List<Func<DummyOptions, DummyOptions>> critB = new()
                {
                    g => GetAndTransform(g, " _B10", o => o.VariableSettings["B"] = 10),
                    g => GetAndTransform(g, " _B20", o => o.VariableSettings["B"] = 20)
                };

                // ---------------- non‑critical (partner of A) ----------------
                List<Func<DummyOptions, DummyOptions>> noncritA = new()
                {
                    g => GetAndTransform(g, " _A1baseline", o => o.VariableSettings["A"] = 1)
                }; // single entry → redundant baseline

                // ---------------- non‑critical (partner of B) ----------------
                List<Func<DummyOptions, DummyOptions>> noncritB = new()
                {
                    g => GetAndTransform(g, " _B10baseline", o => o.VariableSettings["B"] = 10),
                    g => GetAndTransform(g, " _B30", o => o.VariableSettings["B"] = 30)
                };

                // ---------------- independent non‑critical C ----------------
                List<Func<DummyOptions, DummyOptions>> noncritC = new()
                {
                    g => GetAndTransform(g, " _C100baseline", o => o.VariableSettings["C"] = 100),
                    g => GetAndTransform(g, " _C200", o => o.VariableSettings["C"] = 200)
                };

                var allTransformations = new List<List<Func<DummyOptions, DummyOptions>>>
                {
                    critA,      // index 0 — critical
                    critB,      // index 1 — critical
                    noncritA,   // index 2 — partner of critA
                    noncritB,   // index 3 — partner of critB
                    noncritC    // index 4 — independent non‑critical
                };

                return PerformTransformations(
                    allTransformations,
                    numCritical: 2,
                    useAllPermutationsOfTransformations: false,
                    includeBaselineValueForNoncritical: includeBaselineValueForNoncritical,
                    optionsFn: () => new DummyOptions());
            }
        }

        // -----------------------------------------------------------------
        //  Tests
        // -----------------------------------------------------------------
        [TestMethod]
        public void When_DuplicatesKept_AllBatchesPresent()
        {
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: false);

            sets.Should().HaveCount(4, "null + A‑baseline + B‑sweep + C‑sweep");
            sets.Select(s => s.Count).Should().BeEquivalentTo(new[] { 4, 2, 4, 8 });
        }

        [TestMethod]
        public void When_DuplicatesSkipped_BaselineOnlyBatchDropped()
        {
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: true);

            sets.Should().HaveCount(3, "A‑baseline batch is skipped");
            sets.Select(s => s.Count).Should().BeEquivalentTo(new[] { 4, 4, 8 });
        }

        [TestMethod]
        public void CriticalPermutations_AreFullFactorial()
        {
            // Use flag value that doesn't affect critical permutations.
            var sets = new DummyLauncher().BuildVariationSets(includeBaselineValueForNoncritical: true);

            // The first set (critical‑only) must have every combination of A and B.
            var criticalOnly = sets[0];
            var combos = criticalOnly.Select(g => ($"{g.VariableSettings["A"]}", $"{g.VariableSettings["B"]}"));
            combos.Should().BeEquivalentTo(new[] { ("1", "10"), ("1", "20"), ("2", "10"), ("2", "20") });
        }
    }
}

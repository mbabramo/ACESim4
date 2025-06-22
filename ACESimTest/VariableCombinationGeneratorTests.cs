using ACESimBase.Util.Combinatorics;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimTest.Util.Combinatorics
{
    /// <summary>
    /// Exhaustive unit tests for <see cref="VariableCombinationGenerator"/> covering two scenarios:
    /// <list type="number">
    ///   <item><b>Scenario 1</b> – six independent dimensions (2 Global, 2 Core, 2 Modifier) each
    ///     with two values: "A" (default) and "B" (alternative). The expected enumeration contains
    ///     40 rows and is written explicitly so it can be inspected without running the test.</item>
    ///   <item><b>Scenario 2</b> – one variable (<c>C1</c>) appears both as a Core dimension (values
    ///     "A", "B") and as a Modifier dimension (value "Z"). No Globals are present. The expected
    ///     enumeration contains 6 rows.</item>
    /// </list>
    /// </summary>
    [TestClass]
    public sealed class VariableCombinationGeneratorTests
    {
        #region Shared scaffolding
        private sealed class TestOptions
        {
            public string G1 { get; set; } = "A";
            public string G2 { get; set; } = "A";
            public string C1 { get; set; } = "A";
            public string C2 { get; set; } = "A";
            public string M1 { get; set; } = "A";
            public string M2 { get; set; } = "A";
            public override string ToString() =>
                $"G1={G1}, G2={G2}, C1={C1}, C2={C2}, M1={M1}, M2={M2}";
        }

        private static Func<TestOptions, TestOptions> Mutate(Action<TestOptions> act) =>
            o => { act(o); return o; };

        private static string Row(string g1, string g2, string c1, string c2, string m1, string m2) =>
            $"G1={g1}, G2={g2}, C1={c1}, C2={c2}, M1={m1}, M2={m2}";
        #endregion

        //------------------------------------------------------------------
        // Scenario 1 – 2 Globals × 2 Cores × 2 Modifiers
        //------------------------------------------------------------------
        [TestMethod]
        public void FullScenario_EnumeratesFortyRows()
        {
            // Dimensions -----------------------------------------------------
            var dims = new[]
            {
                // Globals
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "G1", new[] { Mutate(_ => {}), Mutate(o => o.G1 = "B") },
                    VariableCombinationGenerator.DimensionRole.Global),
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "G2", new[] { Mutate(_ => {}), Mutate(o => o.G2 = "B") },
                    VariableCombinationGenerator.DimensionRole.Global),

                // Cores
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "C1", new[] { Mutate(_ => {}), Mutate(o => o.C1 = "B") },
                    VariableCombinationGenerator.DimensionRole.Core),
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "C2", new[] { Mutate(_ => {}), Mutate(o => o.C2 = "B") },
                    VariableCombinationGenerator.DimensionRole.Core),

                // Modifiers
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "M1", new[] { Mutate(_ => {}), Mutate(o => o.M1 = "B") },
                    VariableCombinationGenerator.DimensionRole.Modifier),
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "M2", new[] { Mutate(_ => {}), Mutate(o => o.M2 = "B") },
                    VariableCombinationGenerator.DimensionRole.Modifier)
            };

            // Actual ---------------------------------------------------------
            var actual = VariableCombinationGenerator.Generate(
                dims,
                () => new TestOptions(),
                includeBaselineModifierValue: false);

            // Expected -------------------------------------------------------
            var expected = new List<string>();
            string[] AB = { "A", "B" };

            foreach (var g1 in AB)
            foreach (var g2 in AB)
            {
                // Baseline core grid (4 rows)
                foreach (var c1 in AB)
                foreach (var c2 in AB)
                    expected.Add(Row(g1, g2, c1, c2, "A", "A"));

                // Modifier M1 = B (3 rows)
                expected.Add(Row(g1, g2, "A", "A", "B", "A"));
                expected.Add(Row(g1, g2, "B", "A", "B", "A"));
                expected.Add(Row(g1, g2, "A", "B", "B", "A"));

                // Modifier M2 = B (3 rows)
                expected.Add(Row(g1, g2, "A", "A", "A", "B"));
                expected.Add(Row(g1, g2, "B", "A", "A", "B"));
                expected.Add(Row(g1, g2, "A", "B", "A", "B"));
            }

            expected.Should().HaveCount(40);
            actual.Select(o => o.ToString())
                  .Should()
                  .BeEquivalentTo(expected, opt => opt.WithoutStrictOrdering(),
                      "Scenario 1 requires exactly the 40 predefined combinations.");
        }

        //------------------------------------------------------------------
        // Scenario 2 – C1 as Core (A,B) and Modifier (Z)
        //------------------------------------------------------------------
        [TestMethod]
        public void CoreModifierOverlap_EnumeratesSixRows()
        {
            // Dimensions -----------------------------------------------------
            var dims = new[]
            {
                // Core dimensions
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "C1", new[] { Mutate(_ => {}), Mutate(o => o.C1 = "B") },
                    VariableCombinationGenerator.DimensionRole.Core),
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "C2", new[] { Mutate(_ => {}), Mutate(o => o.C2 = "B") },
                    VariableCombinationGenerator.DimensionRole.Core),

                // Modifier using same variable name (non‑critical value Z)
                new VariableCombinationGenerator.Dimension<TestOptions>(
                    "C1", new[] { Mutate(_ => {}), Mutate(o => o.C1 = "Z") },
                    VariableCombinationGenerator.DimensionRole.Modifier)
            };

            // Actual ---------------------------------------------------------
            var actual = VariableCombinationGenerator.Generate(
                dims,
                () => new TestOptions(),
                includeBaselineModifierValue: false);

            // Expected -------------------------------------------------------
            var expected = new List<string>
            {
                // Baseline core grid (4 rows)
                Row("A","A","A","A","A","A"),
                Row("A","A","B","A","A","A"),
                Row("A","A","A","B","A","A"),
                Row("A","A","B","B","A","A"),

                // Modifier C1 = Z (2 rows)
                Row("A","A","Z","A","A","A"),
                Row("A","A","Z","B","A","A")
            };

            expected.Should().HaveCount(6);
            actual.Select(o => o.ToString())
                  .Should()
                  .BeEquivalentTo(expected, opt => opt.WithoutStrictOrdering(),
                      "Scenario 2 requires exactly the 6 predefined combinations.");
        }
    }
}

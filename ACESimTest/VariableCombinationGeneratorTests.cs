using ACESimBase.Util.Combinatorics;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimTest.Util.Combinatorics
{
    /// <summary>
    /// Unit-test suite for the <see cref="VariableCombinationGenerator"/> after its rewrite that
    /// allows a single dimension to carry both critical and modifier value sets.
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
        // Scenario 1 – 2 Globals × 2 Cores × 2 Modifiers (40 rows)
        //------------------------------------------------------------------
        [TestMethod]
        public void FullScenario_EnumeratesFortyRows()
        {
            var dims = new List<VariableCombinationGenerator.Dimension<TestOptions>>
            {
                // Globals ----------------------------------------------------
                new("G1",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.G1="B") },   // critical list
                    null,
                    IsGlobal:true),
                new("G2",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.G2="B") },
                    null,
                    IsGlobal:true),

                // Cores ------------------------------------------------------
                new("C1",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.C1="B") },
                    null),
                new("C2",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.C2="B") },
                    null),

                // Modifiers (modifier-only) ---------------------------------
                new("M1",
                    null,
                    new[]{ Mutate(_=>{}), Mutate(o=>o.M1="B") }),
                new("M2",
                    null,
                    new[]{ Mutate(_=>{}), Mutate(o=>o.M2="B") })
            };

            var actual = VariableCombinationGenerator.Generate(
                dims,
                () => new TestOptions());

            // Build expected -------------------------------------------------
            var expected = new List<string>();
            string[] AB = { "A", "B" };

            foreach (var g1 in AB)
            foreach (var g2 in AB)
            {
                // Baseline core grid
                foreach (var c1 in AB)
                foreach (var c2 in AB)
                    expected.Add(Row(g1,g2,c1,c2,"A","A"));

                // Modifier M1 = B
                expected.Add(Row(g1,g2,"A","A","B","A"));
                expected.Add(Row(g1,g2,"B","A","B","A"));
                expected.Add(Row(g1,g2,"A","B","B","A"));

                // Modifier M2 = B
                expected.Add(Row(g1,g2,"A","A","A","B"));
                expected.Add(Row(g1,g2,"B","A","A","B"));
                expected.Add(Row(g1,g2,"A","B","A","B"));
            }

            expected.Should().HaveCount(40);
            actual.Select(o=>o.ToString())
                  .Should().BeEquivalentTo(expected, cfg=>cfg.WithoutStrictOrdering());
        }

        //------------------------------------------------------------------
        // Scenario 2 – C1 has critical (A,B) + modifier Z (6 rows)
        //------------------------------------------------------------------
        [TestMethod]
        public void CoreModifierOverlap_EnumeratesSixRows()
        {
            var dims = new List<VariableCombinationGenerator.Dimension<TestOptions>>
            {
                // C1 appears once with both lists ---------------------------
                new("C1",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.C1="B") },          // critical values A,B
                    new[]{ Mutate(_=>{}), Mutate(o=>o.C1="Z") }),          // modifier value Z
                // C2 core only
                new("C2",
                    new[]{ Mutate(_=>{}), Mutate(o=>o.C2="B") },
                    null)
            };

            var actual = VariableCombinationGenerator.Generate(
                dims,
                () => new TestOptions());

            var expected = new List<string>
            {
                Row("A","A","A","A","A","A"),
                Row("A","A","B","A","A","A"),
                Row("A","A","A","B","A","A"),
                Row("A","A","B","B","A","A"),
                Row("A","A","Z","A","A","A"),
                Row("A","A","Z","B","A","A")
            };

            expected.Should().HaveCount(6);
            actual.Select(o=>o.ToString())
                  .Should().BeEquivalentTo(expected, cfg=>cfg.WithoutStrictOrdering());
        }
    }
}
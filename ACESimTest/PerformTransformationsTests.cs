using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.GameSolvingSupport.Settings;
using ACESim;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.Launcher
{
    /// <summary>
    /// Integration tests for the <see cref="PermutationalLauncher.PerformTransformations{T}"/>
    /// method after its migration to the generic <c>VariableCombinationGenerator</c> back‑end.
    ///
    /// The new contract returns a <strong>single flat list</strong> that already contains every
    /// legal combination exactly once, so each test merely confirms that the wrapper still obeys
    /// that shape for various <c>numSupercriticals</c> values.
    /// </summary>
    [TestClass]
    public sealed class PerformTransformationsTests
    {
        #region Minimal stub plumbing
        private sealed class DummyOptions : GameOptions
        {
            public DummyOptions()
            {
                Name             = string.Empty;
                VariableSettings = new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// A pared‑down launcher that exposes <c>BuildSets</c> so tests can call the protected
        /// <c>PerformTransformations</c> with arbitrary <c>numSupercriticals</c>.
        /// </summary>
        private sealed class DummyLauncher : PermutationalLauncher
        {
            #region PermutationalLauncher abstract members (minimal impl.)
            public override List<string> NamesOfVariationSets => new() { "FlatList" };
            public override List<(string, string)> DefaultVariableValues => new()
            {
                ("A", "1"), ("B", "10"), ("C", "100")
            };
            public override List<(string, string[])> CriticalVariableValues => new()
            {
                ("A", new[] { "1", "2" }),
                ("B", new[] { "10", "20" })
            };
            public override string ReportPrefix => "TEST";
            public override List<List<GameOptions>> GetVariationSets(bool _) => throw new NotImplementedException();
            public override List<GameOptions>       GetOptionsSets()                    => throw new NotImplementedException();
            public override GameDefinition          GetGameDefinition()                 => null;
            public override GameOptions             GetDefaultSingleGameOptions()       => new DummyOptions();
            #endregion

            /// <summary>
            /// Builds option sets using the protected <c>PerformTransformations</c> helper.
            /// </summary>
            public List<List<GameOptions>> BuildSets(int numSupercriticals)
            {
                // Critical variables ---------------------------------------------------
                var critA = new List<Func<DummyOptions, DummyOptions>>
                {
                    o => GetAndTransform(o, " A1", t => t.VariableSettings["A"] = 1),
                    o => GetAndTransform(o, " A2", t => t.VariableSettings["A"] = 2)
                };
                var critB = new List<Func<DummyOptions, DummyOptions>>
                {
                    o => GetAndTransform(o, " B10", t => t.VariableSettings["B"] = 10),
                    o => GetAndTransform(o, " B20", t => t.VariableSettings["B"] = 20)
                };

                // Non‑critical variables ----------------------------------------------
                var modB = new List<Func<DummyOptions, DummyOptions>>
                {
                    o => GetAndTransform(o, " B10base", t => t.VariableSettings["B"] = 10), // baseline duplicate
                    o => GetAndTransform(o, " B30",     t => t.VariableSettings["B"] = 30)
                };
                var modC = new List<Func<DummyOptions, DummyOptions>>
                {
                    o => GetAndTransform(o, " C100base", t => t.VariableSettings["C"] = 100), // baseline duplicate
                    o => GetAndTransform(o, " C200",     t => t.VariableSettings["C"] = 200)
                };

                var all = new List<List<Func<DummyOptions, DummyOptions>>> { critA, critB, modB, modC };
                const int numCritical = 2; // A, B

                // Call the migrated PerformTransformations (one overload now)
                return PerformTransformations(
                    all,
                    numCritical,
                    numSupercriticals,
                    includeBaselineValueForNoncritical: false,
                    optionsFactory: () => new DummyOptions());
            }
        }
        #endregion

        //------------------------------------------------------------------
        // Test cases – we only assert that exactly one outer list is returned
        //------------------------------------------------------------------

        [DataTestMethod]
        [DataRow(2)] // both criticals are super‑critical
        [DataRow(1)] // only first critical is super‑critical
        [DataRow(0)] // no super‑critical variables
        public void PerformTransformations_ReturnsSingleFlatList(int numSupercriticals)
        {
            var sets = new DummyLauncher().BuildSets(numSupercriticals);
            sets.Should().HaveCount(1, "the new implementation flattens all combinations into a single list");
            sets[0].Should().NotBeEmpty();
        }
    }
}

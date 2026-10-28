using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests;

[TestClass, DoNotParallelize]
public class EquilibriumWorkflowCompatibilityTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task ArticleProfileReportsMatchBeforeRefactoring(bool endogenous)
    {
        var actual = await EquilibriumWorkflowFixtures.CaptureAsync(endogenous);
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "EquilibriumWorkflows", actual.Case + ".json"));
        Assert.AreEqual(expected, JsonSerializer.Serialize(actual),
            "Article reports, correlated/average semantics, ordering or final policy state changed.");
    }
}

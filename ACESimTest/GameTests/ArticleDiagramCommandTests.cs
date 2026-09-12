using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.GameTests;

[TestClass]
[DoNotParallelize]
public class ArticleDiagramCommandTests
{
    [TestMethod]
    public void ArticleTargetsExcludeEndogenousAndDuplicateBeginning()
    {
        CollectionAssert.DoesNotContain(ArticleDiagramCommand.ExpandTarget("all"), "endogenous");
        CollectionAssert.Contains(ArticleDiagramCommand.ExpandTarget("endogenous"), "endogenous");
        Assert.AreEqual(5, ArticleDiagramCommand.TreeStems.Length);
        Assert.AreEqual(1, ArticleDiagramCommand.TreeStems.Count(s => s.Contains("beginning")));
        Assert.ThrowsException<ArgumentException>(() => ArticleDiagramCommand.ExpandTarget("typo"));
    }

    [TestMethod]
    public void ConfigurationPathsAreRelativeToTheConfigNotTheWorkingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "example-article");
        Assert.AreEqual(Path.Combine(root, "Figures", "tree.tex"),
            ArticleDiagramCommand.ResolvePath(Path.Combine(root, "article-diagrams.json"), "Figures/tree.tex"));
    }

    [TestMethod]
    public async Task NoArgumentsIsHelpAndInvalidArgumentsFailSafely()
    {
        Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync([]));
        Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["unknown"]));
        Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "all", "--sources-only", "--compile-only"]));
        Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "all", "--config"]));
        Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(["diagrams", "all", "--typo"]));
    }

    [TestMethod]
    public async Task ListDoesNotWriteAndMissingExpectedFiguresFailBeforeWriting()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-diagram-tests-");
        try
        {
            string source = Path.Combine(temp.FullName, "existing");
            Directory.CreateDirectory(source);
            string tex = Path.Combine(source, "one.tex");
            await File.WriteAllTextAsync(tex, @"\documentclass{standalone}");
            string configFile = Path.Combine(temp.FullName, "config.json");
            var config = new ArticleDiagramCommand.Configuration
            {
                IndividualResultsDirectory = "existing", ExpectedIndividualCount = 1
            };
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(config));
            string output = Path.Combine(temp.FullName, "review");
            string[] args = ["diagrams", "individual-results", "--config", configFile, "--output-root", output];
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Concat(new[] { "--list" }).ToArray()));
            Assert.IsFalse(Directory.Exists(output));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Concat(new[] { "--sources-only" }).ToArray()));
            Assert.AreEqual(await File.ReadAllTextAsync(tex),
                await File.ReadAllTextAsync(Path.Combine(output, "Individual results", "one.tex")));
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(config with { ExpectedIndividualCount = 2 }));
            string failedOutput = Path.Combine(temp.FullName, "must-not-exist");
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(
                ["diagrams", "individual-results", "--config", configFile, "--output-root", failedOutput, "--sources-only"]));
            Assert.IsFalse(Directory.Exists(failedOutput));
            Assert.IsTrue(File.Exists(tex));
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task CompileOnlyDoesNotRequireExtractionInputs()
    {
        var temp = Directory.CreateTempSubdirectory("acesim-diagram-tests-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temp.FullName, WorkedPathDiagram.FileStem + ".tex"), @"\documentclass{standalone}");
            string config = Path.Combine(temp.FullName, "config.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new ArticleDiagramCommand.Configuration
            {
                GameTreesDirectory = ".", WorkedPathRequest = "absent.json"
            }));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(
                ["diagrams", "worked-path", "--config", config, "--compile-only", "--list"]));
            Assert.AreEqual(1, await ArticleDiagramCommand.RunAsync(
                ["diagrams", "worked-path", "--config", config, "--list"]));
        }
        finally { temp.Delete(recursive: true); }
    }

    [DataTestMethod]
    [DataRow("inverse-signals")]
    [DataRow("party-to-party")]
    public async Task SignalBeliefsUseConfiguredModelsAndSupportReadOnlyAndCompileOnlyModes(string target)
    {
        var temp = Directory.CreateTempSubdirectory("acesim-inverse-tests-");
        try
        {
            string configFile = Path.Combine(temp.FullName, "config.json");
            var config = new ArticleDiagramCommand.Configuration { SignalsDirectory = "figures" };
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(config));
            string[] args = ["diagrams", target, "--config", configFile];
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Append("--list").ToArray()));
            string output = Path.Combine(temp.FullName, "figures");
            Assert.IsFalse(Directory.Exists(output));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Append("--sources-only").ToArray()));
            var tex = Directory.GetFiles(output, "*.tex");
            Assert.AreEqual(6, tex.Length);
            Assert.AreEqual(2, tex.Count(p => p.Contains("Direct binary")));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Concat(new[] { "--compile-only", "--list" }).ToArray()));
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(config with { SignalSpecifications = ["DirectBinaryStateSignals"] }));
            Assert.AreEqual(0, await ArticleDiagramCommand.RunAsync(args.Append("--list").ToArray()));
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task CompilerReportsNonzeroExitAndTimeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        await Assert.ThrowsExceptionAsync<IOException>(() => DiagramCompiler.RunProcessAsync(
            executable, Path.GetTempPath(), 30, "-NoProfile", "-Command", "exit 7"));
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => DiagramCompiler.RunProcessAsync(
            executable, Path.GetTempPath(), 1, "-NoProfile", "-Command", "Start-Sleep -Seconds 30"));
    }
}

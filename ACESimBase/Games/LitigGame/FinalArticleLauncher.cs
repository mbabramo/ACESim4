using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESim;

/// <summary>Uses the established single-exact workflow and shared process coordinator.</summary>
public sealed class FinalArticleLauncher : LitigGameCorrelatedSignalsArticleLauncher
{
    public FinalArticleExecution.Manifest Manifest { get; }
    public string ManifestPath { get; }
    public string ManifestSha256 { get; }
    public override string MasterReportNameForDistributedProcessing => "FinalAgreement";
    private readonly FinalArticleExecution.Case[] newCases;
    public FinalArticleLauncher(string manifest) : base(ProductionRunPlan.AgreementToBargain)
    {
        ManifestPath = Path.GetFullPath(manifest);
        ManifestSha256 = FinalArticleExecution.Hash(ManifestPath);
        Manifest = FinalArticleExecution.Read(ManifestPath);
        newCases = Manifest.Cases.Where(c => !c.Parameters.IsExternalImport).ToArray();
        LaunchSingleOptionsSetOnly = false;
    }
    public override List<GameOptions> GetOptionsSets() => newCases.Select(c => {
        var options=FinalArticleCaseFactory.Create(c.Parameters);
        var original=options.ModifyEvolutionSettings;
        options.ModifyEvolutionSettings=settings => { original?.Invoke(settings); settings.UseExistingEquilibriaIfAvailable=false; };
        return (GameOptions)options;
    }).ToList();

    protected override async Task BeforeDistributedOptimizationAsync(int optionSetIndex, int repetition, int? scenario)
    {
        if (repetition != 0 || scenario != null || Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT") != "1")
            throw new InvalidOperationException("Primary article cases require one single-threaded exact initialization without separated scenarios.");
        if (FinalArticleExecution.Hash(ManifestPath) != ManifestSha256) throw new IOException("Execution manifest changed.");
        var item = newCases[optionSetIndex];
        var options = FinalArticleCaseFactory.Create(item.Parameters);
        // Initialization is read-only and starts no solve. This guard occurs before
        // constructing the actual solver developer or acquiring its permanent claim.
        var check = await ArticleWorkedPathExtraction.InitializeAsync(options);
        var identity = StrategicGameFingerprint.Capture(check);
        if (identity != item.Identity) throw new InvalidDataException("Instantiated game differs from frozen inventory.");
        var registry = new ArticleSolveRegistry(Manifest.ClaimsDirectory, Manifest.ExternalRegistry.Path);
        var claim = registry.TryClaimNew(item.Parameters.Id, identity.CompleteSha256,
            FinalArticleExecution.Hash(typeof(FinalArticleLauncher).Assembly.Location), ManifestSha256);
        if (!claim.Allowed) throw new InvalidOperationException(claim.Reason + ": " + item.Parameters.Id);
        string checkpoint = Path.Combine(Manifest.ResultsDirectory,item.Parameters.Id+".dispatch.json");
        using var stream = new FileStream(checkpoint,FileMode.CreateNew,FileAccess.Write);
        JsonSerializer.Serialize(stream,new { item.Parameters.Id, Identity=identity, Claim=claim,
            ManifestSha256, Seed=0, Arithmetic="ExactValue; one initialization; no fallback", StartedUtc=DateTime.UtcNow },FinalArticleExecution.Json);
        stream.Flush(true);
    }
}

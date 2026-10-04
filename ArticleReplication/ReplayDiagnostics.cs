using ACESim;
using LitigCharts;
using System.IO.Compression;
using System.Text.Json;

namespace ArticleReplication;

/// <summary>Opt-in, read-only terminal data for diagnosing platform differences in report replay.</summary>
public static class ReplayDiagnostics
{
    public static void Write(string output,LitigGameOptions options,IEnumerable<(GameProgress theProgress,double weight)> saved)
    {
        Directory.CreateDirectory(output);
        object? prior=null;
        if(options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator continuous)
        {
            var q=continuous.GetPriorQualityForReporting();prior=new{q.Quality,q.Weights};
        }
        Files.Save(Path.Combine(output,"replay-diagnostic.json"),new{Runtime=Environment.Version.ToString(),
            OS=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            HardwareIntrinsics=Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),Prior=prior});
        using var file=new FileStream(Path.Combine(output,"terminal-replay.json.gz"),FileMode.CreateNew);
        using var gzip=new GZipStream(file,CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip,saved.Select(x=>
        {
            var p=(LitigGameProgress)x.theProgress;
            return new{x.weight,p.IsTrulyLiable,p.PFiles,p.DAnswers,p.PAbandons,p.DDefaults,p.TrialOccurs,
                p.PWinsAtTrial,p.SettlementValue,p.DamagesAwarded,p.PChangeWealth,p.DChangeWealth,
                p.TotalExpensesIncurred,p.FalseNegativeShortfall,p.FalsePositiveExpenditures,Payment=SavedProfileWelfare.Payment(p)};
        }),Files.Json);
    }
}

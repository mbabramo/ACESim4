namespace ArticleReplication;

public static class RunSettings
{
    public static CorrelatedSignalsSettings Resolve(Dictionary<string,string> args)
    {
        var s=new CorrelatedSignalsSettings();
        if(args.TryGetValue("costs",out var costs))s=s with{MainCostMultipliers=costs.Split(',').Select(double.Parse).ToArray()};
        if(args.TryGetValue("steps",out var steps))s=s with{Steps=steps.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)};
        if(args.TryGetValue("starts",out var starts))s=s with{StartsPerCore=int.Parse(starts)};
        if(args.TryGetValue("pivots",out var pivots))s=s with{ApproximatePivotLimit=int.Parse(pivots)};
        if(args.TryGetValue("cutoff",out var cutoff))s=s with{ApproximateRoundingCutoff=double.Parse(cutoff)};
        if(args.TryGetValue("gain-units",out var gain))s=s with{ApproximateGainUnits=Enum.Parse<ACESimBase.Games.LitigGame.ManualReports.ArticleApproximateGainUnits>(gain)};
        if(args.TryGetValue("extensions",out var ext))s=s with{IncludeExtensions=bool.Parse(ext)};
        if(args.TryGetValue("trial-only",out var trial))s=s with{IncludeTrialOnly=bool.Parse(trial)};
        if(args.TryGetValue("noise",out var noise))s=s with{NoiseLevels=noise.Split(',').Select(double.Parse).ToArray()};
        if(args.TryGetValue("trembles",out var trembles))s=s with{Trembles=trembles.Split(',').Select(double.Parse).ToArray()};
        if(args.TryGetValue("tremble-directions",out var directions))s=s with{TrembleDirections=int.Parse(directions)};
        if(args.TryGetValue("grids",out var grids))s=s with{Grids=grids.Split(',').Select(g=>{var risk=g.Split(':');if(risk.Length>2)throw new ArgumentException("Use signals x offers with optional :rn or :ra.");var p=risk[0].Split('x');if(p.Length!=2)throw new ArgumentException("Use signals x offers.");return new CorrelatedSignalsSettings.Grid(byte.Parse(p[0]),int.Parse(p[1]),risk.Length==2?risk[1]:null);}).ToArray()};
        if(args.TryGetValue("truth-exponents",out var truth))s=s with{TruthExponents=truth.Split(',').Select(double.Parse).ToArray()};
        if(args.TryGetValue("manuscript",out var manuscript))s=s with{Steps=bool.Parse(manuscript)?s.Steps.Union(["Manuscript"]).ToArray():s.Steps.Except(["Manuscript"]).ToArray()};
        return s;
    }
}

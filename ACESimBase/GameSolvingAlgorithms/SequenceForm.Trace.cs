using System;
using System.Linq;
using ACESim;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using Rationals;

namespace ACESimBase.GameSolvingAlgorithms;

public partial class SequenceForm
{
    public System.Collections.Generic.IReadOnlyList<InformationSetInfo> TraceInformationSets => InformationSetInfos.AsReadOnly();
    public double[][] TraceOutcomeUtilities() => Outcomes.Select(n => n.Utilities.Take(2).ToArray()).ToArray();

    /// <summary>Explicit diagnostic solve. Does not save equilibria or touch the
    /// production multi-prior cache. Callbacks are optional and synchronous.</summary>
    public double[] TraceECTA<T>(double[] initialProbabilities = null,
        Action<ECTATreeDefinition<T>> beforeSolve = null,
        Action<ECTATreeDefinition<T>, ECTAPivotSnapshot> afterPivot = null,
        double probabilityFloor = 0, int maxPivots = 0, int seed = 0) where T : IMaybeExact<T>, new()
    {
        DetermineGameNodeRelationships();
        if (BlockedPlayerActions != null) throw new InvalidOperationException("Tracing requires unrestricted actions.");
        var runner = GetECTARunner<T>(1);
        runner.maxPivotSteps = maxPivots;
        runner.initialProbabilityFloor = probabilityFloor;
        runner.useSuppliedInitialProbabilities = initialProbabilities != null;
        runner.outputEquilibrium = false;
        runner.BeforeSolve = beforeSolve;
        runner.PivotObserver = afterPivot;
        // Null uses the production prior generator (exact seed zero is uniform).
        var initial = initialProbabilities?.Select(p => IMaybeExact<T>.FromRational((Rational)p)).ToArray();
        var results = runner.Execute(SetupECTA, null, seed, initial);
        if (results.Count != 1) throw new InvalidOperationException("ECTA path failed to reach an equilibrium.");
        return results[0].equilibrium.Select(p => p.AsDouble).ToArray();
    }
}

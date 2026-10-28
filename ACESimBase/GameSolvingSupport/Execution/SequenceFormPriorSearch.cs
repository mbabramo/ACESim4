using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>Historical multi-prior recovery/fallback protocol. Numerical solvers retain their
/// own initialization rules and within-batch pivot budgets. Recovery frequency is not a mixture weight.</summary>
public static class SequenceFormPriorSearch
{
    public static EquilibriumSearchResult Collect(
        int requested, bool parallel, bool tryInexact,
        Func<int, List<(double[] equilibrium, int frequency)>> exact,
        Func<int, List<(double[] equilibrium, int frequency)>> inexact,
        Action<string> log = null)
    {
        if (requested < 1) throw new ArgumentOutOfRangeException(nameof(requested));
        var equilibria = new List<(double[] equilibrium, int frequency)>();
        int exactAttempts = 0, inexactAttempts = 0;
        if (!parallel)
        {
            log?.Invoke("Using exact arithmetic for initial prior");
            exactAttempts++;
            var initial = exact(1);
            if (initial.Count == 0)
                throw new InvalidOperationException("The initial exact prior returned no verified equilibrium.");
            equilibria.Add(initial.First());
        }
        int remaining = requested - equilibria.Sum(x => x.frequency);
        if (tryInexact && remaining > 0)
        {
            log?.Invoke($"Trying inexact arithmetic for up to {remaining} random priors");
            inexactAttempts += remaining;
            AddAdditionalEquilibria(equilibria, inexact(remaining));
        }
        remaining = requested - equilibria.Sum(x => x.frequency);
        if (remaining > 0)
        {
            log?.Invoke($"Using exact arithmetic for {remaining} remaining random priors");
            exactAttempts += remaining;
            AddAdditionalEquilibria(equilibria, exact(remaining));
        }
        if (equilibria.Count == 0)
            throw new InvalidOperationException("No verified equilibrium was recovered after exact fallback.");
        // Failed exact attempts remain failures in the recovery report, not invented recoveries.
        log?.Invoke($"Verified recoveries: {equilibria.Sum(x => x.frequency)}; requested: {requested}; distinct profiles: {equilibria.Count}.");
        return new EquilibriumSearchResult(equilibria, requested, exactAttempts, inexactAttempts);
    }

    private static void AddAdditionalEquilibria(List<(double[] equilibrium, int frequency)> equilibria, List<(double[] equilibrium, int frequency)> additionalEquilibria)
    {
        foreach (var additionalEquilibrium in additionalEquilibria)
        {
            bool matchFound = false;
            for (int i = 0; i < equilibria.Count; i++)
            {
                if (equilibria[i].equilibrium.SequenceEqual(additionalEquilibrium.equilibrium))
                {
                    equilibria[i] = (equilibria[i].equilibrium, additionalEquilibrium.frequency + equilibria[i].frequency);
                    matchFound = true;
                    break;
                }
            }
            if (!matchFound)
                equilibria.Add(additionalEquilibrium);
        }
    }
}

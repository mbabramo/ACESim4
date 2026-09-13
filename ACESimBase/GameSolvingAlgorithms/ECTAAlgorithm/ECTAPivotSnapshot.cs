using System;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;

/// <summary>Read-only copies of the augmented LCP state after an actual pivot.
/// Z excludes z0. Raw LCP variables are not behavioral probabilities.</summary>
public sealed record ECTAPivotSnapshot(int Pivot, int LeavingVariable, int EnteringVariable,
    bool Final, double Auxiliary, double[] Z, double[] W,
    double OriginalFeasibilityViolation, double OriginalComplementarityViolation,
    double AugmentedComplementarityViolation);

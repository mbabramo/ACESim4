using ACESim;
using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using System;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Reporting;

/// <summary>Native economic replay of detached joint weights, without installing policies
/// or modifying training state. The callback receives expanded latent outcomes.</summary>
public static class JointPolicyOutcomeReplay
{
    public static void Visit(StrategiesDeveloperBase developer, PolicyGameTree tree,
        PlayerPolicy[] plaintiffs, PlayerPolicy[] defendants, double[] weights, Action<GameProgress, double> visit)
    {
        bool reporting = developer.GamePlayer.ReportingMode;
        developer.GamePlayer.ReportingMode = true;
        try
        {
            tree.VisitJointTerminals(plaintiffs, defendants, weights, (actions, probability) =>
            {
                var (progress, _) = developer.GamePlayer.PlayPath(actions.ToList(), false);
                var expanded = progress.GetGameProgressIncludingAnySplits();
                if (developer.GameDefinition.GenerateConsistentGameProgresses)
                    expanded = expanded.SelectMany(x => x.progress.BayesianCalculations_GenerateAllConsistentGameProgresses(x.Item2));
                foreach (var item in expanded)
                {
                    double weight = probability * item.Item2;
                    if (!double.IsFinite(weight) || weight < 0) throw new ArithmeticException("Invalid expanded outcome weight.");
                    if (weight > 0) visit(item.progress, weight);
                }
            });
        }
        finally { developer.GamePlayer.ReportingMode = reporting; }
    }
}

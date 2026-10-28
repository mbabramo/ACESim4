using ACESim;
using ACESimBase.Util.Serialization;
using System;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>Scenario/PCA artifact naming shared by the execution and analysis workflows.</summary>
public sealed class WorkflowFiles
{
    private readonly StrategiesDeveloperBase developer;
    public WorkflowFiles(StrategiesDeveloperBase developer)
    {
        this.developer = developer ?? throw new ArgumentNullException(nameof(developer));
    }

    public void GetPathAndFilenameForScenario(int overallScenarioIndex, out string path, out string filename)
    {
        path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
        filename = developer.GameDefinition.OptionSetName + "-" + developer.EvolutionSettings.SerializeResultsPrefixPlus(overallScenarioIndex, developer.GameDefinition.NumScenarioPermutations);
    }

    public void GetPathAndFilenameForOptionSet(string suffix, out string path, out string filename)
    {
        path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
        filename = developer.GameDefinition.OptionSetName + "-" + suffix;
    }
}

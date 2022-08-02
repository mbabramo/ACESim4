using ACESimBase.StagedTasks;
using Lazinator.Attributes;
using Lazinator.Core;
using System;

namespace ACESimBase.StagedTasks
{
    [Serializable]
    public partial class IndividualTask : IIndividualTask
    {
        public TimeSpan DurationOfLongestComplete => Completed == null ? TimeSpan.FromSeconds(0) : (TimeSpan) (Completed - Started); // we do not count incomplete tasks here
        public bool Complete
        {
            get;
            set;
        }

        public IndividualTask(string taskType, int id, int repetition, int? restrictToScenarioIndex)
        {
            TaskType = taskType;
            ID = id;
            Repetition = repetition;
            RestrictToScenarioIndex = restrictToScenarioIndex;
        }

        public override string ToString()
        {
            return $"{TaskType} {ID} {Repetition} {(RestrictToScenarioIndex != null ? $"Scenario: {RestrictToScenarioIndex}" : "")}Started:{Started} Complete:{Complete}";
        }
    }
}

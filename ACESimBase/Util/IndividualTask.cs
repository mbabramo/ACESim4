using System;

namespace ACESim.Util
{
    [Serializable]
    public class IndividualTask
    {
        public string TaskType;
        public int ID;
        public int Repetition;
        public int? RestrictToScenarioIndex;
        public DateTime? Started;
        public DateTime? Completed;
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

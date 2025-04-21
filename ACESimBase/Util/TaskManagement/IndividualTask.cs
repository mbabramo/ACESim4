using System;

namespace ACESimBase.Util.TaskManagement
{
    [Serializable]
    public class IndividualTask
    {
        public string TaskType;
        public int ID;
        public int Repetition;
        public int? RestrictToScenarioIndex;
        public DateTime? Started;
        public bool Complete;

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

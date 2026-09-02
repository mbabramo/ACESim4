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
        public string PlanLabel;
        public DateTime? Started;
        public bool Complete;
        public bool Failed;

        public IndividualTask(
            string taskType,
            int id,
            int repetition,
            int? restrictToScenarioIndex,
            string planLabel = null)
        {
            TaskType = taskType;
            ID = id;
            Repetition = repetition;
            RestrictToScenarioIndex = restrictToScenarioIndex;
            PlanLabel = planLabel ?? "";
        }

        public override string ToString()
        {
            return $"{Identity} Started:{Started:O} Complete:{Complete} Failed:{Failed}";
        }

        public string Identity =>
            $"{TaskType} ID {ID} Repetition {Repetition}" +
            (RestrictToScenarioIndex != null ? $" Scenario {RestrictToScenarioIndex}" : "");
    }
}

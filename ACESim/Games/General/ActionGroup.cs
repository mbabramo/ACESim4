using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ActionGroup
    {
        public List<ActionPoint> ActionPoints;
        public int? ActionGroupExecutionIndex;
        public int? ModuleNumber;
        public string Name;
        public List<string> Tags;
        private List<int?> RepetitionCorrespondingToTag;
        private List<bool> IsFirstRepetitionOfThisTag;
        private List<bool> IsLastRepetitionOfThisTag;
        public object ActionGroupSettings;
        public List<ActionGroup> FirstRepetitionCorrespondingToTag;
        public List<ActionGroup> PreviousRepetitionCorrespondingToTag;
        public int? FirstDecisionNumber;
        public int? LastDecisionNumber;

        public bool ContainsTag(string tag)
        {
            return Tags != null && Tags.Contains(tag);
        }

        public void AddTag(string tag)
        {
            if (Tags == null)
            {
                Tags = new List<string>();
                RepetitionCorrespondingToTag = new List<int?>();
                IsFirstRepetitionOfThisTag = new List<bool>();
                IsLastRepetitionOfThisTag = new List<bool>();
            }
            if (!Tags.Contains(tag))
            {
                Tags.Add(tag);
                RepetitionCorrespondingToTag.Add(null);
                IsFirstRepetitionOfThisTag.Add(true);
                IsLastRepetitionOfThisTag.Add(true);
            }
        }

        public int? GetRepetitionNumberForTag(string tag)
        {
            int repetitionNum = 0;
            if (Tags != null)
                for (int t = 0; t < Tags.Count; t++)
                    if (Tags[t] == tag)
                    {
                        return RepetitionCorrespondingToTag[t] ?? 1;
                    }
            // no tag found
            return null;
        }

        public void GetRepetitionInfoForTag(string tag, out int? repetitionNum, out bool isFirstRepetition, out bool isLastRepetition)
        {
            if (Tags != null)
                for (int t = 0; t < Tags.Count; t++)
                    if (Tags[t] == tag)
                    {
                        repetitionNum = RepetitionCorrespondingToTag[t] ?? 1;
                        isFirstRepetition = IsFirstRepetitionOfThisTag[t];
                        isLastRepetition = IsLastRepetitionOfThisTag[t];
                        return;
                    }
            // no tag found
            repetitionNum = null;
            isFirstRepetition = false;
            isLastRepetition = false;
        }

        public void SetRepetitionForTag(string tag, int repetition, bool isFirstRepetition, bool isLastRepetition)
        {
            if (tag == "All")
                return;
            for (int t = 0; t < Tags.Count; t++)
                if (Tags[t] == tag)
                {
                    RepetitionCorrespondingToTag[t] = repetition;
                    IsFirstRepetitionOfThisTag[t] = isFirstRepetition;
                    IsLastRepetitionOfThisTag[t] = isLastRepetition;
                    return;
                }
            throw new Exception("Tag does not exist for this ActionGroup");
        }

        public string RepetitionTagString()
        {
            string rts = "";
            if (RepetitionCorrespondingToTag != null)
                rts = String.Join(", ", RepetitionCorrespondingToTag.Where(x => x != null));
            if (rts != "")
                rts = "-" + rts;
            return rts;
        }

        public string RepetitionTagStringLongForm()
        {
            string repetitionInfo = "";
            if (Tags != null)
                for (int t = 0; t < Tags.Count; t++)
                {
                    if (RepetitionCorrespondingToTag[t] != null)
                        repetitionInfo += (t == 0 ? "" : " ") + Tags[t] + ": " + RepetitionCorrespondingToTag[t];
                };
            return repetitionInfo;
        }

        private bool ActionGroupMatchesThisOneExceptInSpecifiedRespect(ActionGroup ag, int tagIndex, int desiredRepetitionForThatTag)
        {
            bool match = true;
            for (int t = 0; t < Tags.Count && match; t++)
            {
                match = ag.Tags != null && ag.Tags.SequenceEqual(Tags);
                if (match)
                {
                    if (t == tagIndex)
                        match = ag.RepetitionCorrespondingToTag != null && ag.RepetitionCorrespondingToTag[t] == desiredRepetitionForThatTag;
                    else
                        match = ag.RepetitionCorrespondingToTag != null && ag.RepetitionCorrespondingToTag[t] == RepetitionCorrespondingToTag[t];
                }
            }
            return match;
        }

        private ActionGroup FindMatchingActionGroup(List<ActionGroup> agList, int tagIndex, int desiredRepetitionForThatTag)
        {
            ActionGroup match = agList.FirstOrDefault(x => ActionGroupMatchesThisOneExceptInSpecifiedRespect(x, tagIndex, desiredRepetitionForThatTag));
            return match;
        }

        public void SetFirstAndPreviousRepetitionsFromList(List<ActionGroup> agList)
        {
            FirstRepetitionCorrespondingToTag = new List<ActionGroup>();
            PreviousRepetitionCorrespondingToTag = new List<ActionGroup>();
            if (Tags != null)
                for (int t = 0; t < Tags.Count; t++)
                {
                    if (RepetitionCorrespondingToTag[t] == null)
                    {
                        FirstRepetitionCorrespondingToTag.Add(null);
                        PreviousRepetitionCorrespondingToTag.Add(null);
                    }
                    else
                    {
                        FirstRepetitionCorrespondingToTag.Add(FindMatchingActionGroup(agList, t, 1));
                        PreviousRepetitionCorrespondingToTag.Add(FindMatchingActionGroup(agList, t, (int) RepetitionCorrespondingToTag[t] - 1));
                    }
                }
        }

        public ActionGroup DeepCopy()
        {
            ActionGroup ag = new ActionGroup()
            {
                ActionGroupExecutionIndex = ActionGroupExecutionIndex,
                ModuleNumber = ModuleNumber,
                Name = Name,
                Tags = Tags == null ? null : Tags.ToList(),
                RepetitionCorrespondingToTag = RepetitionCorrespondingToTag == null ? null : RepetitionCorrespondingToTag.ToList(),
                IsFirstRepetitionOfThisTag = IsFirstRepetitionOfThisTag == null ? null : IsFirstRepetitionOfThisTag.ToList(),
                IsLastRepetitionOfThisTag = IsLastRepetitionOfThisTag == null ? null : IsLastRepetitionOfThisTag.ToList(),
                ActionGroupSettings = ActionGroupSettings, // note that this is a shallow copy, since we don't know the object's type
                FirstRepetitionCorrespondingToTag = FirstRepetitionCorrespondingToTag == null ? null : FirstRepetitionCorrespondingToTag.ToList(),
                PreviousRepetitionCorrespondingToTag = PreviousRepetitionCorrespondingToTag == null ? null : PreviousRepetitionCorrespondingToTag.ToList()
            };
            ag.ActionPoints = ActionPoints.Select(x => x.DeepCopy(ag)).ToList();
            return ag;
        }

        public override string ToString()
        {
            string repetitionInfo = RepetitionTagStringLongForm();
            string theString = Name + repetitionInfo + ": ";
            foreach (ActionPoint ap in ActionPoints)
            {
                string asteriskForDecisionPoint = ap is DecisionPoint ? "*" : "";
                theString += ap.Name + asteriskForDecisionPoint + " ";
            }
            return theString;
        }

    }
}

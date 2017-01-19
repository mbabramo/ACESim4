using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class InputGroup
    {
        public string Name;
        public List<InputInfo> Inputs;
    }

    [Serializable]
    public class InputInfo
    {
        public string InputName;
        public List<InputGroup> SubGroups;
    }


    public static class ListInputGroupPlusCopier
    {
        public static List<InputGroupPlus> DeepCopy(List<InputGroupPlus> original, InputInfoPlus parent)
        {
            if (original == null)
            {
                return null;
            }

            List<InputGroupPlus> listCopy = new List<InputGroupPlus>();
            foreach (InputGroupPlus inputGroupPlusOriginal in original)
            {
                List<InputGroupPlus> earlierSiblings = listCopy.ToList();
                InputGroupPlus inputGroupPlusCopy = inputGroupPlusOriginal.DeepCopy(earlierSiblings, parent);
                listCopy.Add(inputGroupPlusCopy);
            }
            return listCopy;
        }

        public static InputGroupPlus FindCorrespondingInputGroupInCopy(List<InputGroupPlus> original, List<InputGroupPlus> copy, InputGroupPlus itemToFindInOriginal)
        {
            if (itemToFindInOriginal == null)
                return null;
            for (int i = 0; i < original.Count(); i++)
            {
                InputGroupPlus inputGroupPlusOriginal = original[i];
                if (inputGroupPlusOriginal == itemToFindInOriginal)
                    return copy[i];
                else
                {
                    InputGroupPlus match = null;
                    if (original[i].Inputs != null)
                    {
                        var subgroupsOriginal = original[i].Inputs.Select(x => x.SubGroups).ToList();
                        var subgroupsCopy = copy[i].Inputs.Select(x => x.SubGroups).ToList();
                        for (int sg = 0; sg < subgroupsOriginal.Count(); sg++)
                        {
                            match = FindCorrespondingInputGroupInCopy(subgroupsOriginal[sg], subgroupsCopy[sg], itemToFindInOriginal);
                            if (match != null)
                                return match;
                        }
                    }
                }
            }
            return null;
        }

        public static void SetOverallStrategyForIStrategyComponents(List<InputGroupPlus> list, Strategy overallStrategy)
        {
            if (list != null)
                list.ForEach(x => x.SetOverallStrategyForIStrategyComponents(overallStrategy));
        }
    }

    [Serializable]
    public class InputGroupPlus : ISerializationPrep
    {
        public string Name;
        public IStrategyComponent IStrategyComponent;
        public List<InputInfoPlus> Inputs;
        public List<InputGroupPlus> EarlierSiblings;
        public InputInfoPlus Parent;
        public int DevelopmentOrder;

        public int CountContainedInputGroupPlus()
        {
            return 1 + Inputs.Sum(x => x.CountContainedInputGroupPlus());
        }

        public InputGroupPlus DeepCopy(List<InputGroupPlus> earlierSiblings, InputInfoPlus parent)
        {
            List<InputInfoPlus> inputs = Inputs.Select(x => x.DeepCopy(null)).ToList(); // we haven't created the group they are in yet
            InputGroupPlus copy = new InputGroupPlus() { Name = Name, IStrategyComponent = IStrategyComponent.DeepCopy(), Inputs = inputs, EarlierSiblings = earlierSiblings, Parent = parent };
            foreach (var input in inputs)
                input.Group = copy; // now we can set the group
            return copy;
        }

        public void SetOverallStrategyForIStrategyComponents(Strategy overallStrategy)
        {
            IStrategyComponent.OverallStrategy = overallStrategy;
            foreach (InputInfoPlus inputInfoPlus in Inputs)
                inputInfoPlus.SetOverallStrategyForIStrategyComponents(overallStrategy);
        }

        public virtual void PreSerialize()
        {
            IStrategyComponent.PreSerialize();
        }

        public virtual void UndoPreSerialize()
        {
            IStrategyComponent.UndoPreSerialize();
        }
    }

    

    [Serializable]
    public class InputInfoPlus
    {
        public string InputName;
        public int Index;
        public InputGroupPlus Group;
        public List<InputGroupPlus> SubGroups;

        public int CountContainedInputGroupPlus()
        {
            if (SubGroups == null || !SubGroups.Any())
                return 0;
            return SubGroups.Sum(x => x.CountContainedInputGroupPlus());
        }

        public InputInfoPlus DeepCopy(InputGroupPlus group)
        {
            InputInfoPlus returnInputInfoPlus = new InputInfoPlus() { InputName = InputName, Index = Index, Group = group, SubGroups = ListInputGroupPlusCopier.DeepCopy(SubGroups, null) };
            if (returnInputInfoPlus.SubGroups != null)
            {
                foreach (InputGroupPlus subgroup in returnInputInfoPlus.SubGroups)
                {
                    subgroup.Parent = returnInputInfoPlus;
                }
            }
            return returnInputInfoPlus;
        }

        public void SetOverallStrategyForIStrategyComponents(Strategy overallStrategy)
        {
            if (SubGroups != null)
                SubGroups.ForEach(x => x.SetOverallStrategyForIStrategyComponents(overallStrategy));
        }
    }

}

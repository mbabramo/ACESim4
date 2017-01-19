using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ChangeSimulationSettings : IChangeSimulationSettingPermutator
    {
        public List<Setting> groupOfSimultaneousSettingChanges;
        public ChangeSimulationSettings(List<Setting> theNewValues)
        {
            groupOfSimultaneousSettingChanges = theNewValues;
        }

        public List<List<Setting>> GenerateAll()
        {
            return new List<List<Setting>>() { groupOfSimultaneousSettingChanges }; // there is just one list of changes to be done concurrently
        }
    }
}

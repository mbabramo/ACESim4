using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class SettingsSet
    {
        public string name;
        public List<Setting> settings;

        public SettingsSet()
        {
        }

        public SettingsSet(string name)
        {
            this.name = name;
        }
        public int CountInputsNeeded()
        {
            return settings.OfType<SettingDistribution>().Sum(x => x.Value.NumberSeedsRequired);
        }
        public void ConfirmNoInputsNeeded()
        {
            foreach (SettingDistribution theDistribution in settings.OfType<SettingDistribution>())
                if (theDistribution.Value.NumberSeedsRequired > 0)
                    throw new Exception("Distribution " + theDistribution.Name + " requires inputs.");
        }
    }
}

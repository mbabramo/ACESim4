using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class StoredSettingAndFieldInfo
    {

        public List<SettingAndFieldInfo> settingAndFieldInfoList;
        public int numSeeds;
        public Func<double[], Dictionary<string, double>, object> compiledExpression;
        public bool[] flipSeed;
        public int?[] substituteSeed;
    }
}

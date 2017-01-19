using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public abstract class SettingVariable : Setting
    {
        public SettingVariable(string name, Dictionary<string, double> allVariablesFromProgram) : base(name, allVariablesFromProgram)
        {
        }
    }
}

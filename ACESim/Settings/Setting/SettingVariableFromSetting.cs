using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingVariableFromSetting : SettingVariable
    {
        public string VariableName;

        public SettingVariableFromSetting(string name, Dictionary<string, double> allVariablesFromProgram, string varName)
            : base(name, allVariablesFromProgram)
        {
            VariableName = varName;
            Type = SettingType.VariableFromSetting;
        }

        public override Setting DeepCopy()
        {
            return new SettingVariableFromSetting(Name, AllVariablesFromProgram, VariableName);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return compiler.GetExpressionForVariableFromSetting(VariableName);
        }

        internal override double GetDoubleValue(List<double> inputs)
        {
            double returnVal = VariableFromSettingTracker.Value.RequestNextSettingVariableFromSetting();
            return returnVal;
        }

        public override Type GetReturnType()
        {
            return typeof(double);
        }

        public override bool ContainsUnresolvedVariable()
        {
            return true;
        }

        public override void RequestVariableFromSettingValues(Dictionary<string, Setting> previousSettings, List<Setting> requestedSettings, ref int requestNumber)
        {
            if (!previousSettings.ContainsKey(VariableName))
                throw new Exception("Trying to request a variable that has not been set yet.");
            Setting requestedSetting = previousSettings[VariableName];
            requestedSettings.Add(requestedSetting);
            requestedSetting.AddValueRequested(requestNumber);
            requestNumber++;
        }
    }
}

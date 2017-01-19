using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingVariableFromProgram : SettingVariable
    {
        public string VariableName;

        public SettingVariableFromProgram(string name, Dictionary<string, double> allVariablesFromProgram, string varName)
            : base(name, allVariablesFromProgram)
        {
            VariableName = varName;
            Type = SettingType.VariableFromProgram;
        }


        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return compiler.GetExpressionForVariableFromProgram(VariableName);
        }

        public override Setting DeepCopy()
        {
            return new SettingVariableFromProgram(Name, AllVariablesFromProgram, VariableName);
        }

        internal override double GetDoubleValue(List<double> inputs)
        {
            if (!AllVariablesFromProgram.ContainsKey(VariableName))
                throw new Exception("Variable " + VariableName + " has not been set. If this is within a changesSimulationSetting, be sure you use a variable setting.");
            double theDoubleValue = AllVariablesFromProgram[VariableName];
            return (double)theDoubleValue;
        }

        public override Type GetReturnType()
        {
            return typeof(double);
        }

        public override bool ContainsUnresolvedVariable()
        {
            return !AllVariablesFromProgram.ContainsKey(VariableName);
        }
    }
}

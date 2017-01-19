using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingString : Setting
    {
        public string Value;

        public SettingString(string name, Dictionary<string, double> allVariablesFromProgram, string value)
            : base(name, allVariablesFromProgram)
        {
            Value = value;
            Type = SettingType.String;
        }


        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return Expression.Constant(Value);
        }

        public override Setting DeepCopy()
        {
            return new SettingString(Name, AllVariablesFromProgram, Value);
        }

        public override Type GetReturnType()
        {
            return typeof(String);
        }
    }
}

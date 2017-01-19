using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingDouble : Setting
    {
        public double Value;

        public SettingDouble(string name, Dictionary<string, double> allVariablesFromProgram, double value)
            : base(name, allVariablesFromProgram)
        {
            Value = value;
            Type = SettingType.Double;
        }

        public override Setting DeepCopy()
        {
            return new SettingDouble(Name, AllVariablesFromProgram, Value);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return Expression.Constant(Value);
        }

        internal override double GetDoubleValue(List<double> inputs)
        {
            return Value;
        }

        public override Type GetReturnType()
        {
            return typeof(double);
        }
    }
}

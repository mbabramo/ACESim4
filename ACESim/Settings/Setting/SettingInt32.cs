using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingInt32 : Setting
    {
        public int Value;

        public SettingInt32(string name, Dictionary<string, double> allVariablesFromProgram, int value)
            : base(name, allVariablesFromProgram)
        {
            Value = value;
            Type = SettingType.Int32;
        }

        public override Setting DeepCopy()
        {
            return new SettingInt32(Name, AllVariablesFromProgram, Value);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return Expression.Constant(Value);
        }

        internal override double GetDoubleValue(List<double> inputs)
        {
            double theValue = Convert.ToDouble(Value);
            if (VariableFromSettingTracker.Value != null && ValueIsRequestedAsVariableFromSetting)
                VariableFromSettingTracker.Value.StoreSettingVariableFromSetting(ValueFromSettingRequestedOrder[0], theValue);
            return theValue;
        }

        public override Type GetReturnType()
        {
            return typeof(int);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingBoolean : Setting
    {
        internal bool _Value;
        public bool Value { get { return _Value; } set { _Value = value; AllVariablesFromProgram[Name] = _Value ? 1.0 : 0.0; } }

        public SettingBoolean(string name, Dictionary<string, double> allVariablesFromProgram, bool value)
            : base(name, allVariablesFromProgram)
        {
            Value = value;
            Type = SettingType.Boolean;
        }


        public override Setting DeepCopy()
        {
            return new SettingBoolean(Name, AllVariablesFromProgram, Value);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return Expression.Constant(Value);
        }

        public override Type GetReturnType()
        {
            return typeof(bool);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingStrategy : Setting
    {
        public Strategy Value;
        public string Filename;
        public int StrategyNumber;

        public SettingStrategy(string name, Dictionary<string, double> allVariablesFromProgram, string theFilename, int theStrategyNum)
            : base(name, allVariablesFromProgram)
        {
            Filename = theFilename;
            StrategyNumber = theStrategyNum;
            Type = SettingType.Strategy;
        }

        public override Setting DeepCopy()
        {
            return new SettingStrategy(Name, AllVariablesFromProgram, Filename, StrategyNumber);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            throw new Exception("This is not implemented yet.");
        }

        public void LoadFromFile()
        {
            try
            {
                Strategy[] strategies = StrategySerialization.DeserializeStrategies(Filename);
                //PolynomialStrategy[] strategies = (PolynomialStrategy[])XMLSerialization.GetSerializedObject(Filename, typeof(PolynomialStrategy[]));
                Value = strategies[StrategyNumber];
            }
            catch
            {
                Value = null;
            }
        }

        public Strategy GetStrategy()
        {
            if (Value == null)
            {
                LoadFromFile();
                if (Value == null)
                    throw new Exception("Strategy number " + StrategyNumber + " could not be loaded from " + Filename);
            }
            return Value;
        }

        public override Type GetReturnType()
        {
            return typeof(Strategy);
        }
    }
}

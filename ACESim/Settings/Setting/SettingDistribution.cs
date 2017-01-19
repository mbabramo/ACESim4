using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public class SettingDistribution : Setting
    {
        public IDistribution Value;

        public SettingDistribution(string name, Dictionary<string, double> allVariablesFromProgram, IDistribution distribution)
            : base(name, allVariablesFromProgram)
        {
            Value = distribution;
            Type = SettingType.Distribution;

            //if (!ContainsUnresolvedVariable())
            //{
            //    if (Value.NumberSeedsRequired == 0)
            //    { // See if it is possible to calculate the value yet. 
            //        double initialValue = (double)GetDoubleValue(new List<double>());
            //        AllVariablesFromProgram[Name] = initialValue;
            //    }
            //}
        }


        public override Setting DeepCopy()
        {
            return new SettingDistribution(Name, AllVariablesFromProgram, Value.DeepCopy());
        }
        
        public override int GetNumSeedsRequired()
        {
            return Value.NumberSeedsRequired;
        }

        internal override double GetDoubleValue(List<double> inputs)
        {
            double returnValue = Value.GetDoubleValue(inputs);
            return returnValue;
        }


        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            return Value.GetExpressionForSetting(compiler);
        }

        public override Type GetReturnType()
        {
            return typeof(double);
        }

        public override void ReplaceContainedVariableSettingWithDouble(string variableName, double replacementValue)
        {
            List<Setting> ContainedSettings = Value.Params;
            int numContainedSettings = ContainedSettings.Count;
            for (int i = 0; i < numContainedSettings; i++)
            {
                Setting theSetting = ContainedSettings[i];
                if ((theSetting is SettingVariableFromProgram || theSetting is SettingVariableFromSetting) && theSetting.Name == variableName)
                    ContainedSettings[i] = new SettingDouble(variableName, AllVariablesFromProgram, replacementValue);
                else
                    theSetting.ReplaceContainedVariableSettingWithDouble(variableName, replacementValue);
            }
        }

        public override bool ContainsUnresolvedVariable()
        {
            return Value.DistributionInputsDirectlyProvided.Any(x => x.ContainsUnresolvedVariable());
        }

        public override void SetVariableFromSettingTracker(SettingVariableFromSettingTracker variableFromSettingTracker)
        {
            List<Setting> ContainedSettings = Value.Params;
            base.SetVariableFromSettingTracker(variableFromSettingTracker);
            ContainedSettings.ForEach(x => x.SetVariableFromSettingTracker(variableFromSettingTracker));
        }

        public override void RequestVariableFromSettingValues(Dictionary<string, Setting> previousSettings, List<Setting> requestedSettings, ref int requestNumber)
        {
            List<Setting> ContainedSettings = Value.Params;
            foreach (var setting in ContainedSettings)
                setting.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
            base.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace ACESim
{
    public interface IDistribution
    {
        Distribution DeepCopy();
        double GetDoubleValue(List<double> theRandomizedInputs);
        void Initialize(List<Setting> paramsAndInputs);
        int NumberSeedsRequired { get; set; }
        List<Setting> Params { get; }
        List<SettingVariable> DistributionInputsDirectlyProvided { get; }
        Expression GetExpressionForSetting(SettingCompilation compiler);
    }
}

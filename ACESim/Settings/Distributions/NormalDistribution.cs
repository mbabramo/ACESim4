using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    [Export(typeof(IDistribution))]
    [ExportMetadata("DistributionName", "normal")]
    public class NormalDistribution : Distribution
    {
        public NormalDistribution()
        {
        }

        public override string DistributionName { get { return "normal"; } }

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);
            if (theParams.Where(x => x.Name == "mean").Count() != 1)
                throw new Exception("Normal distribution mean not specified.");
            if (theParams.Where(x => x.Name == "stdev").Count() != 1)
                throw new Exception("Normal distribution stdev not specified.");
            if (theParams.Count() != 2)
                throw new Exception("Normal distribution unknown parameters specified.");
            if (theDistributionInputsDirectlyProvided.Count() > 1)
                throw new Exception("Normal distribution too many inputs specified.");
            if (theDistributionInputsDirectlyProvided.Count() == 1 && !(theDistributionInputsDirectlyProvided.First() is SettingVariable))
                throw new Exception("Distribution inputs must be of type variable.");
            int numberInputsRequired = 1;
            SetNumSeedsRequired(numberInputsRequired);
        }

        public override Distribution DeepCopy()
        {
            NormalDistribution theNormalDistributionCopy = new NormalDistribution();
            DeepCopySetFields(theNormalDistributionCopy);
            return theNormalDistributionCopy;
        }

        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            double mean = GetSettingValue(theTracker.Value, "mean");
            double stdev = GetSettingValue(theTracker.Value, "stdev");
            double input = theTracker.Value.GetNextInput();
            double returnVal = mean + stdev * alglib.normaldistr.invnormaldistribution(input);
            return returnVal;
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            Expression mean = theParams.Single(x => x.Name == "mean").GetExpressionForSetting(compiler);
            Expression stdev = theParams.Single(x => x.Name == "stdev").GetExpressionForSetting(compiler);
            Setting inputSetting = theDistributionInputsDirectlyProvided.SingleOrDefault(x => x.Name == "input");
            Expression inputVal;
            if (inputSetting == null)
                inputVal = compiler.GetExpressionForNextInputArrayIndex();
            else
                inputVal = inputSetting.GetExpressionForSetting(compiler);
            Expression<Func<double, double>> invNorm = (x) => alglib.normaldistr.invnormaldistribution(x);
            Expression invoked = Expression.Invoke(invNorm, inputVal);
            return Expression.Add(mean, Expression.Multiply(stdev, invoked));
        }
    }
}

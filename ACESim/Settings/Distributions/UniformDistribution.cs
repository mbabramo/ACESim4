using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    [Export(typeof(IDistribution))]
    [ExportMetadata("DistributionName", "uniform")]
    public class UniformDistribution : Distribution
    {
        public UniformDistribution()
        {
        }

        public override string DistributionName { get { return "uniform"; } }

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);
            if (theParams.Where(x => x.Name == "fromVal").Count() != 1)
                throw new Exception("Uniform distribution fromVal not specified.");
            if (theParams.Where(x => x.Name == "toVal").Count() != 1)
                throw new Exception("Uniform distribution toVal not specified.");
            if (theParams.Count() != 2)
                throw new Exception("Uniform distribution unknown parameters specified.");
            if (theDistributionInputsDirectlyProvided.Count() > 1)
                throw new Exception("Uniform distribution too many inputs specified.");
            if (theDistributionInputsDirectlyProvided.Count() == 1 && !(theDistributionInputsDirectlyProvided.First() is SettingVariable))
                throw new Exception("Distribution inputs must be of type variable.");
            int numberInputsRequired = 1;
            SetNumSeedsRequired(numberInputsRequired);
        }

        public override Distribution DeepCopy()
        {
            UniformDistribution theUniformDistributionCopy = new UniformDistribution();
            DeepCopySetFields(theUniformDistributionCopy);
            return theUniformDistributionCopy;
        }

        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            double fromVal = GetSettingValue(theTracker.Value, "fromVal");
            double toVal = GetSettingValue(theTracker.Value, "toVal");
            double input = theTracker.Value.GetNextInput();
            if (input < 0 || input > 1)
                throw new Exception("Invalid input to a uniform distribution. Must be between zero and one.");
            double returnVal = fromVal + (toVal - fromVal) * input;
            return returnVal;
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            Expression fromVal = theParams.Single(x => x.Name == "fromVal").GetExpressionForSetting(compiler);
            Expression toVal = theParams.Single(x => x.Name == "toVal").GetExpressionForSetting(compiler);
            Setting inputSetting = theDistributionInputsDirectlyProvided.SingleOrDefault(x => x.Name == "input");
            Expression inputVal;
            if (inputSetting == null)
                inputVal = compiler.GetExpressionForNextInputArrayIndex();
            else
                inputVal = inputSetting.GetExpressionForSetting(compiler);
            return Expression.Add(fromVal, Expression.Multiply(Expression.Subtract(toVal, fromVal), inputVal));
        }
    }
}

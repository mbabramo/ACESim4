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
    [ExportMetadata("DistributionName", "gamma")]
    public class GammaDistribution : Distribution
    {
        public GammaDistribution()
        {
        }

        public override string DistributionName { get { return "gamma"; } }

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);
            if (theParams.Where(x => x.Name == "k").Count() != 1)
                throw new Exception("Gamma distribution k not specified.");
            if (theParams.Where(x => x.Name == "theta").Count() != 1)
                throw new Exception("Gamma distribution beta not specified.");
            if (theParams.Count() != 2)
                throw new Exception("Gamma distribution unknown parameters specified.");
            if (theDistributionInputsDirectlyProvided.Count() > 1)
                throw new Exception("Gamma distribution too many inputs specified.");
            if (theDistributionInputsDirectlyProvided.Count() == 1 && !(theDistributionInputsDirectlyProvided.First() is SettingVariable))
                throw new Exception("Distribution inputs must be of type variable.");
            int numberInputsRequired = 1;
            SetNumSeedsRequired(numberInputsRequired);
        }

        public override Distribution DeepCopy()
        {
            GammaDistribution theGammaDistributionCopy = new GammaDistribution();
            DeepCopySetFields(theGammaDistributionCopy);
            return theGammaDistributionCopy;
        }

        private double InverseGamma(double p, double k, double theta)
        {
            return alglib.igammaf.incompletegamma(k, p / theta) / alglib.gammafunc.gammafunction(k); // formula from wikipedia
        }


        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            double k = GetSettingValue(theTracker.Value, "k");
            double theta = GetSettingValue(theTracker.Value, "theta");

            double input = theTracker.Value.GetNextInput();
            if (input < 0 || input > 1)
                throw new Exception("Invalid input to an inverse cumulative gamma distribution. Must be between zero and one.");
            double returnVal = InverseGamma(input, k, theta);
            return returnVal;
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            Expression k = theParams.Single(x => x.Name == "k").GetExpressionForSetting(compiler);
            Expression theta = theParams.Single(x => x.Name == "theta").GetExpressionForSetting(compiler);
            Setting inputSetting = theDistributionInputsDirectlyProvided.SingleOrDefault(x => x.Name == "input");
            Expression inputVal;
            if (inputSetting == null)
                inputVal = compiler.GetExpressionForNextInputArrayIndex();
            else
                inputVal = inputSetting.GetExpressionForSetting(compiler);
            Expression<Func<double, double, double, double>> invGamma = (p, k1, t) => InverseGamma(p, k1, t);
            Expression invoked = Expression.Invoke(invGamma, new Expression[] { inputVal, k, theta });
            return invoked;
        }
    }
}

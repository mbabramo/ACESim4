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
    [ExportMetadata("DistributionName", "beta")]
    public class BetaDistribution : Distribution
    {
        public BetaDistribution()
        {
        }

        public override string DistributionName { get { return "beta"; } }

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);
            if (theParams.Where(x => x.Name == "fromVal").Count() != 1)
                throw new Exception("Beta distribution fromVal not specified.");
            if (theParams.Where(x => x.Name == "toVal").Count() != 1)
                throw new Exception("Beta distribution toVal not specified.");
            if (theParams.Where(x => x.Name == "alpha").Count() != 1)
                throw new Exception("Beta distribution alpha not specified.");
            if (theParams.Where(x => x.Name == "beta").Count() != 1)
                throw new Exception("Beta distribution beta not specified.");
            if (theParams.Count() != 4)
                throw new Exception("Beta distribution unknown parameters specified.");
            if (theDistributionInputsDirectlyProvided.Count() > 1)
                throw new Exception("Beta distribution too many inputs specified.");
            if (theDistributionInputsDirectlyProvided.Count() == 1 && !(theDistributionInputsDirectlyProvided.First() is SettingVariable))
                throw new Exception("Distribution inputs must be of type variable.");
            int numberInputsRequired = 1;
            SetNumSeedsRequired(numberInputsRequired);
        }

        public override Distribution DeepCopy()
        {
            BetaDistribution theBetaDistributionCopy = new BetaDistribution();
            DeepCopySetFields(theBetaDistributionCopy);
            return theBetaDistributionCopy;
        }

        /// <summary>
        /// Returns the inverse of the cumulative beta probability density function.
        /// </summary>
        /// <param name="p">Probability associated with the beta distribution.</param>
        /// <param name="alpha">Parameter of the distribution.</param>
        /// <param name="beta">Parameter of the distribution.</param>
        /// <param name="fromVal">Optional lower bound to the interval of x.</param>
        /// <param name="toVal">Optional upper bound to the interval of x.</param>
        /// <returns>Inverse of the cumulative beta probability density function for a given probability</returns>

        private double InverseBeta(double p, double alpha, double beta, double fromVal, double toVal)
        {
            double x = 0;
            double a = 0;
            double b = 1;
            double precision = Math.Pow(10, -6); // converge until there is 6 decimal places precision

            while ((b - a) > precision)
            {
                x = (a + b) / 2;
                if (alglib.ibetaf.incompletebeta(alpha, beta, x) > p)
                {
                    b = x;
                }
                else
                {
                    a = x;
                }
            }

            if ((toVal > 0) && (fromVal > 0))
            {
                x = x * (toVal - fromVal) + fromVal;
            }
            return x;
        } 


        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            double fromVal = GetSettingValue(theTracker.Value, "fromVal");
            double toVal = GetSettingValue(theTracker.Value, "toVal");
            double alpha = GetSettingValue(theTracker.Value, "alpha");
            double beta = GetSettingValue(theTracker.Value, "beta");

            double input = theTracker.Value.GetNextInput();
            if (input < 0 || input > 1)
                throw new Exception("Invalid input to an inverse cumulative beta distribution. Must be between zero and one.");
            double returnVal = InverseBeta(input, alpha, beta, fromVal, toVal);
            return returnVal;
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            Expression fromVal = theParams.Single(x => x.Name == "fromVal").GetExpressionForSetting(compiler);
            Expression toVal = theParams.Single(x => x.Name == "toVal").GetExpressionForSetting(compiler);
            Expression alpha = theParams.Single(x => x.Name == "alpha").GetExpressionForSetting(compiler);
            Expression beta = theParams.Single(x => x.Name == "beta").GetExpressionForSetting(compiler);
            Setting inputSetting = theDistributionInputsDirectlyProvided.SingleOrDefault(x => x.Name == "input");
            Expression inputVal;
            if (inputSetting == null)
                inputVal = compiler.GetExpressionForNextInputArrayIndex();
            else
                inputVal = inputSetting.GetExpressionForSetting(compiler);
            Expression<Func<double, double, double, double, double, double>> invBeta = (p, a, b, f, t) => InverseBeta(p, a, b, f, t);
            Expression invoked = Expression.Invoke(invBeta, new Expression[] { inputVal, alpha, beta, fromVal, toVal } );
            return invoked;
        }
    }
}

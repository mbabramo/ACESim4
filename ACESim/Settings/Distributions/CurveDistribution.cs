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
    [ExportMetadata("DistributionName", "curve")]
    public class CurveDistribution : Distribution
    {
        public CurveDistribution()
        {
        }

        public override string DistributionName { get { return "curve"; } }

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);
            if (theParams.Where(x => x.Name == "fromVal").Count() != 1)
                throw new Exception("Curve distribution fromVal not specified.");
            if (theParams.Where(x => x.Name == "toVal").Count() != 1)
                throw new Exception("Curve distribution toVal not specified.");
            if (theParams.Where(x => x.Name == "curvature").Count() != 1)
                throw new Exception("Curve distribution curvature not specified.");
            if (theParams.Count() != 3)
                throw new Exception("Curve distribution unknown parameters specified.");
            if (theDistributionInputsDirectlyProvided.Count() > 1)
                throw new Exception("Curve distribution too many inputs specified.");
            if (theDistributionInputsDirectlyProvided.Count() == 1 && !(theDistributionInputsDirectlyProvided.First() is SettingVariable))
                throw new Exception("Distribution inputs must be of type variable.");
            int numberInputsRequired = 1;
            SetNumSeedsRequired(numberInputsRequired);
        }

        public override Distribution DeepCopy()
        {
            CurveDistribution theCurveDistributionCopy = new CurveDistribution();
            DeepCopySetFields(theCurveDistributionCopy);
            return theCurveDistributionCopy;
        }

        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            double fromVal = GetSettingValue(theTracker.Value, "fromVal");
            double toVal = GetSettingValue(theTracker.Value, "toVal");
            double curvature = GetSettingValue(theTracker.Value, "curvature");
            if (curvature <= 0)
                throw new Exception("Invalid input to a curve distribution. Curvature must be greater than zero.");
            double input = theTracker.Value.GetNextInput();
            if (input < 0 || input > 1)
                throw new Exception("Invalid input to a curve distribution (which is an inverse cumulative distribution). Must be between zero and one.");
            double returnVal = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(fromVal, toVal, curvature, input);
            return returnVal;
        }

        

        public static double CalculateCurvature(double fromVal, double toVal, double halfWayValue)
        {
            return Math.Log(fromVal) / (Math.Log((halfWayValue - fromVal) / (toVal - fromVal)));
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            Expression fromVal = theParams.Single(x => x.Name == "fromVal").GetExpressionForSetting(compiler);
            Expression toVal = theParams.Single(x => x.Name == "toVal").GetExpressionForSetting(compiler);
            Expression curvature = theParams.Single(x => x.Name == "curvature").GetExpressionForSetting(compiler);
            Setting inputSetting = theDistributionInputsDirectlyProvided.SingleOrDefault(x => x.Name == "input");
            Expression inputVal;
            if (inputSetting == null)
                inputVal = compiler.GetExpressionForNextInputArrayIndex();
            else
                inputVal = inputSetting.GetExpressionForSetting(compiler);
            Expression<Func<double, double, double>> pow = (x,y) => Math.Pow(x,y);
            Expression adjustedProportion = Expression.Invoke(pow, inputVal, Expression.Divide(Expression.Constant(1.0), curvature));
            return Expression.Add(fromVal, Expression.Multiply(Expression.Subtract(toVal, fromVal), adjustedProportion));
        }
    }
}

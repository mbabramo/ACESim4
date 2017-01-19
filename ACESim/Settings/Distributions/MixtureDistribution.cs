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
    [ExportMetadata("DistributionName", "mixture")]
    public class MixtureDistribution : Distribution
    {
        public MixtureDistribution()
        {
        }

        public override string DistributionName { get { return "mixture"; } }

        bool averageDifferentDistributions; // we either average different distributions or just pick one
        List<IDistribution> subdistributions;
        List<Setting> subdistributionSettings;
        List<int> numSeedsRequiredForSubdistribution;

        public override void Initialize(List<Setting> paramsAndInputs)
        {
            base.Initialize(paramsAndInputs);

            if (theParams.Where(x => x.Name == "average").Count() != 1)
                throw new Exception("Combination distribution average boolean parameter not specified.");
            SettingBoolean averageParam = theParams.Single(x => x.Name == "average") as SettingBoolean;
            if (averageParam == null)
                throw new Exception("Combination distribution average parameter should be boolean.");
            averageDifferentDistributions = averageParam.Value;
            
            if (theParams.Where(x => x.Type == SettingType.List).Count() != 1)
                throw new Exception("Combination distribution must contain exactly one list of distributions.");
            SettingList distributionList = theParams.Single(x => x.Type == SettingType.List) as SettingList;
            subdistributionSettings = distributionList.ContainedSettings;
            if (!subdistributionSettings.Any())
                throw new Exception("Combination distribution must contain at least one subdistribution.");
            if (subdistributionSettings.Any(x => x.Type != SettingType.Distribution))
                throw new Exception("Combination distribution's list must contain only distributions.");

            subdistributions = new List<IDistribution>();
            foreach (Setting setting in subdistributionSettings)
            {
                SettingDistribution distributionSetting = setting as SettingDistribution;
                subdistributions.Add(distributionSetting.Value);
            }

            int numberSeedsRequired;
            numSeedsRequiredForSubdistribution = subdistributionSettings.Select(x => x.GetNumSeedsRequired()).ToList();
            if (averageDifferentDistributions)
                numberSeedsRequired = numSeedsRequiredForSubdistribution.Sum();
            else
                numberSeedsRequired = numSeedsRequiredForSubdistribution.Max() + 1; 
            SetNumSeedsRequired(numberSeedsRequired);
        }

        public override Distribution DeepCopy()
        {
            MixtureDistribution theCombinationDistributionCopy = new MixtureDistribution();
            DeepCopySetFields(theCombinationDistributionCopy);
            return theCombinationDistributionCopy;
        }

        public override double GetDoubleValue(List<double> theRandomizedInputs)
        {
            theRandomizedInputs = PrepareToGetDoubleValue(theRandomizedInputs);
            if (averageDifferentDistributions)
                return GetDoubleValueFromAllDistributions(theRandomizedInputs);
            return GetDoubleValueFromOneDistribution(theRandomizedInputs);
        }

        public double GetDoubleValueFromAllDistributions(List<double> theRandomizedInputs)
        {
            // we will calculate an average, and we must keep track of how many of the randomized inputs we have used
            double total = 0;
            int numRandomizedInputsUsed = 0;
            for (int d = 0; d < subdistributions.Count; d++)
            {
                double addToTotal = subdistributions[d].GetDoubleValue(theRandomizedInputs.Skip(numRandomizedInputsUsed).Take(numSeedsRequiredForSubdistribution[d]).ToList());
                total += addToTotal;
                numRandomizedInputsUsed += numSeedsRequiredForSubdistribution[d];
            }
            double returnVal = total / (double)subdistributions.Count;
            return returnVal;
        }

        public double GetDoubleValueFromOneDistribution(List<double> theRandomizedInputs)
        {
            // first, we're going to randomly pick a distribution based on the first input to this
            int distributionNumber = (int) Math.Floor(subdistributions.Count * theRandomizedInputs[0]);
            // now, get the value from the distribution
            return subdistributions[distributionNumber].GetDoubleValue(theRandomizedInputs.Skip(1).ToList());
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            if (averageDifferentDistributions)
                return GetExpressionForSetting_AllDistributions(compiler);
            else
                return GetExpressionForSetting_OneDistribution(compiler);
        }

        public Expression[] GetAllExpressionsInArray(SettingCompilation compiler)
        {
            int originalNumInputArrayAccess = compiler.NumInputArrayAccess;
            Expression[] subdistributionExpressions = subdistributions
                .Select(x => { 
                    compiler.NumInputArrayAccess = originalNumInputArrayAccess; 
                    return x.GetExpressionForSetting(compiler); 
                }).ToArray();
            return subdistributionExpressions;
        }


        public Expression GetExpressionForSetting_AllDistributions(SettingCompilation compiler)
        {
            Expression[] expressions = GetAllExpressionsInArray(compiler);
            return compiler.Average(expressions);
        }


        public Expression GetExpressionForSetting_OneDistribution(SettingCompilation compiler)
        {
            // first, randomly pick a distribution
            Expression inputVal = compiler.GetExpressionForNextInputArrayIndex();
            Expression<Func<int, double, int>> pick = (subdistCount, input) => (int)Math.Floor(subdistCount * input);
            Expression pickResult = Expression.Invoke(pick, Expression.Constant(subdistributions.Count), inputVal);
            // now, use that as an index into all the expressions
            Expression[] expressions = GetAllExpressionsInArray(compiler);
            NewArrayExpression newArrayExpression = Expression.NewArrayInit(typeof(double), expressions.ToList()); // now, when we index this, we get a double, rather than an expression
            return Expression.ArrayIndex(newArrayExpression, pickResult);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public abstract class Distribution : IDistribution, IDistributionName
    {
        public int NumberSeedsRequired { get; set; }

        internal List<Setting> theParams = new List<Setting>();
        public List<Setting> Params { get { return theParams; } }

        internal List<SettingVariable> theDistributionInputsDirectlyProvided = new List<SettingVariable>();
        public List<SettingVariable> DistributionInputsDirectlyProvided { get { return theDistributionInputsDirectlyProvided; } }

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal ThreadLocal<DistributionInputsTracker> theTracker = new ThreadLocal<DistributionInputsTracker> (() => null);

        public Distribution()
        {
        }

        public abstract string DistributionName { get; }

        public virtual void Initialize(List<Setting> paramsAndInputs)
        {
            theParams = paramsAndInputs.Where(x => x.Name != "input").ToList();
            theDistributionInputsDirectlyProvided = paramsAndInputs.OfType<SettingVariable>().Where(x => x.Name == "input").ToList();
        }

        internal void SetNumSeedsRequired(int numberInputsRequired)
        {
            NumberSeedsRequired = numberInputsRequired - theDistributionInputsDirectlyProvided.Count + theParams.OfType<SettingDistribution>().Sum(x => x.Value.NumberSeedsRequired);
        }

        public abstract Distribution DeepCopy();

        public void DeepCopySetFields(Distribution copy)
        {
            copy.theParams = theParams.Select(x => x.DeepCopy()).ToList();
            copy.theDistributionInputsDirectlyProvided = theDistributionInputsDirectlyProvided.Select(x => x.DeepCopy()).Cast<SettingVariable>().ToList();
            copy.NumberSeedsRequired = NumberSeedsRequired;
        }

        public abstract Expression GetExpressionForSetting(SettingCompilation compiler);

        internal double GetSettingValue(DistributionInputsTracker theTracker, string settingName)
        {
            Setting theSetting = theParams.SingleOrDefault(x => x.Name == settingName);
            if (theSetting == null)
                throw new Exception("Distribution parameter " + settingName + " not found.");
            double valueOfSettingWithinDistribution = GetSettingValue(theTracker, theSetting);
            return valueOfSettingWithinDistribution;
        }

        internal double GetSettingValue(DistributionInputsTracker theTracker, Setting theSetting)
        {
            int numInputsRequired = theSetting.GetNumSeedsRequired(null);
            return theSetting.GetDoubleValue(theTracker.GetInputs(numInputsRequired));
        }

        public abstract double GetDoubleValue(List<double> theRandomizedInputs);

        internal List<double> PrepareToGetDoubleValue(List<double> theRandomizedInputs)
        {
            if (theRandomizedInputs != null && theRandomizedInputs.Any())
            {
                var DEBUG = 0;
            }
            theTracker.Value = new DistributionInputsTracker(theRandomizedInputs, theDistributionInputsDirectlyProvided);
            if (theRandomizedInputs == null)
                theRandomizedInputs = new List<double>();
            if (theRandomizedInputs.Count != NumberSeedsRequired)
                throw new Exception("Wrong number of inputs for distribution.");
            return theRandomizedInputs;
        }

    }

    
}

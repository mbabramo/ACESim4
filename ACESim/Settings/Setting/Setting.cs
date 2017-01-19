using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public enum SettingType { 
        Boolean, 
        Int32,
        Int64,
        Double, 
        String, 
        VariableFromProgram,
        VariableFromSetting, 
        Distribution, 
        Strategy, 
        List, 
        Class,
        Calc
    };

    [Serializable]
    public abstract class Setting
    {
        public string Name;
        public SettingType Type;
        [System.Xml.Serialization.XmlIgnore] // this is by necessity, since dictionaries cannot be xml serialized -- but it would be a problem if we relied on xml serialization other than for reporting purposes
        public Dictionary<string, double> AllVariablesFromProgram;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public ThreadLocal<SettingVariableFromSettingTracker> VariableFromSettingTracker = new ThreadLocal<SettingVariableFromSettingTracker>( () => null );
        public bool ValueIsRequestedAsVariableFromSetting = false; 
        public List<int> ValueFromSettingRequestedOrder = new List<int>(); // keeps track of the occasions in which this value has been requested as a variable from setting. For example, if this is 0 and 3, then the 1st and 4th times that a setting was requested as a variableFromSetting, it was this setting that was requested

        public static Dictionary<string, SettingType> SettingTypeStringToSettingType;

        static Setting()
        {
            // Initialize the SettingTypeStringToSettingType dictionary
            SettingTypeStringToSettingType = new Dictionary<string, SettingType>();
            SettingTypeStringToSettingType.Add("bool", SettingType.Boolean);
            SettingTypeStringToSettingType.Add("int", SettingType.Int32);
            SettingTypeStringToSettingType.Add("long", SettingType.Int64);
            SettingTypeStringToSettingType.Add("double", SettingType.Double);
            SettingTypeStringToSettingType.Add("text", SettingType.String);
            SettingTypeStringToSettingType.Add("variableFromProgram", SettingType.VariableFromProgram);
            SettingTypeStringToSettingType.Add("variableFromSetting", SettingType.VariableFromSetting);
            SettingTypeStringToSettingType.Add("distribution", SettingType.Distribution);
            SettingTypeStringToSettingType.Add("strategy", SettingType.Strategy);
            SettingTypeStringToSettingType.Add("list", SettingType.List);
            SettingTypeStringToSettingType.Add("class", SettingType.Class);
        }

        //public Setting()
        //{
        //    // Nothing.  Added this for InputVariables.GetSettings
        //}

        public Setting(string name, Dictionary<string, double> allVariablesFromProgram)
        {
            this.Name = name;
            this.AllVariablesFromProgram = allVariablesFromProgram;
        }


        public abstract Type GetReturnType();

        public virtual int GetNumSeedsRequired()
        {
            return 0;
        }

        public int GetNumSeedsRequired(CurrentExecutionInformation settingsToLookForOverride)
        {
            if (settingsToLookForOverride != null && settingsToLookForOverride.SettingOverride != null)
            {
                Setting theOverrideSetting = settingsToLookForOverride.SettingOverride.groupOfSimultaneousSettingChanges.SingleOrDefault(x => x.Name == this.Name);
                if (theOverrideSetting != null)
                    return theOverrideSetting.GetNumSeedsRequired(null);
            }

            return GetNumSeedsRequired();
        }

        public double? GetOverrideValue(List<double> inputs, CurrentExecutionInformation settings)
        {
            if (settings.SettingOverride != null)
            {
                Setting theOverrideSetting = settings.SettingOverride.groupOfSimultaneousSettingChanges.SingleOrDefault(x => x.Name == this.Name);
                if (theOverrideSetting != null)
                {
                    double doubleValue = theOverrideSetting.GetDoubleValue(inputs);
                    return doubleValue;
                }
            }
            return null;
        }

        public double GetDoubleValueOrOverride(List<double> inputs, CurrentExecutionInformation settings)
        {
            double returnValue;
            double? overrideValue = GetOverrideValue(inputs, settings);
            if (overrideValue != null)
                returnValue = (double)overrideValue;
            else
                returnValue = GetDoubleValue(inputs);
            if (VariableFromSettingTracker.Value != null && ValueIsRequestedAsVariableFromSetting)
                VariableFromSettingTracker.Value.StoreSettingVariableFromSetting(ValueFromSettingRequestedOrder[0], returnValue);
            return returnValue;
        }

        public abstract Expression GetExpressionForSetting(SettingCompilation compiler);

        internal virtual double GetDoubleValue(List<double> inputs)
        {
            throw new Exception(String.Format("The {0} setting {1} does not return a double type.", Type, Name));
        }

        public virtual void ReplaceContainedVariableSettingWithDouble(string variableName, double replacementValue)
        {
            // by default, do nothing -- overriden for class or list
        }

        public virtual bool ContainsUnresolvedVariable()
        {
            return false;
        }

        public void AddValueRequested(int requestOrder)
        {
            ValueIsRequestedAsVariableFromSetting = true;
            ValueFromSettingRequestedOrder.Add(requestOrder);
        }

        public virtual void SetVariableFromSettingTracker(SettingVariableFromSettingTracker variableFromSettingTracker) // must be overriden by classes with contained settings
        {
            this.VariableFromSettingTracker.Value = variableFromSettingTracker;
        }

        public virtual void RequestVariableFromSettingValues(Dictionary<string, Setting> previousSettings, List<Setting> requestedSettings, ref int requestNumber)
        {
            // by default, just report this setting 
            if (previousSettings.ContainsKey(Name))
                previousSettings[Name] = this;
            else
                previousSettings.Add(Name, this);
        }

        public abstract Setting DeepCopy();
    }
}

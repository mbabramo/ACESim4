using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{

    [Serializable]
    public class ChangeSimulationSettingGenerator : IChangeSimulationSettingPermutator
    {
        public string SimulationSettingName;
        public double StartingValue;
        public double Increment;
        public int NumValues;
        SettingType NumericSettingType;
        List<Setting> GeneratedList = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="originalSimulationSettingName"></param>
        /// <param name="startingValue"></param>
        /// <param name="increment">If numericSettingType is an integral type, then this must be an integer value.</param>
        /// <param name="numValues"></param>
        /// <param name="numericSettingType">One of the numeric versionsn of SettingType (SettingType.Int32 or SettingType.Single)</param>
        public ChangeSimulationSettingGenerator(
            string originalSimulationSettingName,
            double startingValue, 
            double increment, 
            int numValues, 
            SettingType numericSettingType)
        {
            SimulationSettingName = originalSimulationSettingName;
            StartingValue = startingValue;

            if ((numericSettingType == SettingType.Int32 || numericSettingType == SettingType.Int64) && !(increment % 1.0 == 0.0))
            {
                throw new ArgumentException(String.Format(
                    "NumericSettingType is integral ({0}), but non-integral increment value was passed ({1}).",
                    numericSettingType,
                    increment));
            }
            Increment = increment;

            NumValues = numValues;

            if (!(numericSettingType == SettingType.Double || numericSettingType == SettingType.Int32 || numericSettingType == SettingType.Int64))
            {
                throw new ArgumentException(String.Format(
                    "numericSettingType must be a numeric SettingType, either SettingType.Single or SettingType.Int32.  It was {0}.",
                    numericSettingType));
            }
            NumericSettingType = numericSettingType;
        }

        private void GenerateSimulationSettings()
        {
            List<Setting> theSettingsList = new List<Setting>();
            double value = StartingValue;
            for (int i = 1; i <= NumValues; i++)
            {

                Setting numericSetting;
                if (NumericSettingType == SettingType.Double)
                    numericSetting = new SettingDouble(SimulationSettingName, null, value);
                else if (NumericSettingType == SettingType.Int32)
                    numericSetting = new SettingInt32(SimulationSettingName, null, (int)value);
                else if (NumericSettingType == SettingType.Int64)
                    numericSetting = new SettingInt64(SimulationSettingName, null, (long)value);
                else
                    throw new Exception(String.Format("Exhausted numeric setting types ({0})", NumericSettingType));

                theSettingsList.Add(numericSetting);
                
                value += Increment;
            }
            GeneratedList = theSettingsList;
        }

        public List<List<Setting>> GenerateAll()
        {
            // because these settings are to be done consecutively, we must put each setting into a separate list of settings
            return GeneratedList.Select(x => new List<Setting>() { x }).ToList();
        }
    }
}

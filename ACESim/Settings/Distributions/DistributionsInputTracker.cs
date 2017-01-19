using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class DistributionInputsTracker
    {
        public int numInputsRequested;
        List<double> randomizedInputs;
        List<SettingVariable> distributionInputsDirectlyProvided;
        public DistributionInputsTracker(List<double> theRandomizedInputs, List<SettingVariable> theDistributionInputsDirectlyProvided)
        {
            distributionInputsDirectlyProvided = theDistributionInputsDirectlyProvided;
            randomizedInputs = theRandomizedInputs;
        }
        internal double GetNextInput()
        {
            int randomizedInputsCount = (randomizedInputs == null ? 0 : randomizedInputs.Count);
            numInputsRequested++;
            // Note that the variable inputs for this distribution should be drawn last, since we first need to 
            // get the distribution parameters (e.g., mean) using any inputs necessary.
            if (numInputsRequested <= randomizedInputsCount)
                return randomizedInputs[numInputsRequested - 1];
            else
                return distributionInputsDirectlyProvided[numInputsRequested - randomizedInputsCount - 1].GetDoubleValue(null);
        }
        public List<double> GetInputs(int numToGet)
        {
            List<double> theList = new List<double>();
            for (int i = 1; i <= numToGet; i++)
                theList.Add(GetNextInput());
            return theList;
        }
    }
}

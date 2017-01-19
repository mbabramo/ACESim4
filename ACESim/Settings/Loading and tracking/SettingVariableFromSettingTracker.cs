using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class SettingVariableFromSettingTracker
    {
        List<Setting> RequestedSettings;
        List<int> RequestOrder;
        int NumDistinctSettingsRequested;
        int NumRequests;
        long NumIterations;
        int[] NumValuesPreviouslyStoredForIteration;
        int[] NumRequestsPreviouslyMadeForIteration; 
        List<double[]> StoredSettingsValues;
        public int IterationNum;

        public SettingVariableFromSettingTracker(List<Setting> requestedSettings)
        {
            int numIterations = 1; // this used to work for multiple iterations at once, but we have removed that feature
            NumIterations = -1; // to trigger reset of other variables
            RequestedSettings = requestedSettings;
            NumRequests = 0;
            if (RequestedSettings.Any())
            {
                RequestOrder = new List<int>();
                NumRequests = requestedSettings.Count;
                // Figure out the request order. This is 0, 1, 2, ... is all requests are distinct. But if a request is repeated, the earlier index is used: e.g., 0, 1, 0, 2, ...
                int numberUniqueFinds = 0;
                for (int r = 0; r < NumRequests; r++)
                {
                    bool requestFound = false;
                    int r2 = 0;
                    while (!requestFound && r2 < r)
                    {
                        requestFound = requestedSettings[r2] == requestedSettings[r];
                        if (!requestFound)
                            r2++;
                    }
                    if (requestFound)
                        RequestOrder.Add(RequestOrder[r2]);
                    else
                    {
                        RequestOrder.Add(numberUniqueFinds);
                        numberUniqueFinds++;
                    }
                }
                NumDistinctSettingsRequested = requestedSettings.Distinct().Count();

                ResetRequestsAndStorageTracking();
            }
        }

        public void ResetRequestsAndStorageTracking()
        {
            int numIterations = 1; // this used to work for many iterations but now we are only doing one at a time
            NumRequestsPreviouslyMadeForIteration = new int[numIterations];
            NumValuesPreviouslyStoredForIteration = new int[numIterations];
            if (NumIterations != numIterations)
            {
                NumIterations = numIterations;
                StoredSettingsValues = new List<double[]>();
                for (int i = 0; i < numIterations; i++)
                    StoredSettingsValues.Add(new double[NumDistinctSettingsRequested]);
            }
        }

        public double RequestNextSettingVariableFromSetting()
        {
            bool useFirstIterationInstead = false;
            int settingRequested = RequestOrder[NumRequestsPreviouslyMadeForIteration[IterationNum]];
            if (settingRequested >= NumValuesPreviouslyStoredForIteration[IterationNum])
                useFirstIterationInstead = true; // this is a setting where we copied a single value to all iterations, without storing the setting for all iterations
            double returnVal = StoredSettingsValues[useFirstIterationInstead ? 0 : IterationNum][settingRequested];
            NumRequestsPreviouslyMadeForIteration[IterationNum]++;
            if (NumRequestsPreviouslyMadeForIteration[IterationNum] == NumRequests)
                NumRequestsPreviouslyMadeForIteration[IterationNum] = 0;
            return returnVal;
        }

        public void StoreSettingVariableFromSetting(int indexInRequested, double valueToStore)
        {
            StoredSettingsValues[IterationNum][RequestOrder[indexInRequested]] = valueToStore;
            NumValuesPreviouslyStoredForIteration[IterationNum]++;
        }
    }
}

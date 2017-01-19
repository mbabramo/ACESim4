using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;

namespace ACESim
{
    public static class AzureReset
    {
        public static void Go()
        {
            if (!AzureSetup.resetAzureAtStart)
                return;

            Debug.WriteLine("Resetting Azure settings " + (AzureSetup.useDevelopmentAccount ? " using local storage " : " using cloud storage"));
            AzureSetup.SetConfigurationSettingPublisher();
            bool isReady = false;
            while (!isReady)
            {
                isReady = AzureSetup.GetCloudStorageAccount("DataConnectionString") != null;
                if (!isReady)
                    Thread.Sleep(100);
            }
            AzureQueue.Clear("taskresult");
            AzureQueue.Clear("tasktodo");
            AzureBlob.DeleteItems("inputblobs");
            AzureBlob.DeleteItems("outputblobs");

        }
    }
}

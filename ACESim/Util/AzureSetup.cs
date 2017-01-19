using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Configuration;

namespace ACESim
{

//How to deploy:
//Using worker coordinator as well as worker processor solution.
    //The coordinator worker role will run in an Azure worker process.
    //make sure settings are acceptable (including the name so that stats can be examined,
    //iterations chunked for remoting and use worker roles for remoting, etc.)
    //All settings, modules, and strategies xml files will be copied to both roles.
    // Former approach: pre-build event xcopy "$(ProjectDir)..\ACESim\Games\LitigationGame\Modules\*.xml" "$(ProjectDir)\WorkerCoordinatorContent\Modules" /S /E /Y
    // and xcopy "$(ProjectDir)..\ACESim\Games\LitigationGame\Settings\*.xml" "$(ProjectDir)\WorkerCoordinatorContent\Settings" /S /E /Y
    // New approach: AzureRoleContent in project file for WindowsAzsure1
    //Make sure that Strategies subdirectory contains only those strategies that we want to copy
    //go to azure storage explorer to clear queues, blobs, tables for abramowiczacesim
    //go to serviceconfiguration.cloud to set number of instances. maximum number is 25 if using remote debugging for all roles.
    //in AzureSetup below, disable use development account
    //go to cloud project (not solution), and choose publish (or run remotely)
    //wait for the play icon
// Running entirely locally -- use local coordinator solution.
    // Setup files should specify that the worker roles are not to be used.
    // Clear development storage or the azure storage (make appropriate decision below on disable use development account)
// With worker processor locally and local WinForms coordination 
    //Use the Local coordinator and worker processor solution.
    // set useBlobsForInterRoleCommunication to true
    // set useDevelopmentAccount as desired
    //go to azure storage explorer to clear queues, blobs, tables for abramowiczacesim if not using development account
    //clear out the Reports and Strategies folders (unless a Strategy is being used as an input)
    //go to serviceconfiguration.local to set number of instances. 10 is a good maximum
    //Run the program and wait for role instances to start
    //Turn off catching exceptions so that we can automatically retry
// With worker processor deployed on azure but local WinForms coordination 
    //Start with the Local coordinator and worker processor solution.
    //make sure settings are acceptable (including the name so that stats can be examined,
    //iterations chunked for remoting and use worker roles for remoting, etc.)
    //set useBlobsForInterRoleCommunication to true
    //set useDevelopmentAccount to false
    //go to azure storage explorer to clear queues, blobs, tables for abramowiczacesim
    //set resetAzureAtStart to false (if a database clearing is desired, run the local coordinator once with resetAzureAtStart = true first).
    //clear out the Reports and Strategies folders (unless a Strategy is being used as an input)
    //go to serviceconfiguration.cloud to set number of instances. maximum number is 25 if using remote debugging for all roles (under advanced settings)
    //publish
    //When possible, clean project.
    //If we click run, we will deploy more worker roles locally. We can do this, or alternatively, run the program locally using the Local Coordinator solution.
    //If debugging worker role is desired, use the Local coordinator and worker processor solution, go to Server Explorer, and right-click on the right to attach the debugger.

//When complete, go back to main screen and delete the production deployment

    public static class AzureSetup
    {
        public static string defaultAccount = "abramowiczacesim";
        public static string defaultKey = "OoQqupGIUkIkmnEd38lbb5gYBCVjvwo70ClAMU7hchRit/i7hx716TB03ZYNfRxFq2deRWtA+i3nvV/ge0dYtw==";
        public static bool resetAzureAtStart = false; // set to false if not using azure at all, or if resetting manually using the storage explorer (which has the benefit of avoiding risking inadvertent restart if we open multiple acesim windows)
        public static bool runCompleteSettingsInAzure = false; // set to true if we are running a lot of simulations at once. When this is true, other Azure execution approaches (at the sub-settings set level) will be disabled)
        public static bool useDevelopmentAccount = false; // false = use real azure storage; true = use local emulator
        public static bool useBlobsForInterRoleCommunication = true; // if false, we use Sockets where possible -- probably not necessary now that we have condensed blob size
        public static string workerCoordinatorSettingsFile = "Temp1.xml"; // when using the Worker Coordinator solution, set this to the starting settings file.

        public static CloudStorageAccount GetCloudStorageAccount(string settingName, bool useConfigFiles = false)
        {
            if (useConfigFiles)
                return CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting(settingName));
            else if (useDevelopmentAccount)
                return CloudStorageAccount.DevelopmentStorageAccount; 
            return new CloudStorageAccount(new StorageCredentialsAccountAndKey(defaultAccount, defaultKey), false);
        }

        public static Microsoft.WindowsAzure.Storage.CloudStorageAccount GetCloudStorageAccountV2(string settingName, bool useConfigFiles = false)
        {
            if (useConfigFiles)
                return Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting(settingName));
            else if (useDevelopmentAccount)
                return Microsoft.WindowsAzure.Storage.CloudStorageAccount.DevelopmentStorageAccount;
            return new Microsoft.WindowsAzure.Storage.CloudStorageAccount(new Microsoft.WindowsAzure.Storage.Auth.StorageCredentials(defaultAccount, defaultKey), false);
        }


        public static void SetConfigurationSettingPublisher()
        {
            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
            {
                var connectionString = GetConfigurationSettingFromPublisher(configName);
                configSetter(connectionString);
                //configSetter(RoleEnvironment.GetConfigurationSettingValue(configName));
            });
        }

        public static string GetConfigurationSetting(string configName)
        {
            SetConfigurationSettingPublisher();
            return  RoleEnvironment.IsAvailable ? RoleEnvironment.GetConfigurationSettingValue(configName)
                : "Data Source=.\\SQLSERVER;Initial Catalog=Rateroo7;Integrated Security=True;Connect Timeout=300";
                     // : ConfigurationManager.AppSettings[configName];
        }

        internal static string GetConfigurationSettingFromPublisher(string configName)
        {
            return RoleEnvironment.IsAvailable
                      ? RoleEnvironment.GetConfigurationSettingValue(configName)
                      : ConfigurationManager.AppSettings[configName];
        }

    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Resources
{
    public static class CloudPW
    {
        public static string GetCloudStorageAccountConnectionString() => "DefaultEndpointsProtocol=https;AccountName=acesim;AccountKey=INSERTKEYHERE==;EndpointSuffix=core.windows.net";
    }
}

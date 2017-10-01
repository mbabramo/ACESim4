using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{
    public class AzureFunctionResult
    {
        public bool Success;
        public string Info;
    }

    public static class RunAzureFunction
    {
        public static async Task<AzureFunctionResult> RunFunction<T>(string apiURL, T inputObject)
        {
            try
            {
                string results = null; // DEBUG
                //var client = new HttpClient();
                //HttpResponseMessage response = await client.PostAsJsonAsync(apiURL, inputObject);
                //var results = response.Content.ReadAsStringAsync().Result;
                return new AzureFunctionResult() {Success = true, Info = results};
            }
            catch (Exception e)
            {
                return new AzureFunctionResult() {Success = false, Info = e.Message};
            }
        }
    }
}

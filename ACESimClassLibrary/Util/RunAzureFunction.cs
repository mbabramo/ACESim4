using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
                var client = new HttpClient();
                var stringContent = new StringContent(JsonConvert.SerializeObject(inputObject), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiURL, stringContent);
                var results = response.Content.ReadAsStringAsync().Result;
                return new AzureFunctionResult() {Success = true, Info = results};
            }
            catch (Exception e)
            {
                return new AzureFunctionResult() {Success = false, Info = e.Message};
            }
        }
    }
}

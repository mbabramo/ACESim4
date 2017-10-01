using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
        public static async Task<AzureFunctionResult> RunFunction<T>(string apiURL, T inputObject, string azureBlobInterimReportName)
        {
            const int maxTimeout = 11 * 60 * 1000; // 11 minutes
            Stopwatch s = new Stopwatch();
            s.Start();
            try
            {
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromMilliseconds(maxTimeout);
                string serializedObject = JsonConvert.SerializeObject(inputObject);
                var stringContent = new StringContent(serializedObject, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiURL, stringContent);
                var escapedResult = response.Content.ReadAsStringAsync().Result;
                string finalResult = Unescape(escapedResult);
                if (finalResult.Contains("web server failed"))
                {
                    // We're past the 230 second limit to get a response. So we need to periodically poll to see if the results have been put in the blob.
                    while (s.ElapsedMilliseconds < maxTimeout)
                    {
                        await Task.Delay(10000); // wait 10 seconds
                        string storedResult = AzureBlob.GetBlobText("results", azureBlobInterimReportName);
                        if (storedResult != null)
                            return new AzureFunctionResult() {Success = true, Info = Unescape(storedResult)};
                    }
                    return new AzureFunctionResult() { Success = false, Info = "Timed out" };
                }
                return new AzureFunctionResult() {Success = true, Info = finalResult };
            }
            catch (Exception e)
            {
                return new AzureFunctionResult() {Success = false, Info = e.Message + $" seconds elapsed: {s.Elapsed.TotalSeconds}"};
            }
        }

        private static string Unescape(string escapedResult)
        {
            var withoutOuterQuoteMarks = System.Uri.UnescapeDataString(escapedResult.Trim('"'));
            var finalResult = Regex.Replace(withoutOuterQuoteMarks, @"\\[rnt]", m =>
            {
                switch (m.Value)
                {
                    case @"\r": return "\r";
                    case @"\n": return "\n";
                    case @"\t": return "\t";
                    default: return m.Value;
                }
            });
            return finalResult;
        }
    }
}

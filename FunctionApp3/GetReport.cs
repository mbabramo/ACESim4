using System;
using System.Net;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;

namespace FunctionApp3
{
    public static class GetReport
    {
        [FunctionName("GetReport")]
        public static async Task<object> Run([HttpTrigger(WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"Webhook was triggered!");

            string jsonContent = await req.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(jsonContent);

            if (data.optionSet == null || data.repetition == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, new
                {
                    error = "Please pass option set properties in the input object"
                });
            }

            string result;
            try
            {
                result = ACESim.MyGameRunner.GetSingleRepetitionReport((int)data.optionSet, (int)data.repetition);
            }
            catch (Exception e)
            {
                result = e.Message + (e.InnerException?.Message ?? "") + e.StackTrace;
            }

            return req.CreateResponse(HttpStatusCode.OK, result);
        }
    }
}

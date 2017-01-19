using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class ExceptionExtraction
    {
        public static string ExtractMessage(this Exception ex)
        {
            string exceptionMessage = ex.Message;
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                exceptionMessage += "\n " + ex.Message;
            }
            exceptionMessage += "\nStack trace: " + ex.StackTrace;
            return exceptionMessage;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public static class Retry
    {
        public static void TryNTimes(Action action, int n, int millisecondsDelay)
        {
            int retries = n;
            while (true)
            {
                try
                {
                    action();
                    break; // success!
                }
                catch
                {
                    if (--retries == 0) throw;
                    else Thread.Sleep(millisecondsDelay);
                }
            }
        }


    }
}

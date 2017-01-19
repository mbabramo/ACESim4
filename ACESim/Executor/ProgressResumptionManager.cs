using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class ProgressResumptionManager
    {
        public ProgressResumptionManagerInfo Info = new ProgressResumptionManagerInfo();
        public ProgressResumptionOptions ProgressResumptionOption;
        public string ProgressString;

        public ProgressResumptionManager(ProgressResumptionOptions progressResumptionOption, string progressString)
        {
            ProgressResumptionOption = progressResumptionOption;
            ProgressString = progressString;
        }

        internal void SaveProgressIfSaving()
        {
            throw new NotImplementedException();
        }
    }
}

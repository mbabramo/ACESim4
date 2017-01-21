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
        public string Path;

        public ProgressResumptionManager(ProgressResumptionOptions progressResumptionOption, string path)
        {
            ProgressResumptionOption = progressResumptionOption;
            Path = path;
            if (ProgressResumptionOption == ProgressResumptionOptions.SkipToPreviousPositionThenResume)
                LoadProgress();
        }

        internal void LoadProgress()
        {
            Info = (ProgressResumptionManagerInfo) BinarySerialization.GetSerializedObject(Path, false);
        }

        internal void SaveProgressIfSaving()
        {
            BinarySerialization.SerializeObject(Path, Info, true, false);
        }
    }
}

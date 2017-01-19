using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    interface IChangeSimulationSettingPermutator
    {
        List<List<Setting>> GenerateAll();
    }
}

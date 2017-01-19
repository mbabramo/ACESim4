using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ModularGameInputsSet : GameInputs
    {
        public List<GameModuleInputs> GameModulesInputs;
    }
}

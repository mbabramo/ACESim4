using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class DropOrDefaultChoiceInputs : DropOrDefaultInputs
    {
        public bool MakeDecisionsAssumingOtherWillNotDrop;
        [FlipInputSeed]
        public double RandomSeedIfBothGiveUpAtOnce;
    }
}

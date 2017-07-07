using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public abstract class CRMChanceNodeSettings
    {
        public byte PlayerNum;
        public byte DecisionByteCode;
        public abstract double GetActionProbability(int action);

        public abstract bool AllProbabilitiesEqual();
    }
}

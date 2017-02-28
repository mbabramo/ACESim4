using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract class CRMChanceNodeSettings
    {
        public byte DecisionNum;
        public abstract double GetActionProbability(int action);

        public abstract bool AllProbabilitiesEqual();
    }
}

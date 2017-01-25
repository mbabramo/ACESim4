using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IDefaultBehaviorBeforeEvolution
    {
        double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber);
    }
}

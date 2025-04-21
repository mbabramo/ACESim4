using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Randomization
{
    public interface IRandomProducer
    {
        double NextDouble();
        double GetDoubleAtIndex(int index);

    }
}

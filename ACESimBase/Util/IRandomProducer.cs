﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IRandomProducer
    {
        double NextDouble();
        double GetDoubleAtIndex(int index);

    }
}

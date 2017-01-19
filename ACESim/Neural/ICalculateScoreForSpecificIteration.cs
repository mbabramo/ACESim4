using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encog.Neural.Networks.Training;
using Encog.Neural.Networks;

namespace ACESim
{
    interface ICalculateScoreForSpecificIteration : ICalculateScore
    {
        void CalculateScoreForSpecificDataSample(BasicNetwork network, int dataSample, StatCollector stats);
    }
}

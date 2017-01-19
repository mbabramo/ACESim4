using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PointChartReport
    {
        public string XAxisVariableName;
        public string YAxisVariableName;
        public Graph2DSettings Graph2DSettings;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract class SimpleReportColumnItem
    {
        public string Name;
        public int Width = 10;
        public abstract double? GetValueToRecord(GameProgress completedGame);

        public SimpleReportColumnItem(string name, int? width = null)
        {
            Name = name;
            if (width == null)
                Width = name.Length + 3;
            else
                Width = Math.Max(name.Length + 3, (int)width);
        }
    }
}

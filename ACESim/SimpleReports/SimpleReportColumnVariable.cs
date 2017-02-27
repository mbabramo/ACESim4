using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class SimpleReportColumnVariable : SimpleReportColumnItem
    {
        public bool Stdev; // if false, report the mean
        public Func<GameProgress, double> GetColumnItem;

        public override double GetValueToRecord(GameProgress completedGame)
        {
            return GetColumnItem(completedGame);
        }

        public SimpleReportColumnVariable(string name, Func<GameProgress, double> getColumnItem, int? width = null) : base(name, width)
        {
            GetColumnItem = getColumnItem;
        }
    }
}

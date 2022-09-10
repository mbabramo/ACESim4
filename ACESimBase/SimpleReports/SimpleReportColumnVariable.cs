using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    [Serializable]
    public class SimpleReportColumnVariable : SimpleReportColumnItem
    {
        public ColumnVariableOptions columnVariableOptions = ColumnVariableOptions.Mean;
        public Func<GameProgress, double?> GetColumnItem;

        public override double? GetValueToRecord(GameProgress completedGame)
        {
            return GetColumnItem(completedGame);
        }

        public SimpleReportColumnVariable(string name, Func<GameProgress, double?> getColumnItem, int? width = null) : base(name, width)
        {
            GetColumnItem = getColumnItem;
        }
    }
}

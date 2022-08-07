using System.Collections.Generic;

namespace ACESimBase.Util.Tikz
{
    public record TikzLineGraphData(List<List<double?>> proportionalHeights, List<string> lineAttributes, List<string> dataSeriesNames)
    {
        public TikzLineGraphData Subset(int everyNItems)
        {
            var newProportionalHeights = new List<List<double?>>();
            var newLineAttributes = new List<string>();
            var newDataSeriesNames = new List<string>();
            for (int i = 0; i < proportionalHeights.Count; i++)
            {
                if (i % everyNItems == 0)
                {
                    newProportionalHeights.Add(proportionalHeights[i]);
                    newLineAttributes.Add(lineAttributes[i]);
                    newDataSeriesNames.Add(dataSeriesNames[i]);
                }
            }
            return new TikzLineGraphData(newProportionalHeights, newLineAttributes, newDataSeriesNames);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class WeightedPercentileTracker
    {
        double sumWeights = 0;
        List<Tuple<double, double>> itemsWithWeights = new List<Tuple<double, double>>();
        bool ordered = false;

        public void AddItem(double item, double weight)
        {
            sumWeights += weight;
            itemsWithWeights.Add(new Tuple<double, double>(item, weight));
            ordered = false;
        }

        private void Order()
        {
            if (!ordered)
            {
                itemsWithWeights = itemsWithWeights.OrderBy(x => x.Item1).ToList();
                ordered = true;
            }
        }

        double weightSoFar = 0;
        int index = 0;

        public double CalculatePercentileResult(double percentileExpressedFrom0To1, bool reset = true)
        {
            Order();
            double addWeightsUntil = percentileExpressedFrom0To1 * sumWeights;
            if (reset)
            {
                weightSoFar = 0;
                index = 0;
            }
            while (weightSoFar < addWeightsUntil && index < itemsWithWeights.Count)
            {
                weightSoFar += itemsWithWeights[index].Item2;
                index++;
            }
            if (index == itemsWithWeights.Count)
                index = itemsWithWeights.Count - 1;
            return itemsWithWeights[index].Item1;
        }

        public List<double> GetUnweightedList(int numItems)
        {
            List<double> list = new List<double>();
            for (int i = 0; i < numItems; i++)
            {
                double percentile = 0 + ((double)i / ((double)(numItems - 1)));
                double percentileResult = CalculatePercentileResult(percentile, percentile == 0);
                list.Add(percentileResult);
            }
            return list;
        }
    }
}

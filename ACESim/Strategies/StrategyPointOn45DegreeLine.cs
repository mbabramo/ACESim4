using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class StrategyPointOn45DegreeLine
    {
        public static double FindStrategyPointOn45DegreeLine(Strategy theStrategy, double centerPoint = 0.5, double rangeOfRandomStrategiesToTest = 2.0)
        {
            double result;
            List<double> pointsOn45DegreeLine = FindStrategyPointOn45DegreeLineInRange(theStrategy, (centerPoint - 0.5 * rangeOfRandomStrategiesToTest), (centerPoint + 0.5 * rangeOfRandomStrategiesToTest));
            if (pointsOn45DegreeLine.Any())
            {
                if (pointsOn45DegreeLine.Any(x => x >= 0 && x <= 1))
                    result = pointsOn45DegreeLine.Where(x => x >= 0 && x <= 1).Min(); // take least aggressive symmetric strategy within a normal range
                else
                    result = pointsOn45DegreeLine.OrderBy(x => Math.Abs(x - 0.0)).First(); // take symmetric strategy closest to normal range
                TabbedText.WriteLine("Point on 45-degree line: " + result);
            }
            else
            {
                if (theStrategy.Calculate(new List<double> { 0.0 }) > 0)
                    result = (0.5 + 0.5 * rangeOfRandomStrategiesToTest); // assume maximum level of aggressiveness
                else
                    result = (0.5 - 0.5 * rangeOfRandomStrategiesToTest); // assume minimal level of aggressiveness
                TabbedText.WriteLine("No point on 45 degree line, so using  " + result);
            }
            return result;
        }

        internal static List<double> FindStrategyPointOn45DegreeLineInRange(Strategy theStrategy, double startVal, double endVal)
        {
            List<double> pointsOn45DegreeLine = new List<double>();
            const int numIncrements = 800;
            double incrementSize = (endVal - startVal) / (double)numIncrements;
            double currentVal = startVal;
            // we are looking for intersections with a 45 degree line (i.e., spots in which the output of the strategy equals the input)
            bool currentlyAboveLine = false, previouslyAboveLine = false;
            for (int step = 0; step <= numIncrements; step++)
            {
                currentVal += incrementSize;
                double calcResult = theStrategy.Calculate(new List<double> { currentVal });
                bool thisAboveLine = calcResult > currentVal;
                if (step == 0)
                    currentlyAboveLine = thisAboveLine;
                else
                {
                    previouslyAboveLine = currentlyAboveLine;
                    currentlyAboveLine = thisAboveLine;
                    if (currentlyAboveLine != previouslyAboveLine)
                        pointsOn45DegreeLine.Add(currentVal - 0.5 * incrementSize);
                }
            }
            return pointsOn45DegreeLine;
        }
    }
}

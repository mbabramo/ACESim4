using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public static class MonotonicCurve
    {
        // curvature of 1.0 is linear; 10.0 means that at a proportion of 0.5, we'll be 93.3% of the way there; 0.1 means, 0.09% of the way there.
        public static double CalculateValueBasedOnProportionOfWayBetweenValues(double fromVal, double toVal, double curvature, double proportion)
        {
            if (toVal < fromVal)
                return CalculateValueBasedOnProportionOfWayBetweenValues(toVal, fromVal, curvature, 1.0 - proportion);
            double adjustedProportion = Math.Pow(proportion, 1.0 / curvature);
            double returnVal = fromVal + (toVal - fromVal) * adjustedProportion;
            return returnVal;
        }

        public static double CalculateYValueForX(double fromVal, double toVal, double curvature, double x)
        {
            double proportion = (x - fromVal) / (toVal - fromVal);
            double yVal = CalculateValueBasedOnProportionOfWayBetweenValues(fromVal, toVal, curvature, proportion);
            return yVal;
        }

        public static double CalculateXValueForY(double fromVal, double toVal, double curvature, double y)
        {
            // invert the function
            if (toVal < fromVal)
            {
                double yReversed = fromVal - (y - toVal);
                return CalculateXValueForY(toVal, fromVal, curvature, yReversed);
            }
            return Math.Exp(curvature * Math.Log((y - fromVal) / (toVal - fromVal)) + Math.Log(toVal - fromVal)) + fromVal;
        }

        public static double CalculateCurvatureForThreePoints(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            if (y3 < y1)
                return CalculateCurvatureForThreePoints(x3, y3, x2, y2, x1, y1); // reverse order
            double proportion = (x2 - x1) / (x3 - x1);
            // solve for c in y2 = y1 + (y3 - y1) * p^(1/c)
            return Math.Log(proportion) / (Math.Log(y2 - y1) - Math.Log(y3 - y1));
        }

        public static double CalculateYGivenExtremesMiddle(double minX, double maxX, double yForMinX, double yForMaxX, double yForHalfway, double x)
        {
            double curvature = CalculateCurvatureForThreePoints(minX, yForMinX, 0.5 * (minX + maxX), yForHalfway, maxX, yForMaxX);
            double y = CalculateYValueForX(minX, maxX, curvature, x);
            return y;
        }
    }
}

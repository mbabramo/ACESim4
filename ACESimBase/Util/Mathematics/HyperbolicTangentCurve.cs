using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESimBase.Util.Mathematics
{
    public static class HyperbolicTangentCurve
    {
        public static double GetYValueWhereYAsymptoteIsAboveYCriticalPointAndXIsToRightOfCriticalPoint(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteAsXGoesToInfinity, double exampleXOnCurve, double exampleYOnCurve, double actualXOnCurve)
        {
            double theta = tanhinverse((exampleYOnCurve - yValAtCriticalPoint) / (yAsymptoteAsXGoesToInfinity - yValAtCriticalPoint)) / (exampleXOnCurve - xValAtCriticalPoint);
            return yValAtCriticalPoint + (yAsymptoteAsXGoesToInfinity - yValAtCriticalPoint) * Math.Tanh(theta * (actualXOnCurve - xValAtCriticalPoint));
        }


        public static double GetYValue(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteInDirectionOfExample, double exampleXOnCurve, double exampleYOnCurve, double actualXOnCurve)
        {
            Func<double, double> rotateAroundCriticalPointYAxis = z => yValAtCriticalPoint - (z - yValAtCriticalPoint);
            Func<double, double> rotateAroundCriticalPointXAxis = z => xValAtCriticalPoint - (z - xValAtCriticalPoint);
            bool doRotateY = yAsymptoteInDirectionOfExample < yValAtCriticalPoint;
            bool doRotateX = actualXOnCurve < xValAtCriticalPoint;
            Func<double, double> rotateAroundCriticalPointYAxisIfNecessary = z => doRotateY ? rotateAroundCriticalPointYAxis(z) : z;
            Func<double, double> rotateAroundCriticalPointXAxisIfNecessary = z => doRotateX ? rotateAroundCriticalPointXAxis(z) : z;

            return rotateAroundCriticalPointYAxisIfNecessary(GetYValueWhereYAsymptoteIsAboveYCriticalPointAndXIsToRightOfCriticalPoint(xValAtCriticalPoint, yValAtCriticalPoint, rotateAroundCriticalPointYAxisIfNecessary(yAsymptoteInDirectionOfExample), rotateAroundCriticalPointXAxisIfNecessary(exampleXOnCurve), rotateAroundCriticalPointYAxisIfNecessary(exampleYOnCurve), rotateAroundCriticalPointXAxisIfNecessary(actualXOnCurve)));
        }

        public static double GetYValueTwoSided(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteAsXGoesToNegativeInfinity, double yAsymptoteAsXGoesToInfinity, double exampleXOnCurveToLeftOfCriticalPoint, double exampleYOnCurveToLeftOfCriticalPoint, double exampleXOnCurveToRightOfCriticalPoint, double exampleYOnCurveToRightOfCriticalPoint, double actualXOnCurve)
        {
            if (actualXOnCurve > xValAtCriticalPoint)
                return GetYValue(xValAtCriticalPoint, yValAtCriticalPoint, yAsymptoteAsXGoesToInfinity, exampleXOnCurveToRightOfCriticalPoint, exampleYOnCurveToRightOfCriticalPoint, actualXOnCurve);
            else
                return GetYValue(xValAtCriticalPoint, yValAtCriticalPoint, yAsymptoteAsXGoesToNegativeInfinity, exampleXOnCurveToLeftOfCriticalPoint, exampleYOnCurveToLeftOfCriticalPoint, actualXOnCurve);
        }

        public static double GetXValueWhereYAsymptoteIsAboveYCriticalPointAndXIsToRightOfCriticalPoint(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteAsXGoesToInfinity, double exampleXOnCurve, double exampleYOnCurve, double actualYOnCurve)
        {
            double theta = tanhinverse((exampleYOnCurve - yValAtCriticalPoint) / (yAsymptoteAsXGoesToInfinity - yValAtCriticalPoint)) / (exampleXOnCurve - xValAtCriticalPoint);
            return tanhinverse((actualYOnCurve - yValAtCriticalPoint) / (yAsymptoteAsXGoesToInfinity - yValAtCriticalPoint)) / theta + xValAtCriticalPoint;
        }

        public static double GetXValue(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteInDirectionOfExample, double exampleXOnCurve, double exampleYOnCurve, double actualYOnCurve)
        {
            Func<double, double> rotateAroundCriticalPointYAxis = z => yValAtCriticalPoint - (z - yValAtCriticalPoint);
            Func<double, double> rotateAroundCriticalPointXAxis = z => xValAtCriticalPoint - (z - xValAtCriticalPoint);
            bool doRotateY = yAsymptoteInDirectionOfExample < yValAtCriticalPoint;
            bool doRotateX = exampleXOnCurve < xValAtCriticalPoint;
            Func<double, double> rotateAroundCriticalPointYAxisIfNecessary = z => doRotateY ? rotateAroundCriticalPointYAxis(z) : z;
            Func<double, double> rotateAroundCriticalPointXAxisIfNecessary = z => doRotateX ? rotateAroundCriticalPointXAxis(z) : z;

            return rotateAroundCriticalPointXAxisIfNecessary(GetXValueWhereYAsymptoteIsAboveYCriticalPointAndXIsToRightOfCriticalPoint(xValAtCriticalPoint, yValAtCriticalPoint, rotateAroundCriticalPointYAxisIfNecessary(yAsymptoteInDirectionOfExample), rotateAroundCriticalPointXAxisIfNecessary(exampleXOnCurve), rotateAroundCriticalPointYAxisIfNecessary(exampleYOnCurve), rotateAroundCriticalPointYAxisIfNecessary(actualYOnCurve)));
        }

        public static double GetXValueTwoSided(double xValAtCriticalPoint, double yValAtCriticalPoint, double yAsymptoteAsXGoesToNegativeInfinity, double yAsymptoteAsXGoesToInfinity, double exampleXOnCurveToLeftOfCriticalPoint, double exampleYOnCurveToLeftOfCriticalPoint, double exampleXOnCurveToRightOfCriticalPoint, double exampleYOnCurveToRightOfCriticalPoint, double actualYOnCurve)
        {
            if (exampleYOnCurveToRightOfCriticalPoint > yValAtCriticalPoint == actualYOnCurve > yValAtCriticalPoint) // equivalent to (actualXOnCurve > xValAtCriticalPoint), but since actualXOnCurve is unknown this tests whether the actualYOnCurve is on the same side of yValAtCriticalPoint as exampleYOnCurveToRightOfCriticalPoint
                return GetXValue(xValAtCriticalPoint, yValAtCriticalPoint, yAsymptoteAsXGoesToInfinity, exampleXOnCurveToRightOfCriticalPoint, exampleYOnCurveToRightOfCriticalPoint, actualYOnCurve);
            else
                return GetXValue(xValAtCriticalPoint, yValAtCriticalPoint, yAsymptoteAsXGoesToNegativeInfinity, exampleXOnCurveToLeftOfCriticalPoint, exampleYOnCurveToLeftOfCriticalPoint, actualYOnCurve);
        }

        public static double tanhinverse(double toInvert)
        {
            return 0.5 * Math.Log((1 + toInvert) / (1 - toInvert));
        }

        public static void TestInverse()
        {
            double yVal = GetYValueTwoSided(3.0, 3.1, 1.0, 8.0, 2.0, 2.1, 5.0, 5.1, 4.0);
            double xVal = GetXValueTwoSided(3.0, 3.1, 1.0, 8.0, 2.0, 2.1, 5.0, 5.1, yVal);
            Debug.Assert(Math.Abs(xVal - 4.0) < 0.0001);
            yVal = GetYValueTwoSided(3.0, 3.1, 1.0, 8.0, 2.0, 2.1, 5.0, 5.1, 2.5);
            xVal = GetXValueTwoSided(3.0, 3.1, 1.0, 8.0, 2.0, 2.1, 5.0, 5.1, yVal);
            Debug.Assert(Math.Abs(xVal - 2.5) < 0.0001);
            yVal = GetYValueTwoSided(3.0, 3.1, 11.0, 1.0, -4.0, 7.1, 5.0, 2.1, 3.5);
            xVal = GetXValueTwoSided(3.0, 3.1, 11.0, 1.0, -4.0, 7.1, 5.0, 2.1, yVal);
            Debug.Assert(Math.Abs(xVal - 3.5) < 0.0001);
            yVal = GetYValueTwoSided(3.0, 3.1, 11.0, 1.0, -4.0, 7.1, 5.0, 2.1, 2.5);
            xVal = GetXValueTwoSided(3.0, 3.1, 11.0, 1.0, -4.0, 7.1, 5.0, 2.1, yVal);
            Debug.Assert(Math.Abs(xVal - 2.5) < 0.0001);
        }
    }
}

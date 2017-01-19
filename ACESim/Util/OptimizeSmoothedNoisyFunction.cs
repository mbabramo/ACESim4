using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    public class OptimizeSmoothedNoisyFunction
    {
        public bool Minimizing;
        public double Precision;
        public bool AllowRangeToExpandIfNecessary;
        public double LowerBound, UpperBound;
        public int NumberSeedPoints = 10;
        public Func<double, double> TheFunction;

        private List<Tuple<double, double>> TestedPoints = new List<Tuple<double, double>>();
        private double? CurrentOptimalXVal;
        private double HigherDistanceRange;

        public double Optimize()
        {
            SeedPointsToTest();
            while (HigherDistanceRange > Precision)
            {
                Debug.WriteLine(CurrentOptimalXVal.ToSignificantFigures() + " ----- " + String.Join(",", TestedPoints.Select(x => x.Item1.ToSignificantFigures() + " --> " + x.Item2.ToSignificantFigures()).ToArray()));
                CurrentOptimalXVal = GetOptimalValueBasedOnTestedPoints();
                Tuple<double, double> newPoint1 = TestUntestedPointAtSpecifiedDistanceFromExistingPoint(HigherDistanceRange);
                Tuple<double, double> newPoint2 = TestUntestedPointAtSpecifiedDistanceFromExistingPoint(HigherDistanceRange / 2.0);
                if (newPoint1 == null && newPoint2 == null)
                {
                    HigherDistanceRange /= 2.0;
                }
                else if (newPoint1 != null && newPoint2 != null)
                {
                    double deltaFromAddingPoint1 = CalculateDeltaInOptimalValueAddingNewPoint(newPoint1);
                    double deltaFromAddingPoint2 = CalculateDeltaInOptimalValueAddingNewPoint(newPoint2);
                    //Debug.WriteLine("Current optimum: " + CurrentOptimalXVal + " High distance: " + HigherDistanceRange + " High distance point: " + newPoint1.Item1 + " low distance point: " + newPoint2.Item1 + " more useful: " + ((deltaFromAddingPoint1 > deltaFromAddingPoint2) ? "high" : "low"));
                    if (deltaFromAddingPoint1 > deltaFromAddingPoint2) // further point added makes more of a difference, so we should expand our search rather than narrow it
                        HigherDistanceRange *= 2.0;
                    else
                        HigherDistanceRange /= 2.0;
                }
                if (newPoint1 != null)
                    TestedPoints.Add(newPoint1);
                if (newPoint2 != null)
                    TestedPoints.Add(newPoint2);
                CurrentOptimalXVal = GetOptimalValueBasedOnTestedPoints();
            }
            return (double) CurrentOptimalXVal;
        }

        private void OrderTestedPoints()
        {
            if (CurrentOptimalXVal != null)
                TestedPoints = TestedPoints.OrderBy(x => Math.Abs((double)CurrentOptimalXVal - x.Item1)).ToList();
        }

        private double CalculateDeltaInOptimalValueAddingNewPoint(Tuple<double, double> newPointToAdd)
        {
            TestedPoints.Add(newPointToAdd);
            double hypotheticalOptimalXVal = GetOptimalValueBasedOnTestedPoints();
            TestedPoints.Remove(newPointToAdd);
            return Math.Abs((double)CurrentOptimalXVal - hypotheticalOptimalXVal);
        }

        private Tuple<double, double> TestUntestedPointAtSpecifiedDistanceFromExistingPoint(double dist)
        {
            double? xValToUse = null;
            OrderTestedPoints();
            for (int p = 0; p < TestedPoints.Count && xValToUse == null; p++)
            {
                double higherPoint = TestedPoints[p].Item1 + dist;
                if (higherPoint > UpperBound && !AllowRangeToExpandIfNecessary)
                    higherPoint = TestedPoints[p].Item1;
                bool higherPointAlreadyTested = XValIsOnTestedPointsList(higherPoint);
                double lowerPoint = TestedPoints[p].Item1 - dist;
                if (lowerPoint < LowerBound && !AllowRangeToExpandIfNecessary)
                    lowerPoint = TestedPoints[p].Item1;
                bool lowerPointAlreadyTested = XValIsOnTestedPointsList(lowerPoint);
                if (!higherPointAlreadyTested && !lowerPointAlreadyTested)
                    xValToUse = Math.Abs(higherPoint - (double)CurrentOptimalXVal) < Math.Abs(lowerPoint - (double)CurrentOptimalXVal) ? higherPoint : lowerPoint; // return point closer to current optimum
                else if (higherPointAlreadyTested && !lowerPointAlreadyTested)
                    xValToUse = lowerPoint;
                else if (!higherPointAlreadyTested && lowerPointAlreadyTested)
                    xValToUse = higherPoint;
            }
            if (xValToUse == null)
                return null;
            return new Tuple<double, double>((double)xValToUse, TheFunction((double)xValToUse));
        }

        private bool XValIsOnTestedPointsList(double xVal)
        {
            return TestedPoints.Any(x => Math.Abs(x.Item1 - xVal) < Precision / 100.0);
        }

        private void SeedPointsToTest()
        {
            double distanceBetweenPoints = (UpperBound - LowerBound) / (NumberSeedPoints - 1);
            for (int i = 0; i < NumberSeedPoints; i++)
            {
                double xVal = LowerBound + distanceBetweenPoints * i;
                double yVal = TheFunction(xVal);
                TestedPoints.Add(new Tuple<double, double>(xVal, yVal));
            }
            HigherDistanceRange = distanceBetweenPoints;
        }

        private double GetOptimalValueBasedOnTestedPoints_RPROP()
        {
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(1, TestedPoints.Select(x => x.Item1).ConvertToArrayFormTrainingHalf(), TestedPoints.Select(x => x.Item2).ConvertToArrayFormTrainingHalf());
            NeuralNetworkTrainingData validationData = new NeuralNetworkTrainingData(1, TestedPoints.Select(x => x.Item1).ConvertToArrayFormValidationHalf(), TestedPoints.Select(x => x.Item2).ConvertToArrayFormValidationHalf());
            TrainingInfo trainingInfo = new TrainingInfo() { Technique = TrainingTechnique.ResilientPropagation, Epochs = 50, ValidateEveryNEpochs = 50 };
            NeuralNetworkWrapper net = new NeuralNetworkWrapper(trainingData, validationData, 20, 0, trainingInfo);
            double initialMin = TestedPoints.Min(x => x.Item1);
            double initialMax = TestedPoints.Max(x => x.Item1);
            GoldenSectionOptimizer opt = new GoldenSectionOptimizer() { LowExtreme = initialMin, HighExtreme = initialMax, Minimizing = Minimizing, Precision = 10.0 * Precision, TheFunction = x => net.CalculateResult(new List<double> { x }) };
            double optimalSmoothedValue = AllowRangeToExpandIfNecessary ?  opt.OptimizeAllowingRangeToExpandIfNecessary() : opt.Optimize();
            return optimalSmoothedValue;
        }

        private double GetOptimalValueBasedOnTestedPoints()
        {
            GRNN grnn = new GRNN(TestedPoints.Select(x => new double[] { x.Item1 }).ToList(), TestedPoints.Select(x => x.Item2).ToList(), null);
            double initialMin = TestedPoints.Min(x => x.Item1);
            double initialMax = TestedPoints.Max(x => x.Item1);
            GoldenSectionOptimizer opt = new GoldenSectionOptimizer() { LowExtreme = initialMin, HighExtreme = initialMax, Minimizing = Minimizing, Precision = Precision / 10.0, TheFunction = x => grnn.CalculateOutput(new double[] { x }) };
            double optimalSmoothedValue = AllowRangeToExpandIfNecessary ? opt.OptimizeAllowingRangeToExpandIfNecessary() : opt.Optimize();
            return optimalSmoothedValue;
        }
    }

    public static class OptimizeSmoothedNoisyFunctionTester
    {
        public static void DoTest()
        {
            Func<double, double> func = x => x * x - 2 * x + 1; // +Math.Cos(20 * x) + Math.Cos(50.0 * x) / 10.0; // This looks like a parabola minimized at 1.0, but with a lot of noise. Note that we are interested in the case, like this one, where the noise is constant across function evaluations.
            Stopwatch sw = new Stopwatch();
            sw.Start();
            OptimizeSmoothedNoisyFunction opt = new OptimizeSmoothedNoisyFunction() { LowerBound = -3, UpperBound = 3, AllowRangeToExpandIfNecessary = false, Minimizing = true, NumberSeedPoints = 10, Precision = 0.01, TheFunction = func };
            double result = opt.Optimize();
            sw.Stop();
            Debug.WriteLine("Result (should be 1.0): "  + result + " in " + sw.ElapsedMilliseconds + " milliseconds");
        }
    }
}

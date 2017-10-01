using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public abstract class UtilityMaximizer : GameModuleInputs
    {
        [OptionalSetting]
        public double InitialWealth;

        public abstract double GetSubjectiveUtilityForWealthLevel(double laterWealth);
        public abstract double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility);
        public double GetDeltaFromSpecifiedWealthProducingSpecifiedSubjectiveUtility(double specifiedWealth, double subjectiveUtility)
        {
            return GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(subjectiveUtility) + (InitialWealth - specifiedWealth);
        }
        public double GetDeltaFromSpecifiedWealthProducingSpecifiedDeltaSubjectiveUtility(double specifiedWealth, double deltaSubjectiveUtility)
        {
            return GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(GetSubjectiveUtilityForWealthLevel(specifiedWealth) + deltaSubjectiveUtility) + (InitialWealth - specifiedWealth);
        }

        public abstract List<double> GetParametersForThisTypeOfUtilityMaximization();
        public abstract List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization();
    }

    [Serializable]
    public class RiskNeutralUtilityMaximizer : UtilityMaximizer
    {
        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>();
        }
        
        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { };
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            return laterWealth;
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return subjectiveUtility - InitialWealth;
        }
    }

    //[Serializable]
    //public class SimpleRiskAverseUtilityMaximizer : UtilityMaximizer
    //{
    //    public double InitialUtility = 100000.0;
    //    public double IncrementSize;
    //    public double UtilityWithWealthIncrement;
    //    public double UtilityWithWealthDecrement;
    //    internal double? Curvature;
    //    internal double FromVal, ToVal;

    //    public override List<double> GetParametersForThisTypeOfUtilityMaximization()
    //    {
    //        return new List<double>() { UtilityWithWealthIncrement, UtilityWithWealthDecrement };
    //    }

    //    public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
    //    {
    //        if (Curvature == null) 
    //            Initialize();
    //        double returnVal = StrictlyIncreasingCurve.CalculateYValueForX(FromVal, ToVal, (double)Curvature, laterWealth);
    //        return returnVal;
    //    }

    //    private void Initialize()
    //    {
    //        // we are here inverting the formula we use for a simple curve that is strictly increasing to find the curvature.
    //        FromVal = (InitialWealth - IncrementSize);
    //        ToVal = (InitialWealth + IncrementSize);
    //        Curvature = StrictlyIncreasingCurve.CalculateCurvatureForThreePoints(FromVal, UtilityWithWealthDecrement, InitialWealth, InitialUtility, ToVal, UtilityWithWealthIncrement);
    //    }

    //    public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
    //    {
    //        if (Curvature == null)
    //            Initialize();
    //        double wealthProducingSubjectiveUtility = StrictlyIncreasingCurve.CalculateXValueForY(FromVal, ToVal, (double)Curvature, subjectiveUtility);
    //        return wealthProducingSubjectiveUtility - InitialWealth;
    //    }
    //}

    [Serializable]
    public class LogRiskAverseUtilityMaximizer : UtilityMaximizer
    {
        // Note: It doesn't matter what the logarithm base is; that won't affect the degree of risk aversion, which is affected only by changing the initial wealth.

        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>() { InitialWealth };
        }

        
        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("Initial wealth", "initwealth") };
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            if (laterWealth < 0.000000001)
                laterWealth = 0.000000001; // this allows us to avoid an exception when testing actions that would have disastrous consequences
            return Math.Log(laterWealth);
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return Math.Exp(subjectiveUtility) - InitialWealth;
        }
    }

    [Serializable]
    public class CARARiskAverseUtilityMaximizer_Old : UtilityMaximizer
    {
        public double Alpha; // coefficient for constant absolute risk aversion, should be on the order of magnitude of 1 / laterWealth. As alpha goes up, risk aversion increases.

        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>() { Alpha };
        }

        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("Alpha parameter", "alpha") };
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            return 1.0 - Math.Exp(-Alpha * laterWealth);
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return -Math.Log(1.0 - subjectiveUtility) / Alpha - InitialWealth;
        }
    }

    [Serializable]
    public class CARARiskAverseUtilityMaximizer : UtilityMaximizer
    {
        [SwapInputSeeds("RiskAversionParameter")]
        // do not flip input seed
        public double RiskAversionParameter; // if 0, risk neutral; > 0, risk averse; < 0, risk loving
        internal const double MappedInitialWealth = 10.0; // to avoid floating point errors, initial wealth will be mapped to this value
        internal double WorstCaseWealth = 0; // the lowest possible wealth we are allowing unmapped; mapped, this will be 0

        internal double MapWealth(double actualWealth)
        {
            return ((actualWealth - WorstCaseWealth) / (InitialWealth - WorstCaseWealth)) * MappedInitialWealth;
        }

        internal double UnmapWealth(double transformedWealth)
        {
            return WorstCaseWealth + (transformedWealth / MappedInitialWealth) * (InitialWealth - WorstCaseWealth);
        }

        // the following are for the formulas below (imported from an adaptation of seth chandler's mathematica worksheet)
        internal double k { get { return RiskAversionParameter; } }
        internal double x1 { get { return MapWealth(InitialWealth); } }
        internal double x2 { get { return MapWealth(InitialWealth * 2.0); } }
        const double ArbitraryInitialUtilityLevel = 20.0;
        const double MultiplyUtilityForDoubleInitialWealth = 1.5; // doesn't matter since curve will look the same for any value
        internal double f1 { get { return ArbitraryInitialUtilityLevel; } } 
        internal double f2 { get { return ArbitraryInitialUtilityLevel * MultiplyUtilityForDoubleInitialWealth; } }

        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>() { RiskAversionParameter };
        }

        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("Risk aversion parameter", "r") };
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            double x = MapWealth(laterWealth);
            if (k == 0)
                return (f1 * x - f2 * x + f2 * x1 - f1 * x2) / (x1 - x2);
            else
                return (Math.Exp( k * (x + x1)) * f1 - Math.Exp( k * (x + x2)) * f2 + Math.Exp( k * (x1 + x2)) * (-f1 + f2)) /
   (Math.Exp( k * x) * (Math.Exp( k * x1) - Math.Exp( k * x2)));
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            double y = subjectiveUtility;
            double mappedWealthProducingSubjectiveUtility;
            if (k == 0)
                mappedWealthProducingSubjectiveUtility = ((f2 * x1) / (x1 - x2) - (f1 * x2) / (x1 - x2) - y) / (-(f1 / (x1 - x2)) + f2 / (x1 - x2));
            else
                mappedWealthProducingSubjectiveUtility = (k * x1 + k * x2 + Math.Log((f1 - f2) / (Math.Exp(k * x1) * f1 - Math.Exp(k * x2) * f2 - Math.Exp(k * x1) * y + Math.Exp(k * x2) * y))) / k;
            double unmappedWealthProducingSubjectiveUtility = UnmapWealth(mappedWealthProducingSubjectiveUtility);
            return unmappedWealthProducingSubjectiveUtility - InitialWealth;
        }
    }

    [Serializable]
    public class GeneralRiskAverseUtilityMaximizer : UtilityMaximizer
    {
        // TODO: Needs fixing. Something is amiss here.
        public double r; // 0 < r < 1. As r goes up we go from slight to extreme risk aversion. This is a parameter we defined to make this more intuitive to parameterize
        public double a // 0 < a, a != 1, as a goes up, there is LESS risk aversion. The fact that this is not defined for a = 1 is a bit of a problem, but we correct for it by changing a = 1 to a = 0.999999;
        { 
            get { double returnVal = Math.Pow(2.0, 5.0 - r * 10.0); if (returnVal == 1.0) returnVal = 0.999999; return returnVal; }
            set { r = (5 - Math.Log(value) / 0.693147 /* Math.Log(2.0) */ ) / 10.0; }
        } 
        public double b; // set b to 0 for CRRA (constant relative risk aversion). as b goes up orders of magnitude, it seems that risk aversion becomes a little less sensitive to how big the change is relative to wealth

        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>() { r, b };
        }

        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("r parameter", "r"), new Tuple<string, string>("b parameter", "b") };
        }
        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            return (Math.Pow(laterWealth + (b / a), (1 - 1 / a))) / (1 - 1 / a);
        }

        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return (Math.Exp(Math.Log(subjectiveUtility * (1 - 1 / a))/(1 - 1/a))) - (b / a) - InitialWealth;
        }
    }

    [Serializable]
    public class LossAverseUtilityMaximizer : UtilityMaximizer
    {
        public double MaxSubjectiveLossAsMultipleOfSubjectiveGain;
        public double GainNeededToGetToHalfOfSubjectiveMaxGain;
        public double LossNeededToGetToHalfOfSubjectiveMaxLoss;
        [InternallyDefinedSetting]
        public double WealthReferencePoint;

        public override List<double> GetParametersForThisTypeOfUtilityMaximization()
        {
            return new List<double>() { MaxSubjectiveLossAsMultipleOfSubjectiveGain, GainNeededToGetToHalfOfSubjectiveMaxGain, LossNeededToGetToHalfOfSubjectiveMaxLoss };
        }
        
        public override List<Tuple<string, string>> GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("Maximum subjective loss as multiple of subjective gain", "mlmgratio"), new Tuple<string, string>("Gain needed to get to half of subjective maximum gain", "gainForHalf"), new Tuple<string, string>("Loss needed to get to half of subjective maximum loss", "lossForHalf") };
        }

        public double CalculateSubjectiveUtilityBasedOnGainOrLoss(double actualGainOrLoss)
        {
            if (LossNeededToGetToHalfOfSubjectiveMaxLoss < 0 || GainNeededToGetToHalfOfSubjectiveMaxGain < 0 || MaxSubjectiveLossAsMultipleOfSubjectiveGain <= 0)
                throw new Exception("Invalid loss aversion parameters.");
            return HyperbolicTangentCurve.GetYValueTwoSided(0.0, 0.0, -1.0 * MaxSubjectiveLossAsMultipleOfSubjectiveGain, 1.0, 0 - LossNeededToGetToHalfOfSubjectiveMaxLoss, -0.5, GainNeededToGetToHalfOfSubjectiveMaxGain, 0.5, actualGainOrLoss);
        }

        public override double GetSubjectiveUtilityForWealthLevel(double laterWealth)
        {
            if (WealthReferencePoint == 0)
                WealthReferencePoint = InitialWealth;
            return CalculateSubjectiveUtilityBasedOnGainOrLoss(laterWealth - WealthReferencePoint);
        }


        public override double GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(double subjectiveUtility)
        {
            return HyperbolicTangentCurve.GetXValueTwoSided(0.0, 0.0, -1.0 * MaxSubjectiveLossAsMultipleOfSubjectiveGain, 1.0, 0 - LossNeededToGetToHalfOfSubjectiveMaxLoss, -0.5, GainNeededToGetToHalfOfSubjectiveMaxGain, 0.5, subjectiveUtility);
        }
    }

    public static class UtilityCalculationTest
    {
        public static void Check()
        {
            double initialWealth = 100000;
            double initialUtility = 10000000.0;

            //double[] utilityDecrementForHundredthWealthDecrement = new double[] { 1.1, 1.0, 4.0, 8.0, 16.0, 32.0 };
            //foreach (double u in utilityDecrementForHundredthWealthDecrement)
            //{
            //    SimpleRiskAverseUtilityMaximizer ra = new SimpleRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, InitialUtility = initialUtility, IncrementSize = 1000.0, UtilityWithWealthIncrement = initialUtility + 1.0, UtilityWithWealthDecrement = initialUtility - u };
            //    CalculateUtilityForWealthFrom98000To102000(u, ra);
            //}
            //double[] kList = new double[] { -1.0, -0.5, 0, 0.5, 1.0, 1.5 };
            //foreach (double k in kList)
            //{
            //    CARARiskAverseUtilityMaximizer riskAverseGeneral3 = new CARARiskAverseUtilityMaximizer() { InitialWealth = initialWealth, MultiplyUtilityForDoubleInitialWealth = 1.5, RiskAversionParameter = k };
            //    CalculateUtilityForWealthFrom98000To102000(k, riskAverseGeneral3);
            //}

            double[] kList = new double[] { -2.0, 0, 2.0, 4.0, 8.0, 16.0 };
            foreach (double k in kList)
            {
                CARARiskAverseUtilityMaximizer riskAverseGeneral3 = new CARARiskAverseUtilityMaximizer() { InitialWealth = initialWealth, RiskAversionParameter = k };
                CalculateUtilityForWealthFrom98000To102000(k, riskAverseGeneral3);
            }

            //double[] alphaList = new double[] { 0.000001, 0.00001, 0.00002, 0.00003, 0.00004, 0.00009 };
            //foreach (double a in alphaList)
            //{
            //    CARARiskAverseUtilityMaximizer riskAverseGeneral4 = new CARARiskAverseUtilityMaximizer() { InitialWealth = initialWealth, Alpha = a };
            //    CalculateUtilityForWealthFrom98000To102000(a, riskAverseGeneral4);
            //}

            Debug.WriteLine("CRRA test");
            double[] rList = new double[] { 0.4, 0.48, 0.49, 0.493, 0.495, 0.499 };
            foreach (double r in rList)
            {
                GeneralRiskAverseUtilityMaximizer riskAverseGeneral2 = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, r = r, b = 0 };
                CalculateUtilityForWealthFrom98000To102000(r, riskAverseGeneral2);
            }
            //double equiv = riskAverseGeneral.GetDeltaWealthProducingSubjectiveUtility(riskAverseGeneral.GetLaterSubjectiveUtility(10000) + 3.895E-18);

            GeneralRiskAverseUtilityMaximizer riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.2, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.8, b = 0.3 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.4, b = 10000.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 0.05, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.05, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 0.1, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.1, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 0.2, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.2, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);


            Debug.WriteLine("");
            Debug.WriteLine("a = 0.5, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.5, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 0.999, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 0.999, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 1.1, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 1.1, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 2, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 2, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 4, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 4, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            Debug.WriteLine("");
            Debug.WriteLine("a = 8, b = 0");
            riskAverseGeneral = new GeneralRiskAverseUtilityMaximizer() { InitialWealth = initialWealth, a = 8, b = 0.0 };
            CalculateEffect(initialWealth, riskAverseGeneral);
            Debug.WriteLine("Above r is " + riskAverseGeneral.r);

            RiskNeutralUtilityMaximizer riskNeutral = new RiskNeutralUtilityMaximizer() { InitialWealth = initialWealth };
            CalculateEffect(initialWealth, riskNeutral);

            LogRiskAverseUtilityMaximizer riskAverseLog = new LogRiskAverseUtilityMaximizer() { InitialWealth = initialWealth };
            CalculateEffect(initialWealth, riskAverseLog);

            CARARiskAverseUtilityMaximizer_Old riskAverseCARA = new CARARiskAverseUtilityMaximizer_Old() { InitialWealth = initialWealth, Alpha = 0.000001 };
            CalculateEffect(initialWealth, riskAverseCARA);

            riskAverseCARA = new CARARiskAverseUtilityMaximizer_Old() { InitialWealth = initialWealth, Alpha = 0.000002 };
            CalculateEffect(initialWealth, riskAverseCARA);

            Debug.WriteLine("Loss aversion");
            LossAverseUtilityMaximizer lossAverse = new LossAverseUtilityMaximizer() { InitialWealth = initialWealth, WealthReferencePoint = initialWealth, GainNeededToGetToHalfOfSubjectiveMaxGain = 20000, LossNeededToGetToHalfOfSubjectiveMaxLoss = 10000, MaxSubjectiveLossAsMultipleOfSubjectiveGain = 2.5 };
            for (int w = 0; w <= 200000; w += 5000)
                Debug.Write(lossAverse.GetSubjectiveUtilityForWealthLevel(w) + "\t");
            //for (int w = 98000; w <= 102000; w += 100)
            //    Debug.Write(lossAverse.GetSubjectiveUtilityForWealthLevel(w) + "\t");

            double expectedUtility = (riskAverseLog.GetSubjectiveUtilityForWealthLevel(initialWealth - 10000) + riskAverseLog.GetSubjectiveUtilityForWealthLevel(initialWealth + 10000)) / 2.0;
            double deltaWealthCorrespondingToExpectedUtility = riskAverseLog.GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(expectedUtility);
        }

        private static void CalculateUtilityForWealthFrom10To30(double parameterToExplore, UtilityMaximizer u)
        {
            double utilityAt20 = u.GetSubjectiveUtilityForWealthLevel(20);
            StringBuilder sb = new StringBuilder();
            sb.Append(parameterToExplore.ToString() + "\t");
            for (double wealth = 10; wealth <= 30; wealth += 0.5)
            {
                double ratio = u.GetSubjectiveUtilityForWealthLevel(wealth); // / utilityAt20;
                string ratioString = NumberPrint.ToSignificantFigures(ratio, 10);
                sb.Append(ratioString);
                if (wealth != 30)
                    sb.Append("\t");
            }
            Debug.WriteLine(sb.ToString());
        }

        private static void CalculateUtilityForWealthFrom98000To102000(double parameterToExplore, UtilityMaximizer u)
        {
            double utilityAt100000 = u.GetSubjectiveUtilityForWealthLevel(100000);
            StringBuilder sb = new StringBuilder();
            sb.Append(parameterToExplore.ToString() + "\t");
            for (double wealth = 98000; wealth <= 102000; wealth += 100.0)
            {
                double ratio = u.GetSubjectiveUtilityForWealthLevel(wealth) / utilityAt100000;
                string ratioString = NumberPrint.ToSignificantFigures(ratio, 10);
                sb.Append(ratioString);
                if (wealth != 102000)
                    sb.Append("\t");
            }
            Debug.WriteLine(sb.ToString());
        }

        private static void CalculateEffect(double initialWealth, UtilityMaximizer u)
        {
            Debug.WriteLine(u.GetType().ToString());

            CalculateEffectHelper(initialWealth, u, 10000); 
            CalculateEffectHelper(initialWealth, u, 20000);

            //for (int laterWealth = 90000; laterWealth <= 110000; laterWealth += 5000)
            //{
            //    double laterSubjectiveUtility = u.GetLaterSubjectiveUtility(laterWealth);
            //    Debug.WriteLine(laterWealth + ":" + laterSubjectiveUtility + " Difference: " + (laterSubjectiveUtility - baseLineSubjectiveUtility));
            //    double wealthDifferential = u.GetDeltaWealthProducingSubjectiveUtility(laterSubjectiveUtility);
            //    double checkFinalWealth = ((double)initialWealth + wealthDifferential);
            //    Debug.Assert(Math.Abs(laterWealth - checkFinalWealth) < 0.001);
            //}
        }

        private static void CalculateEffectHelper(double initialWealth, UtilityMaximizer u, int delta)
        {
            double baseLineSubjectiveUtility = u.GetSubjectiveUtilityForWealthLevel(initialWealth);

            double laterWealthLower = initialWealth - delta;
            double laterSubjectiveUtilityLower = u.GetSubjectiveUtilityForWealthLevel(laterWealthLower);
            double differenceLower = laterSubjectiveUtilityLower - baseLineSubjectiveUtility;
            Debug.WriteLine(laterWealthLower + ":" + laterSubjectiveUtilityLower + " Difference: " + (differenceLower));
            double wealthDifferential = u.GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(laterSubjectiveUtilityLower);
            double checkFinalWealth = ((double)initialWealth + wealthDifferential);
            Debug.Assert(Math.Abs(laterWealthLower - checkFinalWealth) < 0.001);

            Debug.WriteLine(initialWealth + ":" + baseLineSubjectiveUtility);

            double laterWealthHigher = initialWealth + delta;
            double laterSubjectiveUtilityHigher = u.GetSubjectiveUtilityForWealthLevel(laterWealthHigher);
            double differenceHigher = laterSubjectiveUtilityHigher - baseLineSubjectiveUtility;
            Debug.WriteLine(laterWealthHigher + ":" + laterSubjectiveUtilityHigher + " Difference: " + (differenceHigher));
            wealthDifferential = u.GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(laterSubjectiveUtilityHigher);
            checkFinalWealth = ((double)initialWealth + wealthDifferential);
            Debug.Assert(Math.Abs(laterWealthHigher - checkFinalWealth) < 0.001);

            Debug.WriteLine("Ratio of higher difference to lower difference (lower ratios mean more risk aversion)" + ((0 - differenceHigher) / differenceLower));
        }
    }
}

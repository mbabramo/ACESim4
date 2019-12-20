using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{
    public class EqFinder
    {
        bool logDetailedProgress = true;
        bool printUtilitiesInDetailedLog = false;

        double q, c, t;
        int optimalPStrategy = -1, optimalDStrategy = -1;
        public Outcome TheOutcome;

        // Each player's strategy is a line, represented by a minimum strategy value and a maximum strategy value. This class seeks to optimize that line
        // by playing a range of strategies. To ensure that it considers very high slopes (positive or negative), as well as intermediate ones, it converts
        // relatively high and low strategy values nonlinearly. Because both lines must have
        // non-negative slopes, we add the constraint that the maxSignalStrategy must be greater than or equal to the minSignalStrategy. 
        // So, if party has lowest signal strategy for the minimum value, there are n possibilities for the max. If party has highest signal strategy (n),
        // then there is only one possibility for the max. Note that below, we will also model an outside option where either party can force trial, but we don't
        // include that in the matrix, both because it would take a lot of space and because the equilibrium where both refuse to give reasonable offers is
        // always a Nash equilibrium, albeit a trivial one. 
        const int NumStartOrEndPointsOfLine = 100;
        const int NumNormalizedSignalsPerPlayer = 100;

        const int NumStrategiesPerPlayer = NumStartOrEndPointsOfLine * (NumStartOrEndPointsOfLine + 1) / 2;
        static int ConvertMinMaxToStrategy(int minSignalStrategy, int maxSignalStrategy)
        {
            int i = 0;
            for (int minSignalStrategyCounter = 0; minSignalStrategyCounter < NumStartOrEndPointsOfLine; minSignalStrategyCounter++)
                for (int maxSignalStrategyCounter = minSignalStrategyCounter; maxSignalStrategyCounter < NumStartOrEndPointsOfLine; maxSignalStrategyCounter++)
                {
                    if (minSignalStrategyCounter == minSignalStrategy && maxSignalStrategyCounter == maxSignalStrategy)
                        return i;
                    else
                        i++;
                }
            throw new Exception(); // shouldn't happen.
        }
        static (int minSignalStrategy, int maxSignalStrategy) ConvertStrategyToMinMaxOffers(int strategy)
        {
            int i = 0;
            for (int minSignalStrategy = 0; minSignalStrategy < NumStartOrEndPointsOfLine; minSignalStrategy++)
                for (int maxSignalStrategy = minSignalStrategy; maxSignalStrategy < NumStartOrEndPointsOfLine; maxSignalStrategy++)
                    if (i++ == strategy)
                        return (minSignalStrategy, maxSignalStrategy);
            throw new Exception(); // shouldn't happen.
        }

        // Zoom in feature: We can choose to what value the 25th percentile and the 75th of what each player's strategy corresponds to. Initially, we start at 0.25 and 0.75.
        // Then we zoom in until we get to a required level of accuracy.
        // For each number that we're trying to zoom into, we have a mean value and then a range that takes us from the 25%le to the 75%le
        // (or whatever the percentiles are above). At each zoom in cycle, we center the cutoffs around what we now think is the optimal value.
        // When the number we are targeting was within the 25%le to the 75%le range, we multiply the total distance by (1 - zoomSpeed). 
        // When the number is outside the range, we multiply by (1 + zoomSpeed).
        static double zoomAccuracy = 0.001;
        static double zoomSpeed = 0.1;

        public EqFinder(double q, double c, double t)
        {
            this.q = q;
            this.c = c;
            this.t = t;
            Execute();
        }


        int Cycle;
        private void Execute()
        {
            CalculateFromPaper();
            ConfirmConversions();
            bool done = false;
            Cycle = 0;
            while (!done)
            {
                if (logDetailedProgress)
                    TabbedText.WriteLine($"Cycle {++Cycle}");
                if (logDetailedProgress)
                    TabbedText.WriteLine($"25%le to 75%le ranges: P ({NonlinearCutoffString(true, true, true)} to {NonlinearCutoffString(true, false, true)},{NonlinearCutoffString(true, true, false)} to {NonlinearCutoffString(true, false, false)}) D ({NonlinearCutoffString(false, true, true)} to {NonlinearCutoffString(false, false, true)},{NonlinearCutoffString(false, true, false)} to {NonlinearCutoffString(false, false, false)}) ");
                ResetUtilities();
                CalculateUtilities();
                var eqResult = EvaluateEquilibria();
                if (logDetailedProgress && printUtilitiesInDetailedLog)
                    PrintUtilities();

                AdjustLowerAndUpperCutoffsBasedOnPerformance();
                done = AllCutoffDistancesAccurateEnough(zoomAccuracy);
                if (done && OutsideOptionDominates)
                    SelectOutsideOption();
                if (done || logDetailedProgress)
                    TabbedText.WriteLine(eqResult);
            }
        }

        private void CalculateFromPaper()
        {
            if (t == 0)
            {
                double correctPStart = 0.5 - 3.0 * (5.0 / 6.0 - q) * c;
                double correctPEnd = correctPStart + 1.0 / 3.0;
                double correctDStart = 1.0 / 6.0 + 3 * (q - 1.0 / 6.0) * c;
                double correctDEnd = correctDStart + 1.0 / 3.0;
                var r = CalculateUtilitiesForOfferRanges((correctPStart, correctPEnd), (correctDStart, correctDEnd));
                TabbedText.WriteLine($"Anticipated answer P:{correctPStart.ToSignificantFigures(3)}, {correctPEnd.ToSignificantFigures(3)} D:{correctDStart.ToSignificantFigures(3)}, {correctDEnd.ToSignificantFigures(3)} ==> ({r.pUtility.ToSignificantFigures(3)}, {r.dUtility.ToSignificantFigures(3)})");
            }
        }

        private static void ConfirmConversions()
        {
            var OneDirection = Enumerable.Range(0, NumStrategiesPerPlayer).Select(x => ConvertStrategyToMinMaxOffers(x)).ToArray();
            var OppositeDirection = OneDirection.Select(x => ConvertMinMaxToStrategy(x.minSignalStrategy, x.maxSignalStrategy)).ToArray();
            for (int w = 0; w < NumStrategiesPerPlayer; w++)
            {
                (int minSignalStrategy, int maxSignalStrategy) = ConvertStrategyToMinMaxOffers(w);
                int w2 = ConvertMinMaxToStrategy(minSignalStrategy, maxSignalStrategy);
                if (w != w2)
                    throw new Exception();
            }
        }

        HashSet<(int p, int d)> AllEquilibria;

        private void PrintUtilities()
        {
            for (int p = -1; p < NumStrategiesPerPlayer; p++)
            {
                if (p == -1)
                    TabbedText.Write($"P| D->".PadRight(22));
                else
                    TabbedText.Write($"{p.ToString()} ({ConvertStrategyToMinMaxContinuousOffers(p, true).minSignalStrategy.ToSignificantFigures(3)}, {ConvertStrategyToMinMaxContinuousOffers(p, true).maxSignalStrategy.ToSignificantFigures(3)}): ".PadRight(22));
                for (int d = 0; d < NumStrategiesPerPlayer; d++)
                {
                    if (p == -1)
                        TabbedText.Write($"{d} ({ConvertStrategyToMinMaxContinuousOffers(d, false).minSignalStrategy.ToSignificantFigures(3)}, {ConvertStrategyToMinMaxContinuousOffers(d, false).maxSignalStrategy.ToSignificantFigures(3)}): ".PadRight(22));
                    else
                        TabbedText.Write($"{PUtilities[p, d].ToSignificantFigures(3)}, {DUtilities[p, d].ToSignificantFigures(3)}{(AllEquilibria.Contains((p, d)) ? "*" : "")}".PadRight(22));
                }
                TabbedText.WriteLine();
            }
        }

        double OptimalPMin, OptimalPMax, OptimalDMin, OptimalDMax;
        bool OutsideOptionDominates;

        private string EvaluateEquilibria()
        {
            StringBuilder b = new StringBuilder();
            List<(int pStrategy, int dStrategy)> nashStrategies = PureStrategiesFinder.ComputeNashEquilibria(PUtilities, DUtilities);
            AllEquilibria = nashStrategies.ToHashSet();

            if (!AllEquilibria.Any())
            {
                (optimalPStrategy, optimalDStrategy) = PureStrategiesFinder.GetApproximateNashEquilibrium(PUtilities, DUtilities);
                b.AppendLine($"Using approximate equilibrium {optimalPStrategy},{optimalDStrategy} => ({PUtilities[optimalPStrategy, optimalDStrategy]},{DUtilities[optimalPStrategy, optimalDStrategy]})");
                return b.ToString();
                //if (OutsideOption != null)
                //{
                //    b.AppendLine("Outside option of trial dominates => Trial rate = 1.");
                //    optimalPStrategy = ConvertMinMaxToStrategy(NumValuesEachSideOfLine - 1, NumValuesEachSideOfLine - 1);
                //    optimalDStrategy = ConvertMinMaxToStrategy(0, 0);
                //    PUtilities[optimalPStrategy, optimalDStrategy] = OutsideOption.Value.pOutsideOption;
                //    DUtilities[optimalPStrategy, optimalDStrategy] = OutsideOption.Value.dOutsideOption;
                //    OutsideOptionSelected = true;
                //    return b.ToString();
                //}
                //else
                //{
                //    NoPureStrategy = true;
                //    return "No pure strategy found";
                //}
            }

            OptimalPMin = AllEquilibria.Min(x => ConvertStrategyToMinMaxContinuousOffers(x.p, true).minSignalStrategy);
            OptimalPMax = AllEquilibria.Max(x => ConvertStrategyToMinMaxContinuousOffers(x.p, true).maxSignalStrategy);
            OptimalDMin = AllEquilibria.Min(x => ConvertStrategyToMinMaxContinuousOffers(x.d, true).minSignalStrategy);
            OptimalDMax = AllEquilibria.Max(x => ConvertStrategyToMinMaxContinuousOffers(x.d, true).maxSignalStrategy);

            //var extremeStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] > 0.99999).ToHashSet(); // take into account rounding errors
            //if (extremeStrategies.Any())
            //{
            //    b.AppendLine("Always trial");
            //    nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x) || x == extremeStrategies.Last()).ToList();
            //}

            //extremeStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] == 0).ToHashSet();
            //if (extremeStrategies.Count() > 1)
            //{
            //    b.AppendLine("Always settle (somewhere in bargaining range) [utility numbers not representative]");
            //    nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x) || x == extremeStrategies.First()).ToList();
            //}

            var f1 = nashStrategies.First();
            optimalPStrategy = f1.pStrategy;
            optimalDStrategy = f1.dStrategy;
            if (OutsideOption != null && PUtilities[optimalPStrategy, optimalDStrategy] < OutsideOption?.pOutsideOption || DUtilities[optimalPStrategy, optimalDStrategy] < OutsideOption?.dOutsideOption)
            {
                //(optimalPStrategy, optimalDStrategy) = PureStrategiesFinder.GetApproximateNashEquilibrium(PUtilities, DUtilities);
                //b.AppendLine($"Best approximate equilibrium {optimalPStrategy},{optimalDStrategy} => ({PUtilities[optimalPStrategy, optimalDStrategy]},{DUtilities[optimalPStrategy, optimalDStrategy]})");
                //b.AppendLine("Outside option of trial dominates => Trial rate = 1.");
                OutsideOptionDominates = true;
                //return b.ToString();
            }
            else
                OutsideOptionDominates = false;

            TheOutcome = new Outcome(PUtilities[optimalPStrategy, optimalDStrategy], DUtilities[optimalPStrategy, optimalDStrategy], TrialRate[optimalPStrategy, optimalDStrategy], AccuracySq[optimalPStrategy, optimalDStrategy], AccuracyHypoSq[optimalPStrategy, optimalDStrategy], AccuracyForP[optimalPStrategy, optimalDStrategy], AccuracyForD[optimalPStrategy, optimalDStrategy], ConvertStrategyToMinMaxContinuousOffers(optimalPStrategy, true), ConvertStrategyToMinMaxContinuousOffers(optimalDStrategy, false));
            var grouped = nashStrategies.GroupBy(x => x.pStrategy);
            foreach (var pStrategyGroup in grouped)
            {
                var aNashStrategy = pStrategyGroup.First();
                var pLine = ConvertStrategyToMinMaxContinuousOffers(aNashStrategy.pStrategy, true);
                b.AppendLine($"P (strategy {aNashStrategy.pStrategy}) offers from {pLine.minSignalStrategy.ToSignificantFigures(3)} to {pLine.maxSignalStrategy.ToSignificantFigures(3)} (utility {PUtilities[aNashStrategy.pStrategy, aNashStrategy.dStrategy].ToSignificantFigures(3)})");
                foreach (var nashStrategy in pStrategyGroup)
                {
                    if (Cycle == 20)
                    {
                        var DEBUG = 0;
                    }
                    var dLine = ConvertStrategyToMinMaxContinuousOffers(nashStrategy.dStrategy, false);
                    b.AppendLine($"D (strategy {aNashStrategy.dStrategy}) offers from {dLine.minSignalStrategy.ToSignificantFigures(3)} to {dLine.maxSignalStrategy.ToSignificantFigures(3)} (utility {DUtilities[nashStrategy.pStrategy, nashStrategy.dStrategy].ToSignificantFigures(3)})");
                    b.AppendLine($"--> Trial rate {TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy].ToSignificantFigures(3)}");
                }
            }
            return b.ToString();
        }

        private void SelectOutsideOption()
        {
            optimalPStrategy = ConvertMinMaxToStrategy(NumStartOrEndPointsOfLine - 1, NumStartOrEndPointsOfLine - 1);
            optimalDStrategy = ConvertMinMaxToStrategy(0, 0);
            PUtilities[optimalPStrategy, optimalDStrategy] = OutsideOption.Value.pOutsideOption;
            DUtilities[optimalPStrategy, optimalDStrategy] = OutsideOption.Value.dOutsideOption;
            TabbedText.WriteLine($"Trial equilibrium {optimalPStrategy},{optimalDStrategy} => ({PUtilities[optimalPStrategy, optimalDStrategy]},{DUtilities[optimalPStrategy, optimalDStrategy]})");
        }

        public readonly struct Outcome
        {
            public readonly double PUtility, DUtility, TrialRate, AccuracySq, AccuracyHypoSq, AccuracyForP, AccuracyForD, MinPOffer, MaxPOffer, MinDOffer, MaxDOffer;

            public Outcome(double PUtility, double DUtility, double TrialRate, double AccuracySq, double AccuracyHypoSq, double AccuracyForP, double AccuracyForD, (double, double) POffer, (double, double) DOffer)
            {
                this.PUtility = PUtility;
                this.DUtility = DUtility;
                this.TrialRate = TrialRate;
                this.AccuracySq = AccuracySq;
                this.AccuracyHypoSq = AccuracyHypoSq;
                this.AccuracyForP = AccuracyForP;
                this.AccuracyForD = AccuracyForD;
                this.MinPOffer = POffer.Item1;
                this.MaxPOffer = POffer.Item2;
                this.MinDOffer = DOffer.Item1;
                this.MaxDOffer = DOffer.Item2;
            }

            public override string ToString()
            {
                return $"{PUtility},{DUtility},{TrialRate},{AccuracySq},{AccuracyHypoSq},{AccuracyForP},{AccuracyForD},{MinPOffer},{MaxPOffer},{MinDOffer},{MaxDOffer}";
            }

            public static string GetHeaderString()
            {
                return "PUtility,DUtility,TrialRate,AccuracySq,AccuracyHypoSq,AccuracyForP,AccuracyForD,MinPOffer,MaxPOffer,MinDOffer,MaxDOffer";
            }
        }

        const int EvaluationsPerStrategyCombination = NumNormalizedSignalsPerPlayer * NumNormalizedSignalsPerPlayer;
        (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy, bool plaintiff)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (GetMappedMinOrMaxOffer(minMax.minSignalStrategy, plaintiff, true), GetMappedMinOrMaxOffer(minMax.maxSignalStrategy, plaintiff, false));
        }
        (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToUnmappedMinMaxContinuousOffers(int strategy)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (GetUnmappedOfferValue(minMax.minSignalStrategy), GetUnmappedOfferValue(minMax.maxSignalStrategy));
        }
        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumNormalizedSignalsPerPlayer + 1));

        // The min/max becomes nonlinear in the input strategy for extreme strategies (i.e., below first variable below or above second).
        // For example, if minMaxNonlinearBelowThreshold = 0.25, then when the discrete signal is less than 25% of the maximum discrete signal,
        // we use a nonlinear function to determine the continuous value.
        // NOTE: Of course, we are still trying to find a strategy that represents a line. This just means that we are going to try some 
        // very steep lines, since the optimal strategy may be an almost vertical line (with a very low minimum or a very high maximum). 
        // Note that flat lines are easier -- because the min and max just have to be equal.
        static double minMaxNonlinearBelowThreshold = 0.25;
        static double minMaxNonlinearAboveThreshold = 0.75;

        // The following specifies the mapping from a discrete representation of a min or max of a line and the continuous representation.
        // Initially, we set this to 0 to 1 to capture the typical range of starting and ending points of lines.
        // But based on the outcome, one can zoom into the result by moving this near the initially successful strategy.
        // For example, suppose the optimal min value for p's strategy corresponds to the 35th percentile discrete value (not necessarily
        // a continuous value of 0.35, though it would be that after the first iteration). At that point, it looks like our strategy is in
        // fact within the (25% to 75%) range, so we might zoom into the (0.30, 0.55) area. If we're outside the range, then we zoom out
        // so that our value will be in the range. For example, if we're at the 20% discrete value, we then might make the range
        // from the 10% value to the 30% value.
        double pValueAtLowerNonlinearCutoff_MinSignal = 0;
        double pValueAtUpperNonlinearCutoff_MinSignal = 1.0;
        double dValueAtLowerNonlinearCutoff_MinSignal = 0;
        double dValueAtUpperNonlinearCutoff_MinSignal = 1.0;
        double pValueAtLowerNonlinearCutoff_MaxSignal = 0;
        double pValueAtUpperNonlinearCutoff_MaxSignal = 1.0;
        double dValueAtLowerNonlinearCutoff_MaxSignal = 0;
        double dValueAtUpperNonlinearCutoff_MaxSignal = 1.0;
        private double NonlinearCutoff(bool plaintiff, bool lowerCutoff, bool isMinSignal) => (plaintiff, lowerCutoff, isMinSignal) switch
        {
            (true, true, true) => pValueAtLowerNonlinearCutoff_MinSignal,
            (true, false, true) => pValueAtUpperNonlinearCutoff_MinSignal,
            (false, true, true) => dValueAtLowerNonlinearCutoff_MinSignal,
            (false, false, true) => dValueAtUpperNonlinearCutoff_MinSignal,
            (true, true, false) => pValueAtLowerNonlinearCutoff_MaxSignal,
            (true, false, false) => pValueAtUpperNonlinearCutoff_MaxSignal,
            (false, true, false) => dValueAtLowerNonlinearCutoff_MaxSignal,
            (false, false, false) => dValueAtUpperNonlinearCutoff_MaxSignal,
        };

        private string NonlinearCutoffString(bool plaintiff, bool lowerCutoff, bool isMinOfLine) => NonlinearCutoff(plaintiff, lowerCutoff, isMinOfLine).ToSignificantFigures(5);

        void AdjustLowerAndUpperCutoffsBasedOnPerformance()
        {
            var pOptimum = ConvertStrategyToMinMaxContinuousOffers(optimalPStrategy, true);
            (pValueAtLowerNonlinearCutoff_MinSignal, pValueAtUpperNonlinearCutoff_MinSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(pOptimum.minSignalStrategy, true, true);
            //if (pValueAtLowerNonlinearCutoff_MinSignal > OptimalPMin)
            //    pValueAtLowerNonlinearCutoff_MinSignal = OptimalPMin - 1E-4;
            //if (pValueAtUpperNonlinearCutoff_MinSignal > OptimalPMin)
            //    pValueAtUpperNonlinearCutoff_MinSignal = OptimalPMin + 1E-4;
            (pValueAtLowerNonlinearCutoff_MaxSignal, pValueAtUpperNonlinearCutoff_MaxSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(pOptimum.maxSignalStrategy, true, false);
            //if (pValueAtLowerNonlinearCutoff_MaxSignal < OptimalPMax)
            //    pValueAtLowerNonlinearCutoff_MaxSignal = OptimalPMax - 1E-4;
            //if (pValueAtUpperNonlinearCutoff_MaxSignal < OptimalPMax)
            //    pValueAtUpperNonlinearCutoff_MaxSignal = OptimalPMax + 1E-4;
            var dOptimum = ConvertStrategyToMinMaxContinuousOffers(optimalDStrategy, false);
            (dValueAtLowerNonlinearCutoff_MinSignal, dValueAtUpperNonlinearCutoff_MinSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(dOptimum.minSignalStrategy, false, true);
            //if (dValueAtLowerNonlinearCutoff_MinSignal > OptimalDMin)
            //    dValueAtLowerNonlinearCutoff_MinSignal = OptimalDMin - 1E-4;
            //if (dValueAtUpperNonlinearCutoff_MinSignal > OptimalDMin)
            //    dValueAtUpperNonlinearCutoff_MinSignal = OptimalDMin + 1E-4;
            (dValueAtLowerNonlinearCutoff_MaxSignal, dValueAtUpperNonlinearCutoff_MaxSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(dOptimum.maxSignalStrategy, false, false);
            //if (dValueAtLowerNonlinearCutoff_MaxSignal < OptimalDMax)
            //    dValueAtLowerNonlinearCutoff_MaxSignal = OptimalDMax - 1E-4;
            //if (dValueAtUpperNonlinearCutoff_MaxSignal < OptimalDMax)
            //    dValueAtUpperNonlinearCutoff_MaxSignal = OptimalDMax + 1E-4;
        }

        (double, double) AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(double apparentOptimum, bool plaintiff, bool isMinSignal)
        {
            double lowerCutoff = NonlinearCutoff(plaintiff, true, isMinSignal);
            double upperCutoff = NonlinearCutoff(plaintiff, false, isMinSignal);
            double currentDistance = upperCutoff - lowerCutoff;
            double revisedDistance;
            if (lowerCutoff < apparentOptimum && apparentOptimum < upperCutoff)
                revisedDistance = currentDistance * (1 - zoomSpeed);
            else
                revisedDistance = currentDistance * (1 + zoomSpeed);
            double halfDistance = revisedDistance / 2.0;
            return (apparentOptimum - halfDistance, apparentOptimum + halfDistance);
        }

        public bool CutoffDistanceAccurateEnough(double requiredAccuracy, bool plaintiff, bool isMinSignal)
        {
            double lowerCutoff = NonlinearCutoff(plaintiff, true, isMinSignal);
            double upperCutoff = NonlinearCutoff(plaintiff, false, isMinSignal);
            double currentDistance = upperCutoff - lowerCutoff;
            return currentDistance < requiredAccuracy;
        }

        public bool AllCutoffDistancesAccurateEnough(double requiredAccuracy) => CutoffDistanceAccurateEnough(requiredAccuracy, true, true) && CutoffDistanceAccurateEnough(requiredAccuracy, true, false) && CutoffDistanceAccurateEnough(requiredAccuracy, false, true) && CutoffDistanceAccurateEnough(requiredAccuracy, false, false);

        double GetMappedMinOrMaxOffer(int discreteOffer, bool plaintiff, bool isMinSignal)
        {
            if (plaintiff && discreteOffer == NumStartOrEndPointsOfLine)
                return 1E+10; // plaintiff's highest offer always forces trial
            if (!plaintiff && discreteOffer == NumStartOrEndPointsOfLine)
                return -1E+10; // defendant's lowest offer always forces trial
            double continuousOfferWithLinearSlopeUnadjusted = GetUnmappedOfferValue(discreteOffer);
            return MapFromZeroOneRangeToMinOrMaxOffer(continuousOfferWithLinearSlopeUnadjusted, plaintiff, isMinSignal);
        }

        private static double GetUnmappedOfferValue(int discreteOffer)
        {
            return (double)(discreteOffer + 0.5) / ((double)(NumStartOrEndPointsOfLine));
        }

        private double MapFromZeroOneRangeToMinOrMaxOffer(double continuousOfferWithLinearSlopeUnadjusted, bool plaintiff, bool isMin)
        {
            if ((continuousOfferWithLinearSlopeUnadjusted >= minMaxNonlinearBelowThreshold && continuousOfferWithLinearSlopeUnadjusted <= minMaxNonlinearAboveThreshold))
            {
                return MapFromInnerRangeToMinOrMaxOffer(continuousOfferWithLinearSlopeUnadjusted, plaintiff, isMin);
            }
            double multiplier = 0.2; // arbitrary
            if (continuousOfferWithLinearSlopeUnadjusted < minMaxNonlinearBelowThreshold)
            {
                double kinkPoint = MapFromInnerRangeToMinOrMaxOffer(minMaxNonlinearBelowThreshold, plaintiff, isMin); 
                // now calculate what we want to subtract from this
                // at 0.25 => 0
                // at 0 => inf.
                double amountToSubtract = 1.0 / continuousOfferWithLinearSlopeUnadjusted - 1.0 / minMaxNonlinearBelowThreshold;
                return kinkPoint - multiplier*amountToSubtract;
            }
            else
            {
                double kinkPoint = MapFromInnerRangeToMinOrMaxOffer(minMaxNonlinearAboveThreshold, plaintiff, isMin);
                double amountToAdd = 1.0 / (1.0 - continuousOfferWithLinearSlopeUnadjusted) - 1.0 / minMaxNonlinearAboveThreshold;
                return kinkPoint + multiplier*amountToAdd;
            }
        }

        private double MapFromInnerRangeToMinOrMaxOffer(double continuousOfferWithLinearSlopeUnadjusted, bool plaintiff, bool isMin)
        {
            double proportionOfWayBetweenCutoffs = (continuousOfferWithLinearSlopeUnadjusted - minMaxNonlinearBelowThreshold) / (minMaxNonlinearAboveThreshold - minMaxNonlinearBelowThreshold);
            double adjusted = NonlinearCutoff(plaintiff, true, isMin) + proportionOfWayBetweenCutoffs * (NonlinearCutoff(plaintiff, false, isMin) - NonlinearCutoff(plaintiff, true, isMin));
            return adjusted;
        }

        public double[,] PUtilities;
        public double[,] DUtilities;
        public double[,] TrialRate;
        public double[,] AccuracyHypoSq;
        public double[,] AccuracySq;
        public double[,] AccuracyForP;
        public double[,] AccuracyForD;

        void ResetUtilities()
        {
            PUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            DUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            TrialRate = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyHypoSq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracySq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyForP = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyForD = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        }

        // Outcomes where trial is inevitable is a strategy for both parties. We treat this case separately and compare the outside option at the end.
        (double pOutsideOption, double dOutsideOption)? OutsideOption;

        double[] continuousSignals = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => ContinuousSignal(x)).ToArray();

        void CalculateUtilities()
        {
            OutsideOption = null;
            var offerRanges = Enumerable.Range(0, NumStrategiesPerPlayer).Select(x => (ConvertStrategyToMinMaxContinuousOffers(x, true), ConvertStrategyToMinMaxContinuousOffers(x, false))).ToArray();
            for (int p = 0; p < NumStrategiesPerPlayer; p++)
            {
                Parallel.For(0, NumStrategiesPerPlayer, d =>
                {
                    var pOfferRange = offerRanges[p].Item1;
                    var dOfferRange = offerRanges[d].Item2;
                    bool atLeastOneSettlement;
                    double pUtility, dUtility, trialRate, accuracySq, accuracyHypoSq, accuracyForP, accuracyForD;
                    CalculateResultsForOfferRanges(pOfferRange, dOfferRange, out atLeastOneSettlement, out pUtility, out dUtility, out trialRate, out accuracySq, out accuracyHypoSq, out accuracyForP, out accuracyForD);

                    PUtilities[p, d] = pUtility;
                    DUtilities[p, d] = dUtility;
                    if (!atLeastOneSettlement)
                    {
                        if (OutsideOption == null)
                            OutsideOption = (PUtilities[p, d], DUtilities[p, d]);
                        PUtilities[p, d] = DUtilities[p, d] = -1E+20; // very unattractive utility
                    }


                    TrialRate[p, d] = trialRate;
                    AccuracySq[p, d] = accuracySq;
                    AccuracyHypoSq[p, d] = accuracyHypoSq;
                    AccuracyForP[p, d] = accuracyForP;
                    AccuracyForD[p, d] = accuracyForD;

                });
            }
        }
        private (double pUtility, double dUtility) CalculateUtilitiesForOfferRanges((double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange)
        {
            double pUtility, dUtility;
            CalculateResultsForOfferRanges(pOfferRange, dOfferRange, out _, out pUtility, out dUtility, out _, out _, out _, out _, out _);
            return (pUtility, dUtility);
        }

        private void CalculateResultsForOfferRanges((double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange, out bool atLeastOneSettlement, out double pUtility, out double dUtility, out double trialRate, out double accuracySq, out double accuracyHypoSq, out double accuracyForP, out double accuracyForD)
        {
            double[] pOffers = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => pOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + pOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
            double[] dOffers = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => dOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + dOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
            // Truncations (as specified in paper)
            // 1. Plaintiff never demands less than lowest offer by the defendant.
            for (int i = 0; i < pOffers.Length; i++)
                if (pOffers[i] < dOfferRange.minSignalStrategy)
                    pOffers[i] = dOfferRange.minSignalStrategy;
            // 2. Defendant never offers more than highest demand by the plaintiff. 
            for (int i = 0; i < dOffers.Length; i++)
                if (dOffers[i] > pOfferRange.maxSignalStrategy)
                    dOffers[i] = pOfferRange.maxSignalStrategy;


            atLeastOneSettlement = false;
            pUtility = 0;
            dUtility = 0;
            trialRate = 0;
            accuracySq = 0;
            accuracyHypoSq = 0;
            accuracyForP = 0;
            accuracyForD = 0;
            for (int zp = 0; zp < NumNormalizedSignalsPerPlayer; zp++)
            {
                for (int zd = 0; zd < NumNormalizedSignalsPerPlayer; zd++)
                {
                    ProcessCaseGivenParticularSignals(zp, zd, continuousSignals, pOffers, dOffers, ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, ref accuracySq, ref accuracyHypoSq, ref accuracyForP, ref accuracyForD);
                }
            }
            pUtility /= (double)EvaluationsPerStrategyCombination;
            dUtility /= (double)EvaluationsPerStrategyCombination;
            trialRate /= (double)EvaluationsPerStrategyCombination;
            accuracySq /= (double)EvaluationsPerStrategyCombination;
            accuracyHypoSq /= (double)EvaluationsPerStrategyCombination;
            accuracyForP /= (double)EvaluationsPerStrategyCombination;
            accuracyForD /= (double)EvaluationsPerStrategyCombination;
        }

        private void ProcessCaseGivenParticularSignals(int zp, int zd, double[] continuousSignals, double[] pOffers, double[] dOffers, ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, ref double accuracySq, ref double accuracyHypoSq, ref double accuracyForP, ref double accuracyForD)
        {
            double pOffer = pOffers[zp];
            double dOffer = dOffers[zd];
            double theta_p = continuousSignals[zp] * q;
            double theta_d = q + continuousSignals[zd] * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
            ProcessCaseGivenOfferValues(ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, ref accuracySq, ref accuracyHypoSq, ref accuracyForP, ref accuracyForD, pOffer, dOffer, theta_p, theta_d);
        }

        private void ProcessCaseGivenOfferValues(ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, ref double accuracySq, ref double accuracyHypoSq, ref double accuracyForP, ref double accuracyForD, double pOffer, double dOffer, double theta_p, double theta_d)
        {
            double hypo_theta_p = 0.5 * q; // the theta expected before parties collect information
            double hypo_theta_d = q + 0.5 * (1 - q); // the theta expected before parties collect information
            double j = (theta_p + theta_d) / 2.0;
            double hypo_j = (hypo_theta_p + hypo_theta_d) / 2.0;
            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
            bool treatEqualityAsEquallyLikelyToProduceSettlementOrTrial = true; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
            double equalityMultiplier = treatEqualityAsEquallyLikelyToProduceSettlementOrTrial && equality ? 0.5 : 1.0;
            bool dGreater = dOffer > pOffer || equality; // always count equality with the settlement
            if (dGreater || equality)
            {
                if (equalityMultiplier != 0.5)
                    atLeastOneSettlement = true;
                double settlement = (pOffer + dOffer) / 2.0;
                double pEffect = settlement;
                double dEffect = 1.0 - settlement;
                //Debug.WriteLine($"({p},{d}) settle ({zp},{zd}) => {pEffect}, {dEffect} "); 
                pUtility += equalityMultiplier * pEffect;
                dUtility += equalityMultiplier * (1.0 - pEffect);
                double v = equalityMultiplier * (pEffect - q) * (pEffect - q);
                accuracySq += v;
                accuracyHypoSq += v;
                accuracyForP += equalityMultiplier * Math.Abs(pEffect - j);
                accuracyForD += equalityMultiplier * Math.Abs(dEffect - (1 - j));
            }
            bool dLess = (dOffer < pOffer && !equality) || (equality && treatEqualityAsEquallyLikelyToProduceSettlementOrTrial);
            if (dLess)
            {
                // trial
                double oneMinusTheta_d = 1.0 - theta_d;
                double dPortionOfCosts = true switch
                {
                    _ when theta_p < oneMinusTheta_d && (theta_d < t) => 0,
                    _ when Math.Abs(theta_p - oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (theta_d >= t && theta_p <= t) => 0.5,
                    _ => 1.0
                };
                double hypo_oneMinusTheta_d = 1.0 - hypo_theta_d;
                double hypo_dPortionOfCosts = true switch
                {
                    _ when hypo_theta_p < hypo_oneMinusTheta_d && (hypo_theta_d < t) => 0,
                    _ when Math.Abs(hypo_theta_p - hypo_oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (hypo_theta_d >= t && hypo_theta_p <= t) => 0.5,
                    _ => 1.0
                };
                double dCosts = dPortionOfCosts * c;
                double pCosts = c - dCosts;

                double pEffect = (j - pCosts);
                double hypo_pEffect = (hypo_j - pCosts);
                double dEffect = ((1.0 - j) - dCosts);
                pUtility += equalityMultiplier * pEffect;
                dUtility += equalityMultiplier * dEffect;

                trialRate += equalityMultiplier;
                double accuracyUnsquared = pEffect + 0.5 * c; // the idea here is that the party's own costs are considered relevant to accuracy. Because P paid 0.5 * c out of pocket and this was counted in pEffect, we add this back in. Note that if shifting to defendant has occurred, that means that we have that accuracyUnsquared == j + 0.5*C, with the latter part representing the fee shifting penalty imposed on the defendant.
                double hypo_accuracyUnsquared = hypo_pEffect + 0.5 * c;
                accuracySq += equalityMultiplier * accuracyUnsquared * accuracyUnsquared;
                accuracyHypoSq += equalityMultiplier * hypo_accuracyUnsquared * hypo_accuracyUnsquared;
                accuracyForP += equalityMultiplier * Math.Abs(pEffect - j);
                accuracyForD += equalityMultiplier * Math.Abs(dEffect - (1 - j));
                //Debug.WriteLine($"({p},{d}) trial ({zp},{zd}) => {pEffect}, {dEffect} [based on {theta_p}, {theta_d}");
            }
        }
    }
}

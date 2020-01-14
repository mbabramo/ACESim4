using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{
    public partial class DMSApproximator
    {
           
        double q, c, t;
        double? riskOrRegretAverseP, riskOrRegretAverseD;
        bool useRegretAversion;
        double finalOfferRule; // if not null, then the judgment is compared to the offers. We calculate (POffer - J) (i.e., plaintiff's aggressiveness) and (J - DOffer) (defendant's aggressiveness). Then, we multiply this parameter by the difference. So P pays to D an amount equal to finalOfferRule * ((POffer - J) - (J - DOffer)) = finalOfferRule * (POffer + DOffer - 2J). E.g., if POffer = 50, J = 45, and DOffer = 30, then P was less aggressive, and plaintiff pays -10*finalOfferRule to D, i.e. receives 10 from D. Note that the more plaintiff insists on, the more plaintiff will have to pay to defendant, and the less defendant insists on, the less plaintiff will have to pay to defendant, so this provides each party incentives to modify its behavior.
        bool useFriedmanWittman; // instead of using the full DMS model, we use the original Friedman Wittman model, in which the judgment is the sum of the signals. In effect, we just set theta to the signal, instead of to a function of the signal and q. Note that fee shifting is still possible.
        TruncationOptions truncationOptions;

        public DMSApproximator(double q, double c, double t, double? riskOrRegretAverseP, double? riskOrRegretAverseD, bool useRegretAversion, double? finalOfferRule, bool useFriedmanWittman, TruncationOptions truncationOptions, bool execute)
        {
            this.q = q; // irrelevant with friedman wittman
            this.c = c;
            this.t = t;
            this.riskOrRegretAverseP = riskOrRegretAverseP;
            this.riskOrRegretAverseD = riskOrRegretAverseD;
            this.useRegretAversion = useRegretAversion;
            this.finalOfferRule = finalOfferRule ?? 0;
            this.useFriedmanWittman = useFriedmanWittman;
            this.truncationOptions = truncationOptions;
            TabbedText.WriteLine($"Cost {c} threshold {(useFriedmanWittman ? "N/A" : t.ToString())} riskAverseP {riskOrRegretAverseP} riskAverseD {riskOrRegretAverseD} quality {(useFriedmanWittman ? "N/A" : q.ToString())} finalOfferRule {finalOfferRule}");
            TabbedText.WriteLine(OptionsString);
            if (execute)    
                Execute();
        }

        #region Options

        public bool OutsideOptionIsPossible => finalOfferRule == 0; // with the final offer rule, every strategy leaving always to trial leads to different utilities.

        // Each player's strategy is a line, represented by an initial point on either the x or y axis and by an angle in [0, pi/2). 
        public const int NumEndpointOptions = 50; // DEBUG -- 50
        public const int NumAngleOptions = 50;
        public const int NumSignalsPerPlayer = 100; 
        public const int NumStrategiesPerPlayer = NumEndpointOptions * NumAngleOptions;
        public long NumRequiredGamePlays => Pow(NumEndpointOptions, 4) * Pow(NumSignalsPerPlayer, 2);
        public string OptionsString => $"Signals per player: {NumSignalsPerPlayer} Endpoint options: {NumEndpointOptions} Angle options: {NumAngleOptions} => Required game plays {NumRequiredGamePlays:n0}";
        private static long Pow(int bas, int exp) => Enumerable.Repeat((long)bas, exp).Aggregate((long)1, (a, b) => a * b);

        bool LogDetailedProgress = true;
        bool PrintUtilitiesInDetailedLog = false;

        #endregion


        #region Execution

        int OptimalPStrategy = -1, OptimalDStrategy = -1;
        public DMSApproximatorOutcome TheOutcome => new DMSApproximatorOutcome(PUtilities[OptimalPStrategy, OptimalDStrategy], DUtilities[OptimalPStrategy, OptimalDStrategy], TrialRate[OptimalPStrategy, OptimalDStrategy], AccuracySq[OptimalPStrategy, OptimalDStrategy], AccuracyHypoSq[OptimalPStrategy, OptimalDStrategy], AccuracyForP[OptimalPStrategy, OptimalDStrategy], AccuracyForD[OptimalPStrategy, OptimalDStrategy], ConvertStrategyToMinMaxContinuousOffers(OptimalPStrategy, true), ConvertStrategyToMinMaxContinuousOffers(OptimalDStrategy, false), finalOfferRule != 0);

        bool OutsideOptionDominates;

        int Cycle;

        private void Execute()
        {
            InitializeStrategyConversions();
            Stopwatch s = new Stopwatch();
            s.Start();
            bool done = false;
            Cycle = 0;
            while (!done)
            {
                if (LogDetailedProgress)
                    TabbedText.WriteLine($"Cycle {++Cycle}");
                if (LogDetailedProgress)
                    TabbedText.WriteLine($"25%le to 75%le ranges: P ({NonlinearCutoffString(true, true, true)} to {NonlinearCutoffString(true, false, true)},{NonlinearCutoffString(true, true, false)} to {NonlinearCutoffString(true, false, false)}) D ({NonlinearCutoffString(false, true, true)} to {NonlinearCutoffString(false, false, true)},{NonlinearCutoffString(false, true, false)} to {NonlinearCutoffString(false, false, false)}) ");
                ResetUtilities();
                RecordUtilities();
                var eqResult = EvaluateEquilibria();
                if (LogDetailedProgress && PrintUtilitiesInDetailedLog)
                    PrintUtilities();

                AdjustLowerAndUpperCutoffsBasedOnPerformance();
                done = AllCutoffDistancesAccurateEnough(ZoomAccuracy) || Cycle == MaxZoomCycles;
                if (done && OutsideOptionDominates)
                    SelectOutsideOption();
                if (done || LogDetailedProgress)
                    TabbedText.WriteLine(eqResult);
            }
            s.Stop();
            TabbedText.WriteLine($"Execution time (s): {s.Elapsed.TotalSeconds} (Num required game plays: {NumRequiredGamePlays})");
        }

        HashSet<(int p, int d)> AllEquilibria;

        private string EvaluateEquilibria()
        {
            StringBuilder b = new StringBuilder();
            List<(int pStrategy, int dStrategy)> nashStrategies = PureStrategiesFinder.ComputeNashEquilibria(PUtilities, DUtilities, true);
            AllEquilibria = nashStrategies.ToHashSet();

            if (!AllEquilibria.Any())
            {
                (OptimalPStrategy, OptimalDStrategy) = PureStrategiesFinder.GetApproximateNashEquilibrium(PUtilities, DUtilities);
                b.AppendLine($"No pure equilibria; using approximate equilibrium {OptimalPStrategy},{OptimalDStrategy} => ({PUtilities[OptimalPStrategy, OptimalDStrategy]},{DUtilities[OptimalPStrategy, OptimalDStrategy]})");
                RecordUtilities(true, false, OptimalPStrategy, OptimalDStrategy); // calculate more advanced stats
            }
            else if (AllEquilibria.Count() > 1)
            {
                if (AllEquilibria.All(x => PUtilities[x.p, x.d] == VeryBadUtility && DUtilities[x.p, x.d] == VeryBadUtility))
                    AllEquilibria = AllEquilibria.Take(2).ToHashSet(); // don't need to list all
                // list remaining equilibria
                var grouped = AllEquilibria.GroupBy(x => x.p);
                int numPrinted = 0;
                foreach (var pStrategyGroup in grouped)
                {
                    var firstNashStrategyInGroup = pStrategyGroup.First();
                    var pLine = ConvertStrategyToMinMaxContinuousOffers(firstNashStrategyInGroup.p, true);
                    b.AppendLine($"P (strategy {firstNashStrategyInGroup.p}) offers from {pLine.minSignalStrategy.ToSignificantFigures(3)} to {pLine.maxSignalStrategy.ToSignificantFigures(3)} (utility {PUtilities[firstNashStrategyInGroup.p, firstNashStrategyInGroup.d].ToSignificantFigures(3)})");
                    foreach (var nashStrategy in pStrategyGroup)
                    {
                        var dLine = ConvertStrategyToMinMaxContinuousOffers(nashStrategy.d, false);
                        b.AppendLine($"D (strategy {nashStrategy.d}) offers from {dLine.minSignalStrategy.ToSignificantFigures(3)} to {dLine.maxSignalStrategy.ToSignificantFigures(3)} (utility {DUtilities[nashStrategy.p, nashStrategy.d].ToSignificantFigures(3)})");

                        RecordUtilities(true, false, nashStrategy.p, nashStrategy.d); // calculate more advanced stats
                        b.AppendLine($"--> Trial rate {TrialRate[nashStrategy.p, nashStrategy.d].ToSignificantFigures(3)}");
                        if (numPrinted++ > 25)
                        {
                            b.AppendLine($"Additional equilibria omitted. Total number of equilibria = {AllEquilibria.Count()}");
                            goto abortedPrinting; // acceptable use of goto to break out of two for loops.
                        }
                    }
                }
            abortedPrinting:
                // pick the one with the best approximate equilibrium score
                var allList = AllEquilibria.ToList();
                //var distances = allList.Select(x => PureStrategiesFinder.DistanceFromNash_SingleStrategy(x.p, x.d, PUtilities, DUtilities)).ToList();
                //TabbedText.WriteLine($"Distances from equilibrium: {String.Join(",", distances)}");
                //var orderedByDistance = distances.Select((distance, index) => (distance, index)).OrderBy(x => x.distance).ToArray();
                //var lowestDistance = orderedByDistance.First().distance;
                //var distanceMatches = orderedByDistance.Count(x => x.distance == lowestDistance);
                //bool alwaysChooseBasedOnUtilityDistance = true; 
                //if (distanceMatches > 1 || alwaysChooseBasedOnUtilityDistance)

                // still too many (probably zero distance, i.e. perfect equilibria).
                // pick the one with the lowest distance between parties' utilities (arbitrary)
                var utilityDistances = allList.Select(x => Math.Pow(PUtilities[x.p, x.d] - DUtilities[x.p, x.d], 2.0)).ToList();
                var orderedByDistance = utilityDistances.Select((distance, index) => (distance, index)).OrderBy(x => x.distance).ToArray();

                var bestEquilibrium = allList[orderedByDistance.First().index];
                OptimalPStrategy = bestEquilibrium.p;
                OptimalDStrategy = bestEquilibrium.d;
                RecordUtilities(true, false, OptimalPStrategy, OptimalDStrategy); // calculate more advanced stats
                b.AppendLine($"Strategy with lowest approximate equilibrium distance: {OptimalPStrategy},{OptimalDStrategy} => ({PUtilities[OptimalPStrategy, OptimalDStrategy]},{DUtilities[OptimalPStrategy, OptimalDStrategy]}) offer ranges: P: {offerRanges[OptimalPStrategy].Item1}; D: {offerRanges[OptimalDStrategy].Item2} trial rate: {TrialRate[OptimalPStrategy, OptimalDStrategy]}");
            }
            else
            {
                // exactly 1 equilibrium;
                var f1 = AllEquilibria.First();
                OptimalPStrategy = f1.p;
                OptimalDStrategy = f1.d;
                b.AppendLine($"Exactly one equilibrium: {OptimalPStrategy},{OptimalDStrategy} => ({PUtilities[OptimalPStrategy, OptimalDStrategy]},{DUtilities[OptimalPStrategy, OptimalDStrategy]}) offer ranges: P: {offerRanges[OptimalPStrategy].Item1}; D: {offerRanges[OptimalDStrategy].Item2} trial rate: {TrialRate[OptimalPStrategy, OptimalDStrategy]}");
                RecordUtilities(true, false, OptimalPStrategy, OptimalDStrategy); // calculate more advanced stats
            }

            OutsideOptionDominates = OutsideOption != null && (PUtilities[OptimalPStrategy, OptimalDStrategy] < OutsideOption?.pOutsideOption || DUtilities[OptimalPStrategy, OptimalDStrategy] < OutsideOption?.dOutsideOption);
            if (OutsideOptionDominates)
            {
                RecordUtilities(true, false, OutsideOptionExampleIndex.Value.p, OutsideOptionExampleIndex.Value.d);
                b.AppendLine($"Choosing outside option of trial => ({OutsideOption.Value.pOutsideOption.ToSignificantFigures(3)}, {OutsideOption.Value.dOutsideOption.ToSignificantFigures(3)})");
            }

            return b.ToString();
        }

        private void SelectOutsideOption()
        {
            OptimalPStrategy = OutsideOptionExampleIndex.Value.p;
            OptimalDStrategy = OutsideOptionExampleIndex.Value.d;
            PUtilities[OptimalPStrategy, OptimalDStrategy] = OutsideOption.Value.pOutsideOption;
            DUtilities[OptimalPStrategy, OptimalDStrategy] = OutsideOption.Value.dOutsideOption;
            RecordUtilities(true, false, OptimalPStrategy, OptimalDStrategy);
            //TabbedText.WriteLine($"Trial equilibrium {OptimalPStrategy},{OptimalDStrategy} => ({PUtilities[OptimalPStrategy, OptimalDStrategy]},{DUtilities[OptimalPStrategy, OptimalDStrategy]})");
        }

        #endregion

        #region Strategy-offer conversions

        const double EvaluationsPerStrategyCombination = NumSignalsPerPlayer * NumSignalsPerPlayer;

        (double minSignalStrategy, double maxSignalStrategy)[] pStrategyLines;
        (double minSignalStrategy, double maxSignalStrategy)[] dStrategyLines;

        void InitializeStrategyConversions()
        {
            pStrategyLines = new (double minSignalStrategy, double maxSignalStrategy)[NumStrategiesPerPlayer];
            for (int i = 0; i < NumStrategiesPerPlayer; i++)
                pStrategyLines[i] = ConvertStrategyToMinMaxContinuousOffers_Compute(i);
            // This will produce a very similar set. But this allows us to make the game entirely symmetrical.
            dStrategyLines = new (double minSignalStrategy, double maxSignalStrategy)[NumStrategiesPerPlayer];
            for (int i = 0; i < NumStrategiesPerPlayer; i++)
                dStrategyLines[i] = (1.0 - pStrategyLines[i].maxSignalStrategy, 1.0 - pStrategyLines[i].minSignalStrategy);
        }

        (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy, bool plaintiff)
        {
            // note: Plaintiff parameter currently ignored (we have same line options for both players)
            return plaintiff ? pStrategyLines[strategy] : dStrategyLines[strategy];
        }

        (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers_Compute(int strategy)
        {
            int startingPositionIndex = strategy / NumEndpointOptions;
            double initialValue = ((double)startingPositionIndex) / ((double)(NumEndpointOptions - 1));
            bool startingPositionOnXAxis = initialValue > 0.5; // note that this means that the origin will be considered a y axis point, and that the x axis will not also have an origin
            double startingPointValue;
            if (startingPositionOnXAxis)
            {
                startingPointValue = initialValue * 2 - 1;
            }
            else
            {
                startingPointValue = initialValue * 2;
            }

            double angleIndex = (double) (strategy - startingPositionIndex * NumEndpointOptions); // will range from 0 to NumAngleOptions - 1.
            double angle;
            if (startingPositionOnXAxis)
                angle = (Math.PI / 2) * (angleIndex + 1) / (NumAngleOptions + 1); // avoid angle of 0, since that will be redundant with origin (considered on y axis). Also, avoid pi / 2. So, if there are 10 points on the x axis and 15 angle options, we will go from 1/16 to 15/16, all multiplied by pi/2. 
            else
                angle = (Math.PI / 2) * (angleIndex) / (NumAngleOptions); // here, we want to include an angle of 0, so we would go from 0/15 of pi/2 to 14/15 of pi/2. with 15 angle options.
            double tangent = Math.Tan(angle);

            double leftAxisIntercept, rightAxisIntercept;
            if (startingPositionOnXAxis)
            {
                leftAxisIntercept = 0 - startingPointValue * tangent;
                rightAxisIntercept = (1.0 - startingPointValue) * tangent;
            }
            else
            {
                leftAxisIntercept = startingPointValue;
                rightAxisIntercept = startingPointValue + tangent;
            }

            return (leftAxisIntercept, rightAxisIntercept);
        }

        static double GetUnmappedOfferValue(int discreteOffer)
        {
            return (double)(discreteOffer + 0.5) / ((double)(NumEndpointOptions));
        }

        

        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumSignalsPerPlayer + 1));

        // NOTE: The following may require updating if we reimplement zooming in.
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

        #endregion

        #region Zoom

        // Zoom in feature: We can choose to what value the 25th percentile and the 75th of what each player's strategy corresponds to. Initially, we start at 0.25 and 0.75.
        // Then we zoom in until we get to a required level of accuracy.
        // For each number that we're trying to zoom into, we have a mean value and then a range that takes us from the 25%le to the 75%le
        // (or whatever the percentiles are above). At each zoom in cycle, we center the cutoffs around what we now think is the optimal value.
        // When the number we are targeting was within the 25%le to the 75%le range, we multiply the total distance by (1 - zoomSpeed). 
        // When the number is outside the range, we multiply by (1 + zoomSpeed).
        const double ZoomAccuracy = 0.001;
        const double ZoomSpeed = 0.05;
        const int MaxZoomCycles = 1;

        void AdjustLowerAndUpperCutoffsBasedOnPerformance()
        {
            var pOptimum = ConvertStrategyToMinMaxContinuousOffers(OptimalPStrategy, true);
            (pValueAtLowerNonlinearCutoff_MinSignal, pValueAtUpperNonlinearCutoff_MinSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(pOptimum.minSignalStrategy, true, true);
            (pValueAtLowerNonlinearCutoff_MaxSignal, pValueAtUpperNonlinearCutoff_MaxSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(pOptimum.maxSignalStrategy, true, false);
            var dOptimum = ConvertStrategyToMinMaxContinuousOffers(OptimalDStrategy, false);
            (dValueAtLowerNonlinearCutoff_MinSignal, dValueAtUpperNonlinearCutoff_MinSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(dOptimum.minSignalStrategy, false, true);
            (dValueAtLowerNonlinearCutoff_MaxSignal, dValueAtUpperNonlinearCutoff_MaxSignal) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(dOptimum.maxSignalStrategy, false, false);
        }

        (double, double) AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(double apparentOptimum, bool plaintiff, bool isMinSignal)
        {
            double lowerCutoff = NonlinearCutoff(plaintiff, true, isMinSignal);
            double upperCutoff = NonlinearCutoff(plaintiff, false, isMinSignal);
            double currentDistance = upperCutoff - lowerCutoff;
            double revisedDistance;
            if (lowerCutoff < apparentOptimum && apparentOptimum < upperCutoff)
                revisedDistance = currentDistance * (1 - ZoomSpeed);
            else
                revisedDistance = currentDistance * (1 + ZoomSpeed);
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

        #endregion

        #region Utilities and outcomes

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
        (int p, int d)? OutsideOptionExampleIndex;

        double[] continuousSignals = Enumerable.Range(0, NumSignalsPerPlayer).Select(x => ContinuousSignal(x)).ToArray();

        const double VeryBadUtility = -1E+20;

        ((double minSignalStrategy, double maxSignalStrategy), (double minSignalStrategy, double maxSignalStrategy))[] offerRanges;
        void RecordUtilities()
        {
            OutsideOption = null;
            OutsideOptionExampleIndex = null;

            offerRanges = Enumerable.Range(0, NumStrategiesPerPlayer).Select(x => (ConvertStrategyToMinMaxContinuousOffers(x, true), ConvertStrategyToMinMaxContinuousOffers(x, false))).ToArray();
            for (int p = 0; p < NumStrategiesPerPlayer; p++)
            {
                bool doParallel = true;
                Parallelizer.Go(doParallel, 0, NumStrategiesPerPlayer, d =>
                {
                    RecordUtilities(false, true, p, d);

                });
            }
        }

        private void RecordUtilities(bool calculatePerformanceStats, bool setOutsideOptionUtilityToVeryBadValue, int p, int d)
        {
            var pOfferRange = offerRanges[p].Item1;
            var dOfferRange = offerRanges[d].Item2;
            bool atLeastOneSettlement;
            double pUtility, dUtility, trialRate = 0, accuracySq = 0, accuracyHypoSq = 0, accuracyForP = 0, accuracyForD = 0;
            if (calculatePerformanceStats)
                CalculateResultsForOfferRanges(true, pOfferRange, dOfferRange, out atLeastOneSettlement, out pUtility, out dUtility, out trialRate, out accuracySq, out accuracyHypoSq, out accuracyForP, out accuracyForD);
            else
                CalculateResultsForOfferRanges(pOfferRange, dOfferRange, out atLeastOneSettlement, out pUtility, out dUtility, out trialRate);

            PUtilities[p, d] = pUtility;
            DUtilities[p, d] = dUtility;
            if (!atLeastOneSettlement && OutsideOptionIsPossible && Math.Abs(1.0 - trialRate) < 0.00001) // we might have no pure settlements but trials only half of time
                SetOutsideOption(p, d, setOutsideOptionUtilityToVeryBadValue);
            if (calculatePerformanceStats)
            {
                TrialRate[p, d] = trialRate;
                AccuracySq[p, d] = accuracySq;
                AccuracyHypoSq[p, d] = accuracyHypoSq;
                AccuracyForP[p, d] = accuracyForP;
                AccuracyForD[p, d] = accuracyForD;
            }
        }

        object outsideOptionObj = new object();
        private void SetOutsideOption(int p, int d, bool setUtilityToVeryBadOption)
        {
            if (OutsideOption == null)
            {
                lock (outsideOptionObj)
                {
                    if (OutsideOption == null)
                    {
                        OutsideOption = (PUtilities[p, d], DUtilities[p, d]);
                        OutsideOptionExampleIndex = (p, d);
                    }
                }
            }
            if (setUtilityToVeryBadOption)
                PUtilities[p, d] = DUtilities[p, d] = VeryBadUtility; // very unattractive utility
        }

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

        #endregion

        #region Calculate with pair of P and D strategies

        private (double pUtility, double dUtility) CalculateUtilitiesForOfferRanges((double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange)
        {
            double pUtility, dUtility;
            CalculateResultsForOfferRanges(false, pOfferRange, dOfferRange, out _, out pUtility, out dUtility, out _, out _, out _, out _, out _);
            return (pUtility, dUtility);
        }

        private void CalculateResultsForOfferRanges((double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange, out bool atLeastOneSettlement, out double pUtility, out double dUtility, out double trialRate) => CalculateResultsForOfferRanges(false, pOfferRange, dOfferRange, out atLeastOneSettlement, out pUtility, out dUtility, out trialRate, out _, out _, out _, out _);

        public void CalculateResultsForOfferRanges(bool calculatePerformanceStats, (double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange, out bool atLeastOneSettlement, out double pUtility, out double dUtility, out double trialRate, out double accuracySq, out double accuracyHypoSq, out double accuracyForP, out double accuracyForD)
        {
            double[] pOffers, dOffers;
            CalculateOffers(pOfferRange, dOfferRange, out pOffers, out dOffers);

            atLeastOneSettlement = false;
            pUtility = 0;
            dUtility = 0;
            trialRate = 0;
            accuracySq = 0;
            accuracyHypoSq = 0;
            accuracyForP = 0;
            accuracyForD = 0;
            for (int zp = 0; zp < NumSignalsPerPlayer; zp++)
            {
                double pOffer = pOffers[zp];
                double theta_p = useFriedmanWittman ? continuousSignals[zp] : continuousSignals[zp] * q;
                for (int zd = 0; zd < NumSignalsPerPlayer; zd++)
                {
                    double dOffer = dOffers[zd];
                    double theta_d = useFriedmanWittman ? continuousSignals[zd] : q + continuousSignals[zd] * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
                    double pUtilitySingleCase = 0;
                    double dUtilitySingleCase = 0;
                    double trialRateSingleCase = 0;
                    if (calculatePerformanceStats)
                    {
                        double accuracySqSingleCase = 0;
                        double accuracyHypoSqSingleCase = 0;
                        double accuracyForPSingleCase = 0;
                        double accuracyForDSingleCase = 0;
                        ProcessCaseGivenParticularSignals(pOffer, dOffer, theta_p, theta_d, ref atLeastOneSettlement, ref pUtilitySingleCase, ref dUtilitySingleCase, ref trialRateSingleCase, ref accuracySqSingleCase, ref accuracyHypoSqSingleCase, ref accuracyForPSingleCase, ref accuracyForDSingleCase);
                        accuracySq += accuracySqSingleCase;
                        accuracyHypoSq += accuracyHypoSqSingleCase;
                        accuracyForP += accuracyForPSingleCase;
                        accuracyForD += accuracyForDSingleCase;
                    }
                    else
                    {
                        ProcessCaseGivenParticularSignals(pOffer, dOffer, theta_p, theta_d, ref atLeastOneSettlement, ref pUtilitySingleCase, ref dUtilitySingleCase, ref trialRateSingleCase); // faster version
                    }
                    trialRate += trialRateSingleCase;
                    pUtility += pUtilitySingleCase;
                    dUtility += dUtilitySingleCase;
                }
            }
            pUtility /= (double)EvaluationsPerStrategyCombination;
            dUtility /= (double)EvaluationsPerStrategyCombination;
            trialRate /= (double)EvaluationsPerStrategyCombination;
            if (calculatePerformanceStats)
            {
                accuracySq /= (double)EvaluationsPerStrategyCombination;
                accuracyHypoSq /= (double)EvaluationsPerStrategyCombination;
                accuracyForP /= (double)EvaluationsPerStrategyCombination;
                accuracyForD /= (double)EvaluationsPerStrategyCombination;
            }
        }

        public void GetOffersForOptimalStrategies(out double[] pOffers, out double[] dOffers)
        {
            if (OutsideOptionDominates && finalOfferRule == 0)
                CalculateOffers((1.0, 1.0), (0.0, 0.0), out pOffers, out dOffers);
            else
                CalculateOffers(offerRanges[OptimalPStrategy].Item1, offerRanges[OptimalDStrategy].Item2, out pOffers, out dOffers);
        }

        public ((double, double), (double, double)) GetOptimalOfferRanges() => (offerRanges[OptimalPStrategy].Item1, offerRanges[OptimalDStrategy].Item2);

        public (double pOffer, double dOffer) GetOffersForSignalWithOptimalStrategies(int signalIndex)
        {
            GetOffersForOptimalStrategies(out double[] pOffers, out double[] dOffers);
            return (pOffers[signalIndex], dOffers[signalIndex]);
        }

        public (double pOffer, double dOffer) GetOffersForSignalWithFriedmanWittmanStrategies(int signalIndex)
        {
            double pMin = -2.0 * c + 0.5;
            double pMax = pMin + (2.0 / 3.0);
            double dMin = 2.0 * c - 1.0 / 6.0;
            double dMax = dMin + (2.0 / 3.0);
            CalculateOffers((pMin, pMax), (dMin, dMax), out double[] pOffers, out double[] dOffers);
            return (pOffers[signalIndex], dOffers[signalIndex]);
        }

        public void CalculateOffers((double minSignalStrategy, double maxSignalStrategy) pOfferRange, (double minSignalStrategy, double maxSignalStrategy) dOfferRange, out double[] pOffers, out double[] dOffers)
        {
            pOffers = Enumerable.Range(0, NumSignalsPerPlayer).Select(x => pOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + pOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
            dOffers = Enumerable.Range(0, NumSignalsPerPlayer).Select(x => dOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + dOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
            // Truncations (as specified in paper). 

            if (truncationOptions == TruncationOptions.None)
                return;

            if (truncationOptions == TruncationOptions.Endogenous)
                throw new NotImplementedException(); // replace with endogenous

            // 1. IMPORTANT NOTE: The DMS paper doesn't explain what to do if the plaintiff's highest offer is lower than the defendant's lowest offer, as would be the case when costs are very high and both parties are eager to avoid litigation at all costs. In Fig. A.2 (right panel), it is implicitly assumed that c < 1/3, because otherwise, the bottom horizontal line would be on top of the top horizontal line. That is, the relative positioning of the two truncation lines implies that c < 1/3. But we might have a different cutoff with fee shifting. We will resolve this by truncating all offers to the same midpoint (meaning that every case will settle). 
            if (dOfferRange.minSignalStrategy > pOfferRange.maxSignalStrategy)
            {
                double midpoint = (dOfferRange.minSignalStrategy + pOfferRange.maxSignalStrategy) / 2.0;
                for (int i = 0; i < pOffers.Length; i++)
                {
                    pOffers[i] = dOffers[i] = midpoint;
                }
            }
            // 1b. Next, we have the problem of trivial equilibria, where P wants more than D is willing to give,
            // no matter what the signal. In this case, we trunate each to the least aggressive strategy, if 
            // truncating on both sides
            else if (pOfferRange.minSignalStrategy > dOfferRange.maxSignalStrategy)
            {
                if (truncationOptions == TruncationOptions.Automatic_BothSides)
                {
                    for (int i = 0; i < pOffers.Length; i++)
                    {
                        pOffers[i] = pOfferRange.minSignalStrategy;
                        dOffers[i] = dOfferRange.maxSignalStrategy;
                    }
                }
            }
            else
            {
                // 2. Plaintiff never demands less than lowest offer by the defendant.
                for (int i = 0; i < pOffers.Length; i++)
                    if (pOffers[i] < dOfferRange.minSignalStrategy)
                        pOffers[i] = Math.Min(1, dOfferRange.minSignalStrategy);
                //// 2b. Plaintiff never demands more than the highest offer by the defendant (doesn't affect outcome)
                if (truncationOptions == TruncationOptions.Automatic_BothSides)
                    for (int i = 0; i < pOffers.Length; i++)
                        if (pOffers[i] > dOfferRange.maxSignalStrategy)
                            pOffers[i] = Math.Max(0, dOfferRange.maxSignalStrategy);
                // 3. Defendant never offers more than highest demand by the plaintiff. 
                for (int i = 0; i < dOffers.Length; i++)
                    if (dOffers[i] > pOfferRange.maxSignalStrategy)
                        dOffers[i] = Math.Max(0, pOfferRange.maxSignalStrategy);
                // 3b. Defendant never offers less than lowest demand by the plaintiff (doesn't affect outcome)
                if (truncationOptions == TruncationOptions.Automatic_BothSides)
                    for (int i = 0; i < dOffers.Length; i++)
                        if (dOffers[i] < pOfferRange.minSignalStrategy)
                            dOffers[i] = Math.Min(1, pOfferRange.minSignalStrategy);
            }
        }

        #endregion

        #region Game play with pair of P and D strategies and signals

        // If thetas are not yet calculated, use this. This is not used internally but can be used externally to assess why a particular equilibrium leads to particular results.
        public void ProcessCaseGivenParticularSignals_NormalizedSignals(double pOffer, double dOffer, double q, double zp, double zd, ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, ref double accuracySq, ref double accuracyHypoSq, ref double accuracyForP, ref double accuracyForD)
        {
            double theta_p;
            double theta_d;
            if (useFriedmanWittman)
            {
                theta_p = zp;
                theta_d = zd;
            }
            else
            {
                theta_p = zp * q;
                theta_d = q + zd * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
            }
            ProcessCaseGivenOfferValues(ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, ref accuracySq, ref accuracyHypoSq, ref accuracyForP, ref accuracyForD, pOffer, dOffer, theta_p, theta_d);

            /* The following code should reach the same results as the called code with thetas */
            //double oneMinusQOverQ = (1.0 - q) / q;
            //double tMinusQOver1MinusQ = (t - q) / (1 - q);
            //double oneMinusTOverQ = (1.0 - t) / q;
            //double oneMinusQOverQTimes1MinusZD = oneMinusQOverQ * (1.0 - zd);
            //double dPortionOfCosts = true switch
            //{
            //    _ when zd < oneMinusQOverQTimes1MinusZD && zd < tMinusQOver1MinusQ => 0,
            //    _ when zd == oneMinusQOverQTimes1MinusZD || (zd >= tMinusQOver1MinusQ && zp <= oneMinusTOverQ) => 0.5,
            //    _ when zp > oneMinusQOverQTimes1MinusZD && zp > oneMinusTOverQ => 1.0,
            //    _ => throw new Exception()
            //};
        }

        // first, a method that only gets the essential items when finding equilibria
        private void ProcessCaseGivenParticularSignals(double pOffer, double dOffer, double theta_p, double theta_d, ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate)
        {


            ProcessCaseGivenOfferValues(ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, pOffer, dOffer, theta_p, theta_d);
        }

        private void ProcessCaseGivenParticularSignals(double pOffer, double dOffer, double theta_p, double theta_d, ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, ref double accuracySq, ref double accuracyHypoSq, ref double accuracyForP, ref double accuracyForD)
        {

            /* The following code should reach the same results as the called code with thetas */
            //double oneMinusQOverQ = (1.0 - q) / q;
            //double tMinusQOver1MinusQ = (t - q) / (1 - q);
            //double oneMinusTOverQ = (1.0 - t) / q;
            //double oneMinusQOverQTimes1MinusZD = oneMinusQOverQ * (1.0 - zd);
            //double dPortionOfCosts = true switch
            //{
            //    _ when zd < oneMinusQOverQTimes1MinusZD && zd < tMinusQOver1MinusQ => 0,
            //    _ when zd == oneMinusQOverQTimes1MinusZD || (zd >= tMinusQOver1MinusQ && zp <= oneMinusTOverQ) => 0.5,
            //    _ when zp > oneMinusQOverQTimes1MinusZD && zp > oneMinusTOverQ => 1.0,
            //    _ => throw new Exception()
            //};


            ProcessCaseGivenOfferValues(ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, ref accuracySq, ref accuracyHypoSq, ref accuracyForP, ref accuracyForD, pOffer, dOffer, theta_p, theta_d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetRiskOrRegretAverseUtility(double? riskOrRegretAversion, double effect, double? rejectedOffer = null)
        {
            if (riskOrRegretAversion == null)
                return effect;
            if (useRegretAversion)
            {
                if (rejectedOffer == null)
                    return effect;
                double regret = (double)rejectedOffer - effect;
                if (regret > 0)
                {
                    effect -= (double)riskOrRegretAversion * regret;
                }
                return effect;
            }
            double sum = ((double)riskOrRegretAversion) + effect;
            if (sum <= 0)
                return double.MinValue; // most negative possible value
            else
                return Math.Log(sum);
        }

        private void ProcessCaseGivenOfferValues(ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, double pOffer, double dOffer, double theta_p, double theta_d)
        {
            double j = (theta_p + theta_d) / 2.0;
            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
            bool treatEqualityAsEquallyLikelyToProduceSettlementOrTrial = false; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
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

                double pUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseP, pEffect); 
                double dUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseD, dEffect);
                pUtility += equalityMultiplier * pUtilityThisCase;
                dUtility += equalityMultiplier * dUtilityThisCase;
            }
            bool dLess = (dOffer < pOffer && !equality) || (equality && treatEqualityAsEquallyLikelyToProduceSettlementOrTrial);
            if (dLess)
            {
                // trial
                double oneMinusTheta_d = 1.0 - theta_d;
                double dPortionOfCosts = true switch
                {
                    _ when theta_p < oneMinusTheta_d && (theta_d < t) => 0,
                    _ when Math.Abs(theta_p - oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (theta_d >= t && theta_p <= 1 - t) => 0.5,
                    _ => 1.0
                };
                double dCosts = dPortionOfCosts * c;
                double pCosts = c - dCosts;
                double finalOfferRuleAdjustment = finalOfferRule == 0 ? 0 : finalOfferRule * (pOffer + dOffer - 2 * j);
                double pEffect = (j - pCosts) - finalOfferRuleAdjustment;
                double dEffect = ((1.0 - j) - dCosts) + finalOfferRuleAdjustment;
                double pUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseP, pEffect, dOffer);
                double dUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseD, dEffect, 1.0 - pOffer);
                pUtility += equalityMultiplier * pUtilityThisCase;
                dUtility += equalityMultiplier * dUtilityThisCase;
                trialRate += equalityMultiplier;
            }
        }

        private void ProcessCaseGivenOfferValues(ref bool atLeastOneSettlement, ref double pUtility, ref double dUtility, ref double trialRate, ref double accuracySq, ref double accuracyHypoSq, ref double accuracyForP, ref double accuracyForD, double pOffer, double dOffer, double theta_p, double theta_d)
        {
            double hypo_theta_p = 0.5 * q; // the theta expected before parties collect information
            double hypo_theta_d = q + 0.5 * (1 - q); // the theta expected before parties collect information
            double j = (theta_p + theta_d) / 2.0;
            double hypo_j = (hypo_theta_p + hypo_theta_d) / 2.0;
            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
            bool treatEqualityAsEquallyLikelyToProduceSettlementOrTrial = false; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
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

                // We now calculate the unweighted utility effect of this case, depending on risk aversion
                double pUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseP, pEffect);
                double dUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseD, dEffect);
                // We're aggregating across cases, so we increment the total pUtility measure, adjusting for the equality multiplier.
                pUtility += equalityMultiplier * pUtilityThisCase;
                dUtility += equalityMultiplier * dUtilityThisCase;

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
                    _ when Math.Abs(theta_p - oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (theta_d >= t && theta_p <= 1 - t) => 0.5,
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
                double shiftedCosts = (dPortionOfCosts - 0.5) * c;

                double finalOfferRuleAdjustment = finalOfferRule == 0 ? 0 : finalOfferRule * (pOffer + dOffer - 2 * j);
                double pEffect = (j - pCosts) - finalOfferRuleAdjustment;
                double hypo_pEffect = (hypo_j - pCosts);
                double dEffect = ((1.0 - j) - dCosts) + finalOfferRuleAdjustment;

                // We now calculate the unweighted utility effect of this case, depending on risk aversion
                double pUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseP, pEffect, dOffer);
                double dUtilityThisCase = GetRiskOrRegretAverseUtility(riskOrRegretAverseD, dEffect, 1.0 - pOffer);
                // We're aggregating across cases, so we increment the total pUtility measure, adjusting for the equality multiplier.
                pUtility += equalityMultiplier * pUtilityThisCase;
                dUtility += equalityMultiplier * dUtilityThisCase;

                trialRate += equalityMultiplier;
                double accuracyNotSquared = j + shiftedCosts - q; // == pEffect - q + 0.5 * c // the idea here is that we are trying to assess the accuracy of the judgment ignoring costs as irrelevant, but counting shifted costs as part of the judgment. The party's own costs are considered irrelevant to accuracy. Because P paid 0.5 * c out of pocket and this was deducted in pEffect, we add this back in. Note that if shifting to defendant has occurred, that means that we have that accuracyUnsquared == j + 0.5*C, with the latter part representing the fee shifting penalty imposed on the defendant, so the mechanism is more inaccurate because of what p receives.
                double hypo_accuracyUnsquared = hypo_pEffect + shiftedCosts - q;
                accuracySq += equalityMultiplier * accuracyNotSquared * accuracyNotSquared;
                accuracyHypoSq += equalityMultiplier * hypo_accuracyUnsquared * hypo_accuracyUnsquared;
                accuracyForP += equalityMultiplier * Math.Abs(pEffect - j);
                accuracyForD += equalityMultiplier * Math.Abs(dEffect - (1 - j));
                //Debug.WriteLine($"({p},{d}) trial ({zp},{zd}) => {pEffect}, {dEffect} [based on {theta_p}, {theta_d}");
            }

            #endregion

        }
    }
}

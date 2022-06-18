using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public partial class DMSCalc
    {
        public double T, C, Q;
        int CaseNum;
        Func<double, double> pUntruncFunc;
        Func<double, double> dUntruncFunc;
        public List<(double low, double high)> pPiecewiseLinearRanges, dPiecewiseLinearRanges;

        bool pAboveD, pEntirelyAboveD, dEntirelyAboveP;

        public bool trivial => pEntirelyAboveD || dEntirelyAboveP;

        public DMSCalc(double t, double c, double q)
        {
            this.Q = q;
            if (Q < 1.0 / 3.0 || Q > 2.0 / 3.0)
                throw new ArgumentException("Invalid parameter under Dari-Mattiacci & Saraceno");
            this.C = c;
            this.T = t;
            this.CaseNum = GetCaseNum(T, C, Q);
            (pUntruncFunc, dUntruncFunc) = GetUntruncFuncs();
            pAboveD = PAboveD();
            pEntirelyAboveD = PEntirelyAboveD();
            dEntirelyAboveP = DEntirelyAboveP();
            pPiecewiseLinearRanges = GetPiecewiseLinearRanges(true);
            dPiecewiseLinearRanges = GetPiecewiseLinearRanges(false);
        }

        public override string ToString()
        {
            var correct = GetBids(10, truncated: false);
            string pCorrectString = String.Join(",", correct.Select(x => $"{Math.Round(x.pBid, 2)}"));
            string dCorrectString = String.Join(",", correct.Select(x => $"{Math.Round(x.dBid, 2)}"));
            string combined = $"T: {T}; C: {C}; Q: {Q} \nCorrect (untruncated): P {pCorrectString}; D {dCorrectString}";
            return combined;
        }


        public DMSStrategiesPair GetCorrectStrategiesPair()
        {
            DMSStrategyPretruncation p, d;
            (p, d) = GetCorrectStrategiesPretruncation();

            return new DMSStrategiesPair(p, d, this);
        }

        public (DMSStrategyPretruncation p, DMSStrategyPretruncation d) GetCorrectStrategiesPretruncation()
        {
            var fs = GetUntruncFuncs();
            double pBidMin = fs.pUntruncFunc(0), pBidMax = fs.pUntruncFunc(1), dBidMin = fs.dUntruncFunc(0), dBidMax = fs.dUntruncFunc(1);
            double pSlope = pBidMax - pBidMin;
            double dSlope = dBidMax - dBidMin;

            var p = new DMSStrategyPretruncation(0, pSlope, pPiecewiseLinearRanges.Select(x => fs.pUntruncFunc(x.low)).ToList(), pPiecewiseLinearRanges);
            var d = new DMSStrategyPretruncation(0, dSlope, dPiecewiseLinearRanges.Select(x => fs.dUntruncFunc(x.low)).ToList(), dPiecewiseLinearRanges);
            return (p, d);
        }

        #region Truncations


        public (double pBid, double dBid) GetBids(double zP, double zD, bool truncated = true)
        {
            var fs = GetUntruncFuncs();
            if (truncated) 
                return Truncated(zP, zD);
            return (fs.pUntruncFunc(zP), fs.dUntruncFunc(zD)); 
        }



        public IEnumerable<(double pBid, double dBid)> GetBids(int numItems, bool truncated = true) => Enumerable.Range(0, numItems).Select(i => EquallySpaced.GetLocationOfMidpoint(i, numItems)).Select(x => GetBids(x, x, truncated));

        bool truncate = true;
        bool includeIrrelevantTruncations = false;
        bool standardizeTrivialEq = true; // if P's most generous bid is greater than the D's most generous bid, there will never be a settlement, regardless of the signals. This is a trivial equilibrium, and so we can standardize those.

        private double Truncated(double untrunc, bool plaintiff)
        {
            if (!truncate)
                return untrunc;
            // In DMS, P bids that are always higher than D's are truncated to their min value, since they will never result in settlement anyway.
            // Similarly, if D bids are alwayer lower than p's, 

            double bid = plaintiff ? 1 : 0;
            if (pAboveD)
            {
                if (standardizeTrivialEq && pEntirelyAboveD)
                    return plaintiff ? 1.0 : 0;
                if (includeIrrelevantTruncations)
                {
                    if (plaintiff)
                        bid = Math.Min(untrunc, dUntruncFunc(1.0)); // the case won't settle whether P offers a bid higher than the highest possible D bid of just at that value in the DMS model, so these are equivalent equilibria
                    else
                        bid = Math.Max(pUntruncFunc(0), untrunc); // the case won't settle whether D offers a bid less than the lowest possible P bid or just at that value in the DMS model, so these are equivalent equilibria
                }
            }
            else
            {
                if (dEntirelyAboveP)
                {
                    // There are also unlimited equilibria here, all of which result in settlement.
                    // Let's narrow down to one such equilibrium, where the parties bid the midpoint of D's most aggressive (lowest) bid and P's most aggressive (highest) bid.
                    bid = (dUntruncFunc(0.0) + pUntruncFunc(1.0)) / 2.0;
                    if (bid < 0 && 0 <= dUntruncFunc(0.0))
                        bid = 0;
                    else if (bid > 1.0 && 1.0 >= pUntruncFunc(1.0))
                        bid = 1.0;
                }
                else
                {
                    bid = plaintiff ? Math.Max(untrunc, dUntruncFunc(0.0)) : Math.Min(pUntruncFunc(1.0), untrunc);
                }
            }
            bool truncateAt0And1 = false;
            if (truncateAt0And1)
            {
                bid = Math.Min(bid, 1.0);
                bid = Math.Max(bid, 0.0);
            }
            return bid;
        }

        private (double pBid, double dBid) Truncated(double zP, double zD)
        {
            bool truncate = true;
            if (!truncate)
                return (pUntruncFunc(zP), dUntruncFunc(zD));
            bool includeIrrelevantTruncations = false;
            // In DMS, P bids that are always higher than D's are truncated to their min value, since they will never result in settlement anyway.
            // Similarly, if D bids are alwayer lower than p's, 
            bool standardizeTrivialEq = true; // if P's most generous bid is greater than the D's most generous bid, there will never be a settlement, regardless of the signals. This is a trivial equilibrium, and so we can standardize those.
            double pBid = 1, dBid = 0;
            if (pAboveD)
            {
                if (standardizeTrivialEq && pEntirelyAboveD)
                    return (1.0, 0);
                if (includeIrrelevantTruncations)
                {
                    pBid = Math.Min(pUntruncFunc(zP), dUntruncFunc(1.0)); // the case won't settle whether P offers a bid higher than the highest possible D bid of just at that value in the DMS model, so these are equivalent equilibria
                    dBid = Math.Max(pUntruncFunc(0), dUntruncFunc(zD)); // the case won't settle whether D offers a bid less than the lowest possible P bid or just at that value in the DMS model, so these are equivalent equilibria
                }
            }
            else
            {
                if (dEntirelyAboveP)
                {
                    // There are also unlimited equilibria here, all of which result in settlement.
                    // Let's narrow down to one such equilibrium, where the parties bid the midpoint of D's most aggressive (lowest) bid and P's most aggressive (highest) bid.
                    pBid = dBid = (dUntruncFunc(0.0) + pUntruncFunc(1.0)) / 2.0;
                    if (pBid < 0 && 0 <= dUntruncFunc(0.0))
                        pBid = dBid = 0;
                    else if (dBid > 1.0 && 1.0 >= pUntruncFunc(1.0))
                        pBid = dBid = 1.0;
                }
                else
                {
                    pBid = Math.Max(pUntruncFunc(zP), dUntruncFunc(0.0));
                    dBid = Math.Min(pUntruncFunc(1.0), dUntruncFunc(zD));
                }
            }
            bool truncateAt0And1 = false;
            if (truncateAt0And1)
            {
                pBid = Math.Min(pBid, 1.0);
                pBid = Math.Max(pBid, 0.0);
                dBid = Math.Min(dBid, 1.0);
                dBid = Math.Max(dBid, 0.0);
            }
            return (pBid, dBid);
        }

        private bool PAboveD()
        {

            bool pAboveD = pUntruncFunc(1.0) > dUntruncFunc(1.0);
            bool pAboveD2 = pUntruncFunc(0.0) > dUntruncFunc(0.0);
            if (pAboveD != pAboveD2)
                throw new Exception();
            return pAboveD;
        }

        private bool PEntirelyAboveD()
        {
            return pUntruncFunc(0.0) > dUntruncFunc(1.0);
        }

        private bool DEntirelyAboveP()
        {
            return pUntruncFunc(1.0) < dUntruncFunc(0.0);
        }

        #endregion

        #region Untruncated functions

        private static int GetCaseNum(double tvar, double cvar, double qvar)
        {
            if (tvar <= qvar && qvar <= 1.0 - tvar)
                return 1;
            if (qvar < tvar && tvar < 1.0 - qvar)
                return 2;
            if (1.0 - qvar < tvar && tvar < qvar)
                return 3;
            if (1 - tvar <= qvar && qvar <= tvar && cvar <= (1.0 / 6.0) * (1 - tvar) / (qvar * (1 - qvar)))
                return 4; // case 4A
            if (1 - tvar <= qvar && qvar <= tvar && cvar > (1.0 / 6.0) * (1 - tvar) / (qvar * (1 - qvar)))
                return 5; // case 4B
            throw new NotSupportedException();
        }

        private (Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc) GetUntruncFuncs()
        {
            switch (CaseNum)
            {
                case 1:
                    return (Case1UntruncatedP, Case1UntruncatedD);
                case 2:
                    return (Case2UntruncatedP, Case2UntruncatedD);
                case 3:
                    return (Case3UntruncatedP, Case3UntruncatedD);
                case 4:
                    return (Case4AUntruncatedP, Case4AUntruncatedD);
                case 5:
                    return (Case4BUntruncatedP, Case4BUntruncatedD);
                default:
                    throw new NotImplementedException();
            }
        }

        private double GetCutoff(bool plaintiff, bool secondCutoffForCase4A)
        {
            switch (CaseNum)
            {
                case 1:
                    throw new NotImplementedException();
                case 2:
                    return plaintiff ? 6.0 * C - 1.0 + (T - Q) / (1.0 - Q) : (T - Q) / (1.0 - Q);
                case 3:
                    return plaintiff ? (1.0 - T) / Q : 1.0 - 6.0 * C + (1.0 - T) / Q;
                case 4:
                    if (secondCutoffForCase4A)
                        return plaintiff ? (1.0 - T) / Q : 1.0 - 6.0 * C + (1.0 - T) / Q;
                    return plaintiff ? 6.0 * C - 1.0 + (T - Q) / (1.0 - Q) : (T - Q) / (1.0 - Q);
                case 5:
                    return plaintiff ? 6.0 * C * (1 - Q) : 1 - 6.0 * C * Q;
                default:
                    throw new NotImplementedException();
            }
        }

        private double Case1UntruncatedP(double zP) => 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        private double Case1UntruncatedD(double zD) => 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;

        private double Case2UntruncatedP(double zP)
        {
            bool firstSubcase = zP < 6.0 * C - 1.0 + (T - Q) / (1.0 - Q);
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case2UntruncatedD(double zD)
        {
            bool firstSubcase = zD < (T - Q) / (1.0 - Q);
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
        }
        private double Case3UntruncatedP(double zP)
        {
            bool firstSubcase = zP <= (1.0 - T) / Q;
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case3UntruncatedD(double zD)
        {
            bool firstSubcase = zD <= 1.0 - 6.0 * C + (1.0 - T) / Q;
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }
        private double Case4AUntruncatedP(double zP)
        {
            double expression = 6.0 * C - 1.0 + (T - Q) / (1.0 - Q);
            double expression2 = (1.0 - T) / Q;

            bool firstSubcase = zP < expression;
            bool caseB = expression <= zP && zP <= expression2;
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else if (caseB)
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case4AUntruncatedD(double zD)
        {
            double expression = (T - Q) / (1.0 - Q);
            double expression2 = 1.0 - 6.0 * C + (1.0 - T) / Q;

            bool firstSubcase = zD < expression;
            bool caseB = expression <= zD && zD <= expression2;
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else if (caseB)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }

        private double Case4BUntruncatedP(double zP)
        {
            bool firstSubcase = zP < 6.0 * C * (1 - Q);
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case4BUntruncatedD(double zD)
        {
            bool firstSubcase = zD <= 1.0 - 6.0 * C * Q;
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }

        public string ProduceTableOfCases()
        {
            StringBuilder b = new StringBuilder();
            for (double tvar = 0.0; tvar <= 1.01; tvar += 0.05)
            {
                for (double qvar = 0.35; qvar <= 0.65; qvar += 0.05)
                {
                    int caseNum = GetCaseNum(tvar, C, qvar);
                    b.Append(caseNum.ToString());
                    if (qvar != 0.65)
                        b.Append(",");
                }
                b.AppendLine("");
            }
            return b.ToString();
        }

        public string ProduceTableOfPPiecewiseRanges()
        {
            StringBuilder b = new StringBuilder();
            for (double tvar = 0.0; tvar <= 1.01; tvar += 0.05)
            {
                for (double qvar = 0.35; qvar <= 0.65; qvar += 0.05)
                {
                    DMSCalc d2 = new DMSCalc(tvar, C, qvar);
                    int numRanges = d2.pPiecewiseLinearRanges.Count;
                    b.Append(numRanges.ToString());
                    if (qvar != 0.65)
                        b.Append(",");
                }
                b.AppendLine("");
            }
            return b.ToString();
        }

        #endregion 

        #region Piecewise linear

        private List<(double low, double high)> GetPiecewiseLinearRanges(bool plaintiff)
        {
            return GetPiecewiseLinearRangesHelper(plaintiff).Where(x => x.high >= 0 && x.low <= 1.0).Select(x => (low: Math.Max(x.low, 0), high: Math.Min(x.high, 1))).Where(x => x.low != x.high).ToList();
        }

        private List<(double low, double high)> GetPiecewiseLinearRangesHelper(bool plaintiff)
        {
            switch (CaseNum)
            {
                case 1:
                    return new List<(double x, double y)>() { (double.NegativeInfinity, double.PositiveInfinity) };
                case 2:
                case 3:
                case 5:
                    return new List<(double x, double y)>() { (double.NegativeInfinity, GetCutoff(plaintiff, false)), (GetCutoff(plaintiff, false), double.PositiveInfinity) };
                case 4:
                    return new List<(double x, double y)>() { (double.NegativeInfinity, GetCutoff(plaintiff, false)), (GetCutoff(plaintiff, false), GetCutoff(plaintiff, true)), (GetCutoff(plaintiff, true), double.PositiveInfinity) };
                default:
                    throw new Exception();
            }
        }

        public byte GetPiecewiseLinearRangeIndex(double z, bool plaintiff)
        {
            var ranges = plaintiff ? pPiecewiseLinearRanges : dPiecewiseLinearRanges;
            byte i = 0;
            foreach (var range in ranges)
            {
                if (z >= range.low && (z < range.high || (z == 1.0 && z == range.high)))
                {
                    return i;
                }
                i++;
            }
            throw new NotImplementedException();
        }

        public (double low, double high) GetPiecewiseLinearRange(byte rangeIndex, bool plaintiff)
        {
            (double low, double high) range = plaintiff ? pPiecewiseLinearRanges[rangeIndex] : dPiecewiseLinearRanges[rangeIndex];
            return range;
        }

        public double GetTruncatedDistanceFromStartOfLinearRange(double z, bool plaintiff, double truncationPortion)
        {
            byte rangeIndex = GetPiecewiseLinearRangeIndex(z, plaintiff);
            (double low, double high) range = GetPiecewiseLinearRange(rangeIndex, plaintiff);
            if (plaintiff)
            {
                double threshold = range.low + truncationPortion * (range.high - range.low);
                if (z < threshold)
                    z = threshold;
            }
            else
            {
                double threshold = range.high - truncationPortion * (range.high - range.low);
                if (z > threshold)
                    z = threshold;
            }
            return z - range.low;
        }

        public double GetPiecewiseLinearBidTruncated(double z, bool plaintiff, double minValForRange, double slope, double truncationPortion)
        {
            var bid = minValForRange + slope * GetTruncatedDistanceFromStartOfLinearRange(z, plaintiff, truncationPortion);
            return bid;
        }

        public double GetPiecewiseLinearBidTruncated(double z, bool plaintiff, double slope, double truncationPortion, List<double> minForRange)
        {
            var ranges = plaintiff ? pPiecewiseLinearRanges : dPiecewiseLinearRanges;
            if (ranges.Count != minForRange.Count)
                throw new Exception();
            int i = 0;
            foreach (var range in ranges)
            {
                if (z >= range.low && z < range.high)
                {
                    return GetPiecewiseLinearBidTruncated(z, plaintiff, range.low, slope, truncationPortion);
                }
                i++;
            }
            throw new NotImplementedException();
        }

        public record struct DMSStrategyPretruncation(int index, double slope, List<double> minForRange, List<(double low, double high)> piecewiseRanges)
        {
            public DMSStrategyWithTruncations GetStrategyWithTruncation(double truncationValue, bool truncationIsMin)
            {
                return new DMSStrategyWithTruncations(index, LineSegment.GetTruncatedLineSegments(piecewiseRanges, minForRange, slope, truncationValue, truncationIsMin));
            }

            public override string ToString()
            {
                return index + ": " + String.Join(", ", piecewiseRanges.Zip(minForRange, (x, y) => $"({Math.Round(x.low, 2)},{Math.Round(y,2)})")) + $" (slope {Math.Round(slope, 2)})";
            }
        }

        public struct DMSStrategiesPair
        {
            public DMSStrategyWithTruncations pStrategy;
            public DMSStrategyWithTruncations dStrategy;
            public DMSCalc DMSCalc;
            public List<SingleCaseOutcome> Outcomes;
            public double SettlementPercentage, PNet, DNet;
            public bool Nontrivial => SettlementPercentage is > 0 and < 1;

            public DMSStrategiesPair(DMSStrategyPretruncation pPretruncation, DMSStrategyPretruncation dPretruncation, DMSCalc dmsCalc)
            {
                var pLastMin = pPretruncation.minForRange[pPretruncation.minForRange.Count - 1];
                var pLastLinearRange = dmsCalc.pPiecewiseLinearRanges[dmsCalc.pPiecewiseLinearRanges.Count - 1];
                var pLastLinearRangeDistance = pLastLinearRange.high - pLastLinearRange.low;
                double pAbsoluteMax = pLastMin + pLastLinearRangeDistance * pPretruncation.slope;
                double dAbsoluteMin = dPretruncation.minForRange[0];

                pStrategy = new DMSStrategyWithTruncations(pPretruncation.index, pPretruncation.GetStrategyWithTruncation(dAbsoluteMin, true).lineSegments);
                dStrategy = new DMSStrategyWithTruncations(dPretruncation.index, dPretruncation.GetStrategyWithTruncation(pAbsoluteMax, false).lineSegments);
                DMSCalc = dmsCalc;
                Outcomes = null;
                SettlementPercentage = PNet = DNet = 0;
                Outcomes = GetOutcomes(250).ToList();
                SettlementPercentage = Outcomes.Average(x => x.settles ? 1.0 : 0);
                PNet = Outcomes.Average(x => x.pNet);
                DNet = Outcomes.Average(x => x.dNet);
            }


            public IEnumerable<SingleCaseOutcome> GetOutcomes(int numSignalsPerParty = 100)
            {
                double signalDistance = 1.0 / (double)numSignalsPerParty;
                for (int pSignalIndex = 0; pSignalIndex < numSignalsPerParty; pSignalIndex++)
                {
                    for (int dSignalIndex = 0; dSignalIndex < numSignalsPerParty; dSignalIndex++)
                    {
                        double pSignal = (pSignalIndex + 1) * signalDistance;
                        double dSignal = (dSignalIndex + 1) * signalDistance;
                        yield return GetOutcome(pSignal, dSignal);
                    }
                }
            }

            public SingleCaseOutcome GetOutcome(double pSignal, double dSignal)
            {
                return GetOutcome(pSignal, dSignal, pStrategy.GetBidForSignal(pSignal), dStrategy.GetBidForSignal(dSignal));
            }

            public record struct SingleCaseOutcome(bool settles, double pGross, double pCosts, double dCosts)
            {
                public double dGross => 1.0 - pGross;
                public double pNet => pGross - pCosts;
                public double dNet => dGross - dCosts;
            }

            public SingleCaseOutcome GetOutcome(double pSignal, double dSignal, double pBid, double dBid)
            {
                if (dBid >= pBid - 1E-12)
                    return new SingleCaseOutcome(true, (pBid + dBid) / 2.0, 0, 0);
                double thetaP = pSignal * DMSCalc.Q;
                double thetaD = DMSCalc.Q + dSignal * (1.0 - DMSCalc.Q);
                double judgment = 0.5 * (thetaP + thetaD);
                bool feeShiftingToP = judgment < 0.5 && thetaD < DMSCalc.T;
                bool feeShiftingToD = judgment > 0.5 && thetaP > DMSCalc.T;
                double pCosts = 0, dCosts = 0;
                if (feeShiftingToP)
                    pCosts = DMSCalc.C;
                else if (feeShiftingToD)
                    dCosts = DMSCalc.C;
                else
                    pCosts = dCosts = 0.5 * DMSCalc.C;
                return new SingleCaseOutcome(false, judgment, pCosts, dCosts);
            }

            public override string ToString()
            {
                var pBids = pStrategy.GetBidsForSignal(10);
                string pBidsString = String.Join(",", pBids.Select(x => $"{Math.Round(x, 2)}"));
                var dBids = dStrategy.GetBidsForSignal(10);
                string dBidsString = String.Join(",", dBids.Select(x => $"{Math.Round(x, 2)}"));
                return $"{pStrategy.index}, {dStrategy.index} (Settlement {SettlementPercentage} pNet {PNet} dNet {DNet}): P {pBidsString}; D {dBidsString}";
            }
        }

        public record struct DMSStrategyWithTruncations(int index, List<LineSegment> lineSegments)
        {
            public double GetBidForSignal(double signal)
            {
                foreach (var ls in lineSegments)
                {
                    if (signal >= ls.xStart - 1E-12 && signal <= ls.xEnd + 1E-12)
                        return ls.yVal(signal);
                }
                throw new Exception();
            }

            public IEnumerable<double> GetBidsForSignal(int numItems)
            {
                var copy = this;
                return Enumerable.Range(0, numItems).Select(i => copy.GetBidForSignal(EquallySpaced.GetLocationOfMidpoint(i, numItems)));
            }

            public static IEnumerable<(double pBid, double dBid)> GetBidsForSignal(DMSStrategiesPair pair, int numItems) => Enumerable.Range(0, numItems).Select(i => EquallySpaced.GetLocationOfMidpoint(i, numItems)).Select(x => (pair.pStrategy.GetBidForSignal(x), pair.dStrategy.GetBidForSignal(x)));
        }



        public IEnumerable<DMSStrategyPretruncation> EnumeratePossibleStrategies(bool plaintiff)
        {
            bool IsOrdered<T>(IList<T> list, IComparer<T> comparer = null)
            {
                if (comparer == null)
                {
                    comparer = Comparer<T>.Default;
                }

                if (list.Count > 1)
                {
                    for (int i = 1; i < list.Count; i++)
                    {
                        if (comparer.Compare(list[i - 1], list[i]) > 0)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            double[] slopes = new double[] { 1.0 / 3.0 }; // DEBUG, 2.0 / 3.0, 1.0 };
            int numYPossibilities = 50;
            double[] potentialYPoints = Enumerable.Range(0, numYPossibilities).Select(x => EquallySpaced.GetLocationOfMidpoint(x, numYPossibilities)).ToArray();

            var piecewiseLinearRanges = plaintiff ? pPiecewiseLinearRanges : dPiecewiseLinearRanges;


            int numLinearRanges = piecewiseLinearRanges.Count;
            List<List<double>> permutationSource = new List<List<double>>();
            for (int c = 0; c < numLinearRanges; c++)
                permutationSource.Add(potentialYPoints.ToList());
            List<List<double>> possibilities = PermutationMaker.GetPermutationsOfItems(permutationSource).Where(l => IsOrdered(l)).ToList();

            int index = 0;
            foreach (double slope in slopes)
            {

                for (int j = 0; j < possibilities.Count; j++)
                {
                    List<double> minVals = possibilities[j];

                    var strategy = new DMSStrategyPretruncation(index++, slope, minVals, piecewiseLinearRanges);
                    yield return strategy;
                }
            }
        }


        //public record struct DMSPartialOutcome(double weight, bool settles, double? avgTrialValue, double? avgSettlementValue)
        //{
        //    public static DMSPartialOutcome GetFromSegments(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
        //    {
        //        if (pSegment.YApproximatelyEqual(dSegment))
        //            return GetForOverlappingYs(c, pSegment, dSegment);
        //        if (pSegment.yStart >= dSegment.yEnd - 1E-12)
        //            return GetTrialOutcome(c, pSegment, dSegment);
        //        else if (dSegment.yStart >= pSegment.yEnd - 1E-12)
        //            return GetSettlementOutcome(c, pSegment, dSegment);
        //        else
        //            throw new Exception("Partial outcome cannot be calculated from partially overlapping segments.");
        //    }

        //    private static DMSPartialOutcome GetForOverlappingYs(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
        //    {

        //    }

        //    private static DMSPartialOutcome GetSettlementOutcome(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
        //    {
        //        return new DMSPartialOutcome(GetWeight(pSegment, dSegment), true, null, (dSegment.yAvg - pSegment.yAvg));
        //    }

        //    private static DMSPartialOutcome GetTrialOutcome(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
        //    {
        //        // Whether fee shifting occurs will be uniform within this range. 
        //        double lowPSignal = pSegment.xAvg * c.Q;
        //        double avgDSignal = c.Q + (1.0 - c.Q) * dSegment.xAvg;
        //        double avgJudgment = avgPSignal + avgDSignal;

        //    }

        //    private static double GetWeight(LineSegment pSegment, LineSegment dSegment) => (pSegment.xEnd - pSegment.xStart) * (dSegment.xEnd - dSegment.xStart);
        //}





        #endregion
    }
}

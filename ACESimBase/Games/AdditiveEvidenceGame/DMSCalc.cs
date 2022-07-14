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
        public int CaseNum;
        Func<double, double> pUntruncFunc;
        Func<double, double> dUntruncFunc;
        public List<(double low, double high)> pPiecewiseLinearRanges, dPiecewiseLinearRanges;

        bool pAboveD, pEntirelyAboveD, dEntirelyAboveP;

        public bool manyEquilibria => pEntirelyAboveD || dEntirelyAboveP;

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
            string pCorrectString = String.Join(",", correct.Select(x => $"{Math.Round(x.pBid, 3)}"));
            string dCorrectString = String.Join(",", correct.Select(x => $"{Math.Round(x.dBid, 3)}"));
            string combined = $"T: {T}; C: {C}; Q: {Q} \nCorrect (untruncated): P {pCorrectString}; D {dCorrectString}";
            return combined;
        }

        public string ToStringShort()
        {
            return $"{T:0.000},{C:0.000},{Q:0.000},{CaseNum}";   
        }

        #region Correct strategy pairs
        public DMSStrategiesPair GetCorrectStrategiesPair(bool calculateAnalytically)
        {
            DMSStrategyPretruncation p, d;
            (p, d) = GetCorrectStrategiesPretruncation();

            return new DMSStrategiesPair(p, d, this, calculateAnalytically);
        }

        public (DMSStrategyPretruncation p, DMSStrategyPretruncation d) GetCorrectStrategiesPretruncation()
        {
            var fs = GetUntruncFuncs();
            // We assume the same slope in each piecewise linear range. But because there can be discontinuities, the slope cannot be calculated based on the difference in y values between x values of 0 and 1.
            double pBidStartFirstRange = fs.pUntruncFunc(pPiecewiseLinearRanges[0].low), pBidEndFirstRange = fs.pUntruncFunc(pPiecewiseLinearRanges[0].high - 1E-12), dBidStartFirstRange = fs.dUntruncFunc(dPiecewiseLinearRanges[0].low), dBidEndFirstRange = fs.dUntruncFunc(dPiecewiseLinearRanges[0].high - 1E-12);

            double pSlope = (pBidEndFirstRange - pBidStartFirstRange) / (pPiecewiseLinearRanges[0].high - pPiecewiseLinearRanges[0].low);
            double dSlope = (dBidEndFirstRange - dBidStartFirstRange) / (dPiecewiseLinearRanges[0].high - dPiecewiseLinearRanges[0].low);

            var p = new DMSStrategyPretruncation(0, pSlope, pPiecewiseLinearRanges.Select(x => fs.pUntruncFunc(x.low + 1E-14)).ToList(), pPiecewiseLinearRanges);
            var d = new DMSStrategyPretruncation(0, dSlope, dPiecewiseLinearRanges.Select(x => fs.dUntruncFunc(x.low + 1E-14)).ToList(), dPiecewiseLinearRanges);
            return (p, d);
        }

        #endregion

        #region Outcome calculation

        public record struct DMSOutcome(double pGross, double pCosts, double dCosts)
        {
            public double dGross => 1.0 - pGross;
            public double pNet => pGross - pCosts;
            public double dNet => dGross - dCosts;
            public double feeShiftingFromPToD => 0.5 * (dCosts - pCosts); // Suppose cost is 0.1. If D must pay all fees, then the difference in costs will be 0.1, and the fee shifting amount will be 0.5. 
        }

        public (double proportionSettling, DMSOutcome avgOutcome) GetAverageOutcome(List<DMSPartialOutcome> partialOutcomes)
        {
            double GetWeightedAverage(Func<DMSPartialOutcome, double> f)
            {
                StatCollector s = new StatCollector();
                foreach (var partialOutcome in partialOutcomes)
                    s.Add(f(partialOutcome), partialOutcome.weight);
                return s.Average();
            }

            return (GetWeightedAverage(o => o.settles ? 1.0 : 0), new DMSOutcome(GetWeightedAverage(o => o.pGross), GetWeightedAverage(o => o.pCosts), GetWeightedAverage(o => o.dCosts)));
        }
        public (bool settles, DMSOutcome outcome) GetOutcome(double pSignal, double dSignal, double pBid, double dBid)
        {
            if (dBid >= pBid - 1E-12)
                return (true, new DMSOutcome((pBid + dBid) / 2.0, 0, 0));
            double thetaP = pSignal * Q;
            double thetaD = Q + dSignal * (1.0 - Q);
            double oneMinusThetaD = 1.0 - thetaD;

            double judgment = 0.5 * (thetaP + thetaD);
            bool feeShiftingToP = thetaP < oneMinusThetaD - 1E-12 && thetaD < T - 1E-12;
            bool feeShiftingToD = thetaP > oneMinusThetaD + 1E-12 && thetaP > 1.0 - T + 1E-12;
            // The following is equivalent (but does not include what is necessary to avoid rounding error)
            // bool feeShiftingToP = judgment < 0.5 && thetaD < DMSCalc.T;
            // bool feeShiftingToD = judgment > 0.5 && thetaP > 1 - DMSCalc.T;
            double pCosts = 0, dCosts = 0;
            if (feeShiftingToP)
                pCosts = C;
            else if (feeShiftingToD)
                dCosts = C;
            else
                pCosts = dCosts = 0.5 * C;
            return (false, new DMSOutcome(judgment, pCosts, dCosts));
        }

        #endregion

        #region Truncations

        public (double pBid, double dBid) GetBids(double zP, double zD, bool truncated = true)
        {
            var fs = GetUntruncFuncs();
            if (truncated) 
                return Truncated(zP, zD);
            return (fs.pUntruncFunc(zP), fs.dUntruncFunc(zD)); 
        }

        public IEnumerable<(double pBid, double dBid)> GetBids(int numItems, bool truncated = true) => Enumerable.Range(0, numItems).Select(i => EquallySpaced.GetLocationOfEquallySpacedPoint(i, numItems, true)).Select(x => GetBids(x, x, truncated));

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
                    return RoundIfVeryClose(plaintiff ? 6.0 * C - 1.0 + (T - Q) / (1.0 - Q) : (T - Q) / (1.0 - Q));
                case 3:
                    return RoundIfVeryClose(plaintiff ? (1.0 - T) / Q : 1.0 - 6.0 * C + (1.0 - T) / Q);
                case 4:
                    if (secondCutoffForCase4A)
                        return RoundIfVeryClose(plaintiff ? (1.0 - T) / Q : 1.0 - 6.0 * C + (1.0 - T) / Q);
                    return RoundIfVeryClose(plaintiff ? 6.0 * C - 1.0 + (T - Q) / (1.0 - Q) : (T - Q) / (1.0 - Q));
                case 5:
                    return RoundIfVeryClose(plaintiff ? 6.0 * C * (1 - Q) : 1 - 6.0 * C * Q);
                default:
                    throw new NotImplementedException();
            }
        }

        private double RoundIfVeryClose(double x)
        {
            if (Math.Abs(x) < 1E-8)
                return 0;
            if (Math.Abs(x - 1) < 1E-8)
                return 1.0;
            return x;
        }

        private double Case1UntruncatedP(double zP) => 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        private double Case1UntruncatedD(double zD) => 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;

        private double Case2UntruncatedP(double zP)
        {
            bool firstSubcase = zP < RoundIfVeryClose(6.0 * C - 1.0 + (T - Q) / (1.0 - Q));
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case2UntruncatedD(double zD)
        {
            bool firstSubcase = zD < RoundIfVeryClose((T - Q) / (1.0 - Q));
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
        }
        private double Case3UntruncatedP(double zP)
        {
            bool firstSubcase = zP <= RoundIfVeryClose((1.0 - T) / Q);
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case3UntruncatedD(double zD)
        {
            bool firstSubcase = zD <= RoundIfVeryClose(1.0 - 6.0 * C + (1.0 - T) / Q);
            if (firstSubcase)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }
        private double Case4AUntruncatedP(double zP)
        {
            double expression = RoundIfVeryClose(6.0 * C - 1.0 + (T - Q) / (1.0 - Q));
            double expression2 = RoundIfVeryClose((1.0 - T) / Q);

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
            double expression = RoundIfVeryClose((T - Q) / (1.0 - Q));
            double expression2 = RoundIfVeryClose(1.0 - 6.0 * C + (1.0 - T) / Q);

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
            bool firstSubcase = zP < RoundIfVeryClose(6.0 * C * (1 - Q));
            if (firstSubcase)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case4BUntruncatedD(double zD)
        {
            bool firstSubcase = zD <= RoundIfVeryClose(1.0 - 6.0 * C * Q);
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

        // Note: This calculates analytically the outcome from a subset of cases defined by a strategy pair. Currently, it works only for the Friedman-Wittman
        // subset of the DMS cases, so for other cases, we should continue to use empirical calcultions.
        public record struct DMSPartialOutcome(double weight, bool settles, double pGross, double dGross, double pCosts, double dCosts)
        {
            private bool ConfirmApproximateMatch(DMSPartialOutcome other, double precision)
            {
                return (weight == other.weight && settles == other.settles && Math.Abs(pGross - other.pGross) < precision && Math.Abs(dGross - other.dGross) < precision && Math.Abs(pCosts - other.pCosts) < precision && Math.Abs(dCosts - other.dCosts) < precision) ;
            }

            public static IEnumerable<DMSPartialOutcome> GetFromSegments(DMSCalc c, LineSegment pSegment, LineSegment dSegment, bool confirmEmpirically)
            {
                foreach (var analyticalOutcome in GetFromSegments(c, pSegment, dSegment))
                {
                    if (confirmEmpirically)
                        ConfirmAverageEmpirically(analyticalOutcome, c, analyticalOutcome.weight, pSegment, dSegment);
                    yield return analyticalOutcome;
                }
            }
            private static IEnumerable<DMSPartialOutcome> GetFromSegments(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
            {
                if (pSegment.YApproximatelyEqual(dSegment))
                {
                    yield return GetSettlementOutcome_IdenticalBidRanges(c, pSegment, dSegment);
                    yield return GetTrialOutcome_IdenticalBidRanges(c, pSegment, dSegment);
                }
                else if (pSegment.yStart >= dSegment.yEnd - 1E-6)
                    yield return GetTrialOutcome(c, pSegment, dSegment);
                else if (dSegment.yStart >= pSegment.yEnd - 1E-6)
                    yield return GetSettlementOutcome(c, pSegment, dSegment);
                else 
                    throw new Exception("Partial outcome cannot be calculated from partially overlapping segments.");
            }

            private static void ConfirmAverageEmpirically(DMSPartialOutcome analytical, DMSCalc c, double weight, LineSegment pSegment, LineSegment dSegment)
            {
                var empirical = GetAverageEmpirically(c, weight, pSegment, dSegment, analytical.settles);
                if (!analytical.ConfirmApproximateMatch(empirical, 1E-4))
                    throw new Exception();
            }
            private static DMSPartialOutcome GetAverageEmpirically(DMSCalc c, double weight, LineSegment pSegment, LineSegment dSegment, bool settlingCases)
            {
                List<DMSOutcome> outcomes = new List<DMSOutcome>();
                foreach (double pSignal in EquallySpaced.GetPointsFullyEquallySpaced(1000, pSegment.xStart, pSegment.xEnd))
                {
                    double pBid = pSegment.yVal(pSignal);
                    foreach (double dSignal in EquallySpaced.GetPointsFullyEquallySpaced(1000, dSegment.xStart, dSegment.xEnd))
                    {
                        double dBid = dSegment.yVal(dSignal);
                        var result = c.GetOutcome(pSignal, dSignal, pBid, dBid);
                        if (result.settles == settlingCases)
                            outcomes.Add(result.outcome);
                    }
                }
                DMSPartialOutcome partialOutcome = new DMSPartialOutcome(weight, settlingCases, outcomes.Average(x => x.pGross), outcomes.Average(x => x.dGross), outcomes.Average(x => x.pCosts), outcomes.Average(x => x.dCosts));
                return partialOutcome;
            }
            private static DMSPartialOutcome GetSettlementOutcome(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
            {
                // Every one of these cases will settle, because D's bid is higher than P's. Average settlement value is the average d bid minus the average p bid.
                double settlementValue = 0.25 * (dSegment.yStart + dSegment.yEnd + pSegment.yStart + pSegment.yEnd);
                return new DMSPartialOutcome(GetWeight(pSegment, dSegment), true, settlementValue, 1.0 - settlementValue, 0, 0);
            }

            private static DMSPartialOutcome GetTrialOutcome(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
            {
                // Every one of these cases will go to trial, because D's bid is lower than P's throughout. 
                // Whether fee shifting occurs must be uniform within this range. 
                var trial1 = c.GetOutcome(pSegment.xStart, dSegment.xStart, 1, 0);
                var trial2 = c.GetOutcome(pSegment.xEnd, dSegment.xEnd, 1, 0);
                var trialValue = 0.5 * (trial1.outcome.pGross + trial2.outcome.pGross);

                return new DMSPartialOutcome(GetWeight(pSegment, dSegment), false, trialValue, 1.0 - trialValue, 0.5 * (trial1.outcome.pCosts + trial2.outcome.pCosts), 0.5 * (trial1.outcome.dCosts + trial2.outcome.dCosts));

            }
            private static DMSPartialOutcome GetSettlementOutcome_IdenticalBidRanges(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
            {
                double settlementValue = 0.5 * (pSegment.yStart + pSegment.yEnd);
                return new DMSPartialOutcome(0.5 * GetWeight(pSegment, dSegment), true, settlementValue, 1.0 - settlementValue, 0, 0);
            }

            private static DMSPartialOutcome GetTrialOutcome_IdenticalBidRanges(DMSCalc c, LineSegment pSegment, LineSegment dSegment)
            {
                double avgTrialValue = -(1.0 / 6.0) * (-c.Q * (pSegment.xStart + 2 * pSegment.xEnd + 3) + 2 * dSegment.xStart * (c.Q - 1) + dSegment.xEnd * (c.Q - 1));
                var sampleOutcome = c.GetOutcome(pSegment.xEnd - 1E-12, dSegment.xStart + 1E-12, 1, 0);
                return new DMSPartialOutcome(0.5 * GetWeight(pSegment, dSegment), false, avgTrialValue, 1.0 - avgTrialValue, sampleOutcome.outcome.pCosts, sampleOutcome.outcome.dCosts);
            }

            private static double GetWeight(LineSegment pSegment, LineSegment dSegment) => (pSegment.xEnd - pSegment.xStart) * (dSegment.xEnd - dSegment.xStart);
        }

        #endregion

        #region Strategy pairs

        public record struct DMSStrategyPretruncation(int index, double slope, List<double> minForRange, List<(double low, double high)> piecewiseRanges)
        {
            public DMSStrategyWithTruncations GetStrategyWithTruncation(double truncationValue, bool truncationIsMin)
            {
                return new DMSStrategyWithTruncations(index, LineSegment.GetTruncatedLineSegments(piecewiseRanges, minForRange, slope, truncationValue, truncationIsMin), MinVal(), MaxVal());
            }

            public double MinVal() => minForRange[0];

            public double MaxVal() => minForRange.Last() + slope * (piecewiseRanges.Last().high - piecewiseRanges.Last().low);

            public IEnumerable<(double low, double high)> EnumerateYRanges()
            {
                for (int i = 0; i < minForRange.Count; i++)
                {
                    var low = minForRange[i];
                    var high = low + slope * (piecewiseRanges[i].high - piecewiseRanges[i].low);
                    yield return (low, high);
                }
            }
            public override string ToString()
            {
                return index + ": " + String.Join(", ", piecewiseRanges.Zip(EnumerateYRanges(), (x, y) => $"({Math.Round(x.low, 3)},{Math.Round(y.low,3)})-({Math.Round(x.high, 3)},{Math.Round(y.high, 3)})")) + $" (slope {Math.Round(slope, 3)})";
            }

            public IEnumerable<DMSStrategyPretruncation> EnumerateStrategiesDifferingInOneRange()
            {
                for (int r = 0; r < piecewiseRanges.Count; r++)
                {
                    var rng = piecewiseRanges[r];
                    foreach (var potentialYValue in DMSCalc.potentialYPoints)
                    {
                        if (potentialYValue != minForRange[0] && (r == 0 || potentialYValue > minForRange[r - 1]) && (r == piecewiseRanges.Count - 1 || potentialYValue < minForRange[r + 1]))
                        {
                            var minForRange2 = minForRange.ToList();
                            minForRange2[r] = potentialYValue;
                            foreach (var potentialSlope in DMSCalc.potentialSlopes)
                            {
                                yield return new DMSStrategyPretruncation(index, potentialSlope, minForRange2, piecewiseRanges);
                            }
                        }
                    }
                }
            }
        }
        public struct DMSStrategiesPair
        {
            public DMSStrategyWithTruncations pStrategy;
            public DMSStrategyWithTruncations dStrategy;
            public DMSCalc DMSCalc;
            public double SettlementProportion, PNet, DNet;
            public bool Nontrivial => SettlementProportion is > 0 and < 1;

            const int NumSignalsPerParty = 100;

            public DMSStrategiesPair(DMSStrategyWithTruncations pStrategy, DMSStrategyWithTruncations dStrategy, DMSCalc dmsCalc, bool calculateAnalytically)
            {
                this.pStrategy = pStrategy;
                this.dStrategy = dStrategy;
                DMSCalc = dmsCalc;
                SettlementProportion = PNet = DNet = 0;
                GetOutcomes(calculateAnalytically);
                
            }
            public DMSStrategiesPair(DMSStrategyPretruncation pPretruncation, DMSStrategyPretruncation dPretruncation, DMSCalc dmsCalc, bool calculateAnalytically)
            {
                var pLastMin = pPretruncation.minForRange[pPretruncation.minForRange.Count - 1];
                var pLastLinearRange = dmsCalc.pPiecewiseLinearRanges[dmsCalc.pPiecewiseLinearRanges.Count - 1];
                var pLastLinearRangeDistance = pLastLinearRange.high - pLastLinearRange.low;
                double pAbsoluteMax = pLastMin + pLastLinearRangeDistance * pPretruncation.slope;
                double dAbsoluteMin = dPretruncation.minForRange[0];

                pStrategy = new DMSStrategyWithTruncations(pPretruncation.index, pPretruncation.GetStrategyWithTruncation(dAbsoluteMin + 1E-10 /* be an infinitesimal amount more aggressive to prevent settlements in the event of equality */, true).lineSegments, pPretruncation.MinVal(), pPretruncation.MaxVal());
                dStrategy = new DMSStrategyWithTruncations(dPretruncation.index, dPretruncation.GetStrategyWithTruncation(pAbsoluteMax - 1E-10, false).lineSegments, dPretruncation.MinVal(), dPretruncation.MaxVal());
                DMSCalc = dmsCalc;
                SettlementProportion = PNet = DNet = 0;
                GetOutcomes(calculateAnalytically);
            }

            private void GetOutcomes(bool analytically)
            {
                if (analytically)
                    GetOutcomesAnalytically();
                else
                    GetOutcomesEmpirically();
            }
            private void GetOutcomesAnalytically()
            {
                List<DMSPartialOutcome> partialOutcomes = new List<DMSPartialOutcome>();
                foreach (LineSegment pSegment in pStrategy.lineSegments)
                {
                    foreach (LineSegment dSegment in dStrategy.lineSegments)
                    {
                        List<(LineSegment p, LineSegment d)> nonoverlappingPairs = pSegment.GetPairsOfNonoverlappingAndEntirelyOverlappingYRanges(dSegment).ToList();
                        foreach (var nonoverlappingPair in nonoverlappingPairs)
                            partialOutcomes.AddRange(DMSPartialOutcome.GetFromSegments(DMSCalc, nonoverlappingPair.p, nonoverlappingPair.d, false));
                    }
                }
                var averageOutcome = DMSCalc.GetAverageOutcome(partialOutcomes);
                SettlementProportion = averageOutcome.proportionSettling;
                PNet = averageOutcome.avgOutcome.pNet;
                DNet = averageOutcome.avgOutcome.dNet;
            }
            private void GetOutcomesEmpirically()
            {
                List<(bool settles, DMSOutcome outcome)> EmpiricalOutcomes = GetOutcomesEmpirically_Helper().ToList();
                SettlementProportion = EmpiricalOutcomes.Average(x => x.settles ? 1.0 : 0);
                PNet = EmpiricalOutcomes.Average(x => x.outcome.pNet);
                DNet = EmpiricalOutcomes.Average(x => x.outcome.dNet);
            }
            private IEnumerable<(bool settles, DMSOutcome outcome)> GetOutcomesEmpirically_Helper()
            {
                double signalDistance = 1.0 / (double)NumSignalsPerParty;
                for (int pSignalIndex = 0; pSignalIndex < NumSignalsPerParty; pSignalIndex++)
                {
                    for (int dSignalIndex = 0; dSignalIndex < NumSignalsPerParty; dSignalIndex++)
                    {
                        double pSignal = (pSignalIndex + 1) * signalDistance;
                        double dSignal = (dSignalIndex + 1) * signalDistance;
                        yield return GetOutcome(pSignal, dSignal);
                    }
                }
            }

            public (bool settles, DMSOutcome outcome) GetOutcome(double pSignal, double dSignal)
            {
                return DMSCalc.GetOutcome(pSignal, dSignal, pStrategy.GetBidForSignal(pSignal), dStrategy.GetBidForSignal(dSignal));
            }
            public override string ToString()
            {
                var pBids = pStrategy.GetBidsForSignal(10);
                string pBidsString = String.Join(",", pBids.Select(x => $"{Math.Round(x, 3)}"));
                var dBids = dStrategy.GetBidsForSignal(10);
                string dBidsString = String.Join(",", dBids.Select(x => $"{Math.Round(x, 3)}"));
                return $"{pStrategy.index}, {dStrategy.index} (Settlement {Math.Round(SettlementProportion, 3)} pNet {Math.Round(PNet, 3)} dNet {Math.Round(DNet, 3)}): P {pBidsString}; D {dBidsString}";
            }
        }

        public record struct DMSStrategyWithTruncations(int index, List<LineSegment> lineSegments, double untruncatedMinY, double untruncatedMaxY)
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
                return Enumerable.Range(0, numItems).Select(i => copy.GetBidForSignal(EquallySpaced.GetLocationOfEquallySpacedPoint(i, numItems, true)));
            }

            public static IEnumerable<(double pBid, double dBid)> GetBidsForSignal(DMSStrategiesPair pair, int numItems) => Enumerable.Range(0, numItems).Select(i => EquallySpaced.GetLocationOfEquallySpacedPoint(i, numItems, true)).Select(x => (pair.pStrategy.GetBidForSignal(x), pair.dStrategy.GetBidForSignal(x)));

            public override string ToString()
            {
                return String.Join("; ", lineSegments.Select(x => x.ToString())) + $" Untruncated range: {Math.Round(untruncatedMinY, 3)}-{Math.Round(untruncatedMaxY, 3)}";
            }
        }

        #endregion

        #region Strategy enumeration

        public static double[] potentialSlopes = new double[] { 1.0 / 6.0, 1.0 / 3.0, 1.0 / 2.0, 2.0 / 3.0, 5.0 / 6.0, 1.0 };
        const int numYPossibilities = 50;
        public static double[] potentialYPoints = Enumerable.Range(0, numYPossibilities).Select(x => EquallySpaced.GetLocationOfMidpoint(x, numYPossibilities)).ToArray();
        public IEnumerable<DMSStrategyPretruncation> EnumeratePossiblePretruncationStrategies(bool plaintiff)
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

            var piecewiseLinearRanges = plaintiff ? pPiecewiseLinearRanges : dPiecewiseLinearRanges;


            int numLinearRanges = piecewiseLinearRanges.Count;
            List<List<double>> permutationSource = new List<List<double>>();
            for (int c = 0; c < numLinearRanges; c++)
                permutationSource.Add(potentialYPoints.ToList());
            List<List<double>> possibilities = PermutationMaker.GetPermutationsOfItems(permutationSource).Where(l => IsOrdered(l)).ToList();

            int index = 0;
            foreach (double slope in potentialSlopes)
            {

                for (int j = 0; j < possibilities.Count; j++)
                {
                    List<double> minVals = possibilities[j];

                    var strategy = new DMSStrategyPretruncation(index++, slope, minVals, piecewiseLinearRanges);
                    yield return strategy;
                }
            }
        }

        #endregion
    }
}

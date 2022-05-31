using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class DMSCalc
    {
        public double T, C, Q;
        int CaseNum;
        Func<double, double> pUntruncFunc;
        Func<double, double> dUntruncFunc;
        public List<(double low, double high)> pPiecewiseLinearRanges, dPiecewiseLinearRanges;

        bool pAboveD, pEntirelyAboveD, dEntirelyAboveP;
        public int NumPiecewiseLinearRanges => pPiecewiseLinearRanges.Count;

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

        #region Truncations

        public (double pBid, double dBid) GetBids(double zP, double zD)
        {
            var fs = GetUntruncFuncs();
            return Truncated(zP, zD);
        }

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
                double threshold = range.low + (1.0 - truncationPortion) * (range.high - range.low);
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

        public double GetPiecewiseLinearBid(double z, bool plaintiff, double slope, List<double> minForRange)
        {
            var ranges = plaintiff ? pPiecewiseLinearRanges : dPiecewiseLinearRanges;
            int i = 0;
            foreach (var range in ranges)
            {
                if (z >= range.low && z < range.high)
                {
                    double distance = (z - range.low);
                    double bid = minForRange[i] + distance * slope;
                    return bid;
                }
                i++;
            }
            throw new NotImplementedException();
        }

        #endregion
    }
}

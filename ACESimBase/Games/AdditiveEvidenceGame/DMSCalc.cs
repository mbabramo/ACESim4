﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class DMSCalc
    {
        double Q, C, T;

        public DMSCalc(double q, double c, double t)
        {
            this.Q = q;
            if (Q < 1.0 / 3.0 || Q > 2.0 / 3.0)
                throw new ArgumentException("Invalid parameter under Dari-Mattiacci & Saraceno");
            this.C = c;
            this.T = t;
        }

        public (double pBid, double dBid) GetBids(double zP, double zD)
        {
            var fs = GetUntruncFuncs();
            return Truncated(zP, zD, fs.pUntruncFunc, fs.dUntruncFunc);
        }

        private (Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc) GetUntruncFuncs()
        {
            if (T <= Q && Q <= 1.0 - T)
                return (Case1UntruncatedP, Case1UntruncatedD);
            if (Q < T && T < 1.0 - Q)
                return (Case2UntruncatedP, Case2UntruncatedD);
            if (1.0 - Q < T && T < Q)
                return (Case3UntruncatedP, Case3UntruncatedD);
            return (Case4UntruncatedP, Case4UntruncatedD);
        }

        private (double pBid, double dBid) Truncated(double zP, double zD, Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc)
        {
            bool truncate = true; 
            if (!truncate)
                return (pUntruncFunc(zP), dUntruncFunc(zD));
            bool includeIrrelevantTruncations = false; 
            // In DMS, P bids that are always higher than D's are truncated to their min value, since they will never result in settlement anyway.
            // Similarly, if D bids are alwayer lower than p's, 
            bool pAboveD = PAboveD(pUntruncFunc, dUntruncFunc);
            bool standardizeTrivialEq = true; // if P's most generous bid is greater than the D's most generous bid, there will never be a settlement, regardless of the signals. This is a trivial equilibrium, and so we can standardize those.
            double pBid = 1, dBid = 0;
            if (pAboveD)
            {
                if (standardizeTrivialEq && PAlwaysAboveD(pUntruncFunc, dUntruncFunc))
                    return (1.0, 0);
                if (includeIrrelevantTruncations)
                {
                    pBid = Math.Min(pUntruncFunc(zP), dUntruncFunc(1.0)); // the case won't settle whether P offers a bid higher than the highest possible D bid of just at that value in the DMS model, so these are equivalent equilibria
                    dBid = Math.Max(pUntruncFunc(0), dUntruncFunc(zD)); // the case won't settle whether D offers a bid less than the lowest possible P bid or just at that value in the DMS model, so these are equivalent equilibria
                }
            }
            else
            {
                if (DAlwaysAboveP(pUntruncFunc, dUntruncFunc))
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

        private bool PAboveD(Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc)
        {

            bool pAboveD = pUntruncFunc(1.0) > dUntruncFunc(1.0);
            bool pAboveD2 = pUntruncFunc(0.0) > dUntruncFunc(0.0);
            if (pAboveD != pAboveD2)
                throw new Exception();
            return pAboveD;
        }

        private bool PAlwaysAboveD(Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc)
        {
            return pUntruncFunc(0.0) > dUntruncFunc(1.0);
        }

        private bool DAlwaysAboveP(Func<double, double> pUntruncFunc, Func<double, double> dUntruncFunc)
        {
            return pUntruncFunc(1.0) < dUntruncFunc(0.0);
        }


        private double Case1UntruncatedP(double zP) => 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        private double Case1UntruncatedD(double zD) => 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;

        private double Case2UntruncatedP(double zP)
        {
            bool caseA = zP < 6.0 * C - 1.0 + (T - Q) / (1.0 - Q);
            if (caseA)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case2UntruncatedD(double zD)
        {
            bool caseA = zD < (T - Q) / (1.0 - Q);
            if (caseA)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
        }
        private double Case3UntruncatedP(double zP)
        {
            bool caseA = zP <= (1.0 - T) / Q;
            if (caseA)
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case3UntruncatedD(double zD)
        {
            bool caseA = zD <= 1.0 - 6.0 * C + (1.0 - T) / Q;
            if (caseA)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }
        private double Case4UntruncatedP(double zP)
        {
            double expression = 6.0 * C - 1.0 + (T - Q) / (1.0 - Q);
            double expression2 = (1.0 - T) / Q;

            bool caseA = zP < expression;
            bool caseB = expression <= zP && zP <= expression2;
            if (caseA)
                return 1.0 / 2.0 - 3.0 * (1.0 - Q) * C + (1.0 / 3.0) * zP;
            else if (caseB)
                return 1.0 / 2.0 - 3.0 * (5.0 / 6.0 - Q) * C + (1.0 / 3.0) * zP;
            else
                return 1.0 / 2.0 - 3.0 * (2.0 / 3.0 - Q) * C + (1.0 / 3.0) * zP;
        }
        private double Case4UntruncatedD(double zD)
        {
            double expression = (T - Q) / (1.0 - Q);
            double expression2 = 1.0 - 6.0 * C + (1.0 - T) / Q;

            bool caseA = zD < expression;
            bool caseB = expression <= zD && zD <= expression2;
            if (caseA)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 3.0) * C + (1.0 / 3.0) * zD;
            else if (caseB)
                return 1.0 / 6.0 + 3.0 * (Q - 1.0 / 6.0) * C + (1.0 / 3.0) * zD;
            else
                return 1.0 / 6.0 + 3.0 * Q * C + (1.0 / 3.0) * zD;
        }

        // Truncations. If the P line is above the D line, then P is truncated above at D(1), i.e. will be Min(untruncated P(x)
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESim.ArrayFormConversionExtension;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.RationalOperations;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.BigIntegerOperations;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.ColumnPrinter;
using static ACESimBase.Util.CPrint;
using JetBrains.Annotations;
using Rationals;
using System.Numerics;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class Lemke
    {

        int n;   /* LCP (Linear Complementarity Problem) dimension as used here   */

        /* LCP input    */
        public Rational[][] lcpM;
        public Rational[] rhsq;
        public Rational[] vecd;
        public int lcpdim = 0; /* set in setlcp                */

        /* LCP result   */
        public Rational[] solz;
        public int pivotcount;

        /* tableau:    */
        public BigInteger[][] A;                 /* tableau                              */
        public int[] bascobas;          /* VARS  -> ROWCOL                      */
        public int[] whichvar;          /* ROWCOL -> VARS, inverse of bascobas  */

        /* used for tableau:    */
        public int Z(int i)
        {
            return i;
        }
        public int W(int i)
        {
            return i + n;
        }
        /* VARS   = 0..2n = Z(0) .. Z(n) W(1) .. W(n)           */
        /* ROWCOL = 0..2n,  0 .. n-1: tabl rows (basic vars)    */
        /*                  n .. 2n:  tabl cols  0..n (cobasic) */
        public int RHS()
        {
            return n + 1;
        }
        public int TABCOL(int v)
        {
            return bascobas[v] - n;
        }
        /*  v in VARS, v cobasic:  TABCOL(v) is v's tableau col */
        /*  v  basic:  TABCOL(v) < 0,  TABCOL(v)+n   is v's row */

        /* scale factors for variables z
		 * scfa[Z(0)]   for  d,  scfa[RHS()] for  q
		 * scfa[Z(1..n)] for cols of  M
		 * result variables to be multiplied with these
		 */
        public BigInteger[] scfa;

        public BigInteger det = new BigInteger();                        /* determinant                  */

        public int[] lextested, lexcomparisons;/* statistics for lexminvar     */

        public int[] leavecand;
        /* should be local to lexminvar but defined globally for economy    */

        public const int MAXLCPDIM = 2000;       /* max LCP dimension                       */
        public const int INFOSTRINGLENGTH = 8;   /* string naming vars, e.g. "z0", "w187"   */
        public const int LCPSTRL = 60;          /* length of string containing LCP entry   */

        /*------------------ error message ----------------*/
        public void errexit(string info)
        {
            throw new Exception($"Error {info}. Lemke terminated unsuccessfully.");
        }

        /*------------------ memory allocation -------------------------*/
        public Lemke(int newn)
        {
            if (newn < 1 || newn > MAXLCPDIM)
            {
                throw new Exception($"Problem dimension  n= {newn} not allowed. Minimum  n  is 1, maximum {MAXLCPDIM}");
            }
            n = lcpdim = newn;
            /* LCP input/output data    */
            lcpM = CreateJaggedArray<Rational[][]>(n, n);
            rhsq = new Rational[n];
            vecd = new Rational[n];
            solz = new Rational[n];
            for (int i = 0; i < n; i++)
            {
                rhsq[i] = new Rational();
                vecd[i] = new Rational();
                solz[i] = new Rational();
            }
            /* tableau          */
            A = CreateJaggedArray<BigInteger[][]>(n, n + 2);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n + 2; j++)
                    A[i][j] = new BigInteger();
            scfa = new BigInteger[n + 2];
            for (int i = 0; i < n + 2; i++)
                scfa[i] = new BigInteger();
            bascobas = new int[2 * n + 1];
            whichvar = new int[2 * n + 1];
            lextested = new int[n + 1];
            lexcomparisons = new int[n + 1];
            leavecand = new int[n];
            /* should be local to lexminvar but allocated here for economy  */
            /* initialize all LCP entries to zero       */
            {
                int i, j;
                Rational zero = ratfromi(0);
                for (i = 0; i < n; i++)
                {
                    for (j = 0; j < n; j++)
                        lcpM[i][j] = zero;
                    vecd[i] = rhsq[i] = zero;
                }
            }
        } /* end of  setlcp(newn)       */

        /* asserts that  d >= 0  and not  q >= 0  (o/w trivial sol) 
		 * and that q[i] < 0  implies  d[i] > 0
		 */
        public void ConfirmCoveringVectorOK()
        {
            int i;
            bool isqpos = true;
            for (i = 0; i < n; i++)
            {
                if (vecd[i].Numerator < 0)
                {
                    throw new Exception($"Covering vector  d[{i + 1}] = {vecd[i]} negative\n");
                }
                else if (rhsq[i].Numerator < 0)
                {
                    isqpos = false;
                    if (vecd[i].Numerator == 0)
                    {
                        throw new Exception($"Covering vector  d[{i + 1}] = 0  where  q[{i + 1}] = {rhsq[i]}  is negative. Cannot start Lemke");
                    }
                }
            }   /* end of  for(i=...)   */
            if (isqpos)
            {
                tabbedtextf("No need to start Lemke since  q>=0. Trivial solution z = 0.");
                return;
            }
        }       /* end of  isqdok()     */

        /* ------------------- tableau setup ------------------ */
        public void InitializeTableauVariables()
        /* init tableau variables:                      */
        /* Z(0)...Z(n)  nonbasic,  W(1)...W(n) basic    */
        {
            int i;
            for (i = 0; i <= n; i++)
            {
                bascobas[Z(i)] = n + i;
                whichvar[n + i] = Z(i);
            }
            for (i = 1; i <= n; i++)
            {
                bascobas[W(i)] = i - 1;
                whichvar[i - 1] = W(i);
            }
        }       /* end of inittablvars()        */

        public void FillTableau()
        /* fill tableau from  M, q, d   */
        {
            int i, j;
            BigInteger den, num;
            BigInteger tmp, tmp2;
            for (j = 0; j <= n + 1; j++)
            {
                /* compute lcm  scfa[j]  of denominators for  col  j  of  A         */
                scfa[j] = 1;
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? vecd[i].Denominator :
                      (j == RHS()) ? rhsq[i].Denominator : lcpM[i][j - 1].Denominator;
                    lcm(ref scfa[j], den);
                }
                /* fill in col  j  of  A    */
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? vecd[i].Denominator :
                      (j == RHS()) ? rhsq[i].Denominator : lcpM[i][j - 1].Denominator;
                    num = (j == 0) ? vecd[i].Numerator :
                      (j == RHS()) ? rhsq[i].Numerator : lcpM[i][j - 1].Numerator;
                    /* cols 0..n of  A  contain LHS cobasic cols of  Ax = b     */
                    /* where the system is here         -Iw + dz_0 + Mz = -q    */
                    /* cols of  q  will be negated after first min ratio test   */
                    /* A[i][j] = num * (scfa[j] / den),  fraction is integral       */
                    tmp = den;
                    tmp2 = scfa[j] / tmp;
                    tmp = num;
                    BigInteger c = 0;
                    mulint(tmp2, tmp, ref c);
                    SetValueInA(i, j, c);
                }
            }   /* end of  for(j=...)   */
            InitializeTableauVariables();
            det = (BigInteger)1;
            changesign(ref det);
        }       /* end of filltableau()         */

        /* ---------------- output routines ------------------- */
        public void OutputLCP()
        /* output the LCP as given      */
        {
            int i, j;
            Rational a;
            string s = null;

            tabbedtextf("LCP dimension: %d\n", n);
            colset(n + 2);
            for (j = 0; j < n; j++)
                colpr("");
            colpr("d");
            colpr("q");
            colnl();
            for (i = 0; i < n; i++)
            {
                for (j = 0; j < n; j++)
                {
                    a = lcpM[i][j];
                    if (a.Numerator == 0)
                        colpr(".");
                    else
                    {
                        rattoa(a, ref s);
                        colpr(s);
                    }
                }
                rattoa(vecd[i], ref s);
                colpr(s);
                rattoa(rhsq[i], ref s);
                colpr(s);
            }
            colout();
        }

        public int VariableToString(int v, ref string s)
        /* create string  s  representing  v  in  VARS,  e.g. "w2"    */
        /* return value is length of that string                      */
        {
            if (v > n)
                s = sprintf("w%d", v - n);
            else
                s = sprintf("z%d", v);
            return s.Length;
        }

        public void OutputTableau()
        /* output the current tableau, column-adjusted                  */
        {
            int i, j;
            string s = null;
            string smp = null;
            //char[] s = new char[INFOSTRINGLENGTH];
            //char[] smp = new char[MultiprecisionStatic.Dig2Dec(MAX_DIGITS) + 2];       /* string to print  mp  into    */
            smp = det.ToStringForTable();
            tabbedtextf("Determinant: %s\n", smp);
            colset(n + 3);
            colleft(0);
            colpr("var");                   /* headers describing variables */
            for (j = 0; j <= n + 1; j++)
            {
                if (j == RHS())
                    colpr("RHS()");
                else
                {
                    VariableToString(whichvar[j + n], ref s);
                    colpr(s);
                }
            }
            colpr("scfa");                  /* scale factors                */
            for (j = 0; j <= n + 1; j++)
            {
                if (j == RHS())
                    smp = scfa[RHS()].ToStringForTable();
                else if (whichvar[j + n] > n) /* col  j  is some  W           */
                    smp = sprintf("1");
                else                        /* col  j  is some  Z:  scfa    */
                    smp = scfa[whichvar[j + n]].ToStringForTable();
                colpr(smp);
            }
            colnl();
            for (i = 0; i < n; i++)             /* print row  i                 */
            {
                VariableToString(whichvar[i], ref s);
                colpr(s);
                for (j = 0; j <= n + 1; j++)
                {
                    smp = A[i][j].ToStringForTable();
                    if (smp == "0")
                        colpr(".");
                    else
                        colpr(smp);
                }
            }
            colout();
            tabbedtextf("-----------------end of tableau-----------------\n");
        }       /* end of  outtabl()                                    */

        /* output the current basic solution            */
        public void OutputSolution()
        {
            string s = null;
            string smp = null;
            //char s[INFOSTRINGLENGTH];
            //char smp[2 * DIG2DEC(MAX_DIGITS) + 4];
            /* string to print 2 mp's  into                 */
            int i, row, pos;
            BigInteger num = 0, den = 0;

            colset(n + 2);    /* column printing to see complementarity of  w  and  z */

            colpr("basis=");
            for (i = 0; i <= n; i++)
            {
                if (bascobas[Z(i)] < n)
                    /*  Z(i) is a basic variable        */
                    VariableToString(Z(i), ref s);
                else if (i > 0 && bascobas[W(i)] < n)
                    /*  Z(i) is a basic variable        */
                    VariableToString(W(i), ref s);
                else
                    s = "  ";
                colpr(s);
            }

            colpr("z=");
            for (i = 0; i <= 2 * n; i++)
            {
                if ((row = bascobas[i]) < n)  /*  i  is a basic variable           */
                {
                    if (i <= n)       /* printing Z(i)        */
                        /* value of  Z(i):  scfa[Z(i)]*rhs[row] / (scfa[RHS()]*det)   */
                        mulint(scfa[Z(i)], A[row][RHS()], ref num);
                    else            /* printing W(i-n)      */
                        /* value of  W(i-n)  is  rhs[row] / (scfa[RHS()]*det)         */
                        copy(ref num, A[row][RHS()]);
                    mulint(det, scfa[RHS()], ref den);
                    Rational r = (num / (Rational) den).CanonicalForm;
                    num = r.Numerator;
                    den = r.Denominator;
                    smp = num.ToStringForTable();
                    pos = smp.Length;
                    if (!one(den))  /* add the denominator  */
                    {
                        bool formatAsDoubles = true;
                        if (formatAsDoubles)
                        {
                            double d = (double)r;
                            smp = d.ToString();
                            if (smp.Length > 8)
                                smp = d.ToString("E5");
                        }
                        else
                        {
                            smp += "/";
                            string denstring = null;
                            denstring = den.ToStringForTable();
                            smp += denstring;
                        }
                    }
                    colpr(smp);
                }
                else            /* i is nonbasic    */
                    colpr("0");
                if (i == n)       /* new line since printing slack vars  w  next      */
                {
                    colpr("w=");
                    colpr("");  /* for complementarity in place of W(0)             */
                }
            }   /* end of  for (i=...)          */
            colout();
        }       /* end of outsol                */

        /* current basic solution turned into  solz [0..n-1]
		 * note that Z(1)..Z(n)  become indices  0..n-1
		 */
        public void TransformBasicSolution()
        {
            int i, row;
            BigInteger num = 0, den = 0;

            for (i = 1; i <= n; i++)
                if ((row = bascobas[i]) < n)  /*  i  is a basic variable */
                {
                    /* value of  Z(i):  scfa[Z(i)]*rhs[row] / (scfa[RHS()]*det)   */
                    mulint(scfa[Z(i)], A[row][RHS()], ref num);
                    mulint(det, scfa[RHS()], ref den);
                    solz[i - 1] = ((Rational) num / (Rational) den).CanonicalForm;
                }
                else            /* i is nonbasic    */
                    solz[i - 1] = ratfromi(0);
        } /* end of copysol                     */

        /* --------------- test output and exception routines ---------------- */
        public void AssertVariableIsBasic(int v)
        /* assert that  v  in VARS is a basic variable         */
        /* otherwise error printing  info  where               */
        {
            string s = null;
            if (bascobas[v] >= n)
            {
                VariableToString(v, ref s);
                throw new Exception($"Cobasic variable {s} should be basic.\n");
            }
        }

        public void AssertVariableIsCobasic(int v)
        /* assert that  v  in VARS is a cobasic variable       */
        /* otherwise error printing  info  where               */
        {
            string s = null;
            if (TABCOL(v) < 0)
            {
                VariableToString(v, ref s);
                throw new Exception($"Basic variable {s} should be cobasic.\n");
            }
        }

        public void OutputPivotLeaveAndEnter(int leave, int enter)
        /* leave, enter in  VARS.  Documents the current pivot. */
        /* Asserts  leave  is basic and  enter is cobasic.      */
        {
            string s = null;

            AssertVariableIsBasic(leave);
            AssertVariableIsCobasic(enter);

            VariableToString(leave, ref s);
            tabbedtextf("leaving: %-4s ", s);
            VariableToString(enter, ref s);
            tabbedtextf("entering: %s\n", s);
        }       /* end of  docupivot    */

        void ThrowRayTerminationException(int enter)
        {
            string s = null;
            VariableToString(enter, ref s);
            tabbedtextf($"Ray termination when trying to enter {s}\n");
            OutputTableau();
            tabbedtextf("Current basis, not an LCP solution:\n");
            OutputSolution();
            throw new Exception("Ray termination; current basis, not an LCP solution");
        }

        public void TestTableauVariables()
        /* test tableau variables: error => msg only, continue  */
        {
            int i, j;
            for (i = 0; i <= 2 * n; i++)  /* check if somewhere tableauvars wrong */
                if (bascobas[whichvar[i]] != i || whichvar[bascobas[i]] != i)
                /* found an inconsistency, print everything             */
                {
                    tabbedtextf("Inconsistent tableau variables:\n");
                    for (j = 0; j <= 2 * n; j++)
                    {
                        tabbedtextf("var j:%3d bascobas:%3d whichvar:%3d ",
                            j, bascobas[j], whichvar[j]);
                        tabbedtextf(" b[w[j]]==j: %1d  w[b[j]]==j: %1d\n",
                            bascobas[whichvar[j]] == j, whichvar[bascobas[j]] == j);
                    }
                    break;
                }
        }

        /* --------------- pivoting and related routines -------------- */

        /* complement of  v  in VARS, error if  v==Z(0).
         * this is  W(i) for Z(i)  and vice versa, i=1...n
         */
        public int ComplementOfVariable(int v)
        {
            if (v == Z(0))
                errexit("Attempt to find complement of z0.");
            return (v > n) ? Z(v - n) : W(v);
        }       /* end of  complement (v)     */

        /* initialize statistics for minimum ratio test
         */
        public void InitMinRatioTestStatistics()
        {
            int i;
            for (i = 0; i <= n; i++)
                lextested[i] = lexcomparisons[i] = 0;
        }

        /* output statistics of minimum ratio test
         */
        public void OutputMinRatioTestStatistics()
        {
            int i;
            string s = null;

            colset(n + 2);
            colleft(0);
            colpr("lex-column");
            for (i = 0; i <= n; i++)
                colipr(i);
            colnl();
            colpr("times tested");
            for (i = 0; i <= n; i++)
                colipr(lextested[i]);
            colpr("% times tested");
            if (lextested[0] > 0)
            {
                colpr("100");
                for (i = 1; i <= n; i++)
                {
                    s = sprintf("%2.0f",
                            (double)lextested[i] * 100.0 / (double)lextested[0]);
                    colpr(s);
                }
            }
            else
                colnl();
            colpr("avg comparisons");
            for (i = 0; i <= n; i++)
                if (lextested[i] > 0)
                {
                    s = sprintf("%1.1f",
                        (double)lexcomparisons[i] / (double)lextested[i]);
                    colpr(s);
                }
                else
                    colpr("-");
            colout();
        }

        /* returns the leaving variable in  VARS, given by lexmin row, 
         * when  enter  in VARS is entering variable
         * only positive entries of entering column tested
         * boolean  *z0leave  indicates back that  z0  can leave the
         * basis, but the lex-minratio test is performed fully,
         * so the returned value might not be the index of  z0
         */
        public int GetLeavingVariable(int enter, ref bool z0leave)
        {
            int col, i, j, testcol;
            int numcand;

            AssertVariableIsCobasic(enter);
            col = TABCOL(enter);
            numcand = 0;
            /* leavecand [0..numcand-1] = candidates (rows) for leaving var */
            /* start with  leavecand = { i | A[i][col] > 0 }                        */
            for (i = 0; i < n; i++)
            {
                if (positive(A[i][col]))
                    leavecand[numcand++] = i;
            }
            if (numcand == 0)
                ThrowRayTerminationException(enter);
            if (numcand == 1)
            {
                lextested[0] += 1;
                lexcomparisons[0] += 1;
                z0leave = (leavecand[0] == bascobas[Z(0)]);
            }
            for (j = 0; numcand > 1; j++)
            /* as long as there is more than one leaving candidate perform
             * a minimum ratio test for the columns of  j  in RHS(), W(1),... W(n)
             * in the tableau.  That test has an easy known result if
             * the test column is basic or equal to the entering variable.
             */
            {
                if (j > n)    /* impossible, perturbed RHS() should have full rank  */
                    errexit("lex-minratio test failed");
                lextested[j] += 1;
                lexcomparisons[j] += numcand;

                testcol = (j == 0) ? RHS() : TABCOL(W(j));
                if (testcol != col)       /* otherwise nothing will change      */
                {
                    if (testcol >= 0)
                    /* not a basic testcolumn: perform minimum ratio tests          */
                    {
                        int sgn;
                        int newnum = 0;
                        /* leavecand[0..newnum]  contains the new candidates    */
                        for (i = 1; i < numcand; i++)
                        /* investigate remaining candidates                         */
                        {
                            sgn = comprod(A[leavecand[0]][testcol], A[leavecand[i]][col],
                                      A[leavecand[i]][testcol], A[leavecand[0]][col]);
                            /* sign of  A[l_0,t] / A[l_0,col] - A[l_i,t] / A[l_i,col]   */
                            /* note only positive entries of entering column considered */
                            if (sgn == 0)         /* new ratio is the same as before      */
                                leavecand[++newnum] = leavecand[i];
                            else if (sgn == 1)    /* new smaller ratio detected           */
                                leavecand[newnum = 0] = leavecand[i];
                        }
                        numcand = newnum + 1;
                    }
                    else
                        /* testcol < 0: W(j) basic, Eliminate its row from leavecand    */
                        /* since testcol is the  jth  unit column                       */
                        for (i = 0; i < numcand; i++)
                            if (leavecand[i] == bascobas[W(j)])
                            {
                                leavecand[i] = leavecand[--numcand];
                                /* shuffling of leavecand allowed       */
                                break;
                            }
                }   /* end of  if(testcol != col)                           */

                if (j == 0)
                    /* seek  z0  among the first-col leaving candidates     */
                    for (i = 0; i < numcand; i++)
                        if ((z0leave = (leavecand[i] == bascobas[Z(0)])))
                            break;
                /* alternative, to force z0 leaving the basis:
                 * return whichvar[leavecand[i]];
                 */
            }       /* end of  for ( ... numcand > 1 ... )   */
            return whichvar[leavecand[0]];
        }       /* end of lexminvar (col, *z0leave);                        */


        public void NegateTableauColumn(int col)
        /* negate tableau column  col   */
        {
            int i;
            for (i = 0; i < n; i++)
                ChangeSignInA(i, col);
        }

        public void NegateTableauRow(int row)
        /* negate tableau row.  Used in  pivot()        */
        {
            int j;
            for (j = 0; j <= n + 1; j++)
                if (!zero(A[row][j]))
                    ChangeSignInA(row, j);
        }

        /* leave, enter in  VARS  defining  row, col  of  A
         * pivot tableau on the element  A[row][col] which must be nonzero
         * afterwards tableau normalized with positive determinant
         * and updated tableau variables
         */
        int pivotnum = 0;
        public void Pivot(int leave, int enter)
        {
            int row, col, i, j;
            bool nonzero, negpiv;
            BigInteger pivelt = 0, tmp1 = 0, tmp2 = 0;

            row = bascobas[leave];
            col = TABCOL(enter);

            copy(ref pivelt, A[row][col]);     /* pivelt anyhow later new determinant  */
            negpiv = negative(pivelt);
            if (negpiv)
                changesign(ref pivelt);
            for (i = 0; i < n; i++)
                if (i != row)               /*  A[row][..]  remains unchanged       */
                {
                    nonzero = !zero(A[i][col]);
                    for (j = 0; j <= n + 1; j++)      /*  assume here RHS()==n+1        */
                        if (j != col)
                        /*  A[i,j] =
                           (A[i,j] A[row,col] - A[i,col] A[row,j])/ det     */
                        {
                            mulint(A[i][j], pivelt, ref tmp1);
                            if (nonzero)
                            {
                                mulint(A[i][col], A[row][j], ref tmp2);
                                if (negpiv)
                                    tmp1 = tmp1 + tmp2;
                                else
                                    tmp1 = tmp1 - tmp2;
                            }
                            BigInteger c = 0;
                            divint(tmp1, det, ref c);
                            SetValueInA(i, j, c);
                        }
                    /* row  i  has been dealt with, update  A[i][col]  safely   */
                    if (nonzero && !negpiv)
                        ChangeSignInA(i, col);
                }       /* end of  for (i=...)                              */
            BigInteger temp = 0;
            copy(ref temp, det);
            SetValueInA(row, col, temp);
            if (negpiv)
                NegateTableauRow(row);
            copy(ref det, pivelt);      /* by construction always positive      */

            /* update tableau variables                                     */
            bascobas[leave] = col + n; whichvar[col + n] = leave;
            bascobas[enter] = row; whichvar[row] = enter;
            pivotnum++;
        }       /* end of  pivot (leave, enter)                         */


        void ChangeSignInA(int i, int j)
        {
            changesign(ref A[i][j]);
        }

        void SetValueInA(int i, int j, BigInteger value)
        {
            copy(ref A[i][j], value);
        }

        /* ------------------------------------------------------------ */
        public void RunLemke(LemkeOptions flags)
        {
            int leave, enter;
            bool z0leave = false;

            pivotcount = 1;
            InitMinRatioTestStatistics();

            ConfirmCoveringVectorOK();
            /*  tabbedtextf("LCP seems OK.\n");      */

            FillTableau();
            /*  tabbedtextf("Tableau filled.\n");    */

            if (flags.outputInitialTableau)
            {
                tabbedtextf("After filltableau:\n");
                OutputTableau();
            }

            /* z0 enters the basis to obtain lex-feasible solution      */
            enter = Z(0);
            leave = GetLeavingVariable(enter, ref z0leave);

            /* now give the entering q-col its correct sign             */
            NegateTableauColumn(RHS());

            if (flags.outputTableaux)
            {
                tabbedtextf("After negcol:\n");
                OutputTableau();
            }
            while (true)       /* main loop of complementary pivoting                  */
            {
                TestTableauVariables();
                if (flags.outputPivotingSteps)
                    OutputPivotLeaveAndEnter(leave, enter);
                Pivot(leave, enter);
                if (z0leave)
                {
                    break;  /* z0 will have value 0 but may still be basic. Amend?  */
                }
                if (flags.outputTableaux)
                    OutputTableau();
                enter = ComplementOfVariable(leave);
                leave = GetLeavingVariable(enter, ref z0leave);
                if (pivotcount++ == flags.maxPivotSteps)
                {
                    tabbedtextf("------- stop after %d pivoting steps --------\n",
                   flags.maxPivotSteps);
                    break;
                }
            }

            if (flags.outputInitialTableau)
            {
                tabbedtextf("Final tableau:\n");
                OutputTableau();
            }
            if (flags.outputLexStats)
                OutputMinRatioTestStatistics();
            if (flags.outputSolution)
                OutputSolution();

            TransformBasicSolution();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESim.ArrayFormConversionExtension;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.RatStatic;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.MultiprecisionStatic;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.ColumnPrinter;
using static ACESimBase.Util.CPrint;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class Lemke
    {

        static int n;   /* LCP dimension as used here   */

        /* LCP input    */
        public Rat[][] lcpM;
        public Rat[] rhsq;
        public Rat[] vecd;
        public int lcpdim = 0; /* set in setlcp                */

        /* LCP result   */
        public Rat[] solz;
        public int pivotcount;

        /* tableau:    */
        public static Multiprecision[][] A;                 /* tableau                              */
        public static int[] bascobas;          /* VARS  -> ROWCOL                      */
        public static int[] whichvar;          /* ROWCOL -> VARS, inverse of bascobas  */

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
        public static Multiprecision[] scfa;

        public static Multiprecision det = new Multiprecision();                        /* determinant                  */

        public static int[] lextested, lexcomparisons;/* statistics for lexminvar     */

        public static int[] leavecand;
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
        public void setlcp(int newn)
        {
            if (newn < 1 || newn > MAXLCPDIM)
            {
                throw new Exception($"Problem dimension  n= {newn} not allowed. Minimum  n  is 1, maximum {MAXLCPDIM}");
            }
            n = lcpdim = newn;
            /* LCP input/output data    */
            lcpM = CreateJaggedArray<Rat[][]>(n, n);
            rhsq = new Rat[n];
            vecd = new Rat[n];
            solz = new Rat[n];
            for (int i = 0; i < n; i++)
            {
                rhsq[i] = new Rat();
                vecd[i] = new Rat();
                solz[i] = new Rat();
            }
            /* tableau          */
            A = CreateJaggedArray<Multiprecision[][]>(n, n + 2);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n + 2; j++)
                    A[i][j] = new Multiprecision();
            scfa = new Multiprecision[n + 2];
            for (int i = 0; i < n + 2; i++)
                scfa[i] = new Multiprecision();
            bascobas = new int[2 * n + 1];
            whichvar = new int[2 * n + 1];
            lextested = new int[n + 1];
            lexcomparisons = new int[n + 1];
            leavecand = new int[n];
            /* should be local to lexminvar but allocated here for economy  */
            /* initialize all LCP entries to zero       */
            {
                int i, j;
                Rat zero = ratfromi(0);
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
        public void isqdok()
        {
            int i;
            bool isqpos = true;
            for (i = 0; i < n; i++)
            {
                if (vecd[i].num < 0)
                {
                    throw new Exception($"Covering vector  d[{i + 1}] = {vecd[i].num}/{vecd[i].den} negative\n");
                }
                else if (rhsq[i].num < 0)
                {
                    isqpos = false;
                    if (vecd[i].num == 0)
                    {
                        throw new Exception($"Covering vector  d[{i + 1}] = 0  where  q[{i + 1}] = {rhsq[i].num}/{rhsq[i].den}  is negative. Cannot start Lemke");
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
        public void inittablvars()
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

        public void filltableau()
        /* fill tableau from  M, q, d   */
        {
            int i, j;
            long den, num;
            Multiprecision tmp = new Multiprecision(), tmp2 = new Multiprecision(), tmp3 = new Multiprecision();
            for (j = 0; j <= n + 1; j++)
            {
                /* compute lcm  scfa[j]  of denominators for  col  j  of  A         */
                itomp(MultiprecisionStatic.ONE, scfa[j]);
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? vecd[i].den :
                      (j == RHS()) ? rhsq[i].den : lcpM[i][j - 1].den;
                    itomp(den, tmp);
                    lcm(scfa[j], tmp);
                }
                /* fill in col  j  of  A    */
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? vecd[i].den :
                      (j == RHS()) ? rhsq[i].den : lcpM[i][j - 1].den;
                    num = (j == 0) ? vecd[i].num :
                      (j == RHS()) ? rhsq[i].num : lcpM[i][j - 1].num;
                    /* cols 0..n of  A  contain LHS cobasic cols of  Ax = b     */
                    /* where the system is here         -Iw + dz_0 + Mz = -q    */
                    /* cols of  q  will be negated after first min ratio test   */
                    /* A[i][j] = num * (scfa[j] / den),  fraction is integral       */
                    itomp(den, tmp);
                    copy(tmp3, scfa[j]);
                    divint(tmp3, tmp, tmp2);        /* divint modifies 1st argument */
                    itomp(num, tmp);
                    var c = new Multiprecision();
                    mulint(tmp2, tmp, c);
                    setinA(i, j, c);
                }
            }   /* end of  for(j=...)   */
            inittablvars();
            itomp(ONE, det);
            changesign(det);
        }       /* end of filltableau()         */

        /* ---------------- output routines ------------------- */
        public void outlcp()
        /* output the LCP as given      */
        {
            int i, j;
            Rat a;
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
                    if (a.num == 0)
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

        public int vartoa(int v, ref string s)
        /* create string  s  representing  v  in  VARS,  e.g. "w2"    */
        /* return value is length of that string                      */
        {
            if (v > n)
                s = sprintf("w%d", v - n);
            else
                s = sprintf("z%d", v);
            return s.Length;
        }


        public void outtabl()
        /* output the current tableau, column-adjusted                  */
        {
            int i, j;
            string s = null;
            string smp = null;
            //char[] s = new char[INFOSTRINGLENGTH];
            //char[] smp = new char[MultiprecisionStatic.Dig2Dec(MAX_DIGITS) + 2];       /* string to print  mp  into    */
            mptoa(det, ref smp);
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
                    vartoa(whichvar[j + n], ref s);
                    colpr(s);
                }
            }
            colpr("scfa");                  /* scale factors                */
            for (j = 0; j <= n + 1; j++)
            {
                if (j == RHS())
                    mptoa(scfa[RHS()], ref smp);
                else if (whichvar[j + n] > n) /* col  j  is some  W           */
                    smp = sprintf("1");
                else                        /* col  j  is some  Z:  scfa    */
                    mptoa(scfa[whichvar[j + n]], ref smp);
                colpr(smp);
            }
            colnl();
            for (i = 0; i < n; i++)             /* print row  i                 */
            {
                vartoa(whichvar[i], ref s);
                colpr(s);
                for (j = 0; j <= n + 1; j++)
                {
                    mptoa(A[i][j], ref smp);
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
        public void outsol()
        {
            string s = null;
            string smp = null;
            //char s[INFOSTRINGLENGTH];
            //char smp[2 * DIG2DEC(MAX_DIGITS) + 4];
            /* string to print 2 mp's  into                 */
            int i, row, pos;
            Multiprecision num = new Multiprecision(), den = new Multiprecision();

            colset(n + 2);    /* column printing to see complementarity of  w  and  z */

            colpr("basis=");
            for (i = 0; i <= n; i++)
            {
                if (bascobas[Z(i)] < n)
                    /*  Z(i) is a basic variable        */
                    vartoa(Z(i), ref s);
                else if (i > 0 && bascobas[W(i)] < n)
                    /*  Z(i) is a basic variable        */
                    vartoa(W(i), ref s);
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
                        mulint(scfa[Z(i)], A[row][RHS()], num);
                    else            /* printing W(i-n)      */
                        /* value of  W(i-n)  is  rhs[row] / (scfa[RHS()]*det)         */
                        copy(num, A[row][RHS()]);
                    mulint(det, scfa[RHS()], den);
                    reduce(num, den);
                    pos = mptoa(num, ref smp);
                    if (!one(den))  /* add the denominator  */
                    {
                        smp += "/";
                        string denstring = null;
                        mptoa(den, ref denstring);
                        smp += denstring;
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
		 * gives a warning if conversion to ordinary rational fails
		 * and returns 1, otherwise 0
		 */
        public bool notokcopysol()
        {
            bool notok = false;
            int i, row;
            Multiprecision num = new Multiprecision(), den = new Multiprecision();

            for (i = 1; i <= n; i++)
                if ((row = bascobas[i]) < n)  /*  i  is a basic variable */
                {
                    /* value of  Z(i):  scfa[Z(i)]*rhs[row] / (scfa[RHS()]*det)   */
                    mulint(scfa[Z(i)], A[row][RHS()], num);
                    mulint(det, scfa[RHS()], den);
                    reduce(num, den);
                    long num2 = solz[i - 1].num;
                    long den2 = solz[i - 1].den;
                    if (mptoi(num, out num2, 1))
                    {
                        tabbedtextf($"(Numerator of z{i} overflown)\n");
                        notok = true;
                    }
                    if (mptoi(den, out den2, 1))
                    {
                        tabbedtextf($"(Denominator of z{i} overflown)\n");
                        notok = true;
                    }
                    solz[i - 1].num = num2;
                    solz[i - 1].den = den2;
                }
                else            /* i is nonbasic    */
                    solz[i - 1] = ratfromi(0);
            return notok;
        } /* end of copysol                     */

        /* --------------- test output and exception routines ---------------- */
        public void assertbasic(int v, string info)
        /* assert that  v  in VARS is a basic variable         */
        /* otherwise error printing  info  where               */
        {
            string s = null;
            if (bascobas[v] >= n)
            {
                vartoa(v, ref s);
                throw new Exception($"{info}: Cobasic variable {s} should be basic.\n");
            }
        }

        public void assertcobasic(int v, string info)
        /* assert that  v  in VARS is a cobasic variable       */
        /* otherwise error printing  info  where               */
        {
            string s = null;
            if (TABCOL(v) < 0)
            {
                vartoa(v, ref s);
                throw new Exception($"{info}: Basic variable {s} should be cobasic.\n");
            }
        }

        public void docupivot(int leave, int enter)
        /* leave, enter in  VARS.  Documents the current pivot. */
        /* Asserts  leave  is basic and  enter is cobasic.      */
        {
            string s = null;

            assertbasic(leave, "docupivot");
            assertcobasic(enter, "docupivot");

            vartoa(leave, ref s);
            tabbedtextf("leaving: %-4s ", s);
            vartoa(enter, ref s);
            tabbedtextf("entering: %s\n", s);
        }       /* end of  docupivot    */

        void raytermination(int enter)
        {
            string s = null;
            vartoa(enter, ref s);
            tabbedtextf($"Ray termination when trying to enter {s}\n");
            outtabl();
            tabbedtextf("Current basis, not an LCP solution:\n");
            outsol();
            throw new Exception("Ray termination; current basis, not an LCP solution");
        }

        public void testtablvars()
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
        public int complement(int v)
        {
            if (v == Z(0))
                errexit("Attempt to find complement of z0.");
            return (v > n) ? Z(v - n) : W(v);
        }       /* end of  complement (v)     */

        /* initialize statistics for minimum ratio test
         */
        public void initstatistics()
        {
            int i;
            for (i = 0; i <= n; i++)
                lextested[i] = lexcomparisons[i] = 0;
        }

        /* output statistics of minimum ratio test
         */
        public void outstatistics()
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
        public int lexminvar(int enter, ref bool z0leave)
        {
            int col, i, j, testcol;
            int numcand;

            assertcobasic(enter, "Lexminvar");
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
                raytermination(enter);
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


        public void negcol(int col)
        /* negate tableau column  col   */
        {
            int i;
            for (i = 0; i < n; i++)
                changesigninA(i, col);
        }

        public void negrow(int row)
        /* negate tableau row.  Used in  pivot()        */
        {
            int j;
            for (j = 0; j <= n + 1; j++)
                if (!zero(A[row][j]))
                    changesigninA(row, j);
        }

        /* leave, enter in  VARS  defining  row, col  of  A
         * pivot tableau on the element  A[row][col] which must be nonzero
         * afterwards tableau normalized with positive determinant
         * and updated tableau variables
         */
        public void pivot(int leave, int enter)
        {
            int row, col, i, j;
            bool nonzero, negpiv;
            Multiprecision pivelt = new Multiprecision(), tmp1 = new Multiprecision(), tmp2 = new Multiprecision();

            row = bascobas[leave];
            col = TABCOL(enter);

            copy(pivelt, A[row][col]);     /* pivelt anyhow later new determinant  */
            negpiv = negative(pivelt);
            if (negpiv)
                changesign(pivelt);
            for (i = 0; i < n; i++)
                if (i != row)               /*  A[row][..]  remains unchanged       */
                {
                    nonzero = !zero(A[i][col]);
                    for (j = 0; j <= n + 1; j++)      /*  assume here RHS()==n+1        */
                        if (j != col)
                        /*  A[i,j] =
                           (A[i,j] A[row,col] - A[i,col] A[row,j])/ det     */
                        {
                            mulint(A[i][j], pivelt, tmp1);
                            if (nonzero)
                            {
                                mulint(A[i][col], A[row][j], tmp2);
                                linint(tmp1, 1, tmp2, negpiv ? 1 : -1);
                            }
                            Multiprecision c = new Multiprecision();
                            divint(tmp1, det, c);
                            setinA(i, j, c);
                        }
                    /* row  i  has been dealt with, update  A[i][col]  safely   */
                    if (nonzero && !negpiv)
                        changesigninA(i, col);
                }       /* end of  for (i=...)                              */
            Multiprecision temp = new Multiprecision();
            copy(temp, det);
            setinA(row, col, temp);
            if (negpiv)
                negrow(row);
            copy(det, pivelt);      /* by construction always positive      */

            /* update tableau variables                                     */
            bascobas[leave] = col + n; whichvar[col + n] = leave;
            bascobas[enter] = row; whichvar[row] = enter;
        }       /* end of  pivot (leave, enter)                         */


        void changesigninA(int i, int j)
        {
            changesign(A[i][j]);
        }

        void setinA(int i, int j, Multiprecision value)
        {
            copy(A[i][j], value);
        }

        /* ------------------------------------------------------------ */
        public void runlemke(Flagsrunlemke flags)
        {
            int leave, enter;
            bool z0leave = false;

            pivotcount = 1;
            initstatistics();

            isqdok();
            /*  tabbedtextf("LCP seems OK.\n");      */

            filltableau();
            /*  tabbedtextf("Tableau filled.\n");    */

            if (flags.binitabl)
            {
                tabbedtextf("After filltableau:\n");
                outtabl();
            }

            /* z0 enters the basis to obtain lex-feasible solution      */
            enter = Z(0);
            leave = lexminvar(enter, ref z0leave);

            /* now give the entering q-col its correct sign             */
            negcol(RHS());

            if (flags.bouttabl)
            {
                tabbedtextf("After negcol:\n");
                outtabl();
            }
            while (true)       /* main loop of complementary pivoting                  */
            {
                testtablvars();
                if (flags.bdocupivot)
                    docupivot(leave, enter);
                pivot(leave, enter);
                if (z0leave)
                    break;  /* z0 will have value 0 but may still be basic. Amend?  */
                if (flags.bouttabl)
                    outtabl();
                enter = complement(leave);
                leave = lexminvar(enter, ref z0leave);
                if (pivotcount++ == flags.maxcount)
                {
                    tabbedtextf("------- stop after %d pivoting steps --------\n",
                   flags.maxcount);
                    break;
                }
            }

            if (flags.binitabl)
            {
                tabbedtextf("Final tableau:\n");
                outtabl();
            }
            if (flags.boutsol)
                outsol();
            if (flags.blexstats)
                outstatistics();

            notokcopysol();
        }





    }
}

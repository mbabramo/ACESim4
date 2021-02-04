using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESim.ArrayFormConversionExtension;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.ColumnPrinter;
using static ACESimBase.Util.CPrint;
using JetBrains.Annotations;
using System.Numerics;
using ACESim;
using ACESimBase.GameSolvingSupport;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTALemke
    {

        int n;   /* LCP (Linear Complementarity Problem) dimension as used here   */

        /* LCP input    */
        public ExactValue[][] lcpM;
        public ExactValue[] rhsq;
        public ExactValue[] coveringVectorD;
        public int lcpdim = 0; /* set in setlcp                */

        /* LCP result   */
        public ExactValue[] solz;
        public int pivotcount;

        /* tableau:    */
        public ExactValue[][] Tableau;        /* tableau                              */

        /* used for tableau:    */
        /* We keep track of which variables are basic and which are cobasic. */
        /* Note that basic variables correspond to tableau rows, and cobasic variables correspond to tableau columns */
        /* We start by making our first variables basic, with Z representing the basic variables: */
        /* 0..2n = Z(0) .. Z(n) W(1) .. W(n)           */
        /* 0..2n,  0 .. n-1: tabl rows (basic vars)    */
        /*         n .. 2n:  tabl cols  0..n (cobasic) */
        public int[] variableIndexToBasicCobasicIndex;     /* given a variable at index i, shows where that variable is located in the list of basic then cobasic variables                      */
        public int[] basicCobasicIndexToVariable;          /* inverse of variableIndexToBasicCobasicIndex  */
        public int TableauRow(int variableIndexOfBasicVariable) => variableIndexToBasicCobasicIndex[variableIndexOfBasicVariable];
        public int TableauColumn(int variableIndexOfCobasicVariable) => variableIndexToBasicCobasicIndex[variableIndexOfCobasicVariable] - n;
        private bool VariableIsBasic(int v) => variableIndexToBasicCobasicIndex[v] < n;
        private bool VariableIsCobasic(int v) => variableIndexToBasicCobasicIndex[v] >= n;

        public int Z(int i)
        {
            return i;
        }
        public int W(int i)
        {
            return i + n;
        }
        public int RHS()
        {
            return n + 1;
        }

        /* scale factors for variables z
		 * scfa[Z(0)]   for  d,  scfa[RHS()] for  q
		 * scfa[Z(1..n)] for cols of  M
		 * result variables to be multiplied with these
		 */
        public ExactValue[] scaleFactors;

        public ExactValue determinant = new ExactValue();                        /* determinant                  */

        public int[] lexTested, lexComparisons;/* statistics for lexminvar     */

        public int[] leaveCandidates; /* should be local to lexminvar but defined globally for economy    */

        public const int MAXLCPDIM = 2000;       /* max LCP dimension                       */
        public const int INFOSTRINGLENGTH = 8;   /* string naming vars, e.g. "z0", "w187"   */
        public const int LCPSTRL = 60;          /* length of string containing LCP entry   */

        /*------------------ error message ----------------*/
        public void errexit(string info)
        {
            throw new Exception($"Error {info}. Lemke terminated unsuccessfully.");
        }

        /*------------------ memory allocation -------------------------*/
        public ECTALemke(int newn)
        {
            if (newn < 1 || newn > MAXLCPDIM)
            {
                throw new Exception($"Problem dimension  n= {newn} not allowed. Minimum  n  is 1, maximum {MAXLCPDIM}");
            }
            n = lcpdim = newn;
            /* LCP input/output data    */
            lcpM = CreateJaggedArray<ExactValue[][]>(n, n);
            rhsq = new ExactValue[n];
            coveringVectorD = new ExactValue[n];
            solz = new ExactValue[n];
            for (int i = 0; i < n; i++)
            {
                rhsq[i] = new ExactValue();
                coveringVectorD[i] = new ExactValue();
                solz[i] = new ExactValue();
            }
            /* tableau          */
            Tableau = CreateJaggedArray<ExactValue[][]>(n, n + 2);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n + 2; j++)
                    Tableau[i][j] = new ExactValue();
            scaleFactors = new ExactValue[n + 2];
            for (int i = 0; i < n + 2; i++)
                scaleFactors[i] = new ExactValue();
            variableIndexToBasicCobasicIndex = new int[2 * n + 1];
            basicCobasicIndexToVariable = new int[2 * n + 1];
            lexTested = new int[n + 1];
            lexComparisons = new int[n + 1];
            leaveCandidates = new int[n];
            /* should be local to lexminvar but allocated here for economy  */
            /* initialize all LCP entries to zero       */
            {
                int i, j;
                ExactValue zero = ExactValue.Zero();
                for (i = 0; i < n; i++)
                {
                    for (j = 0; j < n; j++)
                        lcpM[i][j] = zero;
                    coveringVectorD[i] = rhsq[i] = zero;
                }
            }
        } /* end of  setlcp(newn)       */

        /* asserts that  d >= 0  and not  q >= 0  (o/w trivial sol) 
		 * and that q[i] < 0  implies  d[i] > 0
		 */
        public void ConfirmCoveringVectorOK()
        {
            int i;
            bool trivialSolutionExists = true;
            for (i = 0; i < n; i++)
            {
                if (coveringVectorD[i].IsLessThan(ExactValue.Zero()))
                {
                    throw new Exception($"Covering vector  d[{i + 1}] = {coveringVectorD[i]} negative\n");
                }
                else if (rhsq[i].IsLessThan(ExactValue.Zero()))
                {
                    trivialSolutionExists = false;
                    if (coveringVectorD[i].IsEqualTo(ExactValue.Zero()))
                    {
                        throw new Exception($"Covering vector  d[{i + 1}] = 0  where  q[{i + 1}] = {rhsq[i]}  is negative. Cannot start Lemke");
                    }
                }
            }   /* end of  for(i=...)   */
            if (trivialSolutionExists)
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
                variableIndexToBasicCobasicIndex[Z(i)] = n + i;
                basicCobasicIndexToVariable[n + i] = Z(i);
            }
            for (i = 1; i <= n; i++)
            {
                variableIndexToBasicCobasicIndex[W(i)] = i - 1;
                basicCobasicIndexToVariable[i - 1] = W(i);
            }
        }       /* end of inittablvars()        */

        public void FillTableau()
        /* fill tableau from  M, q, d   */
        {

            // DEBUG TODO: When generalizing our numeric types, we need to have a generic with two types -- one for the LCP 
            // and one for the tableau. Here, ExactValue is for the LCP and then ExactValue is for the tableau.

            int i, j;
            ExactValue den, num;
            for (j = 0; j <= n + 1; j++)
            {
                // We copy the LCP into the tableau, which has the same number of rows as the LCP but two extra columns.
                // The first column of the tableau is the covering vector. 
                // This is followed by a column for each column of the LCP,
                // and finally a column for the right hand side. 

                // Each row of the tableau represents a basic variable -- i.e., a variable that can take a value of other than 0.
                // Each column of the tableau represents a cobasic variable -- i.e., a variable that takes a value of 0. 
                // When z0 enters the basis initially, it will thus disappear from the column list (replaced by its complement),
                // and it will replace its complement in the row list. 
                // When z0 leaves the basis at the end of all pivoting steps, it will thus be cobasic and represented by
                // a column of the tableau.

                // Here, we scale up each column so that it can be represented by an integer. This would
                // not be necessary if we were using floating point numbers.

                /* compute lcm  scaleFactors[j]  of denominators for  col  j  of  A         */
                scaleFactors[j] = ExactValue.One();
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? coveringVectorD[i].Denominator :
                      (j == RHS()) ? rhsq[i].Denominator : lcpM[i][j - 1].Denominator;
                    scaleFactors[j] = scaleFactors[j].LeastCommonMultiple(den);
                }
                /* fill in col  j  of  A    */
                for (i = 0; i < n; i++)
                {
                    den = (j == 0) ? coveringVectorD[i].Denominator :
                      (j == RHS()) ? rhsq[i].Denominator : lcpM[i][j - 1].Denominator;
                    num = (j == 0) ? coveringVectorD[i].Numerator :
                      (j == RHS()) ? rhsq[i].Numerator : lcpM[i][j - 1].Numerator;
                    /* cols 0..n of  A  contain LHS cobasic cols of  Ax = b     */
                    /* where the system is here         -Iw + dz_0 + Mz = -q    */
                    /* cols of  q  will be negated after first min ratio test   */
                    /* A[i][j] = num * (scaleFactors[j] / den),  fraction is integral       */
                    ExactValue c = num.Times(scaleFactors[j]).DividedBy(den);
                    SetValueInTableau(i, j, c);
                }
            }   /* end of  for(j=...)   */

            InitializeTableauVariables();

            determinant = ExactValue.One();
            determinant = determinant.Negated();
        }       /* end of filltableau()         */

        /* ---------------- output routines ------------------- */
        public void OutputLCP()
        /* output the LCP as given      */
        {
            int i, j;
            ExactValue a;
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
                    if (a.Numerator.IsEqualTo(ExactValue.Zero()))
                        colpr(".");
                    else
                    {
                        s = a.ToString();
                        colpr(s);
                    }
                }
                s = coveringVectorD[i].ToString();
                colpr(s);
                s = rhsq[i].ToString(); 
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
            colset(n + 3);
            colleft(0);
            colpr("var");                   /* headers describing variables */
            for (j = 0; j <= n + 1; j++)
            {
                if (j == RHS())
                    colpr("RHS()");
                else
                {
                    VariableToString(basicCobasicIndexToVariable[j + n], ref s);
                    colpr(s);
                }
            }
            colpr("scfa");                  /* scale factors                */
            for (j = 0; j <= n + 1; j++)
            {
                if (j == RHS())
                    smp = scaleFactors[RHS()].ToString();
                else if (basicCobasicIndexToVariable[j + n] > n) /* col  j  is some  W           */
                    smp = sprintf("1");
                else                        /* col  j  is some  Z:  scfa    */
                    smp = scaleFactors[basicCobasicIndexToVariable[j + n]].ToString();
                colpr(smp);
            }
            colnl();
            for (i = 0; i < n; i++)             /* print row  i                 */
            {
                VariableToString(basicCobasicIndexToVariable[i], ref s);
                colpr(s);
                for (j = 0; j <= n + 1; j++)
                {
                    smp = Tableau[i][j].ToString();
                    if (smp == "0")
                        colpr(".");
                    else
                        colpr(smp);
                }
            }
            colout();
            smp = determinant.ToString();
            tabbedtextf("Determinant: %s\n", smp);
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
            ExactValue num = ExactValue.Zero(), den = ExactValue.Zero();

            colset(n + 2);    /* column printing to see complementarity of  w  and  z */

            colpr("basis=");
            for (i = 0; i <= n; i++)
            {
                if (variableIndexToBasicCobasicIndex[Z(i)] < n)
                    /*  Z(i) is a basic variable        */
                    VariableToString(Z(i), ref s);
                else if (i > 0 && variableIndexToBasicCobasicIndex[W(i)] < n)
                    /*  W(i) is a basic variable        */
                    VariableToString(W(i), ref s);
                else
                    s = "  ";
                colpr(s);
            }

            colpr("z=");
            for (i = 0; i <= 2 * n; i++)
            {
                if ((row = variableIndexToBasicCobasicIndex[i]) < n)  /*  i  is a basic variable           */
                {
                    if (i <= n)       /* printing Z(i)        */
                        /* value of  Z(i):  scfa[Z(i)]*rhs[row] / (scfa[RHS()]*det)   */
                        num = scaleFactors[Z(i)].Times(Tableau[row][RHS()]);
                    else            /* printing W(i-n)      */
                        /* value of  W(i-n)  is  rhs[row] / (scfa[RHS()]*det)         */
                        num = Tableau[row][RHS()];
                    den = determinant.Times(scaleFactors[RHS()]);
                    ExactValue r = num.DividedBy(den);
                    num = r.Numerator;
                    den = r.Denominator;
                    smp = num.ToString();
                    pos = smp.Length;
                    if (!(den.IsOne()))  /* add the denominator  */
                    {
                        if (ExactValue.AbbreviateExactValues)
                        {
                            double d = r.AsDouble;
                            smp = d.ToString();
                            if (smp.Length > 8)
                                smp = d.ToString("E5");
                        }
                        else
                        {
                            smp += "/";
                            string denstring = null;
                            denstring = den.ToString();
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
            ExactValue num = ExactValue.Zero(), den = ExactValue.Zero();

            for (i = 1; i <= n; i++)
            {
                if ((row = TableauRow(i)) < n)  /*  i  is a basic variable */
                {
                    /* value of  Z(i):  scfa[Z(i)]*rhs[row] / (scfa[RHS()]*det)   */
                    num = scaleFactors[Z(i)].Times(Tableau[row][RHS()]);
                    den = determinant.Times(scaleFactors[RHS()]);
                    solz[i - 1] = num.DividedBy(den);
                }
                else            /* i is nonbasic    */
                    solz[i - 1] = ExactValue.Zero();
            }
        } /* end of copysol                     */

        /* --------------- test output and exception routines ---------------- */
        public void AssertVariableIsBasic(int v)
        /* assert that  v  in VARS is a basic variable         */
        /* otherwise error printing  info  where               */
        {
            string s = null;
            if (VariableIsCobasic(v))
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
            if (VariableIsBasic(v))
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
            tabbedtextf("pivotcount %s ", pivotcount);
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
        /* test tableau variables to make sure that inverse functions work as expected: error => msg only, continue  */
        {
            int i, j;
            for (i = 0; i <= 2 * n; i++)  /* check if somewhere tableauvars wrong */
                if (variableIndexToBasicCobasicIndex[basicCobasicIndexToVariable[i]] != i || basicCobasicIndexToVariable[variableIndexToBasicCobasicIndex[i]] != i)
                /* found an inconsistency, print everything             */
                {
                    tabbedtextf("Inconsistent tableau variables:\n");
                    for (j = 0; j <= 2 * n; j++)
                    {
                        tabbedtextf("var j:%3d bascobas:%3d whichvar:%3d ",
                            j, variableIndexToBasicCobasicIndex[j], basicCobasicIndexToVariable[j]);
                        tabbedtextf(" b[w[j]]==j: %1d  w[b[j]]==j: %1d\n",
                            variableIndexToBasicCobasicIndex[basicCobasicIndexToVariable[j]] == j, basicCobasicIndexToVariable[variableIndexToBasicCobasicIndex[j]] == j);
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
                lexTested[i] = lexComparisons[i] = 0;
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
                colipr(lexTested[i]);
            colpr("% times tested");
            if (lexTested[0] > 0)
            {
                colpr("100");
                for (i = 1; i <= n; i++)
                {
                    s = sprintf("%2.0f",
                            (double)lexTested[i] * 100.0 / (double)lexTested[0]);
                    colpr(s);
                }
            }
            else
                colnl();
            colpr("avg comparisons");
            for (i = 0; i <= n; i++)
                if (lexTested[i] > 0)
                {
                    s = sprintf("%1.1f",
                        (double)lexComparisons[i] / (double)lexTested[i]);
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
            int numCandidates;

            AssertVariableIsCobasic(enter);
            col = TableauColumn(enter);
            numCandidates = 0;
            /* leavecand [0..numcand-1] = candidates (rows) for leaving var */
            /* start with  leavecand = { i | A[i][col] > 0 }                        */
            for (i = 0; i < n; i++)
            {
                if ((Tableau[i][col]).IsPositive())
                    leaveCandidates[numCandidates++] = i;
            }
            if (numCandidates == 0)
                ThrowRayTerminationException(enter);
            if (numCandidates == 1)
            {
                lexTested[0] += 1;
                lexComparisons[0] += 1;
                z0leave = (leaveCandidates[0] == variableIndexToBasicCobasicIndex[Z(0)]);
            }
            for (j = 0; numCandidates > 1; j++)
            /* as long as there is more than one leaving candidate perform
             * a minimum ratio test for the columns of  j  in RHS(), W(1),... W(n)
             * in the tableau.  That test has an easy known result if
             * the test column is basic or equal to the entering variable.
             */
            {
                if (j > n)    /* impossible, perturbed RHS() should have full rank  */
                    errexit("lex-minratio test failed");
                lexTested[j] += 1;
                lexComparisons[j] += numCandidates;

                testcol = (j == 0) ? RHS() : TableauColumn(W(j));
                if (testcol != col)       /* otherwise nothing will change      */
                {
                    if (testcol >= 0)
                    /* not a basic testcolumn: perform minimum ratio tests          */
                    {
                        int newnum = 0;
                        /* leavecand[0..newnum]  contains the new candidates    */
                        for (i = 1; i < numCandidates; i++)
                        /* investigate remaining candidates                         */
                        {
                            var productComparison = (Tableau[leaveCandidates[0]][testcol].Times(Tableau[leaveCandidates[i]][col])).Minus
                                      (Tableau[leaveCandidates[i]][testcol].Times(Tableau[leaveCandidates[0]][col]));
                            /* observe sign of  A[l_0,t] / A[l_0,col] - A[l_i,t] / A[l_i,col]   */
                            /* note only positive entries of entering column considered */
                            if (productComparison.IsEqualTo(ExactValue.Zero()))         /* new ratio is the same as before      */
                                leaveCandidates[++newnum] = leaveCandidates[i];
                            else if (productComparison.IsGreaterThan(ExactValue.Zero()))    /* new smaller ratio detected           */
                                leaveCandidates[newnum = 0] = leaveCandidates[i];
                        }
                        numCandidates = newnum + 1;
                    }
                    else
                    {
                        /* testcol < 0: W(j) basic, Eliminate its row from leavecand    */
                        /* since testcol is the  jth  unit column                       */
                        for (i = 0; i < numCandidates; i++)
                        {
                            if (leaveCandidates[i] == variableIndexToBasicCobasicIndex[W(j)])
                            {
                                leaveCandidates[i] = leaveCandidates[--numCandidates];
                                /* shuffling of leavecand allowed       */
                                break;
                            }
                        }
                    }
                }   /* end of  if(testcol != col)                           */

                if (j == 0)
                    /* seek  z0  among the first-col leaving candidates     */
                    for (i = 0; i < numCandidates; i++)
                        if ((z0leave = (leaveCandidates[i] == variableIndexToBasicCobasicIndex[Z(0)])))
                            break;
                /* alternative, to force z0 leaving the basis:
                 * return whichvar[leavecand[i]];
                 */
            }       /* end of  for ( ... numcand > 1 ... )   */
            return basicCobasicIndexToVariable[leaveCandidates[0]];
        }       /* end of lexminvar (col, *z0leave);                        */


        public void NegateTableauColumn(int col)
        /* negate tableau column  col   */
        {
            int i;
            for (i = 0; i < n; i++)
                ChangeSignInTableau(i, col);
        }

        public void NegateTableauRow(int row)
        /* negate tableau row.  Used in  pivot()        */
        {
            int j;
            for (j = 0; j <= n + 1; j++)
                if (!(Tableau[row][j]).IsZero())
                    ChangeSignInTableau(row, j);
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
            bool nonzero, negativePivot;
            ExactValue pivotValue = ExactValue.Zero(), tableauEntry = ExactValue.Zero(), pivotProduct = ExactValue.Zero();

            row = TableauRow(leave); // the rows correspond to the basic variables
            col = TableauColumn(enter); // the columns correspond to the cobasic variables

            pivotValue = Tableau[row][col];     /* pivelt anyhow later new determinant  */
            negativePivot = (pivotValue).IsNegative();
            if (negativePivot)
                pivotValue = pivotValue.Negated(); /* negativePivot also affects how pivoting is done (see below) */
            for (i = 0; i < n; i++)
                if (i != row)               /*  A[row][..]  remains unchanged       */
                {
                    ExactValue sameRowInPivotColumn = Tableau[i][col];
                    nonzero = !(sameRowInPivotColumn.IsZero());
                    for (j = 0; j <= n + 1; j++)      /*  assume here RHS()==n+1        */
                    {
                        if (j != col)
                        /*  Where i != row and j != col, A[i,j] =
                           (A[i,j] A[row,col] - A[i,col] A[row,j])/ det     */
                        /* The result of this operation is that the pivot row and column remain the same in absolute value, */
                        /* except for the pivot cell itself. */ 
                        {
                            // 1. Multiply every cell by the pivot value (the value in the specified row and column)
                            tableauEntry = Tableau[i][j].Times(pivotValue);
                            if (nonzero)
                            {
                                // 2. Add to each cell (for a negative pivot) or subtract from each cell (for a positive
                                // pivot) the product of the value in the pivot column (same row) and the value in the 
                                // pivot row (same column). The row/column operations here amount to multiplying the
                                // pivot 
                                ExactValue sameColumnInPivotRow = Tableau[row][j];
                                pivotProduct = sameRowInPivotColumn.Times(sameColumnInPivotRow);
                                if (negativePivot)
                                    tableauEntry = tableauEntry.Plus(pivotProduct);
                                else
                                    tableauEntry = tableauEntry.Minus(pivotProduct);
                            }
                            tableauEntry = tableauEntry.DividedBy(determinant);
                            SetValueInTableau(i, j, tableauEntry);
                        }
                    }
                    /* row  i  has been dealt with, update  A[i][col]  safely   */
                    if (nonzero && !negativePivot)
                        ChangeSignInTableau(i, col);
                }       /* end of  for (i=...)                              */
            SetValueInTableau(row, col, determinant);
            if (negativePivot)
                NegateTableauRow(row);
            determinant = pivotValue;      /* by construction always positive after first iteration, based on above      */

            /* update tableau variables                                     */
            variableIndexToBasicCobasicIndex[leave] = col + n; 
            basicCobasicIndexToVariable[col + n] = leave;
            variableIndexToBasicCobasicIndex[enter] = row; 
            basicCobasicIndexToVariable[row] = enter;
            pivotnum++;
        }       /* end of  pivot (leave, enter)                         */


        void ChangeSignInTableau(int i, int j)
        {
            Tableau[i][j] = Tableau[i][j].Negated();
        }

        void SetValueInTableau(int i, int j, ExactValue value)
        {
            Tableau[i][j] = value;
        }

        /* ------------------------------------------------------------ */
        public void RunLemke(ECTALemkeOptions flags)
        {
            int leaveBasis, enterBasis;
            bool z0leave = false;

            pivotcount = 1;
            InitMinRatioTestStatistics();

            ConfirmCoveringVectorOK();
            /*  tabbedtextf("LCP seems OK.\n");      */

            FillTableau();
            /*  tabbedtextf("Tableau filled.\n");    */

            if (flags.outputInitialAndFinalTableaux)
            {
                tabbedtextf("After filltableau:\n");
                OutputTableau();
            }

            /* z0 enters the basis to obtain lex-feasible solution      */
            /* note that entering the basis means it is no longer listed as a column on the tableau */
            enterBasis = Z(0);
            leaveBasis = GetLeavingVariable(enterBasis, ref z0leave);

            /* now give the entering q-col (the last column, i.e. the right-hand side, of the tableau) its correct sign             */
            NegateTableauColumn(RHS());

            if (flags.outputTableauxAfterPivots)
            {
                tabbedtextf("After negating entering column (right-hand side):\n");
                OutputTableau();
            }
            while (true)       /* main loop of complementary pivoting                  */
            {
                TestTableauVariables();
                if (flags.outputPivotingSteps)
                    OutputPivotLeaveAndEnter(leaveBasis, enterBasis);
                Pivot(leaveBasis, enterBasis);
                if (VariableIsBasic(leaveBasis))
                {
                    throw new Exception($"Leaving variable is basic."); // DEBUG
                }
                if (z0leave)
                {
                    /* z0 will have value 0 but may still be basic. Amend?  */ // DEBUG
                    break;  
                }
                if (flags.outputTableauxAfterPivots)
                    OutputTableau();
                enterBasis = ComplementOfVariable(leaveBasis);
                leaveBasis = GetLeavingVariable(enterBasis, ref z0leave);

                if (pivotcount++ == flags.maxPivotSteps)
                {
                    tabbedtextf("------- stop after %d pivoting steps --------\n",
                   flags.maxPivotSteps);
                    break;
                }
                
            }

            if (flags.outputInitialAndFinalTableaux)
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

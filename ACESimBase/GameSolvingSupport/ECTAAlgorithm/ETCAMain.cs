using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.CPrint;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.RatStatic;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.ColumnPrinter;
using static ACESim.ArrayFormConversionExtension;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class ETCAMain
    {



        const int MAXACCURACY = 1000;
        const int DEFAULTACCURACY = 23;
        const int FIRSTPRIORSEED = 500;



        int multipriors = 0;         /* parameter for    -M option  */
        int seed = 0;      /* payoff seed for bintree  (-s option) */

        bool outputPivotResults = false;
        bool outputPivotHeaderFirst = false;/* headers first (multiple games)       */

        const int REPEATHEADER = 20;   /* repeat header if more games than this */
        bool outputRawTree = false;     /* output the raw game tree (-g option) */
        bool outputInitialTableau = false;
        bool outputTableaux = false;
        bool ourputLCP = false;       /* output LCP  */
        bool outputPrior = false;     /* output prior */
        bool outputPivotingSteps = false;      /* complementary pivoting steps */
        bool outputEquilibrium = true;        /* output equilibrium           */
        bool outputEquilibriumShort = false;   /* output equilibrium shortly   */
        bool outputSolution = false;
        bool outputLexStats = false; /* output lexical ordering statistics */

        /* global variables for generating and documenting computation  */
        Flagsprior fprior;
        Flagsrunlemke flemke;

        long timeused, sumtimeused;
        int pivots, sumpivots;
        int lcpsize;
        int[] eqsize = new int[Treedef.PLAYERS], sumeqsize = new int[Treedef.PLAYERS];
        bool[] agreenfsf = new bool[Treedef.PLAYERS];

        Stopwatch swatch = new Stopwatch();
        Treedef t = new Treedef();

        /* returns processor SCLOCKUNITS since the last call to
         * stopwatch() and prints them to stdout if  bprint==1
         */
        long stopwatch(bool bprint)
        {
            swatch.Stop();

            long elapsedMilliseconds = swatch.ElapsedMilliseconds;
            if (bprint)
            {
                tabbedtextf($"time elapsed {elapsedMilliseconds:4} millisecs \n");
            }

            swatch.Reset();
            swatch.Start();
            return elapsedMilliseconds;
        }

        /* informs about tree size              */
        void infotree()
        {
            int pl;
            tabbedtextf("\nGame tree has %d nodes, ", t.lastnode - Treedef.rootindex);
            tabbedtextf("of which %d are terminal nodes.\n", t.lastoutcome);
            for (pl = 0; pl < Treedef.PLAYERS; pl++)
            {
                tabbedtextf("    Player %d has ", pl);
                tabbedtextf("%3d information sets, ", t.firstiset[pl + 1] - t.firstiset[pl]);
                tabbedtextf("%3d moves in total\n", t.firstmove[pl + 1] - t.firstmove[pl] - 1);
            }
        }

        /* informs about sequence form, set  lcpsize    */
        void infosf()
        {
            int pl;

            lcpsize = t.nseqs[1] + t.nisets[2] + 1 + t.nseqs[2] + t.nisets[1] + 1;
            tabbedtextf("Sequence form LCP dimension is %d\n", lcpsize);
            for (pl = 1; pl < Treedef.PLAYERS; pl++)
            {
                tabbedtextf("    Player %d has ", pl);
                tabbedtextf("%3d sequences, ", t.nseqs[pl]);
                tabbedtextf("subject to %3d constraints\n", t.nisets[pl] + 1);
            }
        }

        /* give header columns for result information via  inforesult(...)      */
        void inforesultheader()
        {
            tabbedtextf("PRIOR/PAY| ");
            tabbedtextf("SEQUENCE FORM        mixiset");
            tabbedtextf("\n");
            tabbedtextf("Seed/seed| ");
            tabbedtextf("pivot %%n [secs]");
            tabbedtextf("\n");
        }

        /* info about results for game with  priorseed  and  (payoff) seed */
        void infopivotresult(int priorseed, int seed)
        {
            if (!outputPivotResults)
                return;
            string formatstring = "%4d %3.0f %6.2f";
            tabbedtextf("%4d/%4d| ", priorseed, seed);
            tabbedtextf(formatstring, pivots,
                    (double)pivots * 100.0 / (double)lcpsize,
                (double)timeused / 1000);
            tabbedtextf("\n");
        }

        /* summary info about results for  m  games     */
        void infosumresult(int m)
        {
            double mm = (double)m;
            string formatstring = "%6.1f %3.0f %6.2f %3.1f %3.1f";

            tabbedtextf("---------| AVERAGES over  %d  games:\n", m);
            if (m > REPEATHEADER)
                inforesultheader();
            tabbedtextf("         ");
            tabbedtextf(formatstring, (double)sumpivots / mm,
                    (double)sumpivots * 100.0 /
                        (double)(lcpsize * mm),
                (double)sumtimeused / (1000 * mm),
                    (double)sumeqsize[1] / mm,
                    (double)sumeqsize[2] / mm);
            tabbedtextf("\n");
        }

        /* process game for evaluation
         * for comparison:  call first for  NF  then  SF
         * bnf:  NF is processed, compare result with SF result
         * docuseed:  what seed to output for short equilibrium output
         * realplan[][]  must be allocated
         */
        void processgame(int docuseed)
        {
            int equilsize;
            int offset;
            int pl;

            if (outputPivotingSteps)
                tabbedtextf("Generating and solving sequence form.\n");
            t.sflcp();

            t.covvector();
            if (ourputLCP)
                t.Lemke.outlcp();
            stopwatch(false);
            t.Lemke.runlemke(flemke);
            sumtimeused += timeused = stopwatch(false);
            sumpivots += pivots = t.Lemke.pivotcount;
            /* equilibrium size     */
            offset = 0;
            for (pl = 1; pl < Treedef.PLAYERS; pl++)
            {
                equilsize = t.propermixisets(pl, t.Lemke.solz, offset);
                /* the next is  offset  for player 2 */
                offset = t.nseqs[1] + 1 + t.nisets[2];

                sumeqsize[pl] +=
                    eqsize[pl] = equilsize;
            }
            if (outputEquilibrium)
                t.showeq(outputEquilibriumShort, docuseed);
        }

        public int main()
        {
            tabbedtextf("C# TRANSLATION OF ECTA\n"); // DEBUG

            flemke.maxcount = 0;

            flemke.outputPivotingSteps = outputPivotingSteps;
            flemke.outputInitialTableau = outputInitialTableau;
            flemke.outputTableaux = outputTableaux;
            flemke.outputSolution = outputSolution;
            flemke.outputLexStats = outputLexStats;

            fprior.seed = 0;
            fprior.accuracy = DEFAULTACCURACY;

            /* parse options    */
            if (outputPivotingSteps)
            {
                flemke.outputPivotingSteps = true;
                flemke.outputSolution = true;
            }



            tabbedtextf("Solving example from BvS/Elzen/Talman\n");
            t.tracingexample();

            t.genseqin();
            t.autoname();
            t.maxpayminusone(outputPivotingSteps);

            /* game tree is defined, give headline information  */
            infotree(); // INFO

            infosf(); // INFO

            /* process games                    */
            int gamecount = 0;
            int startprior = fprior.seed;

            t.allocrealplan(t.realplan);
            if (outputPivotResults && outputPivotHeaderFirst) /* otherwise the header is garbled by LCP output */
                inforesultheader();
            int priorcount;
            /* multiple priors 	*/
            multipriors = 20;
            for (priorcount = 0; priorcount < multipriors; priorcount++)
            {
                t.genprior(fprior);
                if (outputRawTree)
                    t.rawtreeprint();
                if (outputPrior)
                    t.outprior();
                processgame(seed + gamecount);
                if (outputPivotResults && !outputPivotHeaderFirst)
                    inforesultheader();
                infopivotresult(fprior.seed, seed + gamecount);
                fprior.seed++;
            }
            if (multipriors > 1)    /* give averages */
                infosumresult(multipriors);
            return 0;
        }
    }
}

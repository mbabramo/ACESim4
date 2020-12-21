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
        const int MINLEVEL = 1;
        const int MAXLEVEL = 10;
        const int FILENAMELENGTH = 50;
        const double CLOCKUNITSPERSECOND = 1000.0;
        const string SCLOCKUNITS = "millisecs";
        const int REPEATHEADER = 20;   /* repeat header if more games than this */

        /* global variables for generating and documenting computation  */
        static Flagsprior fprior;
        static bool boutlcp = false;       /* output LCP       (-o option) */
        static bool boutprior = false;     /* output prior     (-O option) */
        static bool bcomment = false;      /* complementary pivoting steps */
        static bool bequil = true;        /* output equilibrium           */
        static bool bshortequil = false;   /* output equilibrium shortly   */
        static bool binterface = false;     /* interface with enumeration	*/
        /* GAMBIT interface file, option parameter	*/
        static char[] gintfname= "dummyname".ToArray();
        static Flagsrunlemke flemke;

        static long timeused, sumtimeused;
        static int pivots, sumpivots;
        static int lcpsize;
        static int mpdigits, summpdigits;
        static int[] eqsize = new int[Treedef.PLAYERS], sumeqsize = new int[Treedef.PLAYERS];
        static bool[] agreenfsf = new bool[Treedef.PLAYERS];


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
                printf($"time elapsed {elapsedMilliseconds:4} millisecs \n");
            }

            swatch.Reset();
            swatch.Start();
            return elapsedMilliseconds;
        }

        /* informs about tree size              */
        void infotree()
        {
            int pl;
            printf("\nGame tree has %d nodes, ", t.lastnode - Treedef.rootindex);
            printf("of which %d are terminal nodes.\n", t.lastoutcome);
            for (pl = 0; pl < Treedef.PLAYERS; pl++)
            {
                printf("    Player %d has ", pl);
                printf("%3d information sets, ", t.firstiset[pl + 1] - t.firstiset[pl]);
                printf("%3d moves in total\n", t.firstmove[pl + 1] - t.firstmove[pl] - 1);
            }
        }

        /* informs about sequence form, set  lcpsize    */
        void infosf()
        {
            int pl;

            lcpsize = t.nseqs[1] + t.nisets[2] + 1 + t.nseqs[2] + t.nisets[1] + 1;
            printf("Sequence form LCP dimension is %d\n", lcpsize);
            for (pl = 1; pl < Treedef.PLAYERS; pl++)
            {
                printf("    Player %d has ", pl);
                printf("%3d sequences, ", t.nseqs[pl]);
                printf("subject to %3d constraints\n", t.nisets[pl] + 1);
            }
        }

        /* give header columns for result information via  inforesult(...)      */
        void inforesultheader()
        {
            printf("PRIOR/PAY| ");
            printf("SEQUENCE FORM        mixiset");
            printf("\n");
            printf("Seed/seed| ");
            printf("pivot %%n [secs] digs pl1 pl2");
            printf("\n");
        }

        /* info about results for game with  priorseed  and  (payoff) seed */
        void inforesult(int priorseed, int seed)
        {
            string formatstring = "%4d %3.0f %6.2f  %3d %3d %3d";
            printf("%4d/%4d| ", priorseed, seed);
            printf(formatstring, pivots,
                    (double)pivots * 100.0 / (double)lcpsize,
                (double)timeused / CLOCKUNITSPERSECOND,
                mpdigits, eqsize[1], eqsize[2]);
            printf("\n");
        }

        /* summary info about results for  m  games     */
        void infosumresult(int m)
        {
            double mm = (double)m;
            string formatstring = "%6.1f %3.0f %6.2f %4.1f %3.1f %3.1f";

            printf("---------| AVERAGES over  %d  games:\n", m);
            if (m > REPEATHEADER)
                inforesultheader();
            printf("         ");
            printf(formatstring, (double)sumpivots / mm,
                    (double)sumpivots * 100.0 /
                        (double)(lcpsize * mm),
                (double)sumtimeused / (CLOCKUNITSPERSECOND * mm),
                (double)summpdigits / mm,
                    (double)sumeqsize[1] / mm,
                    (double)sumeqsize[2] / mm);
            printf("\n");
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

            if (bcomment)
                printf("Generating and solving sequence form.\n");
            t.sflcp();

            t.covvector();
            if (boutlcp)
                t.Lemke.outlcp();
            stopwatch(false);
            t.Lemke.runlemke(flemke);
            sumtimeused += timeused =
            stopwatch(false);
            sumpivots += pivots = t.Lemke.pivotcount;
            summpdigits += mpdigits = MultiprecisionStatic.MAX_DIGITS;
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
            if (bequil)
                t.showeq(bshortequil, docuseed);
        }

        const int MAXACCURACY = 1000;
        const int DEFAULTACCURACY = 23;
        const int FIRSTPRIORSEED = 500;

        public int main()
        {
            int multipriors = 0;         /* parameter for    -M option  */
            int seed = 0;      /* payoff seed for bintree  (-s option) */

            bool bheadfirst = false;/* headers first (multiple games)       */
            bool bgame = false;     /* output the raw game tree (-g option) */

            flemke.maxcount = 0;

            // DEBUG
            flemke.bdocupivot = true;
            flemke.binitabl = false;
            flemke.bouttabl = true;
            flemke.boutsol = true;
            flemke.blexstats = true;

            fprior.seed = 0;
            fprior.accuracy = DEFAULTACCURACY;

            /* parse options    */
            /* options have been input, amend extras	*/
            if (multipriors > 0)
            {
                /* this would exclude the centroid for multiple priors
                    if ( fprior.seed == 0)
                        fprior.seed = 1 ; 
                */
            }
            else
                multipriors = 1;
            if (bcomment)
            {
                flemke.bdocupivot = true;
                flemke.boutsol = true;
            }

            /* options are parsed and flags set */
            /* document the set options         */
            printf("Options chosen,              [ ] = default:\n");
            printf("    Multiple priors     %4d [1],  option -M #\n", multipriors);
            printf("    Accuracy prior      %4d [%d], option -A #\n",
                fprior.accuracy, DEFAULTACCURACY);
            printf("    Seed prior           %3d [0],  ",
                    fprior.seed);
            printf("    Output prior           %s [N],  option -O\n",
                    boutprior ? "Y" : "N");
            printf("    game output            %s [N],  option -g\n",
                bgame ? "Y" : "N");
            printf("    comment LCP pivs & sol %s [N],  option -c\n",
                    bcomment ? "Y" : "N");
            printf("    output LCP             %s [N],  option -o\n",
                    boutlcp ? "Y" : "N");
            printf("    degeneracy statistics  %s [N],  option -d\n",
                flemke.blexstats ? "Y" : "N");
            printf("    tableaus               %s [N],  option -t\n",
                flemke.bouttabl ? "Y" : "N");


            printf("Solving example from BvS/Elzen/Talman\n");
            t.tracingexample();

            t.genseqin();
            t.autoname();
            t.maxpayminusone(bcomment);

            /* game tree is defined, give headline information  */
            infotree(); // INFO

            infosf(); // INFO

            /* process games                    */
            int gamecount = 0;
            int startprior = fprior.seed;

            t.allocrealplan(t.realplan);
            if (bheadfirst) /* otherwise the header is garbled by LCP output */
                inforesultheader();
            int priorcount;
            /* multiple priors 	*/
            bgame = true; 
            boutprior = true; 
            multipriors = 10; // DEBUGX
            for (priorcount = 0; priorcount < multipriors; priorcount++)
            {
                t.genprior(fprior);
                if (bgame)
                    t.rawtreeprint();
                if (boutprior)
                    t.outprior();
                processgame(seed + gamecount);
                if (!bheadfirst)
                    inforesultheader();
                inforesult(fprior.seed, seed + gamecount);
                fprior.seed++;
            }
            if (multipriors > 1)    /* give averages */
                infosumresult(multipriors);
            return 0;
        }
    }
}

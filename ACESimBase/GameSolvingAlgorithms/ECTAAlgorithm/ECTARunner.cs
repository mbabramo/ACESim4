using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.CPrint;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.RationalOperations;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.ColumnPrinter;
using static ACESim.ArrayFormConversionExtension;
using Rationals;
using ACESim;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTARunner
    {
        public int numPriors = 0;      
        public int seed = 0;     

        public bool outputPivotResults = false;
        public bool outputPivotHeaderFirst = false;
        public bool outputAverageResults = false;
        public bool outputGameTreeSetup = true;     
        public bool outputInitialTableau = false;
        public bool outputTableaux = false;
        public bool outputLCP = false;   
        public bool outputPrior = false;   
        public bool outputPivotingSteps = false;  
        public bool outputEquilibrium = true;    
        public bool outputEquilibriumShort = false; 
        public bool outputLCPSolution = false;
        public bool outputLexStats = false; 

        /* global variables for generating and documenting computation  */
        LemkeOptions lemkeOptions;

        long timeused, sumtimeused;
        int pivots, sumpivots;
        int lcpsize;
        int[] eqsize = new int[ECTATreeDefinition.PLAYERS], sumeqsize = new int[ECTATreeDefinition.PLAYERS];

        Stopwatch swatch = new Stopwatch();
        public ECTATreeDefinition t = new ECTATreeDefinition();

        /* returns milliseconds since the last call to
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
            tabbedtextf("\nGame tree has %d nodes, ", t.lastnode - ECTATreeDefinition.rootindex);
            tabbedtextf("of which %d are terminal nodes.\n", t.lastoutcome);
            for (pl = 0; pl < ECTATreeDefinition.PLAYERS; pl++)
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
            for (pl = 1; pl < ECTATreeDefinition.PLAYERS; pl++)
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
            if (!outputAverageResults)
                return;
            double mm = (double)m;
            string formatstring = "%6.1f %3.0f %6.2f %3.1f %3.1f";

            tabbedtextf("---------| AVERAGES over  %d  games:\n", m);
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
            if (outputLCP)
                t.Lemke.OutputLCP();
            stopwatch(false);
            t.Lemke.RunLemke(lemkeOptions);
            sumtimeused += timeused = stopwatch(false);
            sumpivots += pivots = t.Lemke.pivotcount;
            /* equilibrium size     */
            offset = 0;
            for (pl = 1; pl < ECTATreeDefinition.PLAYERS; pl++)
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

        public List<List<double>> Execute_ReturningDoubles(Action<ECTATreeDefinition> setup) => Execute(setup).Select(x => x.Select(y => (double)y).ToList()).ToList();

        public List<Rational[]> Execute(Action<ECTATreeDefinition> setup)
        {
            lemkeOptions.maxPivotSteps = 0; // no limit
            lemkeOptions.outputPivotingSteps = outputPivotingSteps;
            lemkeOptions.outputInitialTableau = outputInitialTableau;
            lemkeOptions.outputTableaux = outputTableaux;
            lemkeOptions.outputSolution = outputLCPSolution;
            lemkeOptions.outputLexStats = outputLexStats;
            /* parse options    */
            if (outputPivotingSteps)
            {
                lemkeOptions.outputPivotingSteps = true;
                lemkeOptions.outputSolution = true;
            }

            List<Rational[]> equilibria = new List<Rational[]>();

            setup(t);

            t.genseqin();
            t.autoname();
            t.maxpayminusone(outputPivotingSteps);

            /* game tree is defined, give headline information  */
            infotree(); // INFO

            infosf(); // INFO

            /* process games                    */
            int gamecount = 0;
            int priorSeed = 0;

            t.allocrealplan(t.realplan);
            if (outputPivotResults && outputPivotHeaderFirst) /* otherwise the header is garbled by LCP output */
                inforesultheader();
            int priorcount;
            /* multiple priors 	*/
            for (priorcount = 0; priorcount < numPriors; priorcount++)
            {
                t.genprior(priorSeed);
                if (outputGameTreeSetup)
                    t.outputGameTree();
                if (outputPrior)
                    t.outprior();
                processgame(seed + gamecount);
                if (outputPivotResults && !outputPivotHeaderFirst)
                    inforesultheader();
                infopivotresult(priorSeed, seed + gamecount);
                priorSeed++;
                var equilibriumProbabilities = t.GetPlayerMoves().ToArray(); // probabilities for each non-chance player, ordered by player, information set, and then action.
                equilibria.Add(equilibriumProbabilities);
                outputGameTreeSetup = false;
            }
            if (numPriors > 1)    /* give averages */
                infosumresult(numPriors);

            bool distinctEquilibriaOnly = true;
            if (distinctEquilibriaOnly)
            {
                var distinctEquilibria = new List<Rational[]>();
                foreach (var eq in equilibria)
                    if (!distinctEquilibria.Any(e2 => e2.SequenceEqual(eq)))
                        distinctEquilibria.Add(eq);
                equilibria = distinctEquilibria;
                TabbedText.WriteLine($"Number distinct equilibria {equilibria.Count}");
            }

            return equilibria;
        }
    }
}

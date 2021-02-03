﻿using System;
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
        public bool outputInitialAndFinalTableaux = false;
        public bool outputTableauxAfterPivots = false;
        public bool outputLCP = false;   
        public bool outputPrior = false;   
        public bool outputPivotingSteps = false;  
        public bool outputEquilibrium = true;    
        public bool outputRealizationPlan = false; 
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
                tabbedtextf("%3d information sets, ", t.firstInformationSet[pl + 1] - t.firstInformationSet[pl]);
                tabbedtextf("%3d moves in total\n", t.firstMove[pl + 1] - t.firstMove[pl] - 1);
            }
        }

        /* informs about sequence form, set  lcpsize    */
        void infosf()
        {
            int pl;

            lcpsize = t.numSequences[1] + t.numInfoSets[2] + 1 + t.numSequences[2] + t.numInfoSets[1] + 1;
            tabbedtextf("Sequence form LCP dimension is %d\n", lcpsize);
            for (pl = 1; pl < ECTATreeDefinition.PLAYERS; pl++)
            {
                tabbedtextf("    Player %d has ", pl);
                tabbedtextf("%3d sequences, ", t.numSequences[pl]);
                tabbedtextf("subject to %3d constraints\n", t.numInfoSets[pl] + 1);
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
            t.generateSequenceFormLCP();

            t.calculateCoveringVectorD();
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
                offset = t.numSequences[1] + 1 + t.numInfoSets[2];

                sumeqsize[pl] +=
                    eqsize[pl] = equilsize;
            }
        }

        public (List<Rational[]> rationals, List<double[]> doubles) Execute_ReturningRationalsAndDoubles(Action<ECTATreeDefinition> setup, Action<int, ECTATreeDefinition> updateActionWhenTracingPathOfEquilibrium)
        {
            var asRationals = Execute(setup, updateActionWhenTracingPathOfEquilibrium).ToList();
            var asDoubles = asRationals.Select(x => x.Select(y => (double)y).ToArray()).ToList();
            return (asRationals, asDoubles);
        }

        /// <summary>
        /// Execute the algorithm, potentially multiple times.
        /// </summary>
        /// <param name="setup"></param>
        /// <param name="updateActionWhenTracingPathOfEquilibrium">Tracing path of an equilibrium means that we use the results of one run to seed the next, but then change the outcomes. The action receives a parameter indicating the index and then changes the outcomes appropriately.</param>
        /// <returns></returns>
        public List<Rational[]> Execute(Action<ECTATreeDefinition> setup, Action<int, ECTATreeDefinition> updateActionWhenTracingPathOfEquilibrium)
        {
            bool tracingEquilibrium = updateActionWhenTracingPathOfEquilibrium != null;

            lemkeOptions.maxPivotSteps = 0; // no limit
            lemkeOptions.outputPivotingSteps = outputPivotingSteps;
            lemkeOptions.outputInitialAndFinalTableaux = outputInitialAndFinalTableaux;
            lemkeOptions.outputTableauxAfterPivots = outputTableauxAfterPivots;
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

            t.generateSequence();
            t.autonameInformationSets();
            t.normalizeMaxPayoutToNegative1(outputPivotingSteps);

            /* game tree is defined, give headline information  */
            infotree(); // INFO

            infosf(); // INFO

            /* process games                    */
            int gamecount = 0;
            int priorSeed = 0;

            t.allocateRealizationPlan();
            if (outputPivotResults && outputPivotHeaderFirst) /* otherwise the header is garbled by LCP output */
                inforesultheader();
            int priorcount; 
            Rational[] equilibriumProbabilities = null;
            /* multiple priors 	*/
            for (priorcount = 0; priorcount < numPriors; priorcount++)
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                TabbedText.WriteLine($"Prior {priorcount + 1} of {numPriors}");
                if (priorcount == 0 || !tracingEquilibrium)
                    t.genprior(priorSeed);
                else
                {
                    t.MakePlayerMovesStrictlyMixed(equilibriumProbabilities, (Rational)1 / (Rational)1_000);
                    updateActionWhenTracingPathOfEquilibrium(priorcount, t);
                }
                if (outputGameTreeSetup)
                    t.outputGameTree();
                if (outputPrior)
                    t.outprior();
                processgame(seed + gamecount);
                if (outputPivotResults && !outputPivotHeaderFirst)
                    inforesultheader();
                infopivotresult(priorSeed, seed + gamecount);
                priorSeed++;
                equilibriumProbabilities = t.GetPlayerMovesFromSolution().ToArray(); // probabilities for each non-chance player, ordered by player, information set, and then action.
                int? sameAsEquilibrium = equilibria.Select((item, index) => ((Rational[] item, int index)?) (item, index)).FirstOrDefault(x => x != null && x.Value.item.SequenceEqual(equilibriumProbabilities))?.index;
                if (sameAsEquilibrium != null)
                    TabbedText.WriteLine($"Same as equilibrium {sameAsEquilibrium + 1}"); // note that equilibria are one-indexed
                else
                {
                    equilibria.Add(equilibriumProbabilities);
                    TabbedText.WriteLine($"Equilibrium {equilibria.Count()}");
                    if (outputEquilibrium)
                        t.showEquilibrium(outputRealizationPlan);
                }
                outputGameTreeSetup = false;
                TabbedText.WriteLine($"Elapsed milliseconds prior {priorcount + 1}: {s.ElapsedMilliseconds}");
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

using ACESim;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.CPrint;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.RatStatic;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.ColumnPrinter;
using static ACESim.ArrayFormConversionExtension;
using System.Reflection.Metadata.Ecma335;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class Treedef
    {
        public Lemke Lemke = new Lemke();

        public const int PLAYERS = 3;
        public const int NAMECHARS = 8;
        public const int ROOT = 1;
        public const int MAXRANDPAY = 100;
        public const int MINUSINFTY = -30000;
        public const int MAXSTRL = 100;
        public char[] an1 = new char[] { '!', 'A', 'a' };
        public char[] an2 = new char[] { '/', 'Z', 'z' };
        public node[] nodes;
        public node root;
        public iset[] isets;
        public move[] moves;
        public outcome[] outcomes;

        public int[] firstiset = new int[PLAYERS];
        public int[] firstmove = new int[PLAYERS];

        public int[] nseqs = new int[3];
        public int[] nisets = new int[3];

        public static Payvec maxpay = new Payvec();


        public int moveIndex(move move) => moves.Select((item, index) => (item, index)).First(x => x.item == move).index;
        public int isetIndex(iset iset) => isets.Select((item, index) => (item, index)).First(x => x.item == iset).index;
        public int outcomeIndex(outcome outcome) => outcomes.Select((item, index) => (item, index)).First(x => x.item == outcome).index;

        int nextrandpay()
        {
            return (int)(RandomGenerator.NextDouble() * MAXRANDPAY);
        }

        void alloctree(int nn, int ni, int nm, int no)
        {
            nodes = new node[nn];
            isets = new iset[ni];
            moves = new move[nm];
            outcomes = new outcome[no];
        }       /* end of alloctree(nn, ni, nm, no)             */

        bool genseqin()
        {
            bool isnotok = false;
            int pl;
            node u;
            move seq;

            /* set  nseqs[], nisets[]               */
            for (pl = 0; pl < PLAYERS; pl++)
            {
                nseqs[pl] = firstmove[pl + 1] - firstmove[pl];
                nisets[pl] = firstiset[pl + 1] - firstiset[pl];
            }

            /* set seqin for all isets to NULL      */
            foreach (var h in isets)
                h.seqin = null;

            for (pl = 0; pl < PLAYERS; pl++)
                root.defseq[pl] = moves[firstmove[pl]];
            root.iset.seqin = firstmove[root.iset.player];

            for (int i = 2; i < nodes.Length; i++)
            {
                u = nodes[i];
                if (u.fatherIndex(nodes) >= i)
                /* tree is not topologically sorted     */
                {
                    isnotok = true;
                    printf("tree not topologically sorted: father %d ",
                        u.fatherIndex(nodes));
                    printf("is larger than node %d itself.\n", i);
                }

                /* update sequence triple, new only for move leading to  u  */
                for (pl = 0; pl < PLAYERS; pl++)
                    u.defseq[pl] = u.father.defseq[pl];
                u.defseq[u.reachedby.atiset.player] = u.reachedby;

                /* update sequence for iset, check perfect recall           */
                if (!(u.terminal))
                {
                    var h = u.iset;
                    seq = u.defseq[h.player];
                    if (h.seqin == null)
                        h.seqin = moveIndex(seq);
                    else if (seq != moves[(int) h.seqin])
                    /* not the same as last sequence leading to info set    */
                    {
                        isnotok = true;
                        /* need output routines for isets, moves, later         */
                        printf("imperfect recall in info set no. %d ", u.isetIndex(isets));
                        printf("named %s\n", h.name);
                        printf("different sequences no. %d,", moveIndex(seq));
                        printf(" %d\n", h.seqin);
                    }
                }       /* end of "u decision node"     */
            }           /* end of "for all nodes u"     */
            return isnotok;
        }       /* end of  bool genseqin()               */

        void maxpayminusone(bool bprint)
        {
            char[] s = new char[MAXSTRL];
            int pm;
            outcome z;
            Payvec addtopay = new Payvec();

            for (pm = 0; pm < PLAYERS - 1; pm++)
            {
                maxpay[pm] = ratfromi(MINUSINFTY);
                for (int zindex = 0; zindex < outcomes.Length; zindex++)
                {
                    z = outcomes[zindex];
                    if (ratgreat(z.pay[pm], maxpay[pm]))
                        maxpay[pm] = z.pay[pm];
                }
                if (bprint)     /* comment to stdout    */
                {
                    rattoa(maxpay[pm], s);
                    printf("Player %d's maximum payoff is %s , ", pm + 1, s);
                    printf("normalize to -1.\n");
                }
                addtopay[pm] = ratneg(ratadd(maxpay[pm], ratfromi(1)));
                for (int zindex = 0; zindex < outcomes.Length; zindex++)
                {
                    z = outcomes[zindex];
                    z.pay[pm] = ratadd(z.pay[pm], addtopay[pm]);
                }
            }
        }

        void autoname()
        {
            int pl, anbase, max, digits, i, i1, j;
            iset h;

            for (pl = 0; pl < PLAYERS; pl++)    /* name isets of player pl      */
            {
                max = anbase = an2[pl] - an1[pl] + 1;
                for (digits = 1; max < nisets[pl]; max *= anbase, digits++)
                    ;
                if (digits >= NAMECHARS)
                {
                    printf("Too many isets (%d) of player %d.  ", nisets[pl], pl);
                    printf("change NAMECHARS to %d or larger\n", digits + 1);
                    throw new Exception("Digits exceeds namechars");
                }
                for (i = 0; i < nisets[pl]; i++)
                {
                    i1 = i;
                    h = isets[firstiset[pl] + i];
                    h.name[digits] = '\0';
                    for (j = digits - 1; j >= 0; j--)
                    {
                        h.name[j] = an1[pl + (i1 % anbase)];
                        i1 /= anbase;
                    }
                }
            }
        }       /* end of  autoname()   */

        int movetoa(move c, int pl, ref string s)
        {
            if (c == null)
            {
                s = sprintf("*");
            }
            else if (c == moves[firstmove[pl]])
            {
                s = sprintf("()");
            }
            else 
                s= sprintf("%s%d", c.atiset.name,  moveIndex(c) - c.atiset.move0);
            return s.Length;
        }       /* end of  int movetoa (c, pl, *s)      */

        int seqtoa(move seq, int pl, ref string s)
        {
            int len;
            if (seq == null)
            {
                s = sprintf("*");
                return s.Length;
            }
            if (seq == moves[firstmove[pl]])
            {
                s = sprintf(".");
                return s.Length;
            }
            len = seqtoa(moves[(int) seq.atiset.seqin], pl, ref s);       /* recursive call       */
            string s2 = null;
            int s2len = movetoa(seq, pl, ref s2);
            s = s + s2;
            return s.Length;
        }       /* end of  int seqtoa (seq, pl, *s)     */


        void rawtreeprint()
        {
            string s = null;
            int pl;
            node u;
            iset h;
            move c;

            /* printing nodes       */
            colset(6 + PLAYERS - 1 + PLAYERS);
            colleft(0);
            colpr("node");
            colpr("leaf");
            colpr("iset");
            colpr("father");
            colpr("reachedby");
            colpr("outcome");
            for (pl = 1; pl < PLAYERS; pl++)
            {
                s = sprintf("pay%d", pl); 
                colpr(s);
            }
            for (pl = 0; pl < PLAYERS; pl++)
            {
                s = sprintf("isp%d", pl); 
                colpr(s);
            }
            colnl();
            for (int uindex = 1; uindex < nodes.Length; uindex++)
            {
                u = nodes[uindex];
                colipr(uindex);
                colipr(u.terminal ? 1 : 0);
                colipr(u.isetIndex(isets));
                colipr(u.fatherIndex(nodes));
                colipr(u.reachedByIndex(moves));
                colipr(u.outcomeIndex(outcomes));
                for (pl = 1; pl < PLAYERS; pl++)
                    if (u.terminal)
                    {
                        rattoa(u.outcome.pay[pl - 1], ref s);
                        colpr(s);
                    }
                    else
                        colpr("");
                for (pl = 0; pl < PLAYERS; pl++)
                    colipr(moveIndex(u.defseq[pl]));
            }
            colout();
            printf("\n");
            /* printing isets       */
            colset(8);
            colleft(0);
            colpr("iset");
            colpr("player");
            colpr("nmoves");
            colpr("move0");
            colpr("name");
            colpr("seqin");
            colpr("ncontin");
            colpr("prefact");
            colnl();
            pl = 0;
            for (int hIndex = 0; hIndex < firstiset[PLAYERS]; hIndex++)
            {
                h = isets[hIndex];
                while (hIndex == firstiset[pl])
                {
                    s = sprintf("pl%d:", pl); 
                    colpr(s);
                    colnl();
                    pl++;
                }
                colipr(isetIndex(h));
                colipr(h.player);
                colipr(h.nmoves);
                colipr(h.move0);
                colpr(new string(h.name));
                colipr((int) h.seqin);
                colipr(h.ncontin);
                colipr(h.prefact);
            }
            colout();
            printf("\n");
            /* printing moves       */
            colset(9);
            colleft(0);
            colleft(1);
            colleft(2);
            colpr("move");
            colpr("name");
            colpr("seqname");
            colpr("atiset");
            colpr("behavprob");
            colpr("realprob");
            colpr("redsfcol");
            colpr("ncompat");
            colpr("offset");
            colnl();
            pl = 0;
            for (int cindex = 0; cindex < firstmove[PLAYERS]; cindex++)
            {
                c = moves[cindex];
                while (cindex == firstmove[pl])
                {
                    s = sprintf("pl%d:", pl); 
                    colpr(s);
                    colnl();
                    pl++;
                }
                /* pl is now the NEXT possible player       */
                colipr(cindex);
                movetoa(c, pl - 1, ref s); 
                colpr(s);
                seqtoa(c, pl - 1, ref s); 
                colpr(s);
                colipr(isetIndex(c.atiset));
                rattoa(c.behavprob, ref s); 
                colpr(s);
                rattoa(c.realprob, ref s); 
                colpr(s);
                colipr(c.redsfcol);
                colipr(c.ncompat);
                colipr(c.offset);
            }
            colout();
        }       /* end of  rawtreeprint()       */

        /* PRIOR */

        public void gencentroid()
        {
            move c;
            int pl;
            for (pl = 1; pl < Treedef.PLAYERS; pl++)
                for (int cindex = firstmove[pl] + 1; cindex < firstmove[pl + 1]; cindex++)
                {
                    c = moves[cindex];
                    c.behavprob.num = 1;
                    c.behavprob.den = c.atiset.nmoves;
                }
        }

        void genprior(Flagsprior flags)
        {
            int pl;
            iset h;

            if (0 == flags.seed)
            {
                gencentroid();
                return;
            }
            /* generate random priors for all information sets	*/
            RandomGeneratorInstanceManager.Reset(false, true); // move to next arbitrary seed
            // srand(FIRSTPRIORSEED + flags.seed);
            for (pl = 1; pl < PLAYERS; pl++)
                for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
                {
                    h = isets[hindex];
                    if (h.nmoves > 2)
                    {
                        // We must create fractions that add up to 1 (each greater than 0).
                        // So, we'll just take random numbers from 1 to 10, use those for numerators
                        // and the sum for a denominator.
                        int denominator = 0;
                        for (int i = 0; i < h.nmoves; i++)
                        {
                            double maxValue = 9;
                            int numerator = 1 + (int) Math.Floor(maxValue * RandomGenerator.NextDouble());
                            moves[h.move0 + i].behavprob = ratfromi(numerator); // store value so that we remember it
                            denominator += numerator;
                        }
                        for (int i = 0; i < h.nmoves; i++)
                        {
                            Rat a;
                            a.num = moves[h.move0 + i].behavprob.num;
                            a.den = denominator;
                            moves[h.move0 + i].behavprob = a;
                        }
                        //Original code:
                        //fprintf(stderr, "Sorry, only binary info sets so far.\n") ; 
                        //exit(1) ;
                    }
                    else
                    {
                        Rat a;
                        double x;

                        x = RandomGenerator.NextDouble();
                        a = contfract(x, flags.accuracy);
                        /* make sure to get a properly mixed prior,
						 * unless  flags.accuracy == 1,
					 * in which case we have a random pure strategy
					 * because this statement flips 0 to 1 and vice versa
					 */
                        if (a.num == 0)
                        {
                            a.num = 1;
                            a.den = flags.accuracy;
                        }
                        else if (a.den == 1)    /* "else" for pure strategy	*/
                        {
                            a.num = flags.accuracy - 1;
                            a.den = flags.accuracy;
                        }
                        moves[h.move0].behavprob = a;
                        moves[h.move0 + 1].behavprob = ratadd(ratfromi(1), ratneg(a));
                    }
                }
        }

        void outprior()
        {
            int pl;

            printf("------Prior behavior strategies player 1, 2:\n");
            for (pl = 1; pl < PLAYERS; pl++)
            {
                behavtorealprob(pl);
                realplanfromprob(pl, realplan[pl]);
                outbehavstrat(pl, realplan[pl], true);
            }
        }

        /* SEQUENCE FORM */


        Payvec[][] sfpay;
        int[][][] sfconstr = new int[PLAYERS][][];

        int oldnseqs1 = 0;
        int[] oldconstrows = new int[PLAYERS] { 0, 0, 0 };

        void allocsf()
        {
            int pl, i, j;
            int nrows;

            /* payoff matrices, two players only here, init to pay 0        */
            oldnseqs1 = nseqs[1];
            sfpay = CreateJaggedArray<Payvec[][]>(nseqs[1], nseqs[2]);
            for (i = 0; i < nseqs[1]; i++)
                for (j = 0; j < nseqs[2]; j++)
                    for (pl = 1; pl < PLAYERS; pl++)
                        sfpay[i][j][pl - 1] = ratfromi(0);
            /* constraint matrices, any number of players           */
            /* sfconstr[0] stays unused                             */
            for (pl = 1; pl < PLAYERS; pl++)
            {
                oldconstrows[pl] = nrows = nisets[pl] + 1;   /* extra row for seq 0  */
                sfconstr[pl] = CreateJaggedArray<int[][]>(nrows, nseqs[pl]);
            }
        }       /* end of allocsf()     */

        void gensf()
        {
            int pl, i, j;
            outcome z = null;
            allocsf();

            behavtorealprob(0);     /* get realization probabilities of leaves      */

            /* sf payoff matrices                   */
            for (int zindex = 0; zindex < outcomes.Length; zindex++)
            {
                z = outcomes[zindex];
                node u = z.whichnode;
                i = moveIndex(u.defseq[1]) - firstmove[1]; 
                j = moveIndex(u.defseq[2]) - firstmove[2];
                for (pl = 1; pl < PLAYERS; pl++)
                    sfpay[i][j][pl - 1] = ratadd(sfpay[i][j][pl - 1],
                        ratmult(u.defseq[0].realprob, z.pay[pl - 1]));
            }
            /* sf constraint matrices, sparse fill  */
            for (pl = 1; pl < PLAYERS; pl++)
            {
                sfconstr[pl][0][0] = 1;     /* empty sequence                       */
                for (i = 0; i < nisets[pl]; i++)
                    sfconstr[pl][i + 1][(int) isets[firstiset[pl] + i].seqin - firstmove[pl]] = -1;
                for (j = 1; j < nseqs[pl]; j++)
                    sfconstr[pl][isetIndex(moves[firstmove[pl] + j].atiset) - firstiset[pl] + 1][j] = 1;
            }
        }       /* end of  gensf()              */

        void sflcp()
        {
            int i;

            gensf();
            Lemke.setlcp(nseqs[1] + nisets[2] + 1 + nseqs[2] + nisets[1] + 1);
            /* fill  M  */
            /* -A       */
            payratmatcpy(sfpay, 0, true, false, nseqs[1], nseqs[2],
            Lemke.lcpM, 0, nseqs[1] + nisets[2] + 1);
            /* -E\T     */
            intratmatcpy(sfconstr[1], true, true, nisets[1] + 1, nseqs[1],
            Lemke.lcpM, 0, nseqs[1] + nisets[2] + 1 + nseqs[2]);
            /* F        */
            intratmatcpy(sfconstr[2], false, false, nisets[2] + 1, nseqs[2],
            Lemke.lcpM, nseqs[1], nseqs[1] + nisets[2] + 1);
            /* -B\T     */
            payratmatcpy(sfpay, 1, true, true, nseqs[1], nseqs[2],
            Lemke.lcpM, nseqs[1] + nisets[2] + 1, 0);
            /* -F\T     */
            intratmatcpy(sfconstr[2], true, true, nisets[2] + 1, nseqs[2],
            Lemke.lcpM, nseqs[1] + nisets[2] + 1, nseqs[1]);
            /* E        */
            intratmatcpy(sfconstr[1], false, false, nisets[1] + 1, nseqs[1],
            Lemke.lcpM, nseqs[1] + nisets[2] + 1 + nseqs[2], 0);
            /* define RHS q,  using special shape of SF constraints RHS e,f     */
            for (i = 0; i < Lemke.lcpdim; i++)
                Lemke.rhsq[i] = ratfromi(0);
            Lemke.rhsq[nseqs[1]] = ratneg(ratfromi(1));
            Lemke.rhsq[nseqs[1] + nisets[2] + 1 + nseqs[2]] = ratneg(ratfromi(1));
        }

        void realplanfromprob(int pl, Rat[] rplan)
        {
            int i;

            for (i = 0; i < nseqs[pl]; i++)
                rplan[i] = moves[firstmove[pl] + i].realprob;
        }

        int propermixisets(int pl, Rat[] rplan)
        {
            int mix = 0;
            int i;
            move c;
            iset h;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    if (rplan[cindex - firstmove[pl]].num != 0 &&
                        !ratiseq(rplan[cindex - firstmove[pl]],
                                  rplan[(int) h.seqin - firstmove[pl]]))
                    {
                        mix++;
                        break;
                    }
                }
            }
            return mix;
        }

        void outrealplan(int pl, Rat[] rplan)
        {
            int i;
            string s = null;

            colset(nseqs[pl]);
            for (i = 0; i < nseqs[pl]; i++)
            {
                seqtoa(moves[firstmove[pl] + i], pl, ref s);
                colpr(s);
            }
            for (i = 0; i < nseqs[pl]; i++)
            {
                rattoa(rplan[i], ref s);
                colpr(s);
            }
            colout();
        }

        void outbehavstrat(int pl, Rat[] rplan, bool bnewline)
        {
            string s = null;
            int i;
            move c;
            iset h;
            Rat rprob, bprob;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[cindex - firstmove[pl]];
                    if (rprob.num != 0)
                    {
                        movetoa(c, pl, ref s);
                        printf(" %s", s);
                        bprob = ratdiv(rprob, rplan[(int) h.seqin - firstmove[pl]]);
                        if (!ratiseq(bprob, ratfromi(1)))
                        {
                            rattoa(bprob, ref s);
                            printf(":%s", s);
                        }
                    }
                }
            }
            if (bnewline)
                printf("\n");
        }

        void outbehavstrat_moves(int pl, Rat[] rplan, int offset, bool bnewline)
        {
            int i;
            move c;
            iset h;
            Rat rprob;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstmove[pl]];
                    printf("%d", rprob.num);
                    printf("/");
                    printf("%d", rprob.den);
                    if (hindex != firstiset[pl + 1] - 1 || i != h.nmoves - 1)
                    {
                        printf(",");
                    }
                }
            }
            if (bnewline)
                printf("\n");
        }

        void sfprint()
        {
            string s = null;
            int i, j, k;
            printf("SF payoffs and constraints:\n");

            colset(nseqs[2] + 2 + nisets[1]);
            colleft(0);
            colpr("pay1");
            for (j = 0; j < nseqs[2]; j++)
            {
                seqtoa(moves[firstmove[2] + j], 2, ref s); 
                colpr(s);
            }
            colnl();
            colpr("pay2");
            for (j = 0; j < nseqs[2]; j++)
                colpr(" ");
            colpr("cons1");
            for (k = 1; k <= nisets[1]; k++)
                /* headers for constraint matrix pl1, printed on right of payoffs   */
                colpr(isets[firstiset[1] + k - 1].name);
            for (i = 0; i < nseqs[1]; i++)
            {
                /* payoffs player 1 */
                seqtoa(moves[firstmove[1] + i], 1, ref s); 
                colpr(s);
                for (j = 0; j < nseqs[2]; j++)
                {
                    rattoa(sfpay[i][j][0], ref s); 
                    colpr(s);
                }
                colnl();
                /* payoffs player 2 */
                colpr("");
                for (j = 0; j < nseqs[2]; j++)
                {
                    rattoa(sfpay[i][j][1], ref s); 
                    colpr(s);
                }
                /* constraints player 1 */
                for (k = 0; k <= nisets[1]; k++)
                    colipr(sfconstr[1][k][i]);
                colnl();
            }
            /* constraints player 2 */
            for (k = 0; k <= nisets[2]; k++)
            {
                colnl();
                if (k == 0)
                    colpr("cons2");
                else
                    colpr(isets[firstiset[2] + k - 1].name);
                for (j = 0; j < nseqs[2]; j++)
                    colipr(sfconstr[2][k][j]);
                colnl();
            }
            colout();
        }       /* end of  sfprint()            */

        /* SFNF */

        Rat[][] realplan = new Rat[PLAYERS][];

        void allocrealplan(Rat[][] realpl)
        {
            int pl;

            for (pl = 0; pl < PLAYERS; pl++)
                realpl[pl] = new Rat[nseqs[pl]];
        }

        void behavtorealprob(int pl)
        {
            move c;
            int lastmoveindex = firstmove[pl + 1];
            move lastmove = moves[lastmoveindex];
            moves[firstmove[pl]].realprob = ratfromi(1);  /* empty seq has probability 1  */
            for (int cindex = firstmove[pl] + 1; cindex < lastmoveindex; cindex++)
            {
                c = moves[cindex];
                c.realprob = ratmult(c.behavprob, moves[(int)c.atiset.seqin].realprob);
            }
        }

        void payratmatcpy(Payvec[][] frommatr, int plminusone, bool bnegate,
                bool btranspfrommatr, int nfromrows, int nfromcols,
                Rat[][] targetmatr, int targrowoffset, int targcoloffset)
        {
            int i, j;
            for (i = 0; i < nfromrows; i++)
                for (j = 0; j < nfromcols; j++)
                    if (btranspfrommatr)
                        targetmatr[j + targrowoffset][i + targcoloffset]
                        = bnegate ? ratneg(frommatr[i][j][plminusone]) : frommatr[i][j][plminusone];
                    else
                        targetmatr[i + targrowoffset][j + targcoloffset]
                        = bnegate ? ratneg(frommatr[i][j][plminusone]) : frommatr[i][j][plminusone];
        }

        void intratmatcpy(int[][] frommatr, bool bnegate,
                bool btranspfrommatr, int nfromrows, int nfromcols,
                Rat[][] targetmatr, int targrowoffset, int targcoloffset)
        {
            int i, j;
            for (i = 0; i < nfromrows; i++)
                for (j = 0; j < nfromcols; j++)
                    if (btranspfrommatr)
                        targetmatr[j + targrowoffset][i + targcoloffset]
                        = ratfromi(bnegate ? -frommatr[i][j] : frommatr[i][j]);
                    else
                        targetmatr[i + targrowoffset][j + targcoloffset]
                        = ratfromi(bnegate ? -frommatr[i][j] : frommatr[i][j]);
        }

        void covvector()
        {
            int i, j, dim1, dim2, offset;

            behavtorealprob(1);
            behavtorealprob(2);

            dim1 = nseqs[1];
            dim2 = nseqs[2];
            offset = dim1 + 1 + nisets[2];
            /* covering vector  = -rhsq */
            for (i = 0; i < Lemke.lcpdim; i++)
                Lemke.vecd[i] = ratneg(Lemke.rhsq[i]);
            /* first blockrow += -Aq    */
            for (i = 0; i < dim1; i++)
                for (j = 0; j < dim2; j++)
                    Lemke.vecd[i] = ratadd(Lemke.vecd[i], ratmult(Lemke.lcpM[i][offset + j],
                              moves[firstmove[2] + j].realprob));
            /* RSF yet to be done*/
            /* third blockrow += -B\T p */
            for (i = offset; i < offset + dim2; i++)
                for (j = 0; j < dim1; j++)
                    Lemke.vecd[i] = ratadd(Lemke.vecd[i], ratmult(Lemke.lcpM[i][j],
                              moves[firstmove[1] + j].realprob));
            /* RSF yet to be done*/
        }


        void showeq(bool bshortequil, int docuseed)
        {
            int offset;


            offset = nseqs[1] + 1 + nisets[2];
            /*  non-sparse printing
            printf("Equilibrium realization plan player 1:\n");
            outrealplan(1, solz);
            printf("Equilibrium realization plan player 2:\n");
            outrealplan(2, solz + offset); */
            if (bshortequil)
                printf("BEQ>%4d<1>", docuseed);
            else
                printf("......Equilibrium behavior strategies player 1, 2:\n");
            outbehavstrat_moves(1, Lemke.solz, 0, !bshortequil); /* remove _moves for original code */
            if (bshortequil)
                printf(" <2>");
            outbehavstrat_moves(2, Lemke.solz, offset, true);  /* remove _moves for original code */
        }

    }
}

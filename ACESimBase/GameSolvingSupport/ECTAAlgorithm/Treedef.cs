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

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class Treedef
    {
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
            root.iset.seqin = moves[firstmove[root.iset.player]];

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
                        h.seqin = seq;
                    else if (seq != h.seqin)
                    /* not the same as last sequence leading to info set    */
                    {
                        isnotok = true;
                        /* need output routines for isets, moves, later         */
                        printf("imperfect recall in info set no. %d ", u.isetIndex(isets));
                        printf("named %s\n", h.name);
                        printf("different sequences no. %d,", moveIndex(seq));
                        printf(" %d\n", moveIndex(h.seqin));
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
                s= sprintf("%s%d", c.atiset.name,  moveIndex(c) - moveIndex(c.atiset.move0));
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
            len = seqtoa(seq.atiset.seqin, pl, ref s);       /* recursive call       */
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
                colipr(moveIndex(h.move0));
                colpr(new string(h.name));
                colipr(moveIndex(h.seqin));
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



    }
}

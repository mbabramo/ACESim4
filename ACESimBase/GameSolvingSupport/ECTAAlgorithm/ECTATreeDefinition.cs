using ACESim;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.CPrint;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.RationalOperations;
using static ACESimBase.GameSolvingSupport.ECTAAlgorithm.ColumnPrinter;
using static ACESim.ArrayFormConversionExtension;
using Rationals;
using System.Numerics;

namespace ACESimBase.GameSolvingSupport.ECTAAlgorithm
{
    public class ECTATreeDefinition
    {
        public Lemke Lemke;

        public const int PLAYERS = 3;
        public const int NAMECHARS = 8;
        public const int ROOT = 1;
        public const int MAXRANDPAY = 100;
        public const int MINUSINFTY = -30000;
        public const int MAXSTRL = 100;
        public char[] an1 = new char[] { '!', 'A', 'a' };
        public char[] an2 = new char[] { '/', 'Z', 'z' };
        public ECTANode[] nodes;
        public const int rootindex = 1;
        public ECTANode root => nodes[rootindex];
        public ECTAInformationSet[] isets;
        public ECTAMove[] moves;
        public ECTAOutcome[] outcomes;

        public int lastnode;
        public int lastoutcome;
        public int[] firstiset = new int[PLAYERS + 1];
        public int[] firstmove = new int[PLAYERS + 1];

        public int[] nseqs = new int[3];
        public int[] nisets = new int[3];

        public ECTAPayVector maxpay = new ECTAPayVector();


        public int moveIndex(ECTAMove move) => moves.Select((item, index) => (item, index)).First(x => x.item == move).index;
        public int isetIndex(ECTAInformationSet iset) => isets.Select((item, index) => (item, index)).First(x => x.item == iset).index;
        public int outcomeIndex(ECTAOutcome outcome) => outcomes.Select((item, index) => (item, index)).First(x => x.item == outcome).index;

        public void alloctree(int nn, int ni, int nm, int no)
        {
            nodes = new ECTANode[nn];
            for (int i = 0; i < nn; i++)
                nodes[i] = new ECTANode();
            lastnode = nn;
            isets = new ECTAInformationSet[ni];
            for (int i = 0; i < ni; i++)
                isets[i] = new ECTAInformationSet();
            moves = new ECTAMove[nm];
            for (int i = 0; i < nm; i++)
                moves[i] = new ECTAMove();
            outcomes = new ECTAOutcome[no];
            for (int i = 0; i < no; i++)
                outcomes[i] = new ECTAOutcome();
            lastoutcome = no;
        }       /* end of alloctree(nn, ni, nm, no)        */

        public bool genseqin()
        {
            bool isnotok = false;
            int pl;
            ECTANode u;
            int seq;


            // the code assumes that firstiset and firstmove are set for a hypothetical next player.
            // This allows us, for any player, to subtract the index for the next player to the index for this
            // player to get the total number of sequences or information sets.
            firstiset[PLAYERS] = isets.Length;
            firstmove[PLAYERS] = moves.Length;

            /* set  nseqs[], nisets[]               */
            for (pl = 0; pl < PLAYERS; pl++)
            {
                nseqs[pl] = firstmove[pl + 1] - firstmove[pl];
                nisets[pl] = firstiset[pl + 1] - firstiset[pl];
            }

            /* set seqin for all isets to NULL      */
            foreach (var h in isets)
                h.seqin = -1;

            for (pl = 0; pl < PLAYERS; pl++)
                root.defseq[pl] = firstmove[pl];
            isets[root.iset].seqin = firstmove[isets[root.iset].player];

            for (int i = 2; i < nodes.Length; i++)
            {
                u = nodes[i];
                if (u.father >= i)
                /* tree is not topologically sorted     */
                {
                    isnotok = true;
                    tabbedtextf("tree not topologically sorted: father %d ",
                        u.father);
                    tabbedtextf("is larger than node %d itself.\n", i);
                }

                /* update sequence triple, new only for move leading to  u  */
                for (pl = 0; pl < PLAYERS; pl++)
                    u.defseq[pl] = nodes[u.father].defseq[pl];
                u.defseq[isets[moves[u.reachedby].atiset].player] = u.reachedby;

                /* update sequence for iset, check perfect recall           */
                if (!(u.terminal))
                {
                    var h = isets[u.iset];
                    seq = u.defseq[h.player];
                    if (h.seqin == -1)
                        h.seqin = seq;
                    else if (seq != (int) h.seqin && h.player != 0)
                    /* not the same as last sequence leading to info set    */
                    {
                        isnotok = true;
                        /* need output routines for isets, moves, later         */
                        tabbedtextf("imperfect recall in info set no. %d ", u.iset);
                        tabbedtextf("named %s\n", h.name);
                        tabbedtextf("different sequences no. %d,", seq);
                        tabbedtextf(" %d\n", h.seqin);
                    }
                }       /* end of "u decision node"     */
            }           /* end of "for all nodes u"     */
            return isnotok;
        }       /* end of  bool genseqin()               */

        public void maxpayminusone(bool bprint)
        {
            char[] s = new char[MAXSTRL];
            int pm;
            ECTAOutcome z;
            ECTAPayVector addtopay = new ECTAPayVector();

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
                    TabbedText.WriteLine($"Player {pm}'s maximum payoff is {maxpay[pm]}, normalized to -1");
                }
                addtopay[pm] = ratneg(ratadd(maxpay[pm], ratfromi(1)));
                for (int zindex = 0; zindex < outcomes.Length; zindex++)
                {
                    z = outcomes[zindex];
                    z.pay[pm] = ratadd(z.pay[pm], addtopay[pm]);
                }
            }
        }

        public void autoname()
        {
            int pl, anbase, max, digits, i, i1, j;
            ECTAInformationSet h;

            for (pl = 0; pl < PLAYERS; pl++)    /* name isets of player pl      */
            {
                max = anbase = an2[pl] - an1[pl] + 1;
                for (digits = 1; max < nisets[pl]; max *= anbase, digits++)
                    ;
                if (digits >= NAMECHARS)
                {
                    tabbedtextf("Too many isets (%d) of player %d.  ", nisets[pl], pl);
                    tabbedtextf("change NAMECHARS to %d or larger\n", digits + 1);
                    throw new Exception("Digits exceeds namechars");
                }
                for (i = 0; i < nisets[pl]; i++)
                {
                    i1 = i;
                    h = isets[firstiset[pl] + i];
                    if (h.name == null || h.name == "")
                    {
                        h.name = "";
                        for (j = digits - 1; j >= 0; j--)
                        {
                            h.name += (char)(an1[pl] + (i1 % anbase));
                            i1 /= anbase;
                        }
                    }
                }
            }
        }       /* end of  autoname()   */

        int movetoa(ECTAMove c, int pl, ref string s)
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
                s= sprintf("%s%d", isets[c.atiset].name,  moveIndex(c) - isets[c.atiset].move0);
            return s.Length;
        }       /* end of  int movetoa (c, pl, *s)      */

        int seqtoa(ECTAMove seq, int pl, ref string s)
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
            len = seqtoa(moves[isets[seq.atiset].seqin], pl, ref s);       /* recursive call       */
            string s2 = null;
            int s2len = movetoa(seq, pl, ref s2);
            s = s + s2;
            return s.Length;
        }       /* end of  int seqtoa (seq, pl, *s)     */


        public void outputGameTree()
        {
            string s = null;
            int pl;
            ECTANode u;
            ECTAInformationSet h;
            ECTAMove c;

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
                colipr(u.iset);
                colipr(u.father);
                colipr(u.reachedby);
                colipr(u.outcome);
                for (pl = 1; pl < PLAYERS; pl++)
                    if (u.terminal)
                    {
                        rattoa(outcomes[u.outcome].pay[pl - 1], ref s);
                        colpr(s);
                    }
                    else
                        colpr("");
                for (pl = 0; pl < PLAYERS; pl++)
                    colipr(u.defseq[pl]);
            }
            colout();
            tabbedtextf("\n");
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
            for (int hIndex = 0; hIndex < isets.Length; hIndex++)
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
                colpr(h.name);
                colipr((int) h.seqin);
                colipr(h.ncontin);
                colipr(h.prefact);
            }
            colout();
            tabbedtextf("\n");
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
            for (int cindex = 0; cindex < moves.Length; cindex++)
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
                colipr(c.atiset);
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

        // Make it possible to have predictable series of random numbers -- for debugging only. 
        bool RandDebuggingMode = false; // DEBUG
        int RandDebuggingIndex = 0;
        const int RandDebuggingTotal = 1000;
        static double[] RandNums = { 0.57093, 0.04351, 0.10683, 0.21573, 0.46976, 0.95347, 0.25219, 0.80208, 0.25461, 0.71108, 0.33842, 0.40057, 0.71189, 0.12997, 0.7447, 0.76698, 0.46236, 0.09272, 0.66976, 0.72175, 0.83181, 0.57792, 0.18254, 0.72391, 0.49899, 0.50533, 0.17756, 0.2092, 0.05193, 0.15968, 0.46083, 0.6745, 0.10398, 0.7443, 0.74163, 0.5077, 0.84802, 0.59178, 0.78791, 0.88328, 0.72022, 0.43067, 0.8651, 0.00503, 0.79837, 0.62926, 0.06685, 0.58658, 0.60144, 0.60647, 0.90122, 0.54036, 0.19372, 0.69091, 0.411, 0.99301, 0.86097, 0.38291, 0.71674, 0.96443, 0.77591, 0.04299, 0.75015, 0.09493, 0.25025, 0.66393, 0.29659, 0.9676, 0.38834, 0.237, 0.91896, 0.87058, 0.30481, 0.78648, 0.00152, 0.40114, 0.71282, 0.17751, 0.92555, 0.86973, 0.43847, 0.59097, 0.60776, 0.44546, 0.25846, 0.92047, 0.48077, 0.41307, 0.3333, 0.74862, 0.64673, 0.4651, 0.87503, 0.82348, 0.10737, 0.67723, 0.68052, 0.77017, 0.75478, 0.13444, 0.33267, 0.09926, 0.19824, 0.36444, 0.68751, 0.03064, 0.23556, 0.40724, 0.68938, 0.00986, 0.28019, 0.68346, 0.45737, 0.84701, 0.52596, 0.18494, 0.43524, 0.30469, 0.83647, 0.32978, 0.18315, 0.88352, 0.6343, 0.63973, 0.59027, 0.45385, 0.99554, 0.48133, 0.67911, 0.3243, 0.72062, 0.94265, 0.42273, 0.79111, 0.53706, 0.33921, 0.39273, 0.24332, 0.75795, 0.22783, 0.68491, 0.07353, 0.72369, 0.32344, 0.46842, 0.96327, 0.00774, 0.02802, 0.29752, 0.92243, 0.24199, 0.37583, 0.9337, 0.425, 0.95129, 0.44915, 0.46496, 0.55851, 0.77418, 0.23366, 0.03509, 0.75423, 0.72813, 0.36509, 0.28923, 0.33754, 0.26073, 0.66626, 0.30995, 0.18675, 0.79221, 0.19192, 0.54674, 0.60864, 0.64487, 0.10962, 0.15983, 0.31085, 0.1713, 0.627, 0.44612, 0.96752, 0.18381, 0.75668, 0.0823, 0.3448, 0.00895, 0.99772, 0.83982, 0.08791, 0.87425, 0.83422, 0.46914, 0.52429, 0.19274, 0.93069, 0.3454, 0.3586, 0.03421, 0.13088, 0.70254, 0.34148, 0.71807, 0.11077, 0.13022, 0.05007, 0.82908, 0.32506, 0.78014, 0.84167, 0.28932, 0.15411, 0.38721, 0.14718, 0.78754, 0.06757, 0.57042, 0.97145, 0.28278, 0.94443, 0.32859, 0.26301, 0.82644, 0.22204, 0.31249, 0.95799, 0.72278, 0.02245, 0.4159, 0.71418, 0.76422, 0.80122, 0.27664, 0.04042, 0.92446, 0.10464, 0.24945, 0.07304, 0.62646, 0.03223, 0.51571, 0.68389, 0.21759, 0.99815, 0.79859, 0.72015, 0.44701, 0.32196, 0.73676, 0.12517, 0.36027, 0.37396, 0.07582, 0.08978, 0.80229, 0.43952, 0.51503, 0.49603, 0.79828, 0.17224, 0.32729, 0.80664, 0.90916, 0.06595, 0.07745, 0.4881, 0.87746, 0.34679, 0.22272, 0.6829, 0.81812, 0.49738, 0.34498, 0.25055, 0.42872, 0.6447, 0.04542, 0.82829, 0.42345, 0.59234, 0.51098, 0.40082, 0.28569, 0.29074, 0.35391, 0.39026, 0.72541, 0.18687, 0.23813, 0.48494, 0.58961, 0.75343, 0.27476, 0.45423, 0.70494, 0.70908, 0.77473, 0.15163, 0.9207, 0.31049, 0.84269, 0.10021, 0.09924, 0.05386, 0.30705, 0.86289, 0.02897, 0.82527, 0.66106, 0.15759, 0.3941, 0.68674, 0.07257, 0.20593, 0.66126, 0.92647, 0.52095, 0.61842, 0.71204, 0.6872, 0.7627, 0.6906, 0.10867, 0.73779, 0.09329, 0.06469, 0.22261, 0.89076, 0.54561, 0.71965, 0.86997, 0.89379, 0.9076, 0.11297, 0.74966, 0.41077, 0.30158, 0.23183, 0.98369, 0.49102, 0.36128, 0.90014, 0.52581, 0.08054, 0.09084, 0.90287, 0.68085, 0.06883, 0.79297, 0.77848, 0.18813, 0.15632, 0.43959, 0.2335, 0.54779, 0.15209, 0.99155, 0.36145, 0.96057, 0.24236, 0.64028, 0.13822, 0.27966, 0.94141, 0.28761, 0.50031, 0.77914, 0.95961, 0.45628, 0.25049, 0.62488, 0.28911, 0.63473, 0.22102, 0.32591, 0.86256, 0.16479, 0.02813, 0.64695, 0.19042, 0.38384, 0.15379, 0.09779, 0.76713, 0.53152, 0.71366, 0.4542, 0.6741, 0.56518, 0.59756, 0.41922, 0.94013, 0.27126, 0.74133, 0.85074, 0.22306, 0.62048, 0.5844, 0.79292, 0.59053, 0.12373, 0.72125, 0.61255, 0.54248, 0.15359, 0.89902, 0.52159, 0.21857, 0.90758, 0.68524, 0.98731, 0.96342, 0.7145, 0.77015, 0.85853, 0.48649, 0.04043, 0.51252, 0.4099, 0.57396, 0.04611, 0.10505, 0.39443, 0.85872, 0.83127, 0.68475, 0.01785, 0.8934, 0.37027, 0.10285, 0.24207, 0.58039, 0.2352, 0.05642, 0.06669, 0.66259, 0.54124, 0.5553, 0.61354, 0.6325, 0.19207, 0.23562, 0.76651, 0.87994, 0.61026, 0.45579, 0.22563, 0.50111, 0.8828, 0.36425, 0.18263, 0.10796, 0.1745, 0.21897, 0.54442, 0.01868, 0.32682, 0.75383, 0.71121, 0.46884, 0.88116, 0.62819, 0.8483, 0.80473, 0.44316, 0.40692, 0.72822, 0.65808, 0.70346, 0.19594, 0.94525, 0.48513, 0.89899, 0.7774, 0.38189, 0.81049, 0.33853, 0.51449, 0.24846, 0.37548, 0.45912, 0.51675, 0.0106, 0.00602, 0.92022, 0.13411, 0.40589, 0.01623, 0.51201, 0.04801, 0.33577, 0.78741, 0.84409, 0.14469, 0.75134, 0.56388, 0.38732, 0.96178, 0.43678, 0.20334, 0.72757, 0.56755, 0.79764, 0.68686, 0.419, 0.69751, 0.20897, 0.3971, 0.83708, 0.73393, 0.68015, 0.81233, 0.50537, 0.33573, 0.00972, 0.36441, 0.61759, 0.84228, 0.88452, 0.30905, 0.00103, 0.71199, 0.14607, 0.65545, 0.86017, 0.15785, 0.64103, 0.75429, 0.02606, 0.81801, 0.42316, 0.37675, 0.07771, 0.04512, 0.64791, 0.89157, 0.7191, 0.32373, 0.58702, 0.22271, 0.92514, 0.00878, 0.17916, 0.77807, 0.36786, 0.52344, 0.28203, 0.50836, 0.13497, 0.38692, 0.94629, 0.54504, 0.07726, 0.12773, 0.2023, 0.01559, 0.42147, 0.14219, 0.8267, 0.26116, 0.05647, 0.45468, 0.37104, 0.01907, 0.31077, 0.3034, 0.73461, 0.46025, 0.68783, 0.11815, 0.64531, 0.29386, 0.25526, 0.6618, 0.38391, 0.99225, 0.53283, 0.368, 0.28758, 0.33672, 0.87581, 0.13267, 0.61932, 0.63914, 0.87172, 0.87812, 0.29949, 0.94999, 0.84282, 0.63232, 0.4701, 0.57691, 0.49704, 0.32586, 0.16624, 0.47046, 0.63774, 0.16008, 0.46731, 0.06447, 0.78883, 0.82179, 0.82054, 0.01683, 0.74161, 0.65243, 0.28978, 0.41546, 0.74381, 0.21005, 0.48276, 0.05347, 0.85461, 0.48997, 0.38535, 0.9238, 0.83536, 0.7319, 0.14735, 0.43265, 0.00391, 0.78462, 0.61743, 0.05551, 0.64805, 0.0684, 0.79682, 0.9294, 0.49556, 0.91346, 0.35451, 0.37275, 0.90069, 0.22312, 0.54789, 0.05402, 0.31213, 0.60249, 0.89753, 0.21929, 0.58833, 0.88403, 0.20618, 0.63277, 0.14956, 0.41663, 0.7223, 0.00706, 0.94052, 0.24244, 0.63509, 0.90584, 0.01273, 0.03466, 0.06056, 0.00421, 0.20436, 0.76503, 0.36878, 0.67321, 0.8556, 0.36039, 0.9199, 0.83035, 0.85554, 0.11001, 0.15278, 0.63149, 0.94208, 0.33132, 0.61167, 0.23287, 0.83437, 0.92806, 0.84431, 0.11988, 0.57845, 0.98466, 0.90594, 0.23142, 0.3461, 0.78976, 0.98888, 0.25313, 0.27837, 0.44868, 0.36001, 0.86603, 0.16256, 0.54368, 0.84967, 0.5471, 0.23371, 0.22432, 0.36369, 0.22795, 0.96413, 0.37582, 0.77723, 0.03955, 0.26385, 0.09081, 0.06498, 0.6092, 0.63076, 0.40222, 0.07147, 0.08468, 0.19035, 0.94067, 0.42576, 0.2197, 0.85909, 0.13736, 0.32711, 0.06584, 0.37151, 0.66677, 0.55199, 0.40686, 0.75, 0.28675, 0.46893, 0.3984, 0.48165, 0.06457, 0.99916, 0.61005, 0.56437, 0.61637, 0.15575, 0.20262, 0.20742, 0.86639, 0.96757, 0.25529, 0.72478, 0.37968, 0.62236, 0.87615, 0.3772, 0.27533, 0.67569, 0.22189, 0.11792, 0.62997, 0.68802, 0.09635, 0.89721, 0.29785, 0.85644, 0.29736, 0.82384, 0.37037, 0.28956, 0.9771, 0.62187, 0.66658, 0.12754, 0.56619, 0.40306, 0.46143, 0.52031, 0.57398, 0.78492, 0.22314, 0.01228, 0.9927, 0.16979, 0.07182, 0.34106, 0.99183, 0.04441, 0.67584, 0.02966, 0.47466, 0.61106, 0.24705, 0.28048, 0.85169, 0.37655, 0.90281, 0.71284, 0.26652, 0.75993, 0.85839, 0.37879, 0.83705, 0.57683, 0.99047, 0.63342, 0.0582, 0.25214, 0.05617, 0.47308, 0.91578, 0.75503, 0.10171, 0.43697, 0.89478, 0.61769, 0.69532, 0.92656, 0.82539, 0.95679, 0.86461, 0.25296, 0.148, 0.34072, 0.81489, 0.36604, 0.37483, 0.3861, 0.27586, 0.18964, 0.50026, 0.74859, 0.25379, 0.81405, 0.92653, 0.84435, 0.17523, 0.41586, 0.17932, 0.4384, 0.28286, 0.73969, 0.98824, 0.20276, 0.11388, 0.71964, 0.50935, 0.81008, 0.3857, 0.234, 0.68124, 0.97624, 0.88744, 0.30973, 0.89532, 0.60543, 0.23079, 0.49633, 0.76194, 0.62443, 0.25859, 0.67211, 0.97628, 0.86653, 0.97282, 0.16719, 0.50124, 0.28766, 0.51043, 0.05043, 0.44246, 0.27945, 0.74724, 0.38699, 0.67393, 0.12492, 0.26472, 0.94524, 0.22684, 0.09526, 0.85669, 0.56475, 0.0004, 0.04186, 0.50841, 0.52402, 0.86115, 0.94406, 0.91873, 0.3211, 0.61356, 0.67889, 0.65393, 0.55489, 0.17981, 0.61075, 0.76341, 0.12171, 0.22994, 0.94669, 0.2184, 0.22169, 0.29965, 0.33527, 0.79153, 0.40178, 0.22901, 0.50045, 0.6358, 0.7704, 0.34071, 0.28555, 0.26949, 0.66668, 0.76774, 0.69384, 0.6717, 0.93442, 0.35812, 0.44881, 0.30604, 0.55718, 0.36893, 0.18933, 0.43687, 0.76357, 0.62553, 0.19235, 0.20719, 0.06318, 0.36151, 0.14301, 0.7153, 0.28015, 0.87686, 0.63499, 0.2651, 0.66513, 0.25033, 0.10662, 0.29501, 0.3607, 0.30826, 0.14833, 0.9804, 0.32801, 0.4094, 0.98725, 0.78715, 0.48597, 0.93905, 0.82899, 0.76359, 0.78113, 0.64065, 0.66122, 0.85277, 0.11032, 0.8984, 0.02796, 0.77625, 0.03666, 0.29326, 0.57262, 0.4089, 0.1977, 0.57026, 0.98934, 0.78982, 0.13275, 0.42874, 0.00657, 0.6841, 0.2515, 0.1538, 0.94534, 0.24892, 0.2206, 0.20893, 0.10886, 0.35417, 0.63828, 0.4052, 0.72122, 0.12414, 0.53252, 0.37942, 0.93416, 0.6395, 0.21564, 0.78223, 0.15478, 0.76673, 0.22236, 0.33037, 0.25835, 0.06744, 0.73564, 0.73943, 0.7827, 0.75775, 0.42006, 0.19743, 0.6544, 0.05723, 0.93248, 0.14489, 0.51209, 0.62734, 0.69531, 0.4123, 0.63216, 0.90139, 0.78955, 0.6738 };
        double randdoub()
        {
            if (RandDebuggingMode)
            {
                if (RandDebuggingIndex == RandDebuggingTotal)
                    RandDebuggingIndex = 0;
                return RandNums[RandDebuggingIndex++];
            }
            else
                return RandomGenerator.NextDouble();
        }

        public void gencentroid()
        {
            ECTAMove c;
            int pl;
            for (pl = 1; pl < ECTATreeDefinition.PLAYERS; pl++)
                for (int cindex = firstmove[pl] + 1; cindex < firstmove[pl + 1]; cindex++)
                {
                    c = moves[cindex];
                    c.behavprob = 1 / (Rational) isets[c.atiset].nmoves;
                    if (c.behavprob == 0)
                        throw new Exception("DEBUG");
                }
        }

        public void genprior(int seed)
        {
            int pl;
            ECTAInformationSet h;

            if (0 == seed)
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
                    // We must create fractions that add up to 1 (each greater than 0).
                    // So, we'll just take random numbers from 1 to 10, use those for numerators
                    // and the sum for a denominator.
                    BigInteger denominator = 0;
                    for (int i = 0; i < h.nmoves; i++)
                    {
                        double maxValue = 9;
                        BigInteger numerator = (BigInteger)(1 + (int)Math.Floor(maxValue * RandomGenerator.NextDouble()));
                        moves[h.move0 + i].behavprob = (Rational)numerator; // store value so that we remember it
                        denominator += numerator;
                    }
                    for (int i = 0; i < h.nmoves; i++)
                    {
                        Rational a = new Rational();
                        a = moves[h.move0 + i].behavprob.Numerator / (Rational)denominator;
                        moves[h.move0 + i].behavprob = a;
                    }
                }
        }

        public void outprior()
        {
            int pl;

            tabbedtextf("------Prior behavior strategies player 1, 2:\n");
            for (pl = 1; pl < PLAYERS; pl++)
            {
                behavtorealprob(pl);
                realplanfromprob(pl, realplan[pl]);
                outbehavstrat(pl, realplan[pl], true);
            }
        }

        /* SEQUENCE FORM */


        ECTAPayVector[][] sfpay;
        int[][][] sfconstr = new int[PLAYERS][][];

        int oldnseqs1 = 0;
        int[] oldconstrows = new int[PLAYERS] { 0, 0, 0 };

        void allocsf()
        {
            int pl, i, j;
            int nrows;

            /* payoff matrices, two players only here, init to pay 0        */
            oldnseqs1 = nseqs[1];
            sfpay = CreateJaggedArray<ECTAPayVector[][]>(nseqs[1], nseqs[2]);
            for (i = 0; i < nseqs[1]; i++)
                for (j = 0; j < nseqs[2]; j++)
                {
                    sfpay[i][j] = new ECTAPayVector();
                    for (pl = 1; pl < PLAYERS; pl++)
                        sfpay[i][j][pl - 1] = ratfromi(0);
                }
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
            ECTAOutcome z = null;
            allocsf();

            behavtorealprob(0);     /* get realization probabilities of leaves      */

            /* sf payoff matrices                   */
            for (int zindex = 0; zindex < outcomes.Length; zindex++)
            {
                z = outcomes[zindex];
                ECTANode u = nodes[z.whichnode];
                i = u.defseq[1] - firstmove[1]; 
                j = u.defseq[2] - firstmove[2];
                for (pl = 1; pl < PLAYERS; pl++)
                    sfpay[i][j][pl - 1] = ratadd(sfpay[i][j][pl - 1],
                        ratmult(moves[u.defseq[0]].realprob, z.pay[pl - 1]));
            }
            /* sf constraint matrices, sparse fill  */
            for (pl = 1; pl < PLAYERS; pl++)
            {
                sfconstr[pl][0][0] = 1;     /* empty sequence                       */
                for (i = 0; i < nisets[pl]; i++)
                    sfconstr[pl][i + 1][(int) isets[firstiset[pl] + i].seqin - firstmove[pl]] = -1;
                for (j = 1; j < nseqs[pl]; j++)
                    sfconstr[pl][moves[firstmove[pl] + j].atiset - firstiset[pl] + 1][j] = 1;
            }
        }       /* end of  gensf()              */

        public void sflcp()
        {
            int i;

            gensf();
            Lemke = new Lemke(nseqs[1] + nisets[2] + 1 + nseqs[2] + nisets[1] + 1);
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

        void realplanfromprob(int pl, Rational[] rplan)
        {
            int i;

            for (i = 0; i < nseqs[pl]; i++)
                rplan[i] = moves[firstmove[pl] + i].realprob;
        }

        public int propermixisets(int pl, Rational[] rplan, int offset)
        {
            int mix = 0;
            int i;
            ECTAMove c;
            ECTAInformationSet h;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    if (rplan[offset + cindex - firstmove[pl]].Numerator != 0 &&
                        !ratiseq(rplan[offset + cindex - firstmove[pl]],
                                  rplan[offset + (int) h.seqin - firstmove[pl]]))
                    {
                        mix++;
                        break;
                    }
                }
            }
            return mix;
        }

        void outrealplan(int pl, Rational[] rplan)
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

        void outbehavstrat(int pl, Rational[] rplan, bool bnewline)
        {
            string s = null;
            int i;
            ECTAMove c;
            ECTAInformationSet h;
            Rational rprob, bprob;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[cindex - firstmove[pl]];
                    if (rprob.Numerator != 0)
                    {
                        movetoa(c, pl, ref s);
                        tabbedtextf(" %s", s);
                        bprob = ratdiv(rprob, rplan[(int) h.seqin - firstmove[pl]]);
                        if (!ratiseq(bprob, ratfromi(1)))
                        {
                            rattoa(bprob, ref s);
                            tabbedtextf(":%s", s);
                        }
                    }
                }
            }
            if (bnewline)
                tabbedtextf("\n");
        }

        private IEnumerable<Rational> GetPlayerMoves(int pl, Rational[] rplan, int offset)
        {
            int i;
            ECTAMove c;
            ECTAInformationSet h;
            Rational rprob;
            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstmove[pl]];
                    yield return rprob;

                }
            }
        }

        public IEnumerable<Rational> GetPlayerMoves()
        {

            int offset = nseqs[1] + 1 + nisets[2];
            foreach (Rational r in GetPlayerMoves(1, Lemke.solz, 0))
                yield return r;
            foreach (Rational r in GetPlayerMoves(2, Lemke.solz, offset))
                yield return r;
        }

        void outbehavstrat_moves(int pl, Rational[] rplan, int offset, bool bnewline)
        {
            int i;
            ECTAMove c;
            ECTAInformationSet h;
            Rational rprob;

            for (int hindex = firstiset[pl]; hindex < firstiset[pl + 1]; hindex++)
            {
                h = isets[hindex];
                i = 0;
                for (int cindex = h.move0; i < h.nmoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstmove[pl]];
                    TabbedText.Write(rprob.ToString());
                    if (hindex != firstiset[pl + 1] - 1 || i != h.nmoves - 1)
                    {
                        tabbedtextf(",");
                    }
                }
            }
            if (bnewline)
                tabbedtextf("\n");
        }

        void sfprint()
        {
            string s = null;
            int i, j, k;
            tabbedtextf("SF payoffs and constraints:\n");

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

        public Rational[][] realplan = new Rational[PLAYERS][];

        public void allocrealplan(Rational[][] realpl)
        {
            int pl;

            for (pl = 0; pl < PLAYERS; pl++)
                realpl[pl] = new Rational[nseqs[pl]];
        }

        public void behavtorealprob(int pl)
        {
            ECTAMove c;
            int lastmoveindex = firstmove[pl + 1];
            moves[firstmove[pl]].realprob = ratfromi(1);  /* empty seq has probability 1  */
            for (int cindex = firstmove[pl] + 1; cindex < lastmoveindex; cindex++)
            {
                c = moves[cindex];
                c.realprob = ratmult(c.behavprob, moves[isets[(int)c.atiset].seqin].realprob);
            }
        }

        void payratmatcpy(ECTAPayVector[][] frommatr, int plminusone, bool bnegate,
                bool btranspfrommatr, int nfromrows, int nfromcols,
                Rational[][] targetmatr, int targrowoffset, int targcoloffset)
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
                Rational[][] targetmatr, int targrowoffset, int targcoloffset)
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

        public void covvector()
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

            //// DEBUG
            //for (i = 0; i < Lemke.lcpM.Length; i++)
            //{
            //    for (j = 0; j < Lemke.lcpM[i].Length; j++)
            //    {
            //        TabbedText.Write(Lemke.lcpM[i][j] + ", ");
            //    }
            //    TabbedText.WriteLine("");
            //}

            /* first blockrow += -Aq    */
            for (i = 0; i < dim1; i++)
                for (j = 0; j < dim2; j++)
                {
                    Lemke.vecd[i] = ratadd(Lemke.vecd[i], ratmult(Lemke.lcpM[i][offset + j],
                              moves[firstmove[2] + j].realprob));
                    // TabbedText.WriteLine($"{Lemke.vecd[i]} {Lemke.lcpM[i][offset + j]} {moves[firstmove[2] + j].realprob.Numerator} {moves[firstmove[2] + j].realprob.Denominator}"); // DEBUG
                }
            /* RSF yet to be done*/
            /* third blockrow += -B\T p */
            for (i = offset; i < offset + dim2; i++)
                for (j = 0; j < dim1; j++)
                    Lemke.vecd[i] = ratadd(Lemke.vecd[i], ratmult(Lemke.lcpM[i][j],
                              moves[firstmove[1] + j].realprob));
            /* RSF yet to be done*/
        }


        public void showeq(bool bshortequil, int docuseed)
        {
            int offset;


            offset = nseqs[1] + 1 + nisets[2];
            /*  non-sparse printing
            tabbedtextf("Equilibrium realization plan player 1:\n");
            outrealplan(1, solz);
            tabbedtextf("Equilibrium realization plan player 2:\n");
            outrealplan(2, solz + offset); */
            if (bshortequil)
                tabbedtextf("BEQ>%4d<1>", docuseed);
            else
                tabbedtextf("......Equilibrium behavior strategies player 1, 2:\n");
            outbehavstrat_moves(1, Lemke.solz, 0, !bshortequil); /* remove _moves for original code */
            if (bshortequil)
                tabbedtextf(" <2>");
            outbehavstrat_moves(2, Lemke.solz, offset, true);  /* remove _moves for original code */
        }

        /* EXAMPLE */


        public void examplegame()
        {
            int[][] pay = new int[2][] { new int[153]
                { 0, 1000, 125, 312, 500, 47, 109, 172, 500, 688, 47, 109, 172, 47, 109, 172, 875, 0, 1000, 125, 312, 500, 117, 180, 242, 500, 688, 117, 180, 242, 117, 180, 242, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 117, 180, 242, 500, 688, 117, 180, 242, 117, 180, 242, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 508, 570, 633, 500, 688, 508, 570, 633, 508, 570, 633, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 508, 570, 633, 500, 688, 508, 570, 633, 508, 570, 633, 875, 0, 1000, 125, 312, 500, 578, 641, 703, 500, 688, 578, 641, 703, 578, 641, 703, 875 }, new int[153]
    { 1000, 0, 875, 688, 500, 703, 641, 578, 500, 312, 703, 641, 578, 703, 641, 578, 125, 1000, 0, 875, 688, 500, 633, 570, 508, 500, 312, 633, 570, 508, 633, 570, 508, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 633, 570, 508, 500, 312, 633, 570, 508, 633, 570, 508, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 242, 180, 117, 500, 312, 242, 180, 117, 242, 180, 117, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 242, 180, 117, 500, 312, 242, 180, 117, 242, 180, 117, 125, 1000, 0, 875, 688, 500, 172, 109, 47, 500, 312, 172, 109, 47, 172, 109, 47, 125 }
            };
            alloctree(239, 15, 42, 153);
            firstiset[0] = 0;
            firstiset[1] = 3;
            firstiset[2] = 9;
            firstmove[0] = 0;
            firstmove[1] = 10;
            firstmove[2] = 26;

            int zindex = 0;
            ECTAOutcome z = outcomes[zindex];

            // root node is at index 1 (index 0 is skipped)
            nodes[rootindex].father = -1;
            nodes[2].father = 1;

            nodes[3].father = 2;

            nodes[4].father = 3;

            nodes[5].father = 4;

            nodes[6].father = 5;

            nodes[7].father = 5;

            nodes[8].father = 7;

            nodes[9].father = 5;

            nodes[10].father = 9;

            nodes[11].father = 9;

            nodes[12].father = 2;

            nodes[13].father = 12;

            nodes[14].father = 13;

            nodes[15].father = 14;

            nodes[16].father = 14;

            nodes[17].father = 16;

            nodes[18].father = 14;

            nodes[19].father = 18;

            nodes[20].father = 18;

            nodes[21].father = 2;

            nodes[22].father = 21;

            nodes[23].father = 22;

            nodes[24].father = 23;

            nodes[25].father = 23;

            nodes[26].father = 25;

            nodes[27].father = 23;

            nodes[28].father = 27;

            nodes[29].father = 27;

            nodes[30].father = 1;

            nodes[31].father = 30;

            nodes[32].father = 31;

            nodes[33].father = 32;

            nodes[34].father = 33;

            nodes[35].father = 33;

            nodes[36].father = 35;

            nodes[37].father = 33;

            nodes[38].father = 37;

            nodes[39].father = 37;

            nodes[40].father = 30;

            nodes[41].father = 40;

            nodes[42].father = 41;

            nodes[43].father = 42;

            nodes[44].father = 42;

            nodes[45].father = 44;

            nodes[46].father = 42;

            nodes[47].father = 46;

            nodes[48].father = 46;

            nodes[49].father = 30;

            nodes[50].father = 49;

            nodes[51].father = 50;

            nodes[52].father = 51;

            nodes[53].father = 51;

            nodes[54].father = 53;

            nodes[55].father = 51;

            nodes[56].father = 55;

            nodes[57].father = 55;

            nodes[58].father = 1;

            nodes[59].father = 58;

            nodes[60].father = 59;

            nodes[61].father = 60;

            nodes[62].father = 61;

            nodes[63].father = 61;

            nodes[64].father = 63;

            nodes[65].father = 61;

            nodes[66].father = 65;

            nodes[67].father = 65;

            nodes[68].father = 58;

            nodes[69].father = 68;

            nodes[70].father = 69;

            nodes[71].father = 70;

            nodes[72].father = 70;

            nodes[73].father = 72;

            nodes[74].father = 70;

            nodes[75].father = 74;

            nodes[76].father = 74;

            nodes[77].father = 58;

            nodes[78].father = 77;

            nodes[79].father = 78;

            nodes[80].father = 79;

            nodes[81].father = 79;

            nodes[82].father = 81;

            nodes[83].father = 79;

            nodes[84].father = 83;

            nodes[85].father = 83;

            nodes[86].father = 3;

            nodes[86].terminal = true;

            nodes[86].outcome = zindex;
            z.whichnode = 86;
            z.pay[0] = ratfromi(pay[0][0]);
            z.pay[1] = ratfromi(pay[1][0]);
            z = outcomes[++zindex];
            nodes[87].father = 4;

            nodes[87].terminal = true;
            nodes[87].outcome = zindex;
            z.whichnode = 87;
            z.pay[0] = ratfromi(pay[0][1]);
            z.pay[1] = ratfromi(pay[1][1]);
            z = outcomes[++zindex];
            nodes[88].father = 6;

            nodes[88].terminal = true;
            nodes[88].outcome = zindex;
            z.whichnode = 88;
            z.pay[0] = ratfromi(pay[0][2]);
            z.pay[1] = ratfromi(pay[1][2]);
            z = outcomes[++zindex];
            nodes[89].father = 6;

            nodes[89].terminal = true;
            nodes[89].outcome = zindex;
            z.whichnode = 89;
            z.pay[0] = ratfromi(pay[0][3]);
            z.pay[1] = ratfromi(pay[1][3]);
            z = outcomes[++zindex];
            nodes[90].father = 6;

            nodes[90].terminal = true;
            nodes[90].outcome = zindex;
            z.whichnode = 90;
            z.pay[0] = ratfromi(pay[0][4]);
            z.pay[1] = ratfromi(pay[1][4]);
            z = outcomes[++zindex];
            nodes[91].father = 8;

            nodes[91].terminal = true;
            nodes[91].outcome = zindex;
            z.whichnode = 91;
            z.pay[0] = ratfromi(pay[0][5]);
            z.pay[1] = ratfromi(pay[1][5]);
            z = outcomes[++zindex];
            nodes[92].father = 8;

            nodes[92].terminal = true;
            nodes[92].outcome = zindex;
            z.whichnode = 92;
            z.pay[0] = ratfromi(pay[0][6]);
            z.pay[1] = ratfromi(pay[1][6]);
            z = outcomes[++zindex];
            nodes[93].father = 8;

            nodes[93].terminal = true;
            nodes[93].outcome = zindex;
            z.whichnode = 93;
            z.pay[0] = ratfromi(pay[0][7]);
            z.pay[1] = ratfromi(pay[1][7]);
            z = outcomes[++zindex];
            nodes[94].father = 7;

            nodes[94].terminal = true;
            nodes[94].outcome = zindex;
            z.whichnode = 94;
            z.pay[0] = ratfromi(pay[0][8]);
            z.pay[1] = ratfromi(pay[1][8]);
            z = outcomes[++zindex];
            nodes[95].father = 7;

            nodes[95].terminal = true;
            nodes[95].outcome = zindex;
            z.whichnode = 95;
            z.pay[0] = ratfromi(pay[0][9]);
            z.pay[1] = ratfromi(pay[1][9]);
            z = outcomes[++zindex];
            nodes[96].father = 10;

            nodes[96].terminal = true;
            nodes[96].outcome = zindex;
            z.whichnode = 96;
            z.pay[0] = ratfromi(pay[0][10]);
            z.pay[1] = ratfromi(pay[1][10]);
            z = outcomes[++zindex];
            nodes[97].father = 10;

            nodes[97].terminal = true;
            nodes[97].outcome = zindex;
            z.whichnode = 97;
            z.pay[0] = ratfromi(pay[0][11]);
            z.pay[1] = ratfromi(pay[1][11]);
            z = outcomes[++zindex];
            nodes[98].father = 10;

            nodes[98].terminal = true;
            nodes[98].outcome = zindex;
            z.whichnode = 98;
            z.pay[0] = ratfromi(pay[0][12]);
            z.pay[1] = ratfromi(pay[1][12]);
            z = outcomes[++zindex];
            nodes[99].father = 11;

            nodes[99].terminal = true;
            nodes[99].outcome = zindex;
            z.whichnode = 99;
            z.pay[0] = ratfromi(pay[0][13]);
            z.pay[1] = ratfromi(pay[1][13]);
            z = outcomes[++zindex];
            nodes[100].father = 11;

            nodes[100].terminal = true;
            nodes[100].outcome = zindex;
            z.whichnode = 100;
            z.pay[0] = ratfromi(pay[0][14]);
            z.pay[1] = ratfromi(pay[1][14]);
            z = outcomes[++zindex];
            nodes[101].father = 11;

            nodes[101].terminal = true;
            nodes[101].outcome = zindex;
            z.whichnode = 101;
            z.pay[0] = ratfromi(pay[0][15]);
            z.pay[1] = ratfromi(pay[1][15]);
            z = outcomes[++zindex];
            nodes[102].father = 9;

            nodes[102].terminal = true;
            nodes[102].outcome = zindex;
            z.whichnode = 102;
            z.pay[0] = ratfromi(pay[0][16]);
            z.pay[1] = ratfromi(pay[1][16]);
            z = outcomes[++zindex];
            nodes[103].father = 12;

            nodes[103].terminal = true;
            nodes[103].outcome = zindex;
            z.whichnode = 103;
            z.pay[0] = ratfromi(pay[0][17]);
            z.pay[1] = ratfromi(pay[1][17]);
            z = outcomes[++zindex];
            nodes[104].father = 13;

            nodes[104].terminal = true;
            nodes[104].outcome = zindex;
            z.whichnode = 104;
            z.pay[0] = ratfromi(pay[0][18]);
            z.pay[1] = ratfromi(pay[1][18]);
            z = outcomes[++zindex];
            nodes[105].father = 15;

            nodes[105].terminal = true;
            nodes[105].outcome = zindex;
            z.whichnode = 105;
            z.pay[0] = ratfromi(pay[0][19]);
            z.pay[1] = ratfromi(pay[1][19]);
            z = outcomes[++zindex];
            nodes[106].father = 15;

            nodes[106].terminal = true;
            nodes[106].outcome = zindex;
            z.whichnode = 106;
            z.pay[0] = ratfromi(pay[0][20]);
            z.pay[1] = ratfromi(pay[1][20]);
            z = outcomes[++zindex];
            nodes[107].father = 15;

            nodes[107].terminal = true;
            nodes[107].outcome = zindex;
            z.whichnode = 107;
            z.pay[0] = ratfromi(pay[0][21]);
            z.pay[1] = ratfromi(pay[1][21]);
            z = outcomes[++zindex];
            nodes[108].father = 17;

            nodes[108].terminal = true;
            nodes[108].outcome = zindex;
            z.whichnode = 108;
            z.pay[0] = ratfromi(pay[0][22]);
            z.pay[1] = ratfromi(pay[1][22]);
            z = outcomes[++zindex];
            nodes[109].father = 17;

            nodes[109].terminal = true;
            nodes[109].outcome = zindex;
            z.whichnode = 109;
            z.pay[0] = ratfromi(pay[0][23]);
            z.pay[1] = ratfromi(pay[1][23]);
            z = outcomes[++zindex];
            nodes[110].father = 17;

            nodes[110].terminal = true;
            nodes[110].outcome = zindex;
            z.whichnode = 110;
            z.pay[0] = ratfromi(pay[0][24]);
            z.pay[1] = ratfromi(pay[1][24]);
            z = outcomes[++zindex];
            nodes[111].father = 16;

            nodes[111].terminal = true;
            nodes[111].outcome = zindex;
            z.whichnode = 111;
            z.pay[0] = ratfromi(pay[0][25]);
            z.pay[1] = ratfromi(pay[1][25]);
            z = outcomes[++zindex];
            nodes[112].father = 16;

            nodes[112].terminal = true;
            nodes[112].outcome = zindex;
            z.whichnode = 112;
            z.pay[0] = ratfromi(pay[0][26]);
            z.pay[1] = ratfromi(pay[1][26]);
            z = outcomes[++zindex];
            nodes[113].father = 19;

            nodes[113].terminal = true;
            nodes[113].outcome = zindex;
            z.whichnode = 113;
            z.pay[0] = ratfromi(pay[0][27]);
            z.pay[1] = ratfromi(pay[1][27]);
            z = outcomes[++zindex];
            nodes[114].father = 19;

            nodes[114].terminal = true;
            nodes[114].outcome = zindex;
            z.whichnode = 114;
            z.pay[0] = ratfromi(pay[0][28]);
            z.pay[1] = ratfromi(pay[1][28]);
            z = outcomes[++zindex];
            nodes[115].father = 19;

            nodes[115].terminal = true;
            nodes[115].outcome = zindex;
            z.whichnode = 115;
            z.pay[0] = ratfromi(pay[0][29]);
            z.pay[1] = ratfromi(pay[1][29]);
            z = outcomes[++zindex];
            nodes[116].father = 20;

            nodes[116].terminal = true;
            nodes[116].outcome = zindex;
            z.whichnode = 116;
            z.pay[0] = ratfromi(pay[0][30]);
            z.pay[1] = ratfromi(pay[1][30]);
            z = outcomes[++zindex];
            nodes[117].father = 20;

            nodes[117].terminal = true;
            nodes[117].outcome = zindex;
            z.whichnode = 117;
            z.pay[0] = ratfromi(pay[0][31]);
            z.pay[1] = ratfromi(pay[1][31]);
            z = outcomes[++zindex];
            nodes[118].father = 20;

            nodes[118].terminal = true;
            nodes[118].outcome = zindex;
            z.whichnode = 118;
            z.pay[0] = ratfromi(pay[0][32]);
            z.pay[1] = ratfromi(pay[1][32]);
            z = outcomes[++zindex];
            nodes[119].father = 18;

            nodes[119].terminal = true;
            nodes[119].outcome = zindex;
            z.whichnode = 119;
            z.pay[0] = ratfromi(pay[0][33]);
            z.pay[1] = ratfromi(pay[1][33]);
            z = outcomes[++zindex];
            nodes[120].father = 21;

            nodes[120].terminal = true;
            nodes[120].outcome = zindex;
            z.whichnode = 120;
            z.pay[0] = ratfromi(pay[0][34]);
            z.pay[1] = ratfromi(pay[1][34]);
            z = outcomes[++zindex];
            nodes[121].father = 22;

            nodes[121].terminal = true;
            nodes[121].outcome = zindex;
            z.whichnode = 121;
            z.pay[0] = ratfromi(pay[0][35]);
            z.pay[1] = ratfromi(pay[1][35]);
            z = outcomes[++zindex];
            nodes[122].father = 24;

            nodes[122].terminal = true;
            nodes[122].outcome = zindex;
            z.whichnode = 122;
            z.pay[0] = ratfromi(pay[0][36]);
            z.pay[1] = ratfromi(pay[1][36]);
            z = outcomes[++zindex];
            nodes[123].father = 24;

            nodes[123].terminal = true;
            nodes[123].outcome = zindex;
            z.whichnode = 123;
            z.pay[0] = ratfromi(pay[0][37]);
            z.pay[1] = ratfromi(pay[1][37]);
            z = outcomes[++zindex];
            nodes[124].father = 24;

            nodes[124].terminal = true;
            nodes[124].outcome = zindex;
            z.whichnode = 124;
            z.pay[0] = ratfromi(pay[0][38]);
            z.pay[1] = ratfromi(pay[1][38]);
            z = outcomes[++zindex];
            nodes[125].father = 26;

            nodes[125].terminal = true;
            nodes[125].outcome = zindex;
            z.whichnode = 125;
            z.pay[0] = ratfromi(pay[0][39]);
            z.pay[1] = ratfromi(pay[1][39]);
            z = outcomes[++zindex];
            nodes[126].father = 26;

            nodes[126].terminal = true;
            nodes[126].outcome = zindex;
            z.whichnode = 126;
            z.pay[0] = ratfromi(pay[0][40]);
            z.pay[1] = ratfromi(pay[1][40]);
            z = outcomes[++zindex];
            nodes[127].father = 26;

            nodes[127].terminal = true;
            nodes[127].outcome = zindex;
            z.whichnode = 127;
            z.pay[0] = ratfromi(pay[0][41]);
            z.pay[1] = ratfromi(pay[1][41]);
            z = outcomes[++zindex];
            nodes[128].father = 25;

            nodes[128].terminal = true;
            nodes[128].outcome = zindex;
            z.whichnode = 128;
            z.pay[0] = ratfromi(pay[0][42]);
            z.pay[1] = ratfromi(pay[1][42]);
            z = outcomes[++zindex];
            nodes[129].father = 25;

            nodes[129].terminal = true;
            nodes[129].outcome = zindex;
            z.whichnode = 129;
            z.pay[0] = ratfromi(pay[0][43]);
            z.pay[1] = ratfromi(pay[1][43]);
            z = outcomes[++zindex];
            nodes[130].father = 28;

            nodes[130].terminal = true;
            nodes[130].outcome = zindex;
            z.whichnode = 130;
            z.pay[0] = ratfromi(pay[0][44]);
            z.pay[1] = ratfromi(pay[1][44]);
            z = outcomes[++zindex];
            nodes[131].father = 28;

            nodes[131].terminal = true;
            nodes[131].outcome = zindex;
            z.whichnode = 131;
            z.pay[0] = ratfromi(pay[0][45]);
            z.pay[1] = ratfromi(pay[1][45]);
            z = outcomes[++zindex];
            nodes[132].father = 28;

            nodes[132].terminal = true;
            nodes[132].outcome = zindex;
            z.whichnode = 132;
            z.pay[0] = ratfromi(pay[0][46]);
            z.pay[1] = ratfromi(pay[1][46]);
            z = outcomes[++zindex];
            nodes[133].father = 29;

            nodes[133].terminal = true;
            nodes[133].outcome = zindex;
            z.whichnode = 133;
            z.pay[0] = ratfromi(pay[0][47]);
            z.pay[1] = ratfromi(pay[1][47]);
            z = outcomes[++zindex];
            nodes[134].father = 29;

            nodes[134].terminal = true;
            nodes[134].outcome = zindex;
            z.whichnode = 134;
            z.pay[0] = ratfromi(pay[0][48]);
            z.pay[1] = ratfromi(pay[1][48]);
            z = outcomes[++zindex];
            nodes[135].father = 29;

            nodes[135].terminal = true;
            nodes[135].outcome = zindex;
            z.whichnode = 135;
            z.pay[0] = ratfromi(pay[0][49]);
            z.pay[1] = ratfromi(pay[1][49]);
            z = outcomes[++zindex];
            nodes[136].father = 27;

            nodes[136].terminal = true;
            nodes[136].outcome = zindex;
            z.whichnode = 136;
            z.pay[0] = ratfromi(pay[0][50]);
            z.pay[1] = ratfromi(pay[1][50]);
            z = outcomes[++zindex];
            nodes[137].father = 31;

            nodes[137].terminal = true;
            nodes[137].outcome = zindex;
            z.whichnode = 137;
            z.pay[0] = ratfromi(pay[0][51]);
            z.pay[1] = ratfromi(pay[1][51]);
            z = outcomes[++zindex];
            nodes[138].father = 32;

            nodes[138].terminal = true;
            nodes[138].outcome = zindex;
            z.whichnode = 138;
            z.pay[0] = ratfromi(pay[0][52]);
            z.pay[1] = ratfromi(pay[1][52]);
            z = outcomes[++zindex];
            nodes[139].father = 34;

            nodes[139].terminal = true;
            nodes[139].outcome = zindex;
            z.whichnode = 139;
            z.pay[0] = ratfromi(pay[0][53]);
            z.pay[1] = ratfromi(pay[1][53]);
            z = outcomes[++zindex];
            nodes[140].father = 34;

            nodes[140].terminal = true;
            nodes[140].outcome = zindex;
            z.whichnode = 140;
            z.pay[0] = ratfromi(pay[0][54]);
            z.pay[1] = ratfromi(pay[1][54]);
            z = outcomes[++zindex];
            nodes[141].father = 34;

            nodes[141].terminal = true;
            nodes[141].outcome = zindex;
            z.whichnode = 141;
            z.pay[0] = ratfromi(pay[0][55]);
            z.pay[1] = ratfromi(pay[1][55]);
            z = outcomes[++zindex];
            nodes[142].father = 36;

            nodes[142].terminal = true;
            nodes[142].outcome = zindex;
            z.whichnode = 142;
            z.pay[0] = ratfromi(pay[0][56]);
            z.pay[1] = ratfromi(pay[1][56]);
            z = outcomes[++zindex];
            nodes[143].father = 36;

            nodes[143].terminal = true;
            nodes[143].outcome = zindex;
            z.whichnode = 143;
            z.pay[0] = ratfromi(pay[0][57]);
            z.pay[1] = ratfromi(pay[1][57]);
            z = outcomes[++zindex];
            nodes[144].father = 36;

            nodes[144].terminal = true;
            nodes[144].outcome = zindex;
            z.whichnode = 144;
            z.pay[0] = ratfromi(pay[0][58]);
            z.pay[1] = ratfromi(pay[1][58]);
            z = outcomes[++zindex];
            nodes[145].father = 35;

            nodes[145].terminal = true;
            nodes[145].outcome = zindex;
            z.whichnode = 145;
            z.pay[0] = ratfromi(pay[0][59]);
            z.pay[1] = ratfromi(pay[1][59]);
            z = outcomes[++zindex];
            nodes[146].father = 35;

            nodes[146].terminal = true;
            nodes[146].outcome = zindex;
            z.whichnode = 146;
            z.pay[0] = ratfromi(pay[0][60]);
            z.pay[1] = ratfromi(pay[1][60]);
            z = outcomes[++zindex];
            nodes[147].father = 38;

            nodes[147].terminal = true;
            nodes[147].outcome = zindex;
            z.whichnode = 147;
            z.pay[0] = ratfromi(pay[0][61]);
            z.pay[1] = ratfromi(pay[1][61]);
            z = outcomes[++zindex];
            nodes[148].father = 38;

            nodes[148].terminal = true;
            nodes[148].outcome = zindex;
            z.whichnode = 148;
            z.pay[0] = ratfromi(pay[0][62]);
            z.pay[1] = ratfromi(pay[1][62]);
            z = outcomes[++zindex];
            nodes[149].father = 38;

            nodes[149].terminal = true;
            nodes[149].outcome = zindex;
            z.whichnode = 149;
            z.pay[0] = ratfromi(pay[0][63]);
            z.pay[1] = ratfromi(pay[1][63]);
            z = outcomes[++zindex];
            nodes[150].father = 39;

            nodes[150].terminal = true;
            nodes[150].outcome = zindex;
            z.whichnode = 150;
            z.pay[0] = ratfromi(pay[0][64]);
            z.pay[1] = ratfromi(pay[1][64]);
            z = outcomes[++zindex];
            nodes[151].father = 39;

            nodes[151].terminal = true;
            nodes[151].outcome = zindex;
            z.whichnode = 151;
            z.pay[0] = ratfromi(pay[0][65]);
            z.pay[1] = ratfromi(pay[1][65]);
            z = outcomes[++zindex];
            nodes[152].father = 39;

            nodes[152].terminal = true;
            nodes[152].outcome = zindex;
            z.whichnode = 152;
            z.pay[0] = ratfromi(pay[0][66]);
            z.pay[1] = ratfromi(pay[1][66]);
            z = outcomes[++zindex];
            nodes[153].father = 37;

            nodes[153].terminal = true;
            nodes[153].outcome = zindex;
            z.whichnode = 153;
            z.pay[0] = ratfromi(pay[0][67]);
            z.pay[1] = ratfromi(pay[1][67]);
            z = outcomes[++zindex];
            nodes[154].father = 40;

            nodes[154].terminal = true;
            nodes[154].outcome = zindex;
            z.whichnode = 154;
            z.pay[0] = ratfromi(pay[0][68]);
            z.pay[1] = ratfromi(pay[1][68]);
            z = outcomes[++zindex];
            nodes[155].father = 41;

            nodes[155].terminal = true;
            nodes[155].outcome = zindex;
            z.whichnode = 155;
            z.pay[0] = ratfromi(pay[0][69]);
            z.pay[1] = ratfromi(pay[1][69]);
            z = outcomes[++zindex];
            nodes[156].father = 43;

            nodes[156].terminal = true;
            nodes[156].outcome = zindex;
            z.whichnode = 156;
            z.pay[0] = ratfromi(pay[0][70]);
            z.pay[1] = ratfromi(pay[1][70]);
            z = outcomes[++zindex];
            nodes[157].father = 43;

            nodes[157].terminal = true;
            nodes[157].outcome = zindex;
            z.whichnode = 157;
            z.pay[0] = ratfromi(pay[0][71]);
            z.pay[1] = ratfromi(pay[1][71]);
            z = outcomes[++zindex];
            nodes[158].father = 43;

            nodes[158].terminal = true;
            nodes[158].outcome = zindex;
            z.whichnode = 158;
            z.pay[0] = ratfromi(pay[0][72]);
            z.pay[1] = ratfromi(pay[1][72]);
            z = outcomes[++zindex];
            nodes[159].father = 45;

            nodes[159].terminal = true;
            nodes[159].outcome = zindex;
            z.whichnode = 159;
            z.pay[0] = ratfromi(pay[0][73]);
            z.pay[1] = ratfromi(pay[1][73]);
            z = outcomes[++zindex];
            nodes[160].father = 45;

            nodes[160].terminal = true;
            nodes[160].outcome = zindex;
            z.whichnode = 160;
            z.pay[0] = ratfromi(pay[0][74]);
            z.pay[1] = ratfromi(pay[1][74]);
            z = outcomes[++zindex];
            nodes[161].father = 45;

            nodes[161].terminal = true;
            nodes[161].outcome = zindex;
            z.whichnode = 161;
            z.pay[0] = ratfromi(pay[0][75]);
            z.pay[1] = ratfromi(pay[1][75]);
            z = outcomes[++zindex];
            nodes[162].father = 44;

            nodes[162].terminal = true;
            nodes[162].outcome = zindex;
            z.whichnode = 162;
            z.pay[0] = ratfromi(pay[0][76]);
            z.pay[1] = ratfromi(pay[1][76]);
            z = outcomes[++zindex];
            nodes[163].father = 44;

            nodes[163].terminal = true;
            nodes[163].outcome = zindex;
            z.whichnode = 163;
            z.pay[0] = ratfromi(pay[0][77]);
            z.pay[1] = ratfromi(pay[1][77]);
            z = outcomes[++zindex];
            nodes[164].father = 47;

            nodes[164].terminal = true;
            nodes[164].outcome = zindex;
            z.whichnode = 164;
            z.pay[0] = ratfromi(pay[0][78]);
            z.pay[1] = ratfromi(pay[1][78]);
            z = outcomes[++zindex];
            nodes[165].father = 47;

            nodes[165].terminal = true;
            nodes[165].outcome = zindex;
            z.whichnode = 165;
            z.pay[0] = ratfromi(pay[0][79]);
            z.pay[1] = ratfromi(pay[1][79]);
            z = outcomes[++zindex];
            nodes[166].father = 47;

            nodes[166].terminal = true;
            nodes[166].outcome = zindex;
            z.whichnode = 166;
            z.pay[0] = ratfromi(pay[0][80]);
            z.pay[1] = ratfromi(pay[1][80]);
            z = outcomes[++zindex];
            nodes[167].father = 48;

            nodes[167].terminal = true;
            nodes[167].outcome = zindex;
            z.whichnode = 167;
            z.pay[0] = ratfromi(pay[0][81]);
            z.pay[1] = ratfromi(pay[1][81]);
            z = outcomes[++zindex];
            nodes[168].father = 48;

            nodes[168].terminal = true;
            nodes[168].outcome = zindex;
            z.whichnode = 168;
            z.pay[0] = ratfromi(pay[0][82]);
            z.pay[1] = ratfromi(pay[1][82]);
            z = outcomes[++zindex];
            nodes[169].father = 48;

            nodes[169].terminal = true;
            nodes[169].outcome = zindex;
            z.whichnode = 169;
            z.pay[0] = ratfromi(pay[0][83]);
            z.pay[1] = ratfromi(pay[1][83]);
            z = outcomes[++zindex];
            nodes[170].father = 46;

            nodes[170].terminal = true;
            nodes[170].outcome = zindex;
            z.whichnode = 170;
            z.pay[0] = ratfromi(pay[0][84]);
            z.pay[1] = ratfromi(pay[1][84]);
            z = outcomes[++zindex];
            nodes[171].father = 49;

            nodes[171].terminal = true;
            nodes[171].outcome = zindex;
            z.whichnode = 171;
            z.pay[0] = ratfromi(pay[0][85]);
            z.pay[1] = ratfromi(pay[1][85]);
            z = outcomes[++zindex];
            nodes[172].father = 50;

            nodes[172].terminal = true;
            nodes[172].outcome = zindex;
            z.whichnode = 172;
            z.pay[0] = ratfromi(pay[0][86]);
            z.pay[1] = ratfromi(pay[1][86]);
            z = outcomes[++zindex];
            nodes[173].father = 52;

            nodes[173].terminal = true;
            nodes[173].outcome = zindex;
            z.whichnode = 173;
            z.pay[0] = ratfromi(pay[0][87]);
            z.pay[1] = ratfromi(pay[1][87]);
            z = outcomes[++zindex];
            nodes[174].father = 52;

            nodes[174].terminal = true;
            nodes[174].outcome = zindex;
            z.whichnode = 174;
            z.pay[0] = ratfromi(pay[0][88]);
            z.pay[1] = ratfromi(pay[1][88]);
            z = outcomes[++zindex];
            nodes[175].father = 52;

            nodes[175].terminal = true;
            nodes[175].outcome = zindex;
            z.whichnode = 175;
            z.pay[0] = ratfromi(pay[0][89]);
            z.pay[1] = ratfromi(pay[1][89]);
            z = outcomes[++zindex];
            nodes[176].father = 54;

            nodes[176].terminal = true;
            nodes[176].outcome = zindex;
            z.whichnode = 176;
            z.pay[0] = ratfromi(pay[0][90]);
            z.pay[1] = ratfromi(pay[1][90]);
            z = outcomes[++zindex];
            nodes[177].father = 54;

            nodes[177].terminal = true;
            nodes[177].outcome = zindex;
            z.whichnode = 177;
            z.pay[0] = ratfromi(pay[0][91]);
            z.pay[1] = ratfromi(pay[1][91]);
            z = outcomes[++zindex];
            nodes[178].father = 54;

            nodes[178].terminal = true;
            nodes[178].outcome = zindex;
            z.whichnode = 178;
            z.pay[0] = ratfromi(pay[0][92]);
            z.pay[1] = ratfromi(pay[1][92]);
            z = outcomes[++zindex];
            nodes[179].father = 53;

            nodes[179].terminal = true;
            nodes[179].outcome = zindex;
            z.whichnode = 179;
            z.pay[0] = ratfromi(pay[0][93]);
            z.pay[1] = ratfromi(pay[1][93]);
            z = outcomes[++zindex];
            nodes[180].father = 53;

            nodes[180].terminal = true;
            nodes[180].outcome = zindex;
            z.whichnode = 180;
            z.pay[0] = ratfromi(pay[0][94]);
            z.pay[1] = ratfromi(pay[1][94]);
            z = outcomes[++zindex];
            nodes[181].father = 56;

            nodes[181].terminal = true;
            nodes[181].outcome = zindex;
            z.whichnode = 181;
            z.pay[0] = ratfromi(pay[0][95]);
            z.pay[1] = ratfromi(pay[1][95]);
            z = outcomes[++zindex];
            nodes[182].father = 56;

            nodes[182].terminal = true;
            nodes[182].outcome = zindex;
            z.whichnode = 182;
            z.pay[0] = ratfromi(pay[0][96]);
            z.pay[1] = ratfromi(pay[1][96]);
            z = outcomes[++zindex];
            nodes[183].father = 56;

            nodes[183].terminal = true;
            nodes[183].outcome = zindex;
            z.whichnode = 183;
            z.pay[0] = ratfromi(pay[0][97]);
            z.pay[1] = ratfromi(pay[1][97]);
            z = outcomes[++zindex];
            nodes[184].father = 57;

            nodes[184].terminal = true;
            nodes[184].outcome = zindex;
            z.whichnode = 184;
            z.pay[0] = ratfromi(pay[0][98]);
            z.pay[1] = ratfromi(pay[1][98]);
            z = outcomes[++zindex];
            nodes[185].father = 57;

            nodes[185].terminal = true;
            nodes[185].outcome = zindex;
            z.whichnode = 185;
            z.pay[0] = ratfromi(pay[0][99]);
            z.pay[1] = ratfromi(pay[1][99]);
            z = outcomes[++zindex];
            nodes[186].father = 57;

            nodes[186].terminal = true;
            nodes[186].outcome = zindex;
            z.whichnode = 186;
            z.pay[0] = ratfromi(pay[0][100]);
            z.pay[1] = ratfromi(pay[1][100]);
            z = outcomes[++zindex];
            nodes[187].father = 55;

            nodes[187].terminal = true;
            nodes[187].outcome = zindex;
            z.whichnode = 187;
            z.pay[0] = ratfromi(pay[0][101]);
            z.pay[1] = ratfromi(pay[1][101]);
            z = outcomes[++zindex];
            nodes[188].father = 59;

            nodes[188].terminal = true;
            nodes[188].outcome = zindex;
            z.whichnode = 188;
            z.pay[0] = ratfromi(pay[0][102]);
            z.pay[1] = ratfromi(pay[1][102]);
            z = outcomes[++zindex];
            nodes[189].father = 60;

            nodes[189].terminal = true;
            nodes[189].outcome = zindex;
            z.whichnode = 189;
            z.pay[0] = ratfromi(pay[0][103]);
            z.pay[1] = ratfromi(pay[1][103]);
            z = outcomes[++zindex];
            nodes[190].father = 62;

            nodes[190].terminal = true;
            nodes[190].outcome = zindex;
            z.whichnode = 190;
            z.pay[0] = ratfromi(pay[0][104]);
            z.pay[1] = ratfromi(pay[1][104]);
            z = outcomes[++zindex];
            nodes[191].father = 62;

            nodes[191].terminal = true;
            nodes[191].outcome = zindex;
            z.whichnode = 191;
            z.pay[0] = ratfromi(pay[0][105]);
            z.pay[1] = ratfromi(pay[1][105]);
            z = outcomes[++zindex];
            nodes[192].father = 62;

            nodes[192].terminal = true;
            nodes[192].outcome = zindex;
            z.whichnode = 192;
            z.pay[0] = ratfromi(pay[0][106]);
            z.pay[1] = ratfromi(pay[1][106]);
            z = outcomes[++zindex];
            nodes[193].father = 64;

            nodes[193].terminal = true;
            nodes[193].outcome = zindex;
            z.whichnode = 193;
            z.pay[0] = ratfromi(pay[0][107]);
            z.pay[1] = ratfromi(pay[1][107]);
            z = outcomes[++zindex];
            nodes[194].father = 64;

            nodes[194].terminal = true;
            nodes[194].outcome = zindex;
            z.whichnode = 194;
            z.pay[0] = ratfromi(pay[0][108]);
            z.pay[1] = ratfromi(pay[1][108]);
            z = outcomes[++zindex];
            nodes[195].father = 64;

            nodes[195].terminal = true;
            nodes[195].outcome = zindex;
            z.whichnode = 195;
            z.pay[0] = ratfromi(pay[0][109]);
            z.pay[1] = ratfromi(pay[1][109]);
            z = outcomes[++zindex];
            nodes[196].father = 63;

            nodes[196].terminal = true;
            nodes[196].outcome = zindex;
            z.whichnode = 196;
            z.pay[0] = ratfromi(pay[0][110]);
            z.pay[1] = ratfromi(pay[1][110]);
            z = outcomes[++zindex];
            nodes[197].father = 63;

            nodes[197].terminal = true;
            nodes[197].outcome = zindex;
            z.whichnode = 197;
            z.pay[0] = ratfromi(pay[0][111]);
            z.pay[1] = ratfromi(pay[1][111]);
            z = outcomes[++zindex];
            nodes[198].father = 66;

            nodes[198].terminal = true;
            nodes[198].outcome = zindex;
            z.whichnode = 198;
            z.pay[0] = ratfromi(pay[0][112]);
            z.pay[1] = ratfromi(pay[1][112]);
            z = outcomes[++zindex];
            nodes[199].father = 66;

            nodes[199].terminal = true;
            nodes[199].outcome = zindex;
            z.whichnode = 199;
            z.pay[0] = ratfromi(pay[0][113]);
            z.pay[1] = ratfromi(pay[1][113]);
            z = outcomes[++zindex];
            nodes[200].father = 66;

            nodes[200].terminal = true;
            nodes[200].outcome = zindex;
            z.whichnode = 200;
            z.pay[0] = ratfromi(pay[0][114]);
            z.pay[1] = ratfromi(pay[1][114]);
            z = outcomes[++zindex];
            nodes[201].father = 67;

            nodes[201].terminal = true;
            nodes[201].outcome = zindex;
            z.whichnode = 201;
            z.pay[0] = ratfromi(pay[0][115]);
            z.pay[1] = ratfromi(pay[1][115]);
            z = outcomes[++zindex];
            nodes[202].father = 67;

            nodes[202].terminal = true;
            nodes[202].outcome = zindex;
            z.whichnode = 202;
            z.pay[0] = ratfromi(pay[0][116]);
            z.pay[1] = ratfromi(pay[1][116]);
            z = outcomes[++zindex];
            nodes[203].father = 67;

            nodes[203].terminal = true;
            nodes[203].outcome = zindex;
            z.whichnode = 203;
            z.pay[0] = ratfromi(pay[0][117]);
            z.pay[1] = ratfromi(pay[1][117]);
            z = outcomes[++zindex];
            nodes[204].father = 65;

            nodes[204].terminal = true;
            nodes[204].outcome = zindex;
            z.whichnode = 204;
            z.pay[0] = ratfromi(pay[0][118]);
            z.pay[1] = ratfromi(pay[1][118]);
            z = outcomes[++zindex];
            nodes[205].father = 68;

            nodes[205].terminal = true;
            nodes[205].outcome = zindex;
            z.whichnode = 205;
            z.pay[0] = ratfromi(pay[0][119]);
            z.pay[1] = ratfromi(pay[1][119]);
            z = outcomes[++zindex];
            nodes[206].father = 69;

            nodes[206].terminal = true;
            nodes[206].outcome = zindex;
            z.whichnode = 206;
            z.pay[0] = ratfromi(pay[0][120]);
            z.pay[1] = ratfromi(pay[1][120]);
            z = outcomes[++zindex];
            nodes[207].father = 71;

            nodes[207].terminal = true;
            nodes[207].outcome = zindex;
            z.whichnode = 207;
            z.pay[0] = ratfromi(pay[0][121]);
            z.pay[1] = ratfromi(pay[1][121]);
            z = outcomes[++zindex];
            nodes[208].father = 71;

            nodes[208].terminal = true;
            nodes[208].outcome = zindex;
            z.whichnode = 208;
            z.pay[0] = ratfromi(pay[0][122]);
            z.pay[1] = ratfromi(pay[1][122]);
            z = outcomes[++zindex];
            nodes[209].father = 71;

            nodes[209].terminal = true;
            nodes[209].outcome = zindex;
            z.whichnode = 209;
            z.pay[0] = ratfromi(pay[0][123]);
            z.pay[1] = ratfromi(pay[1][123]);
            z = outcomes[++zindex];
            nodes[210].father = 73;

            nodes[210].terminal = true;
            nodes[210].outcome = zindex;
            z.whichnode = 210;
            z.pay[0] = ratfromi(pay[0][124]);
            z.pay[1] = ratfromi(pay[1][124]);
            z = outcomes[++zindex];
            nodes[211].father = 73;

            nodes[211].terminal = true;
            nodes[211].outcome = zindex;
            z.whichnode = 211;
            z.pay[0] = ratfromi(pay[0][125]);
            z.pay[1] = ratfromi(pay[1][125]);
            z = outcomes[++zindex];
            nodes[212].father = 73;

            nodes[212].terminal = true;
            nodes[212].outcome = zindex;
            z.whichnode = 212;
            z.pay[0] = ratfromi(pay[0][126]);
            z.pay[1] = ratfromi(pay[1][126]);
            z = outcomes[++zindex];
            nodes[213].father = 72;

            nodes[213].terminal = true;
            nodes[213].outcome = zindex;
            z.whichnode = 213;
            z.pay[0] = ratfromi(pay[0][127]);
            z.pay[1] = ratfromi(pay[1][127]);
            z = outcomes[++zindex];
            nodes[214].father = 72;

            nodes[214].terminal = true;
            nodes[214].outcome = zindex;
            z.whichnode = 214;
            z.pay[0] = ratfromi(pay[0][128]);
            z.pay[1] = ratfromi(pay[1][128]);
            z = outcomes[++zindex];
            nodes[215].father = 75;

            nodes[215].terminal = true;
            nodes[215].outcome = zindex;
            z.whichnode = 215;
            z.pay[0] = ratfromi(pay[0][129]);
            z.pay[1] = ratfromi(pay[1][129]);
            z = outcomes[++zindex];
            nodes[216].father = 75;

            nodes[216].terminal = true;
            nodes[216].outcome = zindex;
            z.whichnode = 216;
            z.pay[0] = ratfromi(pay[0][130]);
            z.pay[1] = ratfromi(pay[1][130]);
            z = outcomes[++zindex];
            nodes[217].father = 75;

            nodes[217].terminal = true;
            nodes[217].outcome = zindex;
            z.whichnode = 217;
            z.pay[0] = ratfromi(pay[0][131]);
            z.pay[1] = ratfromi(pay[1][131]);
            z = outcomes[++zindex];
            nodes[218].father = 76;

            nodes[218].terminal = true;
            nodes[218].outcome = zindex;
            z.whichnode = 218;
            z.pay[0] = ratfromi(pay[0][132]);
            z.pay[1] = ratfromi(pay[1][132]);
            z = outcomes[++zindex];
            nodes[219].father = 76;

            nodes[219].terminal = true;
            nodes[219].outcome = zindex;
            z.whichnode = 219;
            z.pay[0] = ratfromi(pay[0][133]);
            z.pay[1] = ratfromi(pay[1][133]);
            z = outcomes[++zindex];
            nodes[220].father = 76;

            nodes[220].terminal = true;
            nodes[220].outcome = zindex;
            z.whichnode = 220;
            z.pay[0] = ratfromi(pay[0][134]);
            z.pay[1] = ratfromi(pay[1][134]);
            z = outcomes[++zindex];
            nodes[221].father = 74;

            nodes[221].terminal = true;
            nodes[221].outcome = zindex;
            z.whichnode = 221;
            z.pay[0] = ratfromi(pay[0][135]);
            z.pay[1] = ratfromi(pay[1][135]);
            z = outcomes[++zindex];
            nodes[222].father = 77;

            nodes[222].terminal = true;
            nodes[222].outcome = zindex;
            z.whichnode = 222;
            z.pay[0] = ratfromi(pay[0][136]);
            z.pay[1] = ratfromi(pay[1][136]);
            z = outcomes[++zindex];
            nodes[223].father = 78;

            nodes[223].terminal = true;
            nodes[223].outcome = zindex;
            z.whichnode = 223;
            z.pay[0] = ratfromi(pay[0][137]);
            z.pay[1] = ratfromi(pay[1][137]);
            z = outcomes[++zindex];
            nodes[224].father = 80;

            nodes[224].terminal = true;
            nodes[224].outcome = zindex;
            z.whichnode = 224;
            z.pay[0] = ratfromi(pay[0][138]);
            z.pay[1] = ratfromi(pay[1][138]);
            z = outcomes[++zindex];
            nodes[225].father = 80;

            nodes[225].terminal = true;
            nodes[225].outcome = zindex;
            z.whichnode = 225;
            z.pay[0] = ratfromi(pay[0][139]);
            z.pay[1] = ratfromi(pay[1][139]);
            z = outcomes[++zindex];
            nodes[226].father = 80;

            nodes[226].terminal = true;
            nodes[226].outcome = zindex;
            z.whichnode = 226;
            z.pay[0] = ratfromi(pay[0][140]);
            z.pay[1] = ratfromi(pay[1][140]);
            z = outcomes[++zindex];
            nodes[227].father = 82;

            nodes[227].terminal = true;
            nodes[227].outcome = zindex;
            z.whichnode = 227;
            z.pay[0] = ratfromi(pay[0][141]);
            z.pay[1] = ratfromi(pay[1][141]);
            z = outcomes[++zindex];
            nodes[228].father = 82;

            nodes[228].terminal = true;
            nodes[228].outcome = zindex;
            z.whichnode = 228;
            z.pay[0] = ratfromi(pay[0][142]);
            z.pay[1] = ratfromi(pay[1][142]);
            z = outcomes[++zindex];
            nodes[229].father = 82;

            nodes[229].terminal = true;
            nodes[229].outcome = zindex;
            z.whichnode = 229;
            z.pay[0] = ratfromi(pay[0][143]);
            z.pay[1] = ratfromi(pay[1][143]);
            z = outcomes[++zindex];
            nodes[230].father = 81;

            nodes[230].terminal = true;
            nodes[230].outcome = zindex;
            z.whichnode = 230;
            z.pay[0] = ratfromi(pay[0][144]);
            z.pay[1] = ratfromi(pay[1][144]);
            z = outcomes[++zindex];
            nodes[231].father = 81;

            nodes[231].terminal = true;
            nodes[231].outcome = zindex;
            z.whichnode = 231;
            z.pay[0] = ratfromi(pay[0][145]);
            z.pay[1] = ratfromi(pay[1][145]);
            z = outcomes[++zindex];
            nodes[232].father = 84;

            nodes[232].terminal = true;
            nodes[232].outcome = zindex;
            z.whichnode = 232;
            z.pay[0] = ratfromi(pay[0][146]);
            z.pay[1] = ratfromi(pay[1][146]);
            z = outcomes[++zindex];
            nodes[233].father = 84;

            nodes[233].terminal = true;
            nodes[233].outcome = zindex;
            z.whichnode = 233;
            z.pay[0] = ratfromi(pay[0][147]);
            z.pay[1] = ratfromi(pay[1][147]);
            z = outcomes[++zindex];
            nodes[234].father = 84;

            nodes[234].terminal = true;
            nodes[234].outcome = zindex;
            z.whichnode = 234;
            z.pay[0] = ratfromi(pay[0][148]);
            z.pay[1] = ratfromi(pay[1][148]);
            z = outcomes[++zindex];
            nodes[235].father = 85;

            nodes[235].terminal = true;
            nodes[235].outcome = zindex;
            z.whichnode = 235;
            z.pay[0] = ratfromi(pay[0][149]);
            z.pay[1] = ratfromi(pay[1][149]);
            z = outcomes[++zindex];
            nodes[236].father = 85;

            nodes[236].terminal = true;
            nodes[236].outcome = zindex;
            z.whichnode = 236;
            z.pay[0] = ratfromi(pay[0][150]);
            z.pay[1] = ratfromi(pay[1][150]);
            z = outcomes[++zindex];
            nodes[237].father = 85;

            nodes[237].terminal = true;
            nodes[237].outcome = zindex;
            z.whichnode = 237;
            z.pay[0] = ratfromi(pay[0][151]);
            z.pay[1] = ratfromi(pay[1][151]);
            z = outcomes[++zindex];
            nodes[238].father = 83;

            nodes[238].terminal = true;
            nodes[238].outcome = zindex;
            z.whichnode = 238;
            z.pay[0] = ratfromi(pay[0][152]);
            z.pay[1] = ratfromi(pay[1][152]);
            zindex++;
            nodes[1].iset = 0;
            nodes[2].iset = 1;
            nodes[3].iset = 3;
            nodes[4].iset = 9;
            nodes[5].iset = 4;
            nodes[6].iset = 10;
            nodes[7].iset = 10;
            nodes[8].iset = 2;
            nodes[9].iset = 10;
            nodes[10].iset = 2;
            nodes[11].iset = 2;
            nodes[12].iset = 3;
            nodes[13].iset = 11;
            nodes[14].iset = 4;
            nodes[15].iset = 12;
            nodes[16].iset = 12;
            nodes[17].iset = 2;
            nodes[18].iset = 12;
            nodes[19].iset = 2;
            nodes[20].iset = 2;
            nodes[21].iset = 3;
            nodes[22].iset = 13;
            nodes[23].iset = 4;
            nodes[24].iset = 14;
            nodes[25].iset = 14;
            nodes[26].iset = 2;
            nodes[27].iset = 14;
            nodes[28].iset = 2;
            nodes[29].iset = 2;
            nodes[30].iset = 1;
            nodes[31].iset = 5;
            nodes[32].iset = 9;
            nodes[33].iset = 6;
            nodes[34].iset = 10;
            nodes[35].iset = 10;
            nodes[36].iset = 2;
            nodes[37].iset = 10;
            nodes[38].iset = 2;
            nodes[39].iset = 2;
            nodes[40].iset = 5;
            nodes[41].iset = 11;
            nodes[42].iset = 6;
            nodes[43].iset = 12;
            nodes[44].iset = 12;
            nodes[45].iset = 2;
            nodes[46].iset = 12;
            nodes[47].iset = 2;
            nodes[48].iset = 2;
            nodes[49].iset = 5;
            nodes[50].iset = 13;
            nodes[51].iset = 6;
            nodes[52].iset = 14;
            nodes[53].iset = 14;
            nodes[54].iset = 2;
            nodes[55].iset = 14;
            nodes[56].iset = 2;
            nodes[57].iset = 2;
            nodes[58].iset = 1;
            nodes[59].iset = 7;
            nodes[60].iset = 9;
            nodes[61].iset = 8;
            nodes[62].iset = 10;
            nodes[63].iset = 10;
            nodes[64].iset = 2;
            nodes[65].iset = 10;
            nodes[66].iset = 2;
            nodes[67].iset = 2;
            nodes[68].iset = 7;
            nodes[69].iset = 11;
            nodes[70].iset = 8;
            nodes[71].iset = 12;
            nodes[72].iset = 12;
            nodes[73].iset = 2;
            nodes[74].iset = 12;
            nodes[75].iset = 2;
            nodes[76].iset = 2;
            nodes[77].iset = 7;
            nodes[78].iset = 13;
            nodes[79].iset = 8;
            nodes[80].iset = 14;
            nodes[81].iset = 14;
            nodes[82].iset = 2;
            nodes[83].iset = 14;
            nodes[84].iset = 2;
            nodes[85].iset = 2;
            nodes[2].reachedby = 1;
            nodes[3].reachedby = 4;
            nodes[4].reachedby = 12;
            nodes[5].reachedby = 28;
            nodes[6].reachedby = 13;
            nodes[7].reachedby = 14;
            nodes[8].reachedby = 29;
            nodes[9].reachedby = 15;
            nodes[10].reachedby = 29;
            nodes[11].reachedby = 30;
            nodes[12].reachedby = 5;
            nodes[13].reachedby = 12;
            nodes[14].reachedby = 33;
            nodes[15].reachedby = 13;
            nodes[16].reachedby = 14;
            nodes[17].reachedby = 34;
            nodes[18].reachedby = 15;
            nodes[19].reachedby = 34;
            nodes[20].reachedby = 35;
            nodes[21].reachedby = 6;
            nodes[22].reachedby = 12;
            nodes[23].reachedby = 38;
            nodes[24].reachedby = 13;
            nodes[25].reachedby = 14;
            nodes[26].reachedby = 39;
            nodes[27].reachedby = 15;
            nodes[28].reachedby = 39;
            nodes[29].reachedby = 40;
            nodes[30].reachedby = 2;
            nodes[31].reachedby = 4;
            nodes[32].reachedby = 17;
            nodes[33].reachedby = 28;
            nodes[34].reachedby = 18;
            nodes[35].reachedby = 19;
            nodes[36].reachedby = 29;
            nodes[37].reachedby = 20;
            nodes[38].reachedby = 29;
            nodes[39].reachedby = 30;
            nodes[40].reachedby = 5;
            nodes[41].reachedby = 17;
            nodes[42].reachedby = 33;
            nodes[43].reachedby = 18;
            nodes[44].reachedby = 19;
            nodes[45].reachedby = 34;
            nodes[46].reachedby = 20;
            nodes[47].reachedby = 34;
            nodes[48].reachedby = 35;
            nodes[49].reachedby = 6;
            nodes[50].reachedby = 17;
            nodes[51].reachedby = 38;
            nodes[52].reachedby = 18;
            nodes[53].reachedby = 19;
            nodes[54].reachedby = 39;
            nodes[55].reachedby = 20;
            nodes[56].reachedby = 39;
            nodes[57].reachedby = 40;
            nodes[58].reachedby = 3;
            nodes[59].reachedby = 4;
            nodes[60].reachedby = 22;
            nodes[61].reachedby = 28;
            nodes[62].reachedby = 23;
            nodes[63].reachedby = 24;
            nodes[64].reachedby = 29;
            nodes[65].reachedby = 25;
            nodes[66].reachedby = 29;
            nodes[67].reachedby = 30;
            nodes[68].reachedby = 5;
            nodes[69].reachedby = 22;
            nodes[70].reachedby = 33;
            nodes[71].reachedby = 23;
            nodes[72].reachedby = 24;
            nodes[73].reachedby = 34;
            nodes[74].reachedby = 25;
            nodes[75].reachedby = 34;
            nodes[76].reachedby = 35;
            nodes[77].reachedby = 6;
            nodes[78].reachedby = 22;
            nodes[79].reachedby = 38;
            nodes[80].reachedby = 23;
            nodes[81].reachedby = 24;
            nodes[82].reachedby = 39;
            nodes[83].reachedby = 25;
            nodes[84].reachedby = 39;
            nodes[85].reachedby = 40;
            nodes[86].reachedby = 11;
            nodes[87].reachedby = 27;
            nodes[88].reachedby = 29;
            nodes[89].reachedby = 30;
            nodes[90].reachedby = 31;
            nodes[91].reachedby = 7;
            nodes[92].reachedby = 8;
            nodes[93].reachedby = 9;
            nodes[94].reachedby = 30;
            nodes[95].reachedby = 31;
            nodes[96].reachedby = 7;
            nodes[97].reachedby = 8;
            nodes[98].reachedby = 9;
            nodes[99].reachedby = 7;
            nodes[100].reachedby = 8;
            nodes[101].reachedby = 9;
            nodes[102].reachedby = 31;
            nodes[103].reachedby = 11;
            nodes[104].reachedby = 32;
            nodes[105].reachedby = 34;
            nodes[106].reachedby = 35;
            nodes[107].reachedby = 36;
            nodes[108].reachedby = 7;
            nodes[109].reachedby = 8;
            nodes[110].reachedby = 9;
            nodes[111].reachedby = 35;
            nodes[112].reachedby = 36;
            nodes[113].reachedby = 7;
            nodes[114].reachedby = 8;
            nodes[115].reachedby = 9;
            nodes[116].reachedby = 7;
            nodes[117].reachedby = 8;
            nodes[118].reachedby = 9;
            nodes[119].reachedby = 36;
            nodes[120].reachedby = 11;
            nodes[121].reachedby = 37;
            nodes[122].reachedby = 39;
            nodes[123].reachedby = 40;
            nodes[124].reachedby = 41;
            nodes[125].reachedby = 7;
            nodes[126].reachedby = 8;
            nodes[127].reachedby = 9;
            nodes[128].reachedby = 40;
            nodes[129].reachedby = 41;
            nodes[130].reachedby = 7;
            nodes[131].reachedby = 8;
            nodes[132].reachedby = 9;
            nodes[133].reachedby = 7;
            nodes[134].reachedby = 8;
            nodes[135].reachedby = 9;
            nodes[136].reachedby = 41;
            nodes[137].reachedby = 16;
            nodes[138].reachedby = 27;
            nodes[139].reachedby = 29;
            nodes[140].reachedby = 30;
            nodes[141].reachedby = 31;
            nodes[142].reachedby = 7;
            nodes[143].reachedby = 8;
            nodes[144].reachedby = 9;
            nodes[145].reachedby = 30;
            nodes[146].reachedby = 31;
            nodes[147].reachedby = 7;
            nodes[148].reachedby = 8;
            nodes[149].reachedby = 9;
            nodes[150].reachedby = 7;
            nodes[151].reachedby = 8;
            nodes[152].reachedby = 9;
            nodes[153].reachedby = 31;
            nodes[154].reachedby = 16;
            nodes[155].reachedby = 32;
            nodes[156].reachedby = 34;
            nodes[157].reachedby = 35;
            nodes[158].reachedby = 36;
            nodes[159].reachedby = 7;
            nodes[160].reachedby = 8;
            nodes[161].reachedby = 9;
            nodes[162].reachedby = 35;
            nodes[163].reachedby = 36;
            nodes[164].reachedby = 7;
            nodes[165].reachedby = 8;
            nodes[166].reachedby = 9;
            nodes[167].reachedby = 7;
            nodes[168].reachedby = 8;
            nodes[169].reachedby = 9;
            nodes[170].reachedby = 36;
            nodes[171].reachedby = 16;
            nodes[172].reachedby = 37;
            nodes[173].reachedby = 39;
            nodes[174].reachedby = 40;
            nodes[175].reachedby = 41;
            nodes[176].reachedby = 7;
            nodes[177].reachedby = 8;
            nodes[178].reachedby = 9;
            nodes[179].reachedby = 40;
            nodes[180].reachedby = 41;
            nodes[181].reachedby = 7;
            nodes[182].reachedby = 8;
            nodes[183].reachedby = 9;
            nodes[184].reachedby = 7;
            nodes[185].reachedby = 8;
            nodes[186].reachedby = 9;
            nodes[187].reachedby = 41;
            nodes[188].reachedby = 21;
            nodes[189].reachedby = 27;
            nodes[190].reachedby = 29;
            nodes[191].reachedby = 30;
            nodes[192].reachedby = 31;
            nodes[193].reachedby = 7;
            nodes[194].reachedby = 8;
            nodes[195].reachedby = 9;
            nodes[196].reachedby = 30;
            nodes[197].reachedby = 31;
            nodes[198].reachedby = 7;
            nodes[199].reachedby = 8;
            nodes[200].reachedby = 9;
            nodes[201].reachedby = 7;
            nodes[202].reachedby = 8;
            nodes[203].reachedby = 9;
            nodes[204].reachedby = 31;
            nodes[205].reachedby = 21;
            nodes[206].reachedby = 32;
            nodes[207].reachedby = 34;
            nodes[208].reachedby = 35;
            nodes[209].reachedby = 36;
            nodes[210].reachedby = 7;
            nodes[211].reachedby = 8;
            nodes[212].reachedby = 9;
            nodes[213].reachedby = 35;
            nodes[214].reachedby = 36;
            nodes[215].reachedby = 7;
            nodes[216].reachedby = 8;
            nodes[217].reachedby = 9;
            nodes[218].reachedby = 7;
            nodes[219].reachedby = 8;
            nodes[220].reachedby = 9;
            nodes[221].reachedby = 36;
            nodes[222].reachedby = 21;
            nodes[223].reachedby = 37;
            nodes[224].reachedby = 39;
            nodes[225].reachedby = 40;
            nodes[226].reachedby = 41;
            nodes[227].reachedby = 7;
            nodes[228].reachedby = 8;
            nodes[229].reachedby = 9;
            nodes[230].reachedby = 40;
            nodes[231].reachedby = 41;
            nodes[232].reachedby = 7;
            nodes[233].reachedby = 8;
            nodes[234].reachedby = 9;
            nodes[235].reachedby = 7;
            nodes[236].reachedby = 8;
            nodes[237].reachedby = 9;
            nodes[238].reachedby = 41;
            isets[0].player = 0;
            isets[0].move0 = 1;
            isets[0].nmoves = 3;
            isets[1].player = 0;
            isets[1].move0 = 4;
            isets[1].nmoves = 3;
            isets[2].player = 0;
            isets[2].move0 = 7;
            isets[2].nmoves = 3;
            isets[3].player = 1;
            isets[3].move0 = 11;
            isets[3].nmoves = 2;
            isets[4].player = 1;
            isets[4].move0 = 13;
            isets[4].nmoves = 3;
            isets[5].player = 1;
            isets[5].move0 = 16;
            isets[5].nmoves = 2;
            isets[6].player = 1;
            isets[6].move0 = 18;
            isets[6].nmoves = 3;
            isets[7].player = 1;
            isets[7].move0 = 21;
            isets[7].nmoves = 2;
            isets[8].player = 1;
            isets[8].move0 = 23;
            isets[8].nmoves = 3;
            isets[9].player = 2;
            isets[9].move0 = 27;
            isets[9].nmoves = 2;
            isets[10].player = 2;
            isets[10].move0 = 29;
            isets[10].nmoves = 3;
            isets[11].player = 2;
            isets[11].move0 = 32;
            isets[11].nmoves = 2;
            isets[12].player = 2;
            isets[12].move0 = 34;
            isets[12].nmoves = 3;
            isets[13].player = 2;
            isets[13].move0 = 37;
            isets[13].nmoves = 2;
            isets[14].player = 2;
            isets[14].move0 = 39;
            isets[14].nmoves = 3;
            // move 0 is empty sequence for player 0
            moves[1].atiset = 0;
            moves[1].behavprob = 1/(Rational)3;
            moves[2].atiset = 0;
            moves[2].behavprob = 1 / (Rational)3;
            moves[3].atiset = 0;
            moves[3].behavprob = 1 / (Rational)3;
            moves[4].atiset = 1;
            moves[4].behavprob = 1 / (Rational)3;
            moves[5].atiset = 1;
            moves[5].behavprob = 1 / (Rational)3;
            moves[6].atiset = 1;
            moves[6].behavprob = 1 / (Rational)3;
            moves[7].atiset = 2;
            moves[7].behavprob = 1 / (Rational)3;
            moves[8].atiset = 2;
            moves[8].behavprob = 1 / (Rational)3;
            moves[9].atiset = 2;
            moves[9].behavprob = 1 / (Rational)3;
            // move 10 is empty sequence for player 1
            moves[11].atiset = 3;
            moves[12].atiset = 3;
            moves[13].atiset = 4;
            moves[14].atiset = 4;
            moves[15].atiset = 4;
            moves[16].atiset = 5;
            moves[17].atiset = 5;
            moves[18].atiset = 6;
            moves[19].atiset = 6;
            moves[20].atiset = 6;
            moves[21].atiset = 7;
            moves[22].atiset = 7;
            moves[23].atiset = 8;
            moves[24].atiset = 8;
            moves[25].atiset = 8;
            // move 26 is empty sequence for player 2
            moves[27].atiset = 9;
            moves[28].atiset = 9;
            moves[29].atiset = 10;
            moves[30].atiset = 10;
            moves[31].atiset = 10;
            moves[32].atiset = 11;
            moves[33].atiset = 11;
            moves[34].atiset = 12;
            moves[35].atiset = 12;
            moves[36].atiset = 12;
            moves[37].atiset = 13;
            moves[38].atiset = 13;
            moves[39].atiset = 14;
            moves[40].atiset = 14;
            moves[41].atiset = 14;

        }

    }
}

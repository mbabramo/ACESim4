using ACESim;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ACESimBase.Util.CPrint;
using static ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm.ColumnPrinter;
using static ACESim.ArrayFormConversionExtension;
using System.Numerics;
using ACESimBase.GameSolvingSupport;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm
{
    public class ECTATreeDefinition<T> where T : MaybeExact<T>, new()
    {
        public ECTALemke<T> Lemke;

        public const int PLAYERS = 3;
        public const int NAMECHARS = 8;
        public const int ROOT = 1;
        public const int MAXRANDPAY = 100;
        public const int MINUSINFTY = -30000;
        public const int MAXSTRL = 100;
        public char[] an1 = new char[] { '!', 'A', 'a' };
        public char[] an2 = new char[] { '/', 'Z', 'z' };
        public ECTANode<T>[] nodes;
        public const int rootindex = 1;
        public ECTANode<T> root => nodes[rootindex];
        public ECTAInformationSet[] informationSets;
        public ECTAMove<T>[] moves;
        public ECTAOutcome<T>[] outcomes;

        public int lastnode;
        public int lastoutcome;
        public int[] firstInformationSet = new int[PLAYERS + 1];
        public int[] firstMove = new int[PLAYERS + 1];

        public int[] numSequences = new int[3];
        public int[] numInfoSets = new int[3];

        public ECTAPayVector<T> maxpay = new ECTAPayVector<T>();


        public int moveIndex(ECTAMove<T> move) => moves.Select((item, index) => (item, index)).First(x => x.item == move).index;
        public int informationSetIndex(ECTAInformationSet iset) => informationSets.Select((item, index) => (item, index)).First(x => x.item == iset).index;
        public int outcomeIndex(ECTAOutcome<T> outcome) => outcomes.Select((item, index) => (item, index)).First(x => x.item == outcome).index;

        public void allocateTree(int numNodes, int numInformationSets, int numMoves, int numOutcomes)
        {
            nodes = new ECTANode<T>[numNodes];
            for (int i = 0; i < numNodes; i++)
                nodes[i] = new ECTANode<T>();
            lastnode = numNodes;
            informationSets = new ECTAInformationSet[numInformationSets];
            for (int i = 0; i < numInformationSets; i++)
                informationSets[i] = new ECTAInformationSet();
            moves = new ECTAMove<T>[numMoves];
            for (int i = 0; i < numMoves; i++)
                moves[i] = new ECTAMove<T>();
            outcomes = new ECTAOutcome<T>[numOutcomes];
            for (int i = 0; i < numOutcomes; i++)
                outcomes[i] = new ECTAOutcome<T>();
            lastoutcome = numOutcomes;
        }       /* end of alloctree(nn, ni, nm, no)        */

        public bool generateSequence()
        {
            bool isNotOK = false;
            int playerIndex;
            ECTANode<T> node;
            int sequence;

            // the code assumes that firstiset and firstmove are set for a hypothetical next player.
            // This allows us, for any player, to subtract the index for the next player to the index for this
            // player to get the total number of sequences or information sets.
            firstInformationSet[PLAYERS] = informationSets.Length;
            firstMove[PLAYERS] = moves.Length;

            /* set  numSequences[], numInfoSets[]               */
            for (playerIndex = 0; playerIndex < PLAYERS; playerIndex++)
            {
                numSequences[playerIndex] = firstMove[playerIndex + 1] - firstMove[playerIndex];
                numInfoSets[playerIndex] = firstInformationSet[playerIndex + 1] - firstInformationSet[playerIndex];
            }

            /* set sequence for all isets to NULL (represented by -1 here) */
            foreach (var h in informationSets)
                h.sequence = -1;

            for (playerIndex = 0; playerIndex < PLAYERS; playerIndex++)
                root.sequenceForPlayer[playerIndex] = firstMove[playerIndex];
            informationSets[root.iset].sequence = firstMove[informationSets[root.iset].playerIndex];

            for (int i = 2; i < nodes.Length; i++)
            {
                node = nodes[i];
                if (node.father >= i)
                /* tree is not topologically sorted     */
                {
                    isNotOK = true;
                    throw new Exception($"tree not topologically sorted: father {node.father} is larger than node {i} itself.\n");
                }

                /* update sequence triple by updating the move of the last player who played to this node. */
                /* first initialize sequence to same as father node */
                for (playerIndex = 0; playerIndex < PLAYERS; playerIndex++)
                    node.sequenceForPlayer[playerIndex] = nodes[node.father].sequenceForPlayer[playerIndex];
                /* now update sequence for player who moved at father */
                ECTAMove<T> moveAtFather = moves[node.moveAtFather];
                int fromInformationSetIndex = moveAtFather.priorInformationSet;
                int player = informationSets[fromInformationSetIndex].playerIndex;
                node.sequenceForPlayer[player] = node.moveAtFather;

                /* update sequence for information set, check perfect recall           */
                if (!(node.terminal))
                {
                    var h = informationSets[node.iset];
                    sequence = node.sequenceForPlayer[h.playerIndex];
                    if (h.sequence == -1)
                        h.sequence = sequence; /* set the sequence to the last move by the player whose information set this is */ 
                    else if (sequence != (int) h.sequence)
                    /* not the same as last sequence leading to info set    */
                    {
                        isNotOK = true;
                        // Important note: The chance player MUST have perfect recall of all chance decisions. 
                        throw new Exception($"Imperfect recall in information set {node.iset} named {h.name}; different sequences no {sequence} and {h.sequence}");
                        ///* need output routines for isets, moves, later         */
                        //tabbedtextf("imperfect recall in info set no. %d ", u.iset);
                        //tabbedtextf("named %s\n", h.name);
                        //tabbedtextf("different sequences no. %d,", seq);
                        //tabbedtextf(" %d\n", h.seqin);
                    }
                }       /* end of "u decision node"     */
            }           /* end of "for all nodes u"     */
            return isNotOK;
        }       /* end of  bool genseqin()               */

        public void normalizeMaxPayoutToNegative1(bool bprint)
        {
            char[] s = new char[MAXSTRL];
            int playerIndex;
            ECTAOutcome<T> z;
            ECTAPayVector<T> addtopay = new ECTAPayVector<T>();

            for (playerIndex = 0; playerIndex < PLAYERS - 1; playerIndex++)
            {
                maxpay[playerIndex] = MaybeExact<T>.FromInteger(MINUSINFTY);
                for (int zindex = 0; zindex < outcomes.Length; zindex++)
                {
                    z = outcomes[zindex];
                    if (z.pay[playerIndex].IsGreaterThan(maxpay[playerIndex]))
                        maxpay[playerIndex] = z.pay[playerIndex];
                }
                if (bprint)     /* comment to stdout    */
                {
                    TabbedText.WriteLine($"Player {playerIndex}'s maximum payoff is {maxpay[playerIndex]}, normalized to -1");
                }
                addtopay[playerIndex] = (maxpay[playerIndex].Plus(MaybeExact<T>.One())).Negated();
                for (int zindex = 0; zindex < outcomes.Length; zindex++)
                {
                    z = outcomes[zindex];
                    z.pay[playerIndex] = z.pay[playerIndex].Plus(addtopay[playerIndex]);
                }
            }
        }

        public void autonameInformationSets()
        {
            int pl, anbase, max, digits, i, i1, j;
            ECTAInformationSet h;

            for (pl = 0; pl < PLAYERS; pl++)    /* name isets of player pl      */
            {
                max = anbase = an2[pl] - an1[pl] + 1;
                for (digits = 1; max < numInfoSets[pl]; max *= anbase, digits++)
                    ;
                if (digits >= NAMECHARS)
                {
                    tabbedtextf("Too many isets (%d) of player %d.  ", numInfoSets[pl], pl);
                    tabbedtextf("change NAMECHARS to %d or larger\n", digits + 1);
                    throw new Exception("Digits exceeds namechars");
                }
                for (i = 0; i < numInfoSets[pl]; i++)
                {
                    i1 = i;
                    h = informationSets[firstInformationSet[pl] + i];
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

        string moveToString(ECTAMove<T> c, int pl)
        {
            string s;
            if (c == null)
            {
                s = sprintf("*");
            }
            else if (c == moves[firstMove[pl]])
            {
                s = sprintf("()");
            }
            else 
                s= sprintf("%s%d", informationSets[c.priorInformationSet].name,  moveIndex(c) - informationSets[c.priorInformationSet].firstMoveIndex);
            return s;
        }       /* end of  int movetoa (c, pl, *s)      */

        string sequenceToString(ECTAMove<T> seq, int pl)
        {
            string s;
            if (seq == null)
            {
                s = sprintf("*");
                return s;
            }
            if (seq == moves[firstMove[pl]])
            {
                s = sprintf(".");
                return s;
            }

            ECTAInformationSet priorInformationSet = informationSets[seq.priorInformationSet];
            int sequenceAtPriorInformationSet = priorInformationSet.sequence;
            s = sequenceToString(moves[sequenceAtPriorInformationSet], pl);       /* recursive call       */
            string s2 = null;
            s2 = moveToString(seq, pl);
            s = s + s2;
            return s;
        }       /* end of  int seqtoa (seq, pl, *s)     */


        public void outputGameTree()
        {
            string s = null;
            int pl;
            ECTANode<T> u;
            ECTAInformationSet h;
            ECTAMove<T> c;

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
                colipr(u.moveAtFather);
                colipr(u.outcome);
                for (pl = 1; pl < PLAYERS; pl++)
                    if (u.terminal)
                    {
                        s = (outcomes[u.outcome].pay[pl - 1]).ToString();
                        colpr(s);
                    }
                    else
                        colpr("");
                for (pl = 0; pl < PLAYERS; pl++)
                    colipr(u.sequenceForPlayer[pl]);
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
            for (int hIndex = 0; hIndex < informationSets.Length; hIndex++)
            {
                h = informationSets[hIndex];
                while (hIndex == firstInformationSet[pl])
                {
                    s = sprintf("pl%d:", pl); 
                    colpr(s);
                    colnl();
                    pl++;
                }
                colipr(informationSetIndex(h));
                colipr(h.playerIndex);
                colipr(h.numMoves);
                colipr(h.firstMoveIndex);
                colpr(h.name);
                colipr((int) h.sequence);
                colipr(0); // not currently computed -- leave in table only for compatibility with original C program
                colipr(0); // not currently computed -- leave in table only for compatibility with original C program
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
                while (cindex == firstMove[pl])
                {
                    s = sprintf("pl%d:", pl); 
                    colpr(s);
                    colnl();
                    pl++;
                }
                /* pl is now the NEXT possible player       */
                colipr(cindex);
                s = moveToString(c, pl - 1); 
                colpr(s);
                s = sequenceToString(c, pl - 1); 
                colpr(s);
                colipr(c.priorInformationSet);
                s = (c.behavioralProbability).ToString(); 
                colpr(s);
                s = (c.realizationProbability).ToString(); 
                colpr(s);
                colipr(c.redsfcol);
                colipr(c.ncompat);
                colipr(c.offset);
            }
            colout();
        }       /* end of  rawtreeprint()       */

        /* PRIOR */

        // Make it possible to have predictable series of random numbers -- for debugging only. 
        bool RandDebuggingMode = false; 
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
            ECTAMove<T> c;
            int pl;
            for (pl = 1; pl < ECTATreeDefinition<T>.PLAYERS; pl++)
                for (int cindex = firstMove[pl] + 1; cindex < firstMove[pl + 1]; cindex++)
                {
                    c = moves[cindex];
                    c.behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(informationSets[c.priorInformationSet].numMoves));
                }
        }

        public void genprior(int seed)
        {
            int pl;
            ECTAInformationSet h;

            if (0 == seed)
            {
                gencentroid();
                //var isetsDEBUG = informationSets.Where(x => x.name.Contains("Alt 141")).First();
                //moves[isetsDEBUG.move0 + 0].behavprob = ((T)197) / (T)1000;
                //moves[isetsDEBUG.move0 + 1].behavprob = ((T)199) / (T)1000;
                //moves[isetsDEBUG.move0 + 2].behavprob = ((T)200) / (T)1000;
                //moves[isetsDEBUG.move0 + 3].behavprob = ((T)201) / (T)1000;
                //moves[isetsDEBUG.move0 + 4].behavprob = ((T)203) / (T)1000;

                return;
            }
            /* generate random priors for all information sets	*/
            RandomGeneratorInstanceManager.Reset(seed);
            // srand(FIRSTPRIORSEED + flags.seed);
            for (pl = 1; pl < PLAYERS; pl++)
                for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
                {
                    h = informationSets[hindex];
                    // We must create fractions that add up to 1 (each greater than 0).
                    // So, we'll just take random numbers from 1 to 10, use those for numerators
                    // and the sum for a denominator.
                    MaybeExact<T> denominator = MaybeExact<T>.Zero();
                    for (int i = 0; i < h.numMoves; i++)
                    {
                        double maxValue = 9;
                        MaybeExact<T> numerator = MaybeExact<T>.FromInteger((1 + (int)Math.Floor(maxValue * RandomGenerator.NextDouble())));
                        moves[h.firstMoveIndex + i].behavioralProbability = (T)numerator; // store value so that we remember it
                        denominator = denominator.Plus(numerator);
                    }
                    for (int i = 0; i < h.numMoves; i++)
                    {
                        MaybeExact<T> a =  MaybeExact<T>.Zero();
                        a = moves[h.firstMoveIndex + i].behavioralProbability.Numerator.DividedBy(denominator);
                        moves[h.firstMoveIndex + i].behavioralProbability = a.CanonicalForm;
                    }
                }
        }

        public void outprior()
        {
            int pl;

            tabbedtextf("------Prior behavior strategies player 1, 2:\n");
            for (pl = 1; pl < PLAYERS; pl++)
            {
                SetRealizationProbabilitiesFromBehavioralProbabilities(pl);
                realplanfromprob(pl, realizationPlan[pl]);
                outbehavstrat(pl, realizationPlan[pl], true);
            }
        }

        /* SEQUENCE FORM */


        ECTAPayVector<T>[][] sequenceFormPayouts;
        int[][][] sequenceFormConstraints = new int[PLAYERS][][];

        void allocateSequenceFormMemory()
        {
            int playerIndex, player1SequenceIndex, player2SequenceIndex;
            int numrows;

            /* payoff matrices, two players only here, init to pay 0        */
            sequenceFormPayouts = CreateJaggedArray<ECTAPayVector<T>[][]>(numSequences[1], numSequences[2]);
            for (player1SequenceIndex = 0; player1SequenceIndex < numSequences[1]; player1SequenceIndex++)
                for (player2SequenceIndex = 0; player2SequenceIndex < numSequences[2]; player2SequenceIndex++)
                {
                    sequenceFormPayouts[player1SequenceIndex][player2SequenceIndex] = new ECTAPayVector<T>();
                    for (playerIndex = 1; playerIndex < PLAYERS; playerIndex++) // skip chance palyer
                        sequenceFormPayouts[player1SequenceIndex][player2SequenceIndex][playerIndex - 1] = MaybeExact<T>.Zero();
                }
            /* constraint matrices, any number of players           */
            /* sfconstr[0] stays unused                             */
            for (playerIndex = 1; playerIndex < PLAYERS; playerIndex++)
            {
                numrows = numInfoSets[playerIndex] + 1;   /* extra row for seq 0  */
                sequenceFormConstraints[playerIndex] = CreateJaggedArray<int[][]>(numrows, numSequences[playerIndex]);
            }
        }       /* end of allocsf()     */

        void generateSequenceFormPayoutsAndConstraints()
        {
            int pl, player1SequenceIndex, player2SequenceIndex;
            ECTAOutcome<T> z = null;
            allocateSequenceFormMemory();

            SetRealizationProbabilitiesFromBehavioralProbabilities(0);     /* get realization probabilities of leaves      */

            /* sf payoff matrices                   */
            for (int zindex = 0; zindex < outcomes.Length; zindex++)
            {
                z = outcomes[zindex];
                ECTANode<T> node = nodes[z.nodeIndex];
                player1SequenceIndex = node.sequenceForPlayer[1] - firstMove[1]; 
                player2SequenceIndex = node.sequenceForPlayer[2] - firstMove[2];
                for (pl = 1; pl < PLAYERS; pl++)
                    sequenceFormPayouts[player1SequenceIndex][player2SequenceIndex][pl - 1] = sequenceFormPayouts[player1SequenceIndex][player2SequenceIndex][pl - 1].Plus(
                        moves[node.sequenceForPlayer[0]].realizationProbability.Times(z.pay[pl - 1]));
            }
            /* sf constraint matrices, sparse fill (everything else is 0's  */
            for (pl = 1; pl < PLAYERS; pl++)
            {
                sequenceFormConstraints[pl][0][0] = 1;     /* empty sequence                       */
                for (player1SequenceIndex = 0; player1SequenceIndex < numInfoSets[pl]; player1SequenceIndex++)
                    sequenceFormConstraints[pl][player1SequenceIndex + 1][(int) informationSets[firstInformationSet[pl] + player1SequenceIndex].sequence - firstMove[pl]] = -1;
                for (player2SequenceIndex = 1; player2SequenceIndex < numSequences[pl]; player2SequenceIndex++)
                    sequenceFormConstraints[pl][moves[firstMove[pl] + player2SequenceIndex].priorInformationSet - firstInformationSet[pl] + 1][player2SequenceIndex] = 1;
            }
        }       /* end of  gensf()              */

        public void generateSequenceFormLCP()
        {
            int i;
            /*
             * The following shows the initial layout of the LCP. The number of infosets excludes null,
             * but a row/column is needed for the null info set (hence +1). The number of sequences
             * is assumed to already include the null sequence.
                +----------------+-------------+-----------------+-------------+------------------+
                |                | Sequences 1 | Infosets 2 (+1) | Sequences 2 | Infosets 1 (+ 1) |
                +----------------+-------------+-----------------+-------------+------------------+
                | Sequences 1    |             |                 | -A          | -E^T             |
                | Infosets 2 (+1)|             |                 | F           |                  |
                | Sequence 2     | -B^T        | -F^T            |             |                  |
                | Infosets1      | E           |                 |             |                  |
                +----------------+-------------+-----------------+-------------+------------------+
            Note that this diagram excludes the d (covering vector) and q (explained below) columns.
             */

            generateSequenceFormPayoutsAndConstraints();
            Lemke = new ECTALemke<T>(numSequences[1] + numInfoSets[2] + 1 + numSequences[2] + numInfoSets[1] + 1);
            /* fill  M  by copying from sequence form payouts and constraints */
            /* -A. A is a matrix of sequence payouts for the first player (with player 1's sequences as rows, and player 2's as columns). These are
             * negated and placed starting in the first row after columns devoted to each sequence of player 1 and each 
             * information set of player 2. */
            copySequenceFormPayoutsForPlayer(sequenceFormPayouts, 0, true, false, numSequences[1], numSequences[2],
            Lemke.lcpM, 0, numSequences[1] + numInfoSets[2] + 1);
            /* E and F are matrices that show the relationship between moves and information sets for each player. The rows
             * correspond to the information sets for the player (plus 1 for the null sequence), and the columns correspond
             * to the moves for the player (plus 1 for the beginning of the game). A 1 is placed at the move for each information
             * set (including a 1 for the "move" leading to the root information set), and a -1 is placed at the move corresponding
             * to the parent's information set. For example, in a bimatrix game where each player chooses from two strategies, E and
             * F will each look like this:
                +----+---+---+
                | 1  | 0 | 0 |
                +----+---+---+
                | -1 | 1 | 1 |
                +----+---+---+
            * The 1 on the first row represents the player's first information set. The -1 shows that this is the predecessor of 
            * the next information set, and the two 1's represent the two moves available at that information set.
            */
            /* -E\T.      */
            copyFromMatrix(sequenceFormConstraints[1], true, true, numInfoSets[1] + 1, numSequences[1],
            Lemke.lcpM, 0, numSequences[1] + numInfoSets[2] + 1 + numSequences[2]);
            /* F.       */
            copyFromMatrix(sequenceFormConstraints[2], false, false, numInfoSets[2] + 1, numSequences[2],
            Lemke.lcpM, numSequences[1], numSequences[1] + numInfoSets[2] + 1);
            /* -B\T. B is the matrix of sequence payouts for the second player, with the same format as A   */
            copySequenceFormPayoutsForPlayer(sequenceFormPayouts, 1, true, true, numSequences[1], numSequences[2],
            Lemke.lcpM, numSequences[1] + numInfoSets[2] + 1, 0);
            /* -F\T     */
            copyFromMatrix(sequenceFormConstraints[2], true, true, numInfoSets[2] + 1, numSequences[2],
            Lemke.lcpM, numSequences[1] + numInfoSets[2] + 1, numSequences[1]);
            /* E        */
            copyFromMatrix(sequenceFormConstraints[1], false, false, numInfoSets[1] + 1, numSequences[1],
            Lemke.lcpM, numSequences[1] + numInfoSets[2] + 1 + numSequences[2], 0);
            /* define RHS q,  using special shape of SF constraints RHS e,f     */
            /* That is, we have a -1 in the right hand column on the first row containing each of E and F */
            /* because e and f are defined to be vectors with 1 on top and 0 everywhere else */
            for (i = 0; i < Lemke.lcpdim; i++)
                Lemke.rhsq[i] = MaybeExact<T>.Zero();
            Lemke.rhsq[numSequences[1]] = MaybeExact<T>.One().Negated();
            Lemke.rhsq[numSequences[1] + numInfoSets[2] + 1 + numSequences[2]] = MaybeExact<T>.One().Negated();
        }

        void realplanfromprob(int pl, MaybeExact<T>[] rplan)
        {
            int i;

            for (i = 0; i < numSequences[pl]; i++)
                rplan[i] = moves[firstMove[pl] + i].realizationProbability;
        }

        public int propermixisets(int pl, MaybeExact<T>[] rplan, int offset)
        {
            int mix = 0;
            int i;
            ECTAMove<T> c;
            ECTAInformationSet h;

            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                i = 0;
                for (int cindex = h.firstMoveIndex; i < h.numMoves; cindex++, i++)
                {
                    c = moves[cindex];
                    if (rplan[offset + cindex - firstMove[pl]].IsNotEqualTo(MaybeExact<T>.Zero()) &&
                        !(rplan[offset + cindex - firstMove[pl]].IsEqualTo(
                                  rplan[offset + (int) h.sequence - firstMove[pl]])))
                    {
                        mix++;
                        break;
                    }
                }
            }
            return mix;
        }

        void outrealplan(int pl, MaybeExact<T>[] rplan, int offset)
        {
            int i;
            string s = null;

            colset(numSequences[pl]);
            for (i = 0; i < numSequences[pl]; i++)
            {
                s = sequenceToString(moves[firstMove[pl] + i], pl);
                colpr(s);
            }
            for (i = 0; i < numSequences[pl]; i++)
            {
                s = (rplan[i + offset]).ToString();
                colpr(s);
            }
            colout();
        }

        void outbehavstrat(int pl, MaybeExact<T>[] rplan, bool bnewline)
        {
            string s = null;
            int i;
            ECTAMove<T> c;
            ECTAInformationSet h;
            MaybeExact<T> rprob, bprob;

            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                i = 0;
                for (int cindex = h.firstMoveIndex; i < h.numMoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[cindex - firstMove[pl]];
                    if (rprob.IsNotEqualTo(MaybeExact<T>.Zero()))
                    {
                        s = moveToString(c, pl);
                        tabbedtextf(" %s", s);
                        bprob = rprob.DividedBy(rplan[(int) h.sequence - firstMove[pl]]);
                        if (!bprob.IsEqualTo(MaybeExact<T>.One()))
                        {
                            s = (bprob).ToString();
                            tabbedtextf(":%s", s);
                        }
                    }
                }
            }
            if (bnewline)
                tabbedtextf("\n");
        }

        public void MakePlayerMovesStrictlyMixed(MaybeExact<T>[] allMoveProbabilities, MaybeExact<T> minValue)
        {

            int offset = numSequences[1] + 1 + numInfoSets[2];
            MakePlayerMovesStrictlyMixed(1, allMoveProbabilities, 0, minValue);
            MakePlayerMovesStrictlyMixed(2, allMoveProbabilities, offset, minValue);
        }

        private void MakePlayerMovesStrictlyMixed(int pl, MaybeExact<T>[] allMoveProbabilities, int offset, MaybeExact<T> minValue)
        {
            int indexInAllMovesArray = 0;
            int moveIndexInInformationSet;
            ECTAMove<T> c;
            ECTAInformationSet h;
            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                MaybeExact<T> total = MaybeExact<T>.Zero();
                MaybeExact<T> totalAboveMinValue = MaybeExact<T>.Zero();
                moveIndexInInformationSet = 0;
                for (int cindex = h.firstMoveIndex; moveIndexInInformationSet < h.numMoves; cindex++, moveIndexInInformationSet++)
                {
                    c = moves[cindex];
                    c.behavioralProbability = allMoveProbabilities[indexInAllMovesArray++];
                    if (c.behavioralProbability.IsLessThan(minValue))
                        c.behavioralProbability = minValue;
                    total = total.Plus(c.behavioralProbability);
                    if (c.behavioralProbability.IsGreaterThan(minValue))
                        totalAboveMinValue = totalAboveMinValue.Plus(c.behavioralProbability.Minus(minValue));
                }
                MaybeExact<T> excess = total.Minus(MaybeExact<T>.One());
                indexInAllMovesArray = 0;
                moveIndexInInformationSet = 0;
                for (int cindex = h.firstMoveIndex; moveIndexInInformationSet < h.numMoves; cindex++, moveIndexInInformationSet++)
                {
                    c = moves[cindex];
                    if (c.behavioralProbability.IsGreaterThan(minValue))
                    {
                        var proportionOfExcess = (c.behavioralProbability.Minus(minValue)).DividedBy(totalAboveMinValue);
                        c.behavioralProbability = c.behavioralProbability.Minus(excess.Times(proportionOfExcess));
                    }
                    allMoveProbabilities[indexInAllMovesArray++] = c.behavioralProbability;
                }
            }
        }

        public IEnumerable<MaybeExact<T>> GetInformationSetProbabilitySums(int pl, T[] rplan, int offset)
        {
            foreach (List<T> informationSetProbabilities in GetInformationSetProbabilities(pl, rplan, offset))
            {
                MaybeExact<T> total = MaybeExact<T>.Zero();
                foreach (T r in informationSetProbabilities)
                    total = total.Plus(r);
                yield return total;
            }
        }

        public IEnumerable<List<T>> GetInformationSetProbabilities(int pl, T[] rplan, int offset)
        {
            int i;
            ECTAMove<T> c;
            ECTAInformationSet h;
            T rprob;
            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                i = 0;
                List<T> moveProbabilities = new List<T>();
                for (int cindex = h.firstMoveIndex; i < h.numMoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstMove[pl]];
                    moveProbabilities.Add(rprob);
                }
                yield return moveProbabilities;
            }
        }

        private IEnumerable<MaybeExact<T>> GetPlayerMoveProbabilities(int pl, MaybeExact<T>[] rplan, int offset)
        {
            int i;
            ECTAMove<T> c;
            ECTAInformationSet h;
            MaybeExact<T> rprob;
            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                i = 0;
                for (int cindex = h.firstMoveIndex; i < h.numMoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstMove[pl]];
                    yield return rprob;

                }
            }
        }

        public IEnumerable<MaybeExact<T>> GetPlayerMovesFromSolution()
        {
            int offset = numSequences[1] + 1 + numInfoSets[2];
            foreach (MaybeExact<T> r in GetPlayerMoveProbabilities(1, Lemke.solz, 0))
                yield return r;
            foreach (MaybeExact<T> r in GetPlayerMoveProbabilities(2, Lemke.solz, offset))
                yield return r;
        }

        public void SetInitialPlayerMoves(IEnumerable<T> probabilities)
        {
            foreach (var zipped in probabilities.Zip(moves))
                zipped.Second.behavioralProbability = zipped.First;
        }

        void outbehavstrat_moves(int pl, MaybeExact<T>[] rplan, int offset, bool bnewline)
        {
            int i;
            ECTAMove<T> c;
            ECTAInformationSet h;
            MaybeExact<T> rprob;

            for (int hindex = firstInformationSet[pl]; hindex < firstInformationSet[pl + 1]; hindex++)
            {
                h = informationSets[hindex];
                i = 0;
                for (int cindex = h.firstMoveIndex; i < h.numMoves; cindex++, i++)
                {
                    c = moves[cindex];
                    rprob = rplan[offset + cindex - firstMove[pl]];
                    TabbedText.Write(rprob.ToString());
                    if (hindex != firstInformationSet[pl + 1] - 1 || i != h.numMoves - 1)
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

            colset(numSequences[2] + 2 + numInfoSets[1]);
            colleft(0);
            colpr("pay1");
            for (j = 0; j < numSequences[2]; j++)
            {
                s = sequenceToString(moves[firstMove[2] + j], 2); 
                colpr(s);
            }
            colnl();
            colpr("pay2");
            for (j = 0; j < numSequences[2]; j++)
                colpr(" ");
            colpr("cons1");
            for (k = 1; k <= numInfoSets[1]; k++)
                /* headers for constraint matrix pl1, printed on right of payoffs   */
                colpr(informationSets[firstInformationSet[1] + k - 1].name);
            for (i = 0; i < numSequences[1]; i++)
            {
                /* payoffs player 1 */
                s = sequenceToString(moves[firstMove[1] + i], 1); 
                colpr(s);
                for (j = 0; j < numSequences[2]; j++)
                {
                    s = (sequenceFormPayouts[i][j][0]).ToString(); 
                    colpr(s);
                }
                colnl();
                /* payoffs player 2 */
                colpr("");
                for (j = 0; j < numSequences[2]; j++)
                {
                    s = (sequenceFormPayouts[i][j][1]).ToString(); 
                    colpr(s);
                }
                /* constraints player 1 */
                for (k = 0; k <= numInfoSets[1]; k++)
                    colipr(sequenceFormConstraints[1][k][i]);
                colnl();
            }
            /* constraints player 2 */
            for (k = 0; k <= numInfoSets[2]; k++)
            {
                colnl();
                if (k == 0)
                    colpr("cons2");
                else
                    colpr(informationSets[firstInformationSet[2] + k - 1].name);
                for (j = 0; j < numSequences[2]; j++)
                    colipr(sequenceFormConstraints[2][k][j]);
                colnl();
            }
            colout();
        }       /* end of  sfprint()            */

        /* SFNF */

        public MaybeExact<T>[][] realizationPlan = new MaybeExact<T>[PLAYERS][];

        public void allocateRealizationPlan()
        {
            int pl;

            for (pl = 0; pl < PLAYERS; pl++)
                realizationPlan[pl] = new MaybeExact<T>[numSequences[pl]];
        }

        /// <summary>
        /// Sets realization probabilities for a player (including chance player) by multiplying the realization probability of the sequence
        /// excluding the move by the behavioral probability of the move.
        /// </summary>
        /// <param name="pl"></param>
        public void SetRealizationProbabilitiesFromBehavioralProbabilities(int pl)
        {
            ECTAMove<T> c;
            int lastmoveindex = firstMove[pl + 1];
            moves[firstMove[pl]].realizationProbability = MaybeExact<T>.One();  /* empty seq has probability 1  */
            for (int cindex = firstMove[pl] + 1; cindex < lastmoveindex; cindex++)
            {
                c = moves[cindex];
                int sequenceUpToMove = GetPriorSequence(c);
                c.realizationProbability = c.behavioralProbability.Times(moves[sequenceUpToMove].realizationProbability);
            }
        }

        public int GetPriorSequence(ECTAMove<T> c)
        {
            ECTAInformationSet priorInformationSet = GetPriorInformationSet(c);
            int sequenceUpToMove = priorInformationSet.sequence;
            return sequenceUpToMove;
        }

        public ECTAInformationSet GetPriorInformationSet(ECTAMove<T> c)
        {
            return informationSets[(int)c.priorInformationSet];
        }

        void copySequenceFormPayoutsForPlayer(ECTAPayVector<T>[][] sourceMatrix, int playerIndexMinus1, bool negate,
                bool transpose, int numRowsInSourceMatrix, int numColsInSourceMatrix,
                MaybeExact<T>[][] targetMatrix, int targetRowOffset, int targetColOffset)
        {
            int i, j;
            for (i = 0; i < numRowsInSourceMatrix; i++)
                for (j = 0; j < numColsInSourceMatrix; j++)
                    if (transpose)
                        targetMatrix[j + targetRowOffset][i + targetColOffset]
                        = negate ? (sourceMatrix[i][j][playerIndexMinus1]).Negated() : sourceMatrix[i][j][playerIndexMinus1];
                    else
                        targetMatrix[i + targetRowOffset][j + targetColOffset]
                        = negate ? (sourceMatrix[i][j][playerIndexMinus1]).Negated() : sourceMatrix[i][j][playerIndexMinus1];
        }

        void copyFromMatrix(int[][] sourceMatrix, bool negate,
                bool transpose, int numRowsInSourceMatrix, int numColsInSourceMatrix,
                MaybeExact<T>[][] targetMatrix, int targetRowOffset, int targetColOffset)
        {
            int i, j;
            for (i = 0; i < numRowsInSourceMatrix; i++)
                for (j = 0; j < numColsInSourceMatrix; j++)
                    if (transpose)
                        targetMatrix[j + targetRowOffset][i + targetColOffset]
                        = MaybeExact<T>.FromInteger(negate ? -sourceMatrix[i][j] : sourceMatrix[i][j]);
                    else
                        targetMatrix[i + targetRowOffset][j + targetColOffset]
                        = MaybeExact<T>.FromInteger(negate ? -sourceMatrix[i][j] : sourceMatrix[i][j]);
        }

        public void calculateCoveringVectorD()
        {
            /* The covering vector looks like this (with the number of rows indicated in the left column, and the contents of each entry within the table row listed on the right)
             * 
                +-----------------+-------------+
                |    Num rows     |  Contents   |
                +-----------------+-------------+
                | Sequences 1     | -rhsq – Aq  |
                | Infosets 2 (+1) | -rhsq       |
                | Sequence 2      | -rhsq -B^Tp |
                | Infosets1       | -rhsq       |
                +-----------------+-------------+
            */

            int i, j;

            int offsetToStartOfPlayer2Sequences = numSequences[1] + 1 + numInfoSets[2];
            /* covering vector  = -rhsq */
            for (i = 0; i < Lemke.lcpdim; i++)
                Lemke.coveringVectorD[i] = (Lemke.rhsq[i]).Negated();

            /* first blockrow += -Aq    */
            for (i = 0; i < numSequences[1]; i++)
                for (j = 0; j < numSequences[2]; j++)
                {
                    Lemke.coveringVectorD[i] = (Lemke.coveringVectorD[i]).Plus(Lemke.lcpM[i][offsetToStartOfPlayer2Sequences + j] /* Aij, which is offset horizontally in the LCP */.Times(
                              moves[firstMove[2] + j].realizationProbability /* qj, i.e. the realization probability of player 2's sequence */));
                }
            /* RSF yet to be done*/
            /* third blockrow += -B\T p */
            for (i = offsetToStartOfPlayer2Sequences; i < offsetToStartOfPlayer2Sequences + numSequences[2]; i++)
                for (j = 0; j < numSequences[1]; j++)
                    Lemke.coveringVectorD[i] = (Lemke.coveringVectorD[i]).Plus(Lemke.lcpM[i][j].Times( /* B^Tij, which is offset vertically in the LCP */
                              moves[firstMove[1] + j].realizationProbability)); /* pj, i.e. the realization probability of player 1's sequence */
            /* RSF yet to be done*/
        }


        public void showEquilibrium(bool outputRealizationPlan)
        {
            int offset;


            offset = numSequences[1] + 1 + numInfoSets[2];
            if (outputRealizationPlan)
            {
                tabbedtextf("Equilibrium realization plan player 1:\n");
                outrealplan(1, Lemke.solz, 0);
                tabbedtextf("\n");
                tabbedtextf("Equilibrium realization plan player 2:\n");
                outrealplan(2, Lemke.solz, offset);
            }
            tabbedtextf("......Equilibrium behavior strategies player 1, 2:\n");
            outbehavstrat_moves(1, Lemke.solz, 0, outputRealizationPlan); 
            tabbedtextf("\n");
            outbehavstrat_moves(2, Lemke.solz, offset, true);  
        }

        /* EXAMPLE */


        public void setupExampleGame()
        {
            int[][] pay = new int[2][] { new int[153]
                { 0, 1000, 125, 312, 500, 47, 109, 172, 500, 688, 47, 109, 172, 47, 109, 172, 875, 0, 1000, 125, 312, 500, 117, 180, 242, 500, 688, 117, 180, 242, 117, 180, 242, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 117, 180, 242, 500, 688, 117, 180, 242, 117, 180, 242, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 508, 570, 633, 500, 688, 508, 570, 633, 508, 570, 633, 875, 0, 1000, 125, 312, 500, 188, 250, 562, 500, 688, 188, 250, 562, 188, 250, 562, 875, 0, 1000, 125, 312, 500, 508, 570, 633, 500, 688, 508, 570, 633, 508, 570, 633, 875, 0, 1000, 125, 312, 500, 578, 641, 703, 500, 688, 578, 641, 703, 578, 641, 703, 875 }, new int[153]
    { 1000, 0, 875, 688, 500, 703, 641, 578, 500, 312, 703, 641, 578, 703, 641, 578, 125, 1000, 0, 875, 688, 500, 633, 570, 508, 500, 312, 633, 570, 508, 633, 570, 508, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 633, 570, 508, 500, 312, 633, 570, 508, 633, 570, 508, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 242, 180, 117, 500, 312, 242, 180, 117, 242, 180, 117, 125, 1000, 0, 875, 688, 500, 562, 500, 188, 500, 312, 562, 500, 188, 562, 500, 188, 125, 1000, 0, 875, 688, 500, 242, 180, 117, 500, 312, 242, 180, 117, 242, 180, 117, 125, 1000, 0, 875, 688, 500, 172, 109, 47, 500, 312, 172, 109, 47, 172, 109, 47, 125 }
            };
            allocateTree(239, 15, 42, 153);
            firstInformationSet[0] = 0;
            firstInformationSet[1] = 3;
            firstInformationSet[2] = 9;
            firstMove[0] = 0;
            firstMove[1] = 10;
            firstMove[2] = 26;

            int zindex = 0;
            ECTAOutcome<T> z = outcomes[zindex];

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
            z.nodeIndex = 86;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][0]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][0]);
            z = outcomes[++zindex];
            nodes[87].father = 4;

            nodes[87].terminal = true;
            nodes[87].outcome = zindex;
            z.nodeIndex = 87;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][1]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][1]);
            z = outcomes[++zindex];
            nodes[88].father = 6;

            nodes[88].terminal = true;
            nodes[88].outcome = zindex;
            z.nodeIndex = 88;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][2]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][2]);
            z = outcomes[++zindex];
            nodes[89].father = 6;

            nodes[89].terminal = true;
            nodes[89].outcome = zindex;
            z.nodeIndex = 89;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][3]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][3]);
            z = outcomes[++zindex];
            nodes[90].father = 6;

            nodes[90].terminal = true;
            nodes[90].outcome = zindex;
            z.nodeIndex = 90;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][4]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][4]);
            z = outcomes[++zindex];
            nodes[91].father = 8;

            nodes[91].terminal = true;
            nodes[91].outcome = zindex;
            z.nodeIndex = 91;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][5]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][5]);
            z = outcomes[++zindex];
            nodes[92].father = 8;

            nodes[92].terminal = true;
            nodes[92].outcome = zindex;
            z.nodeIndex = 92;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][6]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][6]);
            z = outcomes[++zindex];
            nodes[93].father = 8;

            nodes[93].terminal = true;
            nodes[93].outcome = zindex;
            z.nodeIndex = 93;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][7]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][7]);
            z = outcomes[++zindex];
            nodes[94].father = 7;

            nodes[94].terminal = true;
            nodes[94].outcome = zindex;
            z.nodeIndex = 94;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][8]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][8]);
            z = outcomes[++zindex];
            nodes[95].father = 7;

            nodes[95].terminal = true;
            nodes[95].outcome = zindex;
            z.nodeIndex = 95;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][9]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][9]);
            z = outcomes[++zindex];
            nodes[96].father = 10;

            nodes[96].terminal = true;
            nodes[96].outcome = zindex;
            z.nodeIndex = 96;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][10]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][10]);
            z = outcomes[++zindex];
            nodes[97].father = 10;

            nodes[97].terminal = true;
            nodes[97].outcome = zindex;
            z.nodeIndex = 97;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][11]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][11]);
            z = outcomes[++zindex];
            nodes[98].father = 10;

            nodes[98].terminal = true;
            nodes[98].outcome = zindex;
            z.nodeIndex = 98;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][12]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][12]);
            z = outcomes[++zindex];
            nodes[99].father = 11;

            nodes[99].terminal = true;
            nodes[99].outcome = zindex;
            z.nodeIndex = 99;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][13]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][13]);
            z = outcomes[++zindex];
            nodes[100].father = 11;

            nodes[100].terminal = true;
            nodes[100].outcome = zindex;
            z.nodeIndex = 100;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][14]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][14]);
            z = outcomes[++zindex];
            nodes[101].father = 11;

            nodes[101].terminal = true;
            nodes[101].outcome = zindex;
            z.nodeIndex = 101;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][15]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][15]);
            z = outcomes[++zindex];
            nodes[102].father = 9;

            nodes[102].terminal = true;
            nodes[102].outcome = zindex;
            z.nodeIndex = 102;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][16]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][16]);
            z = outcomes[++zindex];
            nodes[103].father = 12;

            nodes[103].terminal = true;
            nodes[103].outcome = zindex;
            z.nodeIndex = 103;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][17]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][17]);
            z = outcomes[++zindex];
            nodes[104].father = 13;

            nodes[104].terminal = true;
            nodes[104].outcome = zindex;
            z.nodeIndex = 104;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][18]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][18]);
            z = outcomes[++zindex];
            nodes[105].father = 15;

            nodes[105].terminal = true;
            nodes[105].outcome = zindex;
            z.nodeIndex = 105;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][19]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][19]);
            z = outcomes[++zindex];
            nodes[106].father = 15;

            nodes[106].terminal = true;
            nodes[106].outcome = zindex;
            z.nodeIndex = 106;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][20]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][20]);
            z = outcomes[++zindex];
            nodes[107].father = 15;

            nodes[107].terminal = true;
            nodes[107].outcome = zindex;
            z.nodeIndex = 107;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][21]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][21]);
            z = outcomes[++zindex];
            nodes[108].father = 17;

            nodes[108].terminal = true;
            nodes[108].outcome = zindex;
            z.nodeIndex = 108;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][22]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][22]);
            z = outcomes[++zindex];
            nodes[109].father = 17;

            nodes[109].terminal = true;
            nodes[109].outcome = zindex;
            z.nodeIndex = 109;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][23]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][23]);
            z = outcomes[++zindex];
            nodes[110].father = 17;

            nodes[110].terminal = true;
            nodes[110].outcome = zindex;
            z.nodeIndex = 110;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][24]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][24]);
            z = outcomes[++zindex];
            nodes[111].father = 16;

            nodes[111].terminal = true;
            nodes[111].outcome = zindex;
            z.nodeIndex = 111;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][25]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][25]);
            z = outcomes[++zindex];
            nodes[112].father = 16;

            nodes[112].terminal = true;
            nodes[112].outcome = zindex;
            z.nodeIndex = 112;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][26]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][26]);
            z = outcomes[++zindex];
            nodes[113].father = 19;

            nodes[113].terminal = true;
            nodes[113].outcome = zindex;
            z.nodeIndex = 113;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][27]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][27]);
            z = outcomes[++zindex];
            nodes[114].father = 19;

            nodes[114].terminal = true;
            nodes[114].outcome = zindex;
            z.nodeIndex = 114;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][28]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][28]);
            z = outcomes[++zindex];
            nodes[115].father = 19;

            nodes[115].terminal = true;
            nodes[115].outcome = zindex;
            z.nodeIndex = 115;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][29]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][29]);
            z = outcomes[++zindex];
            nodes[116].father = 20;

            nodes[116].terminal = true;
            nodes[116].outcome = zindex;
            z.nodeIndex = 116;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][30]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][30]);
            z = outcomes[++zindex];
            nodes[117].father = 20;

            nodes[117].terminal = true;
            nodes[117].outcome = zindex;
            z.nodeIndex = 117;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][31]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][31]);
            z = outcomes[++zindex];
            nodes[118].father = 20;

            nodes[118].terminal = true;
            nodes[118].outcome = zindex;
            z.nodeIndex = 118;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][32]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][32]);
            z = outcomes[++zindex];
            nodes[119].father = 18;

            nodes[119].terminal = true;
            nodes[119].outcome = zindex;
            z.nodeIndex = 119;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][33]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][33]);
            z = outcomes[++zindex];
            nodes[120].father = 21;

            nodes[120].terminal = true;
            nodes[120].outcome = zindex;
            z.nodeIndex = 120;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][34]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][34]);
            z = outcomes[++zindex];
            nodes[121].father = 22;

            nodes[121].terminal = true;
            nodes[121].outcome = zindex;
            z.nodeIndex = 121;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][35]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][35]);
            z = outcomes[++zindex];
            nodes[122].father = 24;

            nodes[122].terminal = true;
            nodes[122].outcome = zindex;
            z.nodeIndex = 122;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][36]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][36]);
            z = outcomes[++zindex];
            nodes[123].father = 24;

            nodes[123].terminal = true;
            nodes[123].outcome = zindex;
            z.nodeIndex = 123;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][37]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][37]);
            z = outcomes[++zindex];
            nodes[124].father = 24;

            nodes[124].terminal = true;
            nodes[124].outcome = zindex;
            z.nodeIndex = 124;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][38]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][38]);
            z = outcomes[++zindex];
            nodes[125].father = 26;

            nodes[125].terminal = true;
            nodes[125].outcome = zindex;
            z.nodeIndex = 125;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][39]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][39]);
            z = outcomes[++zindex];
            nodes[126].father = 26;

            nodes[126].terminal = true;
            nodes[126].outcome = zindex;
            z.nodeIndex = 126;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][40]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][40]);
            z = outcomes[++zindex];
            nodes[127].father = 26;

            nodes[127].terminal = true;
            nodes[127].outcome = zindex;
            z.nodeIndex = 127;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][41]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][41]);
            z = outcomes[++zindex];
            nodes[128].father = 25;

            nodes[128].terminal = true;
            nodes[128].outcome = zindex;
            z.nodeIndex = 128;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][42]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][42]);
            z = outcomes[++zindex];
            nodes[129].father = 25;

            nodes[129].terminal = true;
            nodes[129].outcome = zindex;
            z.nodeIndex = 129;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][43]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][43]);
            z = outcomes[++zindex];
            nodes[130].father = 28;

            nodes[130].terminal = true;
            nodes[130].outcome = zindex;
            z.nodeIndex = 130;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][44]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][44]);
            z = outcomes[++zindex];
            nodes[131].father = 28;

            nodes[131].terminal = true;
            nodes[131].outcome = zindex;
            z.nodeIndex = 131;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][45]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][45]);
            z = outcomes[++zindex];
            nodes[132].father = 28;

            nodes[132].terminal = true;
            nodes[132].outcome = zindex;
            z.nodeIndex = 132;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][46]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][46]);
            z = outcomes[++zindex];
            nodes[133].father = 29;

            nodes[133].terminal = true;
            nodes[133].outcome = zindex;
            z.nodeIndex = 133;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][47]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][47]);
            z = outcomes[++zindex];
            nodes[134].father = 29;

            nodes[134].terminal = true;
            nodes[134].outcome = zindex;
            z.nodeIndex = 134;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][48]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][48]);
            z = outcomes[++zindex];
            nodes[135].father = 29;

            nodes[135].terminal = true;
            nodes[135].outcome = zindex;
            z.nodeIndex = 135;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][49]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][49]);
            z = outcomes[++zindex];
            nodes[136].father = 27;

            nodes[136].terminal = true;
            nodes[136].outcome = zindex;
            z.nodeIndex = 136;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][50]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][50]);
            z = outcomes[++zindex];
            nodes[137].father = 31;

            nodes[137].terminal = true;
            nodes[137].outcome = zindex;
            z.nodeIndex = 137;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][51]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][51]);
            z = outcomes[++zindex];
            nodes[138].father = 32;

            nodes[138].terminal = true;
            nodes[138].outcome = zindex;
            z.nodeIndex = 138;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][52]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][52]);
            z = outcomes[++zindex];
            nodes[139].father = 34;

            nodes[139].terminal = true;
            nodes[139].outcome = zindex;
            z.nodeIndex = 139;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][53]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][53]);
            z = outcomes[++zindex];
            nodes[140].father = 34;

            nodes[140].terminal = true;
            nodes[140].outcome = zindex;
            z.nodeIndex = 140;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][54]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][54]);
            z = outcomes[++zindex];
            nodes[141].father = 34;

            nodes[141].terminal = true;
            nodes[141].outcome = zindex;
            z.nodeIndex = 141;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][55]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][55]);
            z = outcomes[++zindex];
            nodes[142].father = 36;

            nodes[142].terminal = true;
            nodes[142].outcome = zindex;
            z.nodeIndex = 142;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][56]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][56]);
            z = outcomes[++zindex];
            nodes[143].father = 36;

            nodes[143].terminal = true;
            nodes[143].outcome = zindex;
            z.nodeIndex = 143;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][57]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][57]);
            z = outcomes[++zindex];
            nodes[144].father = 36;

            nodes[144].terminal = true;
            nodes[144].outcome = zindex;
            z.nodeIndex = 144;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][58]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][58]);
            z = outcomes[++zindex];
            nodes[145].father = 35;

            nodes[145].terminal = true;
            nodes[145].outcome = zindex;
            z.nodeIndex = 145;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][59]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][59]);
            z = outcomes[++zindex];
            nodes[146].father = 35;

            nodes[146].terminal = true;
            nodes[146].outcome = zindex;
            z.nodeIndex = 146;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][60]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][60]);
            z = outcomes[++zindex];
            nodes[147].father = 38;

            nodes[147].terminal = true;
            nodes[147].outcome = zindex;
            z.nodeIndex = 147;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][61]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][61]);
            z = outcomes[++zindex];
            nodes[148].father = 38;

            nodes[148].terminal = true;
            nodes[148].outcome = zindex;
            z.nodeIndex = 148;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][62]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][62]);
            z = outcomes[++zindex];
            nodes[149].father = 38;

            nodes[149].terminal = true;
            nodes[149].outcome = zindex;
            z.nodeIndex = 149;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][63]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][63]);
            z = outcomes[++zindex];
            nodes[150].father = 39;

            nodes[150].terminal = true;
            nodes[150].outcome = zindex;
            z.nodeIndex = 150;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][64]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][64]);
            z = outcomes[++zindex];
            nodes[151].father = 39;

            nodes[151].terminal = true;
            nodes[151].outcome = zindex;
            z.nodeIndex = 151;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][65]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][65]);
            z = outcomes[++zindex];
            nodes[152].father = 39;

            nodes[152].terminal = true;
            nodes[152].outcome = zindex;
            z.nodeIndex = 152;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][66]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][66]);
            z = outcomes[++zindex];
            nodes[153].father = 37;

            nodes[153].terminal = true;
            nodes[153].outcome = zindex;
            z.nodeIndex = 153;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][67]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][67]);
            z = outcomes[++zindex];
            nodes[154].father = 40;

            nodes[154].terminal = true;
            nodes[154].outcome = zindex;
            z.nodeIndex = 154;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][68]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][68]);
            z = outcomes[++zindex];
            nodes[155].father = 41;

            nodes[155].terminal = true;
            nodes[155].outcome = zindex;
            z.nodeIndex = 155;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][69]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][69]);
            z = outcomes[++zindex];
            nodes[156].father = 43;

            nodes[156].terminal = true;
            nodes[156].outcome = zindex;
            z.nodeIndex = 156;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][70]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][70]);
            z = outcomes[++zindex];
            nodes[157].father = 43;

            nodes[157].terminal = true;
            nodes[157].outcome = zindex;
            z.nodeIndex = 157;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][71]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][71]);
            z = outcomes[++zindex];
            nodes[158].father = 43;

            nodes[158].terminal = true;
            nodes[158].outcome = zindex;
            z.nodeIndex = 158;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][72]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][72]);
            z = outcomes[++zindex];
            nodes[159].father = 45;

            nodes[159].terminal = true;
            nodes[159].outcome = zindex;
            z.nodeIndex = 159;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][73]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][73]);
            z = outcomes[++zindex];
            nodes[160].father = 45;

            nodes[160].terminal = true;
            nodes[160].outcome = zindex;
            z.nodeIndex = 160;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][74]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][74]);
            z = outcomes[++zindex];
            nodes[161].father = 45;

            nodes[161].terminal = true;
            nodes[161].outcome = zindex;
            z.nodeIndex = 161;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][75]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][75]);
            z = outcomes[++zindex];
            nodes[162].father = 44;

            nodes[162].terminal = true;
            nodes[162].outcome = zindex;
            z.nodeIndex = 162;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][76]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][76]);
            z = outcomes[++zindex];
            nodes[163].father = 44;

            nodes[163].terminal = true;
            nodes[163].outcome = zindex;
            z.nodeIndex = 163;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][77]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][77]);
            z = outcomes[++zindex];
            nodes[164].father = 47;

            nodes[164].terminal = true;
            nodes[164].outcome = zindex;
            z.nodeIndex = 164;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][78]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][78]);
            z = outcomes[++zindex];
            nodes[165].father = 47;

            nodes[165].terminal = true;
            nodes[165].outcome = zindex;
            z.nodeIndex = 165;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][79]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][79]);
            z = outcomes[++zindex];
            nodes[166].father = 47;

            nodes[166].terminal = true;
            nodes[166].outcome = zindex;
            z.nodeIndex = 166;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][80]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][80]);
            z = outcomes[++zindex];
            nodes[167].father = 48;

            nodes[167].terminal = true;
            nodes[167].outcome = zindex;
            z.nodeIndex = 167;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][81]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][81]);
            z = outcomes[++zindex];
            nodes[168].father = 48;

            nodes[168].terminal = true;
            nodes[168].outcome = zindex;
            z.nodeIndex = 168;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][82]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][82]);
            z = outcomes[++zindex];
            nodes[169].father = 48;

            nodes[169].terminal = true;
            nodes[169].outcome = zindex;
            z.nodeIndex = 169;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][83]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][83]);
            z = outcomes[++zindex];
            nodes[170].father = 46;

            nodes[170].terminal = true;
            nodes[170].outcome = zindex;
            z.nodeIndex = 170;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][84]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][84]);
            z = outcomes[++zindex];
            nodes[171].father = 49;

            nodes[171].terminal = true;
            nodes[171].outcome = zindex;
            z.nodeIndex = 171;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][85]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][85]);
            z = outcomes[++zindex];
            nodes[172].father = 50;

            nodes[172].terminal = true;
            nodes[172].outcome = zindex;
            z.nodeIndex = 172;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][86]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][86]);
            z = outcomes[++zindex];
            nodes[173].father = 52;

            nodes[173].terminal = true;
            nodes[173].outcome = zindex;
            z.nodeIndex = 173;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][87]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][87]);
            z = outcomes[++zindex];
            nodes[174].father = 52;

            nodes[174].terminal = true;
            nodes[174].outcome = zindex;
            z.nodeIndex = 174;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][88]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][88]);
            z = outcomes[++zindex];
            nodes[175].father = 52;

            nodes[175].terminal = true;
            nodes[175].outcome = zindex;
            z.nodeIndex = 175;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][89]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][89]);
            z = outcomes[++zindex];
            nodes[176].father = 54;

            nodes[176].terminal = true;
            nodes[176].outcome = zindex;
            z.nodeIndex = 176;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][90]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][90]);
            z = outcomes[++zindex];
            nodes[177].father = 54;

            nodes[177].terminal = true;
            nodes[177].outcome = zindex;
            z.nodeIndex = 177;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][91]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][91]);
            z = outcomes[++zindex];
            nodes[178].father = 54;

            nodes[178].terminal = true;
            nodes[178].outcome = zindex;
            z.nodeIndex = 178;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][92]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][92]);
            z = outcomes[++zindex];
            nodes[179].father = 53;

            nodes[179].terminal = true;
            nodes[179].outcome = zindex;
            z.nodeIndex = 179;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][93]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][93]);
            z = outcomes[++zindex];
            nodes[180].father = 53;

            nodes[180].terminal = true;
            nodes[180].outcome = zindex;
            z.nodeIndex = 180;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][94]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][94]);
            z = outcomes[++zindex];
            nodes[181].father = 56;

            nodes[181].terminal = true;
            nodes[181].outcome = zindex;
            z.nodeIndex = 181;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][95]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][95]);
            z = outcomes[++zindex];
            nodes[182].father = 56;

            nodes[182].terminal = true;
            nodes[182].outcome = zindex;
            z.nodeIndex = 182;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][96]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][96]);
            z = outcomes[++zindex];
            nodes[183].father = 56;

            nodes[183].terminal = true;
            nodes[183].outcome = zindex;
            z.nodeIndex = 183;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][97]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][97]);
            z = outcomes[++zindex];
            nodes[184].father = 57;

            nodes[184].terminal = true;
            nodes[184].outcome = zindex;
            z.nodeIndex = 184;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][98]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][98]);
            z = outcomes[++zindex];
            nodes[185].father = 57;

            nodes[185].terminal = true;
            nodes[185].outcome = zindex;
            z.nodeIndex = 185;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][99]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][99]);
            z = outcomes[++zindex];
            nodes[186].father = 57;

            nodes[186].terminal = true;
            nodes[186].outcome = zindex;
            z.nodeIndex = 186;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][100]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][100]);
            z = outcomes[++zindex];
            nodes[187].father = 55;

            nodes[187].terminal = true;
            nodes[187].outcome = zindex;
            z.nodeIndex = 187;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][101]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][101]);
            z = outcomes[++zindex];
            nodes[188].father = 59;

            nodes[188].terminal = true;
            nodes[188].outcome = zindex;
            z.nodeIndex = 188;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][102]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][102]);
            z = outcomes[++zindex];
            nodes[189].father = 60;

            nodes[189].terminal = true;
            nodes[189].outcome = zindex;
            z.nodeIndex = 189;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][103]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][103]);
            z = outcomes[++zindex];
            nodes[190].father = 62;

            nodes[190].terminal = true;
            nodes[190].outcome = zindex;
            z.nodeIndex = 190;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][104]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][104]);
            z = outcomes[++zindex];
            nodes[191].father = 62;

            nodes[191].terminal = true;
            nodes[191].outcome = zindex;
            z.nodeIndex = 191;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][105]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][105]);
            z = outcomes[++zindex];
            nodes[192].father = 62;

            nodes[192].terminal = true;
            nodes[192].outcome = zindex;
            z.nodeIndex = 192;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][106]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][106]);
            z = outcomes[++zindex];
            nodes[193].father = 64;

            nodes[193].terminal = true;
            nodes[193].outcome = zindex;
            z.nodeIndex = 193;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][107]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][107]);
            z = outcomes[++zindex];
            nodes[194].father = 64;

            nodes[194].terminal = true;
            nodes[194].outcome = zindex;
            z.nodeIndex = 194;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][108]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][108]);
            z = outcomes[++zindex];
            nodes[195].father = 64;

            nodes[195].terminal = true;
            nodes[195].outcome = zindex;
            z.nodeIndex = 195;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][109]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][109]);
            z = outcomes[++zindex];
            nodes[196].father = 63;

            nodes[196].terminal = true;
            nodes[196].outcome = zindex;
            z.nodeIndex = 196;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][110]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][110]);
            z = outcomes[++zindex];
            nodes[197].father = 63;

            nodes[197].terminal = true;
            nodes[197].outcome = zindex;
            z.nodeIndex = 197;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][111]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][111]);
            z = outcomes[++zindex];
            nodes[198].father = 66;

            nodes[198].terminal = true;
            nodes[198].outcome = zindex;
            z.nodeIndex = 198;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][112]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][112]);
            z = outcomes[++zindex];
            nodes[199].father = 66;

            nodes[199].terminal = true;
            nodes[199].outcome = zindex;
            z.nodeIndex = 199;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][113]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][113]);
            z = outcomes[++zindex];
            nodes[200].father = 66;

            nodes[200].terminal = true;
            nodes[200].outcome = zindex;
            z.nodeIndex = 200;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][114]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][114]);
            z = outcomes[++zindex];
            nodes[201].father = 67;

            nodes[201].terminal = true;
            nodes[201].outcome = zindex;
            z.nodeIndex = 201;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][115]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][115]);
            z = outcomes[++zindex];
            nodes[202].father = 67;

            nodes[202].terminal = true;
            nodes[202].outcome = zindex;
            z.nodeIndex = 202;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][116]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][116]);
            z = outcomes[++zindex];
            nodes[203].father = 67;

            nodes[203].terminal = true;
            nodes[203].outcome = zindex;
            z.nodeIndex = 203;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][117]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][117]);
            z = outcomes[++zindex];
            nodes[204].father = 65;

            nodes[204].terminal = true;
            nodes[204].outcome = zindex;
            z.nodeIndex = 204;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][118]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][118]);
            z = outcomes[++zindex];
            nodes[205].father = 68;

            nodes[205].terminal = true;
            nodes[205].outcome = zindex;
            z.nodeIndex = 205;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][119]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][119]);
            z = outcomes[++zindex];
            nodes[206].father = 69;

            nodes[206].terminal = true;
            nodes[206].outcome = zindex;
            z.nodeIndex = 206;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][120]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][120]);
            z = outcomes[++zindex];
            nodes[207].father = 71;

            nodes[207].terminal = true;
            nodes[207].outcome = zindex;
            z.nodeIndex = 207;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][121]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][121]);
            z = outcomes[++zindex];
            nodes[208].father = 71;

            nodes[208].terminal = true;
            nodes[208].outcome = zindex;
            z.nodeIndex = 208;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][122]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][122]);
            z = outcomes[++zindex];
            nodes[209].father = 71;

            nodes[209].terminal = true;
            nodes[209].outcome = zindex;
            z.nodeIndex = 209;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][123]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][123]);
            z = outcomes[++zindex];
            nodes[210].father = 73;

            nodes[210].terminal = true;
            nodes[210].outcome = zindex;
            z.nodeIndex = 210;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][124]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][124]);
            z = outcomes[++zindex];
            nodes[211].father = 73;

            nodes[211].terminal = true;
            nodes[211].outcome = zindex;
            z.nodeIndex = 211;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][125]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][125]);
            z = outcomes[++zindex];
            nodes[212].father = 73;

            nodes[212].terminal = true;
            nodes[212].outcome = zindex;
            z.nodeIndex = 212;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][126]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][126]);
            z = outcomes[++zindex];
            nodes[213].father = 72;

            nodes[213].terminal = true;
            nodes[213].outcome = zindex;
            z.nodeIndex = 213;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][127]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][127]);
            z = outcomes[++zindex];
            nodes[214].father = 72;

            nodes[214].terminal = true;
            nodes[214].outcome = zindex;
            z.nodeIndex = 214;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][128]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][128]);
            z = outcomes[++zindex];
            nodes[215].father = 75;

            nodes[215].terminal = true;
            nodes[215].outcome = zindex;
            z.nodeIndex = 215;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][129]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][129]);
            z = outcomes[++zindex];
            nodes[216].father = 75;

            nodes[216].terminal = true;
            nodes[216].outcome = zindex;
            z.nodeIndex = 216;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][130]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][130]);
            z = outcomes[++zindex];
            nodes[217].father = 75;

            nodes[217].terminal = true;
            nodes[217].outcome = zindex;
            z.nodeIndex = 217;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][131]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][131]);
            z = outcomes[++zindex];
            nodes[218].father = 76;

            nodes[218].terminal = true;
            nodes[218].outcome = zindex;
            z.nodeIndex = 218;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][132]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][132]);
            z = outcomes[++zindex];
            nodes[219].father = 76;

            nodes[219].terminal = true;
            nodes[219].outcome = zindex;
            z.nodeIndex = 219;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][133]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][133]);
            z = outcomes[++zindex];
            nodes[220].father = 76;

            nodes[220].terminal = true;
            nodes[220].outcome = zindex;
            z.nodeIndex = 220;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][134]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][134]);
            z = outcomes[++zindex];
            nodes[221].father = 74;

            nodes[221].terminal = true;
            nodes[221].outcome = zindex;
            z.nodeIndex = 221;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][135]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][135]);
            z = outcomes[++zindex];
            nodes[222].father = 77;

            nodes[222].terminal = true;
            nodes[222].outcome = zindex;
            z.nodeIndex = 222;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][136]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][136]);
            z = outcomes[++zindex];
            nodes[223].father = 78;

            nodes[223].terminal = true;
            nodes[223].outcome = zindex;
            z.nodeIndex = 223;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][137]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][137]);
            z = outcomes[++zindex];
            nodes[224].father = 80;

            nodes[224].terminal = true;
            nodes[224].outcome = zindex;
            z.nodeIndex = 224;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][138]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][138]);
            z = outcomes[++zindex];
            nodes[225].father = 80;

            nodes[225].terminal = true;
            nodes[225].outcome = zindex;
            z.nodeIndex = 225;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][139]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][139]);
            z = outcomes[++zindex];
            nodes[226].father = 80;

            nodes[226].terminal = true;
            nodes[226].outcome = zindex;
            z.nodeIndex = 226;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][140]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][140]);
            z = outcomes[++zindex];
            nodes[227].father = 82;

            nodes[227].terminal = true;
            nodes[227].outcome = zindex;
            z.nodeIndex = 227;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][141]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][141]);
            z = outcomes[++zindex];
            nodes[228].father = 82;

            nodes[228].terminal = true;
            nodes[228].outcome = zindex;
            z.nodeIndex = 228;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][142]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][142]);
            z = outcomes[++zindex];
            nodes[229].father = 82;

            nodes[229].terminal = true;
            nodes[229].outcome = zindex;
            z.nodeIndex = 229;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][143]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][143]);
            z = outcomes[++zindex];
            nodes[230].father = 81;

            nodes[230].terminal = true;
            nodes[230].outcome = zindex;
            z.nodeIndex = 230;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][144]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][144]);
            z = outcomes[++zindex];
            nodes[231].father = 81;

            nodes[231].terminal = true;
            nodes[231].outcome = zindex;
            z.nodeIndex = 231;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][145]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][145]);
            z = outcomes[++zindex];
            nodes[232].father = 84;

            nodes[232].terminal = true;
            nodes[232].outcome = zindex;
            z.nodeIndex = 232;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][146]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][146]);
            z = outcomes[++zindex];
            nodes[233].father = 84;

            nodes[233].terminal = true;
            nodes[233].outcome = zindex;
            z.nodeIndex = 233;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][147]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][147]);
            z = outcomes[++zindex];
            nodes[234].father = 84;

            nodes[234].terminal = true;
            nodes[234].outcome = zindex;
            z.nodeIndex = 234;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][148]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][148]);
            z = outcomes[++zindex];
            nodes[235].father = 85;

            nodes[235].terminal = true;
            nodes[235].outcome = zindex;
            z.nodeIndex = 235;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][149]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][149]);
            z = outcomes[++zindex];
            nodes[236].father = 85;

            nodes[236].terminal = true;
            nodes[236].outcome = zindex;
            z.nodeIndex = 236;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][150]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][150]);
            z = outcomes[++zindex];
            nodes[237].father = 85;

            nodes[237].terminal = true;
            nodes[237].outcome = zindex;
            z.nodeIndex = 237;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][151]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][151]);
            z = outcomes[++zindex];
            nodes[238].father = 83;

            nodes[238].terminal = true;
            nodes[238].outcome = zindex;
            z.nodeIndex = 238;
            z.pay[0] = MaybeExact<T>.FromInteger(pay[0][152]);
            z.pay[1] = MaybeExact<T>.FromInteger(pay[1][152]);
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
            nodes[2].moveAtFather = 1;
            nodes[3].moveAtFather = 4;
            nodes[4].moveAtFather = 12;
            nodes[5].moveAtFather = 28;
            nodes[6].moveAtFather = 13;
            nodes[7].moveAtFather = 14;
            nodes[8].moveAtFather = 29;
            nodes[9].moveAtFather = 15;
            nodes[10].moveAtFather = 29;
            nodes[11].moveAtFather = 30;
            nodes[12].moveAtFather = 5;
            nodes[13].moveAtFather = 12;
            nodes[14].moveAtFather = 33;
            nodes[15].moveAtFather = 13;
            nodes[16].moveAtFather = 14;
            nodes[17].moveAtFather = 34;
            nodes[18].moveAtFather = 15;
            nodes[19].moveAtFather = 34;
            nodes[20].moveAtFather = 35;
            nodes[21].moveAtFather = 6;
            nodes[22].moveAtFather = 12;
            nodes[23].moveAtFather = 38;
            nodes[24].moveAtFather = 13;
            nodes[25].moveAtFather = 14;
            nodes[26].moveAtFather = 39;
            nodes[27].moveAtFather = 15;
            nodes[28].moveAtFather = 39;
            nodes[29].moveAtFather = 40;
            nodes[30].moveAtFather = 2;
            nodes[31].moveAtFather = 4;
            nodes[32].moveAtFather = 17;
            nodes[33].moveAtFather = 28;
            nodes[34].moveAtFather = 18;
            nodes[35].moveAtFather = 19;
            nodes[36].moveAtFather = 29;
            nodes[37].moveAtFather = 20;
            nodes[38].moveAtFather = 29;
            nodes[39].moveAtFather = 30;
            nodes[40].moveAtFather = 5;
            nodes[41].moveAtFather = 17;
            nodes[42].moveAtFather = 33;
            nodes[43].moveAtFather = 18;
            nodes[44].moveAtFather = 19;
            nodes[45].moveAtFather = 34;
            nodes[46].moveAtFather = 20;
            nodes[47].moveAtFather = 34;
            nodes[48].moveAtFather = 35;
            nodes[49].moveAtFather = 6;
            nodes[50].moveAtFather = 17;
            nodes[51].moveAtFather = 38;
            nodes[52].moveAtFather = 18;
            nodes[53].moveAtFather = 19;
            nodes[54].moveAtFather = 39;
            nodes[55].moveAtFather = 20;
            nodes[56].moveAtFather = 39;
            nodes[57].moveAtFather = 40;
            nodes[58].moveAtFather = 3;
            nodes[59].moveAtFather = 4;
            nodes[60].moveAtFather = 22;
            nodes[61].moveAtFather = 28;
            nodes[62].moveAtFather = 23;
            nodes[63].moveAtFather = 24;
            nodes[64].moveAtFather = 29;
            nodes[65].moveAtFather = 25;
            nodes[66].moveAtFather = 29;
            nodes[67].moveAtFather = 30;
            nodes[68].moveAtFather = 5;
            nodes[69].moveAtFather = 22;
            nodes[70].moveAtFather = 33;
            nodes[71].moveAtFather = 23;
            nodes[72].moveAtFather = 24;
            nodes[73].moveAtFather = 34;
            nodes[74].moveAtFather = 25;
            nodes[75].moveAtFather = 34;
            nodes[76].moveAtFather = 35;
            nodes[77].moveAtFather = 6;
            nodes[78].moveAtFather = 22;
            nodes[79].moveAtFather = 38;
            nodes[80].moveAtFather = 23;
            nodes[81].moveAtFather = 24;
            nodes[82].moveAtFather = 39;
            nodes[83].moveAtFather = 25;
            nodes[84].moveAtFather = 39;
            nodes[85].moveAtFather = 40;
            nodes[86].moveAtFather = 11;
            nodes[87].moveAtFather = 27;
            nodes[88].moveAtFather = 29;
            nodes[89].moveAtFather = 30;
            nodes[90].moveAtFather = 31;
            nodes[91].moveAtFather = 7;
            nodes[92].moveAtFather = 8;
            nodes[93].moveAtFather = 9;
            nodes[94].moveAtFather = 30;
            nodes[95].moveAtFather = 31;
            nodes[96].moveAtFather = 7;
            nodes[97].moveAtFather = 8;
            nodes[98].moveAtFather = 9;
            nodes[99].moveAtFather = 7;
            nodes[100].moveAtFather = 8;
            nodes[101].moveAtFather = 9;
            nodes[102].moveAtFather = 31;
            nodes[103].moveAtFather = 11;
            nodes[104].moveAtFather = 32;
            nodes[105].moveAtFather = 34;
            nodes[106].moveAtFather = 35;
            nodes[107].moveAtFather = 36;
            nodes[108].moveAtFather = 7;
            nodes[109].moveAtFather = 8;
            nodes[110].moveAtFather = 9;
            nodes[111].moveAtFather = 35;
            nodes[112].moveAtFather = 36;
            nodes[113].moveAtFather = 7;
            nodes[114].moveAtFather = 8;
            nodes[115].moveAtFather = 9;
            nodes[116].moveAtFather = 7;
            nodes[117].moveAtFather = 8;
            nodes[118].moveAtFather = 9;
            nodes[119].moveAtFather = 36;
            nodes[120].moveAtFather = 11;
            nodes[121].moveAtFather = 37;
            nodes[122].moveAtFather = 39;
            nodes[123].moveAtFather = 40;
            nodes[124].moveAtFather = 41;
            nodes[125].moveAtFather = 7;
            nodes[126].moveAtFather = 8;
            nodes[127].moveAtFather = 9;
            nodes[128].moveAtFather = 40;
            nodes[129].moveAtFather = 41;
            nodes[130].moveAtFather = 7;
            nodes[131].moveAtFather = 8;
            nodes[132].moveAtFather = 9;
            nodes[133].moveAtFather = 7;
            nodes[134].moveAtFather = 8;
            nodes[135].moveAtFather = 9;
            nodes[136].moveAtFather = 41;
            nodes[137].moveAtFather = 16;
            nodes[138].moveAtFather = 27;
            nodes[139].moveAtFather = 29;
            nodes[140].moveAtFather = 30;
            nodes[141].moveAtFather = 31;
            nodes[142].moveAtFather = 7;
            nodes[143].moveAtFather = 8;
            nodes[144].moveAtFather = 9;
            nodes[145].moveAtFather = 30;
            nodes[146].moveAtFather = 31;
            nodes[147].moveAtFather = 7;
            nodes[148].moveAtFather = 8;
            nodes[149].moveAtFather = 9;
            nodes[150].moveAtFather = 7;
            nodes[151].moveAtFather = 8;
            nodes[152].moveAtFather = 9;
            nodes[153].moveAtFather = 31;
            nodes[154].moveAtFather = 16;
            nodes[155].moveAtFather = 32;
            nodes[156].moveAtFather = 34;
            nodes[157].moveAtFather = 35;
            nodes[158].moveAtFather = 36;
            nodes[159].moveAtFather = 7;
            nodes[160].moveAtFather = 8;
            nodes[161].moveAtFather = 9;
            nodes[162].moveAtFather = 35;
            nodes[163].moveAtFather = 36;
            nodes[164].moveAtFather = 7;
            nodes[165].moveAtFather = 8;
            nodes[166].moveAtFather = 9;
            nodes[167].moveAtFather = 7;
            nodes[168].moveAtFather = 8;
            nodes[169].moveAtFather = 9;
            nodes[170].moveAtFather = 36;
            nodes[171].moveAtFather = 16;
            nodes[172].moveAtFather = 37;
            nodes[173].moveAtFather = 39;
            nodes[174].moveAtFather = 40;
            nodes[175].moveAtFather = 41;
            nodes[176].moveAtFather = 7;
            nodes[177].moveAtFather = 8;
            nodes[178].moveAtFather = 9;
            nodes[179].moveAtFather = 40;
            nodes[180].moveAtFather = 41;
            nodes[181].moveAtFather = 7;
            nodes[182].moveAtFather = 8;
            nodes[183].moveAtFather = 9;
            nodes[184].moveAtFather = 7;
            nodes[185].moveAtFather = 8;
            nodes[186].moveAtFather = 9;
            nodes[187].moveAtFather = 41;
            nodes[188].moveAtFather = 21;
            nodes[189].moveAtFather = 27;
            nodes[190].moveAtFather = 29;
            nodes[191].moveAtFather = 30;
            nodes[192].moveAtFather = 31;
            nodes[193].moveAtFather = 7;
            nodes[194].moveAtFather = 8;
            nodes[195].moveAtFather = 9;
            nodes[196].moveAtFather = 30;
            nodes[197].moveAtFather = 31;
            nodes[198].moveAtFather = 7;
            nodes[199].moveAtFather = 8;
            nodes[200].moveAtFather = 9;
            nodes[201].moveAtFather = 7;
            nodes[202].moveAtFather = 8;
            nodes[203].moveAtFather = 9;
            nodes[204].moveAtFather = 31;
            nodes[205].moveAtFather = 21;
            nodes[206].moveAtFather = 32;
            nodes[207].moveAtFather = 34;
            nodes[208].moveAtFather = 35;
            nodes[209].moveAtFather = 36;
            nodes[210].moveAtFather = 7;
            nodes[211].moveAtFather = 8;
            nodes[212].moveAtFather = 9;
            nodes[213].moveAtFather = 35;
            nodes[214].moveAtFather = 36;
            nodes[215].moveAtFather = 7;
            nodes[216].moveAtFather = 8;
            nodes[217].moveAtFather = 9;
            nodes[218].moveAtFather = 7;
            nodes[219].moveAtFather = 8;
            nodes[220].moveAtFather = 9;
            nodes[221].moveAtFather = 36;
            nodes[222].moveAtFather = 21;
            nodes[223].moveAtFather = 37;
            nodes[224].moveAtFather = 39;
            nodes[225].moveAtFather = 40;
            nodes[226].moveAtFather = 41;
            nodes[227].moveAtFather = 7;
            nodes[228].moveAtFather = 8;
            nodes[229].moveAtFather = 9;
            nodes[230].moveAtFather = 40;
            nodes[231].moveAtFather = 41;
            nodes[232].moveAtFather = 7;
            nodes[233].moveAtFather = 8;
            nodes[234].moveAtFather = 9;
            nodes[235].moveAtFather = 7;
            nodes[236].moveAtFather = 8;
            nodes[237].moveAtFather = 9;
            nodes[238].moveAtFather = 41;
            informationSets[0].playerIndex = 0;
            informationSets[0].firstMoveIndex = 1;
            informationSets[0].numMoves = 3;
            informationSets[1].playerIndex = 0;
            informationSets[1].firstMoveIndex = 4;
            informationSets[1].numMoves = 3;
            informationSets[2].playerIndex = 0;
            informationSets[2].firstMoveIndex = 7;
            informationSets[2].numMoves = 3;
            informationSets[3].playerIndex = 1;
            informationSets[3].firstMoveIndex = 11;
            informationSets[3].numMoves = 2;
            informationSets[4].playerIndex = 1;
            informationSets[4].firstMoveIndex = 13;
            informationSets[4].numMoves = 3;
            informationSets[5].playerIndex = 1;
            informationSets[5].firstMoveIndex = 16;
            informationSets[5].numMoves = 2;
            informationSets[6].playerIndex = 1;
            informationSets[6].firstMoveIndex = 18;
            informationSets[6].numMoves = 3;
            informationSets[7].playerIndex = 1;
            informationSets[7].firstMoveIndex = 21;
            informationSets[7].numMoves = 2;
            informationSets[8].playerIndex = 1;
            informationSets[8].firstMoveIndex = 23;
            informationSets[8].numMoves = 3;
            informationSets[9].playerIndex = 2;
            informationSets[9].firstMoveIndex = 27;
            informationSets[9].numMoves = 2;
            informationSets[10].playerIndex = 2;
            informationSets[10].firstMoveIndex = 29;
            informationSets[10].numMoves = 3;
            informationSets[11].playerIndex = 2;
            informationSets[11].firstMoveIndex = 32;
            informationSets[11].numMoves = 2;
            informationSets[12].playerIndex = 2;
            informationSets[12].firstMoveIndex = 34;
            informationSets[12].numMoves = 3;
            informationSets[13].playerIndex = 2;
            informationSets[13].firstMoveIndex = 37;
            informationSets[13].numMoves = 2;
            informationSets[14].playerIndex = 2;
            informationSets[14].firstMoveIndex = 39;
            informationSets[14].numMoves = 3;
            // move 0 is empty sequence for player 0
            moves[1].priorInformationSet = 0;
            moves[1].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[2].priorInformationSet = 0;
            moves[2].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[3].priorInformationSet = 0;
            moves[3].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[4].priorInformationSet = 1;
            moves[4].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[5].priorInformationSet = 1;
            moves[5].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[6].priorInformationSet = 1;
            moves[6].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[7].priorInformationSet = 2;
            moves[7].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[8].priorInformationSet = 2;
            moves[8].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            moves[9].priorInformationSet = 2;
            moves[9].behavioralProbability = MaybeExact<T>.One().DividedBy(MaybeExact<T>.FromInteger(3));
            // move 10 is empty sequence for player 1
            moves[11].priorInformationSet = 3;
            moves[12].priorInformationSet = 3;
            moves[13].priorInformationSet = 4;
            moves[14].priorInformationSet = 4;
            moves[15].priorInformationSet = 4;
            moves[16].priorInformationSet = 5;
            moves[17].priorInformationSet = 5;
            moves[18].priorInformationSet = 6;
            moves[19].priorInformationSet = 6;
            moves[20].priorInformationSet = 6;
            moves[21].priorInformationSet = 7;
            moves[22].priorInformationSet = 7;
            moves[23].priorInformationSet = 8;
            moves[24].priorInformationSet = 8;
            moves[25].priorInformationSet = 8;
            // move 26 is empty sequence for player 2
            moves[27].priorInformationSet = 9;
            moves[28].priorInformationSet = 9;
            moves[29].priorInformationSet = 10;
            moves[30].priorInformationSet = 10;
            moves[31].priorInformationSet = 10;
            moves[32].priorInformationSet = 11;
            moves[33].priorInformationSet = 11;
            moves[34].priorInformationSet = 12;
            moves[35].priorInformationSet = 12;
            moves[36].priorInformationSet = 12;
            moves[37].priorInformationSet = 13;
            moves[38].priorInformationSet = 13;
            moves[39].priorInformationSet = 14;
            moves[40].priorInformationSet = 14;
            moves[41].priorInformationSet = 14;

        }

    }
}

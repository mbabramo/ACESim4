using ACESim;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimTest
{
    [TestClass]
    public class LeducTests
    {
        // TODO: Add test for version with bets of 1-5. For each combination, we need to make a separate entry for each permutation of bets.

        public class DecisionRecord
        {
            public LeducGameDecisions decision;
            public byte action;
            public bool fold;
            public int bet;

            public DecisionRecord(LeducGameDecisions d, byte a, bool f, int b)
            {
                decision = d;
                action = a;
                fold = f;
                bet = b;
            }

            public DecisionRecord DeepCopied => new DecisionRecord(decision, action, fold, bet);

            public DecisionRecord WithBetDoubled => new DecisionRecord(decision, action, fold, bet * 2);

            public override string ToString()
            {
                return
                    $"{decision} action:{action} fold?{fold} bet{bet}";
            }
        }

        private List<List<DecisionRecord>> PlayerDecisions = new List<List<DecisionRecord>>()
        {
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Decision, (byte)LeducPlayerChoice.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1ResponseBetsExcluded, (byte)LeducPlayerChoice.CallOrCheck, false, 0)
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Decision, (byte)LeducPlayerChoice.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1ResponseBetsExcluded, (byte)LeducPlayerChoice.Fold, true, -1)
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Decision, (byte)LeducPlayerChoice.CallOrCheck, false, 0),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Decision, (byte)LeducPlayerChoice.Fold, true, -1),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
                new DecisionRecord(LeducGameDecisions.P2DecisionFoldExcluded, (byte)LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1Response, (byte)LeducPlayerChoice.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Response, (byte)LeducPlayerChoice.CallOrCheck, false, 0),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
                new DecisionRecord(LeducGameDecisions.P2DecisionFoldExcluded, (byte)LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1Response, (byte)LeducPlayerChoice.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P2Response, (byte)LeducPlayerChoice.Fold, true, -1),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
                new DecisionRecord(LeducGameDecisions.P2DecisionFoldExcluded, (byte)LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1Response, (byte)LeducPlayerChoice.CallOrCheck, false, 0),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
                new DecisionRecord(LeducGameDecisions.P2DecisionFoldExcluded, (byte)LeducPlayerChoiceWithoutFold.BetOrRaise01, false, 1),
                new DecisionRecord(LeducGameDecisions.P1Response, (byte)LeducPlayerChoice.Fold, true, -1),
            },
            new List<DecisionRecord>()
            {
                new DecisionRecord(LeducGameDecisions.P1Decision, (byte) LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
                new DecisionRecord(LeducGameDecisions.P2DecisionFoldExcluded, (byte)LeducPlayerChoiceWithoutFold.CallOrCheck, false, 0),
            },
        };

        private List<List<DecisionRecord>> ChanceDecisions()
        {
            List<List<DecisionRecord>> list = new List<List<DecisionRecord>>();
            for (int p1 = 1; p1 <= 3; p1++)
                for (int p2 = 1; p2 <= 3; p2++)
                    for (int flop = 1; flop <= 3; flop++)
                    {
                        if (p1 == p2 && p2 == flop)
                            continue; // only two of each card
                        List<DecisionRecord> inner = new List<DecisionRecord>();
                        inner.Add(new DecisionRecord(LeducGameDecisions.P1Chance, (byte)p1, false, 1 /* this represents the ante */));
                        inner.Add(new DecisionRecord(LeducGameDecisions.P2Chance, (byte)p2, false, 0));
                        inner.Add(new DecisionRecord(LeducGameDecisions.FlopChance, (byte)flop, false, 0));
                        list.Add(inner);
                    }
            return list;
        }

        private List<List<DecisionRecord>> AllDecisionCombinations()
        {
            List<List<DecisionRecord>> list = new List<List<DecisionRecord>>();
            foreach (var p in PlayerDecisions)
            {
                foreach (var c in ChanceDecisions())
                {
                    var combined = p.ToList();
                    combined.AddRange(c);
                    if (p.Any(x => x.fold))
                    {
                        combined = combined.Where(x => x.decision != LeducGameDecisions.FlopChance).ToList();
                        list.Add(combined);
                    }
                    else
                    {
                        foreach (var p2 in PlayerDecisions)
                        {
                            var combined2 = combined.ToList();
                            combined2.AddRange(p2.Select(x => x.WithBetDoubled));
                            list.Add(combined2);
                        }
                    }
                }
            }
            return list;
        }

        private bool? GetWinner(List<DecisionRecord> decisions)
        {
            DecisionRecord fold = decisions.SingleOrDefault(x => x.fold);
            if (fold is DecisionRecord fold2)
            {
                if (fold2.decision == LeducGameDecisions.P1Response || fold2.decision == LeducGameDecisions.P1ResponseBetsExcluded)
                    return false; // p1 folded, so p2 wins
                else
                    return true; // p2 folded, so p1 wins
            }
            return WinnerAtShowdown(decisions);
        }

        private bool? WinnerAtShowdown(List<DecisionRecord> decisions)
        {
            int p1 = decisions.Single(x => x.decision == LeducGameDecisions.P1Chance).action;
            int p2 = decisions.Single(x => x.decision == LeducGameDecisions.P2Chance).action;
            int flop = decisions.Single(x => x.decision == LeducGameDecisions.FlopChance).action;
            if (p1 == p2)
                return null;
            if (p1 == flop)
                return true;
            if (p2 == flop)
                return false;
            return p1 > p2;
        }

        byte[] betSizesFirstRound = new byte[] { 1, 2, 4, 8, 16 };
        byte[] betSizesSecondRound = new byte[] { 2, 4, 8, 16, 32 };
        private IEnumerable<List<DecisionRecord>> ReplaceBetWithAllCombinations(List<DecisionRecord> original, int skip = 0)
        {
            var exactCopy = original.Select(x => x.DeepCopied).ToList();
            yield return exactCopy;
            for (int i = 1; i <= 4; i++)
            {
                var mutatedCopy = exactCopy.Select(x => x.DeepCopied).ToList();
                DecisionRecord recordToCopy = mutatedCopy.Skip(skip).FirstOrDefault(x => x.bet != 0 && !(x.decision == LeducGameDecisions.P1Chance));
                if (recordToCopy == null)
                    yield break;
                recordToCopy.action += (byte) i;
                recordToCopy.bet = recordToCopy.bet == 1 ? betSizesFirstRound[i] : betSizesSecondRound[i];
                yield return mutatedCopy;
            }
            foreach (var recursive in ReplaceBetWithAllCombinations(exactCopy, skip + 1))
                yield return recursive;
        }

        private List<DecisionRecord> SetNegativeBetsOnFold(List<DecisionRecord> original)
        {
            var fold = original.SingleOrDefault(x => x.fold);
            if (fold != null)
            {
                var lastBet = original.Last(x => !x.fold && !(x.decision == LeducGameDecisions.P1Chance) && x.bet != 0);
                fold.bet = 0 - lastBet.bet; // undo the effect of the last bet
            }
            return original;
        }

        [TestMethod]
        public void TestOneBetLeduc()
        {
            var permutations = AllDecisionCombinations();
            LeducGameOptions options = new LeducGameOptions()
            {
                OneBetSizeOnly = true
            };
            Helper(permutations, options);
        }

        [TestMethod]
        public void TestFiveBetLeduc()
        {
            var permutations = AllDecisionCombinations().SelectMany(x => ReplaceBetWithAllCombinations(x)).Select(x => SetNegativeBetsOnFold(x)).ToList();
            
            LeducGameOptions options = new LeducGameOptions()
            {
                OneBetSizeOnly = false
            };
            Helper(permutations, options);
        }

        private void Helper(List<List<DecisionRecord>> permutations, LeducGameOptions options)
        {
            int permutation = -1;
            foreach (var decisionsForGame in permutations)
            {
                permutation++;
                HashSet<byte> decisionsAlreadyPlayed = new HashSet<byte>();
                int countDecisions = 0;
                var myGameProgress = LeducGameRunner.PlayLeducGameOnce(options, (decision, gameProgress) =>
                {
                    countDecisions++;
                    DecisionRecord item;
                    if (!decisionsAlreadyPlayed.Contains(decision.DecisionByteCode))
                    {
                        item = decisionsForGame.First(x => (byte)x.decision == decision.DecisionByteCode);
                        decisionsAlreadyPlayed.Add(decision.DecisionByteCode);
                    }
                    else // same decision in second round of betting
                        item = decisionsForGame.Where(x => (byte)x.decision == decision.DecisionByteCode).Skip(1).First();
                    return item.action;
                });
                countDecisions.Should().Be(decisionsForGame.Count());
                bool? winner = GetWinner(decisionsForGame);
                var utilities = myGameProgress.GetNonChancePlayerUtilities();
                if (winner == null)
                {
                    utilities[0].Should().Be(0);
                    utilities[1].Should().Be(0);
                }
                else
                {
                    int bets = decisionsForGame.Sum(x => x.bet);
                    int p1 = winner == true ? bets : -bets;
                    int p2 = -p1;
                    utilities[0].Should().Be(p1);
                    utilities[1].Should().Be(p2);
                }
            }
        }
    }
}

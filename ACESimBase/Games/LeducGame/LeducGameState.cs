using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    public class LeducGameState
    {
        public int P1Card, P2Card, FlopCard;

        public LeducBettingRound PreFlopRound, PostFlopRound;

        public int StakesPerPlayer => 1 + PreFlopRound.AgreedToBetsSum + (PostFlopRound == null ? 0 : PostFlopRound.AgreedToBetsSum * 2);

        public bool P1HasPair => P1Card == FlopCard;
        public bool P2HasPair => P2Card == FlopCard;
        public bool P1WouldWin => P1HasPair || (!P2HasPair && P1Card > P2Card);
        public bool P2WouldWin => P2HasPair || (!P1HasPair && P2Card > P1Card);
        public bool P1Wins => P2Folds || (!P1Folds && P1WouldWin);
        public bool P2Wins => P1Folds || (!P2Folds && P2WouldWin);
        public bool Tie => !P1Wins && !P2Wins;
        public int P1Gain => P1Wins ? StakesPerPlayer : (P2Wins ? -StakesPerPlayer : 0);
        public int P2Gain => 0 - P1Gain;

        public bool SomeoneFolds => PreFlopRound.SomeoneFolds || (PostFlopRound != null && PostFlopRound.SomeoneFolds);
        public bool P1Folds => PreFlopRound.P1Folds || (PostFlopRound != null && PostFlopRound.P1Folds);
        public bool P2Folds => PreFlopRound.P2Folds || (PostFlopRound != null && PostFlopRound.P2Folds);


        public LeducGameState()
        {
        }

        public LeducGameState(int p1Card, int p2Card, int flopCard)
        {
            P1Card = p1Card;
            P2Card = p2Card;
            FlopCard = flopCard;
            PreFlopRound = new LeducBettingRound();
        }

        public override string ToString()
        {
            return $"P1 {P1Card} P2 {P2Card} Bets {PreFlopRound} {(FlopDealt ? $"Flop {FlopCard} Bets {PostFlopRound}" : "")}";
        }

        public bool GameIsComplete()
        {
            if (PostFlopRound != null)
                return PostFlopRound.Complete;
            return PreFlopRound.SomeoneFolds;
        }

        public LeducBettingRound GetCurrentBettingRound()
        {
            if (PostFlopRound != null)
                return PostFlopRound;
            if (PreFlopRound.Complete)
            {
                PostFlopRound = new LeducBettingRound();
                return PostFlopRound;
            }
            return PreFlopRound;
        }

        public IEnumerable<LeducPlayerChoice> GetAvailableChoices()
        {
            LeducBettingRound r = GetCurrentBettingRound();
            foreach (var move in r.GetAvailableChoices())
            {
                yield return move;
            }
        }

        public void AddChoice(LeducPlayerChoice choice)
        {
            LeducBettingRound br = GetCurrentBettingRound();
            br.AddChoice(choice);
        }

        public LeducTurn GetTurn()
        {
            return GetCurrentBettingRound().GetTurn();
        }

        public string CurrentInfoSet()
        {
            if (GameIsComplete())
                throw new Exception();
            return PInfo(GetTurn() == LeducTurn.P1 ? 1 : 2);
        }

        public string PInfo(int p)
        {
            if (p == 1)
                return $"P1 {P1Card} Bets {PreFlopRound} {(FlopDealt ? $"Flop {FlopCard} Bets {PostFlopRound}" : "")}";
            else
                return $"P2 {P2Card} Bets {PreFlopRound} {(FlopDealt ? $"Flop {FlopCard} Bets {PostFlopRound}" : "")}";
        }

        public override bool Equals(object obj)
        {
            return ToString() == ((LeducGameState)obj).ToString();
        }

        public override int GetHashCode()
        {
            return GetFNV1aHashCode(ToString());
        }

        private static int GetFNV1aHashCode(string str)
        {
            if (str == null)
                return 0;
            var length = str.Length;
            // original FNV-1a has 32 bit offset_basis = 2166136261 but length gives a bit better dispersion (2%) for our case where all the strings are equal length, for example: "3EC0FFFF01ECD9C4001B01E2A707"
            int hash = length;
            for (int i = 0; i != length; ++i)
                hash = (hash ^ str[i]) * 16777619;
            return hash;
        }

        public LeducGameState DeepCopy()
        {
            return new LeducGameState()
            {
                P1Card = P1Card,
                P2Card = P2Card,
                FlopCard = FlopCard,
                PreFlopRound = PreFlopRound.DeepCopy(),
                PostFlopRound = PostFlopRound?.DeepCopy()
            };
        }

        public bool FlopDealt => PreFlopRound.Complete && PreFlopRound.SomeoneFolds == false;
    }
}

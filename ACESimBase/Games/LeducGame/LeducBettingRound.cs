using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public class LeducBettingRound
    {
        public List<LeducChoiceRecord> Choices = new List<LeducChoiceRecord>();

        public bool OneBetSizeOnly;

        public LeducBettingRound(bool oneBetSizeOnly)
        {
            OneBetSizeOnly = oneBetSizeOnly;
        }

        public LeducBettingRound DeepCopy()
        {
            return new LeducBettingRound(OneBetSizeOnly)
            {
                Choices = Choices.Select(x => x.DeepCopy()).ToList()
            };
        }

        public int NumBetsThisRound => Choices.Where(x => x.Choice != LeducPlayerChoice.Fold && x.Choice != LeducPlayerChoice.CallOrCheck).Count();

        private int CountBetTotal(LeducPlayerChoice choice)
        {
            switch (choice)
            {
                case LeducPlayerChoice.Fold:
                    return 0;
                case LeducPlayerChoice.CallOrCheck:
                    return 0;
                case LeducPlayerChoice.BetOrRaise01:
                    return 1;
                case LeducPlayerChoice.BetOrRaise02:
                    return 2;
                case LeducPlayerChoice.BetOrRaise04:
                    return 4;
                case LeducPlayerChoice.BetOrRaise08:
                    return 8;
                case LeducPlayerChoice.BetOrRaise16:
                    return 16;
                default:
                    throw new Exception();
            }
        }

        public int AgreedToBetsSum => DecisionsRelevantToCountingBets.Sum(x => CountBetTotal(x.Choice));

        private IEnumerable<LeducChoiceRecord> DecisionsRelevantToCountingBets => Choices.Take(ChoicesRelevantToCountingBets);

        private int ChoicesRelevantToCountingBets => Choices.Count() - (SomeoneFolds ? 2 : 0);

        public LeducChoiceStage? LastChoiceStage => Choices.LastOrDefault()?.Stage;
        public LeducPlayerChoice? LastChoice => Choices.LastOrDefault()?.Choice;

        public bool P1GoesNext => Choices.Any() ? !P1WentLast : true;

        public bool P1WentLast => LastChoiceStage == LeducChoiceStage.P1Decision || LastChoiceStage == LeducChoiceStage.P1Followup;
        public bool P1Folds => LastChoice == LeducPlayerChoice.Fold && P1WentLast;
        public bool P2Folds => LastChoice == LeducPlayerChoice.Fold && !P1WentLast;
        public bool SomeoneFolds => P1Folds || P2Folds;

        public override string ToString()
        {
            return String.Join(',', Choices);
        }

        public IEnumerable<LeducPlayerChoice> GetAvailableChoices()
        {
            if (SomeoneFolds)
                yield break;
            int choicesSoFar = Choices.Count();
            if (choicesSoFar == 4)
                yield break;
            if (choicesSoFar > 1 && Choices[choicesSoFar - 1].Choice == LeducPlayerChoice.CallOrCheck)
                yield break;
            int betsThisRound = NumBetsThisRound;
            if (betsThisRound > 0)
                yield return LeducPlayerChoice.Fold;
            yield return LeducPlayerChoice.CallOrCheck;
            if (betsThisRound < 2)
            {
                yield return LeducPlayerChoice.BetOrRaise01;
                if (!OneBetSizeOnly)
                {
                    yield return LeducPlayerChoice.BetOrRaise02;
                    yield return LeducPlayerChoice.BetOrRaise04;
                    yield return LeducPlayerChoice.BetOrRaise08;
                    yield return LeducPlayerChoice.BetOrRaise16;
                }
            }
        }

        public bool Complete => !GetAvailableChoices().Any();

        public LeducChoiceStage? NextStage()
        {
            if (Complete)
                return null;
            int choicesSoFar = Choices.Count();
            switch (choicesSoFar)
            {
                case 0:
                    return LeducChoiceStage.P1Decision;
                case 1:
                    return LeducChoiceStage.P2Decision;
                case 2:
                    return LeducChoiceStage.P1Followup;
                case 3:
                    return LeducChoiceStage.P2Followup;
                default:
                    throw new Exception();
            }
        }


        public LeducTurn GetTurn()
        {
            if (Complete)
                return LeducTurn.Complete;
            var next = NextStage();
            return (next == LeducChoiceStage.P1Decision || next == LeducChoiceStage.P1Followup) ? LeducTurn.P1 : LeducTurn.P2;
        }

        public void AddChoice(LeducPlayerChoice choice)
        {
            LeducChoiceStage? nextStage = NextStage();
            if (nextStage == null)
                throw new Exception();
            if (!GetAvailableChoices().Any(x => x == choice))
                throw new Exception();
            Choices.Add(new LeducChoiceRecord() { Choice = choice, Stage = nextStage.Value });
        }
    }
}

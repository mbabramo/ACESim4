using ACESim;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util.Randomization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase
{
    public abstract class DirectGamePlayer : IDirectGamePlayer, IDisposable
    {
        public GameDefinition GameDefinition;
        public GameProgress GameProgress { get; set; }
        public Game Game;

        public bool GameComplete => GameProgress.GameComplete;

        public Decision CurrentDecision => Game.CurrentDecision;

        public byte? CurrentDecisionIndex => Game.CurrentDecisionIndex;

        public PlayerInfo CurrentPlayer => GameDefinition.Players[CurrentDecision.PlayerIndex];

        public DirectGamePlayer(GameDefinition gameDefinition, GameProgress currentProgress, bool advanceToFirstStep)
        {
            GameDefinition = gameDefinition;
            GameProgress = currentProgress;
            Game = GameDefinition.GameFactory.CreateNewGame(null, GameProgress, GameDefinition, false, GameProgress == null || (GameProgress.CurrentDecisionIndex == null && !GameProgress.GameComplete), false);
            if (advanceToFirstStep)
                Game.AdvanceToOrCompleteNextStep();
        }

        public abstract DirectGamePlayer DeepCopy();

        public IDirectGamePlayer CopyAndPlayAction(byte action)
        {
            var copy = DeepCopy();
            copy.PlayAction(action);
            return copy;
        }

        internal GameProgress PlayWithActionsOverride(Func<Decision, GameProgress, byte> actionsOverride)
        {
            var copy = DeepCopy();
            while (!copy.GameComplete)
            {
                byte action = actionsOverride(copy.CurrentDecision, copy.GameProgress);
                copy = (DirectGamePlayer) copy.CopyAndPlayAction(action);
            }
            return copy.GameProgress;
        }

        public void PlayAction(byte actionToPlay)
        {
            Game.ContinuePathWithAction(actionToPlay);
            Game.AdvanceToOrCompleteNextStep();
            GameHistory gameHistory = GameProgress.GameHistory;
            while (!GameProgress.GameComplete && Game.GameDefinition.SkipDecision(Game.CurrentDecision, in gameHistory))
                Game.AdvanceToOrCompleteNextStep();
        }

        public void PlayUntilComplete(int randomSeed)
        {
            ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(randomSeed, 3_000_000);
            while (!GameComplete)
            {
                double[] actionProbabilities = GetActionProbabilities();
                byte index = r.GetRandomIndex(actionProbabilities);
                PlayAction((byte)(index + 1));
            }
        }

        public GameStateTypeEnum GetGameStateType()
        {
            if (GameComplete)
                return GameStateTypeEnum.FinalUtilities;
            if (CurrentPlayer.PlayerIsChance)
                return GameStateTypeEnum.Chance;
            return GameStateTypeEnum.InformationSet;
        }

        public double[] GetFinalUtilities()
        {
            if (!GameComplete)
                throw new Exception();
            Game.FinalProcessing();
            return GameProgress.GetNonChancePlayerUtilities();
        }

        public double GetFinalUtility(byte playerIndex)
        {
            if (!GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            return GameProgress.GetNonChancePlayerUtilities()[CurrentDecision.PlayerIndex];
        }

        public double[] GetActionProbabilities()
        {
            if (CurrentDecision.IsChance)
                return GetChanceProbabilities();
            return GetPlayerProbabilities();
        }

        public abstract double[] GetPlayerProbabilities();

        public double[] GetChanceProbabilities()
        {
            Decision currentDecision = CurrentDecision;
            if (currentDecision.UnevenChanceActions)
            {
                double[] unequalProbabilities = GameDefinition.GetUnevenChanceActionProbabilities(currentDecision.DecisionByteCode, GameProgress);
                return unequalProbabilities;
            }
            double probabilityEachAction = 1.0 / currentDecision.NumPossibleActions;
            return Enumerable.Range(1, CurrentDecision.NumPossibleActions).Select(x => probabilityEachAction).ToArray();
        }

        public byte ChooseChanceAction(double randomValue)
        {
            Decision currentDecision = CurrentDecision;
            if (currentDecision.UnevenChanceActions)
            {
                double[] unequalProbabilities = GameDefinition.GetUnevenChanceActionProbabilities(currentDecision.DecisionByteCode, GameProgress);
                double cumProb = 0;
                byte action = 0;
                while (true)
                {
                    action++;
                    cumProb += unequalProbabilities[action - 1];
                    if (cumProb >= randomValue)
                        return action;
                }
            }
            else
                return (byte)(1 + randomValue * currentDecision.NumPossibleActions);
        }

        
        public string GetInformationSetString()
        {
            StringBuilder s = new StringBuilder();
            GetInformationSet().ForEach(x =>
            {
                s.Append(x.decisionIndex);
                s.Append(x.information);
            });
            return s.ToString();
            // too slow (though the above also is): String.Join(";", GetInformationSet(useDeferredDecisionIndices).Select(x => $"{x.decisionIndex},{x.information}"));
        }

        public IEnumerable<byte> GetInformationSet_PlayerAndInfo()
        {
            yield return CurrentPlayer.PlayerIndex;
            foreach ((byte decisionIndex, byte information) in GetInformationSet())
            {
                yield return information;
            }
        }

        public List<(byte decisionIndex, byte information)> GetInformationSet()
        {
            var result = GameProgress.GameHistory.GetLabeledCurrentInformationSetForPlayer(CurrentDecision.PlayerIndex);
            return result;
        }

        // To detect redundant calls
        private bool _disposed;

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _disposed = true;
                // Dispose managed state (managed objects).
                GameProgress.Dispose();
            }

        }
    }
}

using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase
{
    public class DirectGamePlayer
    {
        public GameDefinition GameDefinition;
        public GameProgress GameProgress;
        public Game Game;

        public DirectGamePlayer(GameDefinition gameDefinition, GameProgress startingProgress, Game game = null)
        {
            GameDefinition = gameDefinition;
            GameProgress = startingProgress.DeepCopy();
            if (game == null)
            {
                Game = GameDefinition.GameFactory.CreateNewGame();
                Game.PlaySetup(null, GameProgress, GameDefinition, false, true);
                Game.AdvanceToOrCompleteNextStep();
            }
            else
            {
                Game = game;
                Game.Progress = GameProgress;
            }
        }

        public DirectGamePlayer DeepCopy()
        {
            return new DirectGamePlayer(GameDefinition, GameProgress, Game);
        }

        public void PlayAction(byte actionToPlay)
        {
            Game.ContinuePathWithAction(actionToPlay);
            Game.AdvanceToOrCompleteNextStep();
        }

        public bool GameComplete => GameProgress.GameComplete;

        public Decision CurrentDecision => Game.CurrentDecision;

        public byte? CurrentDecisionIndex => Game.CurrentDecisionIndex;

        public PlayerInfo CurrentPlayer => GameDefinition.Players[CurrentDecision.PlayerNumber];

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
            return GameProgress.GetNonChancePlayerUtilities()[CurrentDecision.PlayerNumber];
        }

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

        public List<byte> GetInformationSet()
        {
            if (GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            return GameProgress.InformationSetLog.GetPlayerInformationUpToNow(CurrentDecision.PlayerNumber);
        }
    }
}

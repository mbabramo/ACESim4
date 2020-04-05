using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase
{
    public class DirectGamePlayer
    {
        GameDefinition GameDefinition;
        GameProgress GameProgress;
        Game Game;

        public DirectGamePlayer(GameDefinition gameDefinition, GameProgress startingProgress, Game game = null)
        {
            GameDefinition = gameDefinition;
            GameProgress = startingProgress.DeepCopy();
            if (game == null)
            {
                Game = GameDefinition.GameFactory.CreateNewGame();
                Game.PlaySetup(null, GameProgress, GameDefinition, false, true);
            }
            else
                Game = game.DeepCopy(GameProgress);
        }

        public DirectGamePlayer DeepCopy()
        {
            return new DirectGamePlayer(GameDefinition, GameProgress, Game);
        }

        public void PlayAction(byte actionToPlay) => Game.ContinuePathWithAction(actionToPlay);

        public bool GameComplete => GameProgress.GameComplete;

        public Decision CurrentDecision => Game.CurrentDecision;

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
            if (!GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            return GameProgress.GetNonChancePlayerUtilities();
        }

        public double GetFinalUtility(byte playerIndex)
        {
            if (!GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            return GameProgress.GetNonChancePlayerUtilities()[CurrentDecision.PlayerNumber];
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
            if (!GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            return GameProgress.InformationSetLog.GetPlayerInformationUpToNow(CurrentDecision.PlayerNumber);
        }
    }
}

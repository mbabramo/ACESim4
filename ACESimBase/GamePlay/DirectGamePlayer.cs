using ACESim;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase
{
    public abstract class DirectGamePlayer : IDirectGamePlayer
    {
        public GameDefinition GameDefinition;
        public GameProgress GameProgress { get; set; }
        public Game Game;

        public DirectGamePlayer(GameDefinition gameDefinition, GameProgress startingProgress, Game alreadyStartedGame)
        {
            GameDefinition = gameDefinition;
            GameProgress = startingProgress.DeepCopy();
            if (alreadyStartedGame == null)
            {
                Game = GameDefinition.GameFactory.CreateNewGame();
                Game.PlaySetup(null, GameProgress, GameDefinition, false, true);
                Game.AdvanceToOrCompleteNextStep();
            }
            else
            {
                Game = alreadyStartedGame;
                Game.Progress = GameProgress;
            }
        }

        public abstract DirectGamePlayer DeepCopy();

        public IDirectGamePlayer CopyAndPlayAction(byte action)
        {
            var copy = DeepCopy(); // DEBUG -- must make sure that Progress makes a deep copy of the game history, so that we can move to a different thread.
            copy.PlayAction(action);
            return copy;
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

        
        public string GetInformationSetString(bool useDeferredDecisionIndices)
        {
            return String.Join(";", GetInformationSet(useDeferredDecisionIndices).Select(x => $"{x.decisionIndex},{x.information}"));
        }

        public List<(byte decisionIndex, byte information)> GetInformationSet(bool useDeferredDecisionIndices)
        {
            if (GameComplete || CurrentPlayer.PlayerIsChance)
                throw new Exception();
            var result = GameProgress.InformationSetLog.GetPlayerDecisionAndInformationAtPoint(CurrentDecision.PlayerNumber, null).ToList();
            if (useDeferredDecisionIndices)
            {
                int deferredIndex = 0;
                int resultLength = result.Count();
                for (int i = 0; i < resultLength; i++)
                {
                    for (int j = i + 1; j < resultLength; j++)
                    {
                        if (result[i].decisionIndex == result[j].decisionIndex)
                        { // we've found a duplicate decision index, so the first one must be a deferred decision index -- replace it.
                            result[i] = (GameProgress.GameHistory.DeferredDecisionIndices[deferredIndex++], result[i].information);
                            break;
                        }
                    }
                }
            }
            return result;
        }
    }
}

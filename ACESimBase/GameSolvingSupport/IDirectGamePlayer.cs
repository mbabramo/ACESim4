using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public interface IDirectGamePlayer
    {
        double[] GetActionProbabilities();
        GameProgress GameProgress { get; }
        void PlayAction(byte action);
        IDirectGamePlayer CopyAndPlayAction(byte action);
        void PlayUntilComplete(int randomSeed);
        void SynchronizeForSameThread(IEnumerable<IDirectGamePlayer> othersOnSameThread);
        bool GameComplete { get; }
        Decision CurrentDecision { get; }
        byte? CurrentDecisionIndex { get; }
        PlayerInfo CurrentPlayer { get; }
    }
}

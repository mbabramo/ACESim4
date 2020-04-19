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
        IDirectGamePlayer CopyAndPlayAction(byte action);
    }
}

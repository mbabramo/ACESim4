using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public abstract class PostIterationUpdaterBase
    {
        public virtual void PrepareForUpdating(int iteration, EvolutionSettings evolutionSettings)
        {

        }

        public abstract void UpdateInformationSet(InformationSetNode node);
    }
}

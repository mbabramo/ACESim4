using GeneticSharp.Domain.Chromosomes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class InformationSetChromosome : BytesChromosome
    {
        List<InformationSetNode> InformationSets;

        public InformationSetChromosome(List<InformationSetNode> informationSets, int length) : base(length)
        {
            InformationSets = informationSets;
            Initialize(InformationSets.Select(x => (byte) x.NumPossibleActions).ToArray(), null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class DeltaOffersCalculation
    {
        double[] Deltas;

        public DeltaOffersCalculation(LitigGameDefinition myDefinition)
        {
            Deltas = new double[myDefinition.Options.NumOffers];
            // Suppose we have 9 offers. Then the first four are negative offers, the fifth is zero, and the next four are positive offers. 
            // If we have 10 offers, then the first five are negative offers and the next five are positive offers.
            int totalPossibleLevels = (myDefinition.Options.NumOffers / 2);
            // min * multiplier^(totalPossibleLevels - 1) = max. So, ln multiplier = (ln max - ln min)/(totalPossibleLevels - 1).
            double multiplier = Math.Exp((Math.Log(myDefinition.Options.DeltaOffersOptions.MaxDelta) - Math.Log(myDefinition.Options.DeltaOffersOptions.DeltaStartingValue)) / ((double)totalPossibleLevels - 1.0));
            for (int d = 0; d < totalPossibleLevels; d++)
            {
                if (d == 0)
                    Deltas[d] = 0 - myDefinition.Options.DeltaOffersOptions.MaxDelta;
                else if (d < totalPossibleLevels - 1)
                    Deltas[d] = Deltas[d - 1] / multiplier;
                else if (d == totalPossibleLevels - 1)
                    Deltas[d] = 0 - myDefinition.Options.DeltaOffersOptions.DeltaStartingValue;
                Deltas[Deltas.Length - 1 - d] = 0 - Deltas[d];
            }
        }

        public double GetOfferValue(double previousOffer, byte newOffer)
        {
            return previousOffer + Deltas[newOffer - 1];
        }
    }
}

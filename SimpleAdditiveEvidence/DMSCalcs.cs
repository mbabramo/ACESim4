using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleAdditiveEvidence
{
    class DMSCalcs
    {

        double q, c, t;
        public DMSCalcs(double q, double c, double t)
        {
            this.q = q;
            this.c = c;
            this.t = t;
        }

        private ((double minOffer, double maxOffer) pLine, (double minOffer, double maxOffer) dLine) CalculateDMSCorrectResult()
        {
            // TODO: Add all calculations, including all lines (plus truncations).
            double correctPStart = 0, correctPEnd = 0, correctDStart = 0, correctDEnd = 0;
            if (t <= q && q <= 1 - t)
            {
                // Case 1
                correctPStart = 0.5 - 3.0 * ((5.0 / 6.0) - q) * c;
                correctPEnd = correctPStart + 1.0 / 3.0;
                correctDStart = 1.0 / 6.0 + 3 * (q - 1.0 / 6.0) * c;
                correctDEnd = correctDStart + 1.0 / 3.0;
            }
            else if (q < t && t < 1 - q)
            {
                // Case 2
                double zp_threshold = 6.0 * c - 1.0 + (t - q) / (t - q) / (1.0 - q);
                // TODO ...
            }
            //(double pUtility, double dUtility) r = CalculateUtilitiesForOfferRanges((correctPStart, correctPEnd), (correctDStart, correctDEnd));
            //TabbedText.WriteLine($"Anticipated answer P:{correctPStart.ToSignificantFigures(3)}, {correctPEnd.ToSignificantFigures(3)} D:{correctDStart.ToSignificantFigures(3)}, {correctDEnd.ToSignificantFigures(3)} ==> ({r.pUtility.ToSignificantFigures(3)}, {r.dUtility.ToSignificantFigures(3)})");
            //return ((correctPStart, correctPEnd), (correctDStart, correctDEnd));
            return default;
        }
    }
}

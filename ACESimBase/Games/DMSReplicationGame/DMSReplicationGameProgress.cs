using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.DMSReplicationGame
{
    public class DMSReplicationGameProgress : GameProgress
    {
        public DMSReplicationGameProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }
        public DMSReplicationGameDefinition DMSReplicationGameDefinition => (DMSReplicationGameDefinition)GameDefinition;
        public DMSReplicationGameOptions DMSReplicationGameOptions => DMSReplicationGameDefinition.Options;

        public double PSlope, DSlope, PMinValue, DMinValue, PTruncationPortion, DTruncationPortion;
        public (double p, double d, double settleProportion, double pPaysForDProportion, double dPaysForPProportion) Outcomes;
        public override GameProgress DeepCopy()
        {
            DMSReplicationGameProgress copy = new DMSReplicationGameProgress(FullHistoryRequired);

            // copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);
            copy.PSlope = PSlope;
            copy.DSlope = DSlope;
            copy.PMinValue = PMinValue;
            copy.DMinValue = DMinValue;
            copy.PTruncationPortion = PTruncationPortion;
            copy.DTruncationPortion = DTruncationPortion;

            return copy;
        }

        public void CalculateGameOutcomes()
        {
            Outcomes = GetOutcomes();
        }

        // Calculate welfare and case outcomes evenly sampling over plaintiff and defendant signals, using the definition of the P offer 
        // and D demand functions set above.
        (double p, double d, double settleProportion, double pPaysForDProportion, double dPaysForPProportion) GetOutcomes()
        {
            int settlements = 0;
            int trials = 0;
            int pPaysForDCount = 0;
            int dPaysForPCount = 0;
            double pSum = 0, dSum = 0;
            int n = 0;
            const double increment = 0.01; // DEBUG
            for (double zP = increment; zP < 1.0; zP += increment)
            {
                for (double zD = increment; zD < 1.0; zD += increment)
                {
                    (double p, double d, bool settles, bool pPaysForD, bool dPaysForP) = SignalsToOutcome(zP, zD);
                    pSum += p;
                    dSum += d;
                    n++;
                    if (settles)
                        settlements++;
                    else
                    {
                        trials++;
                        if (pPaysForD)
                            pPaysForDCount++;
                        if (dPaysForP)
                            dPaysForPCount++;
                    }
                }
            }
            double totalCases = (double)(settlements + trials);
            return (pSum / n, dSum / n, settlements / totalCases, pPaysForDCount / totalCases, dPaysForPCount / totalCases);
        }

        double P(double zp)
        {
            if (zp < PTruncationPortion)
                zp = PTruncationPortion;
            return PMinValue + PSlope * zp;
        }
        double D(double zd)
        {
            if (zd > DTruncationPortion)
                zd = PTruncationPortion;
            return DMinValue + PSlope * zd;
        }

        // Calculate welfare and case outcomes based on plaintiff and defendant signals, using the definition of the P offer 
        // and D demand functions set above.
        (double p, double d, bool settles, bool pPaysForD, bool dPaysForP) SignalsToOutcome(double zP, double zD)
        {
            // Get bids.
            double pDemand = P(zP);
            double dOffer = D(zD);

            // Get the outcome
            return SignalsAndBidsToOutcome(zP, zD, pDemand, dOffer);
        }

        // Calculates welfare outcomes, including information on whether settlement and fee shifting occurs, based 
        // on signals and bids
        (double p, double d, bool settles, bool pPaysForD, bool dPaysForP) SignalsAndBidsToOutcome(double zP, double zD, double pDemand, double dOffer)
        {
            // Check for settlement.
            if (dOffer >= pDemand)
            {
                // See p.4
                double settlement = (dOffer + pDemand) / 2.0;
                return (settlement, 1.0 - settlement, true, false, false);
            }

            // Conduct trial.
            double thetaP = zP * DMSReplicationGameOptions.Q; // p. 6
            double thetaD = DMSReplicationGameOptions.Q + zD * (1.0 - DMSReplicationGameOptions.Q); // p. 6
            double judgment = 0.5 * (thetaP + thetaD); // p. 4 
            bool feeShiftingToP = thetaP < 1.0 - thetaD && thetaD < DMSReplicationGameOptions.T; // equation 2 p. 5
            bool feeShiftingToD = thetaP > 1.0 - thetaD && thetaP > 1.0 - DMSReplicationGameOptions.T;// equation 2 p. 5
            double pCosts = 0, dCosts = 0;
            if (feeShiftingToP)
            {
                pCosts = DMSReplicationGameOptions.C;
            }
            else if (feeShiftingToD)
            {
                dCosts = DMSReplicationGameOptions.C;
            }
            else
            {
                pCosts = dCosts = 0.5 * DMSReplicationGameOptions.C;
            }
            return (judgment - pCosts, 1.0 - judgment - dCosts, false, feeShiftingToP, feeShiftingToD);
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class LitigationGameTwoClaimToyModel
    {
        static double standardDeviationOfObfuscationParties = 0.1;
        static double tasteForOutOfCourtResolution = 0;
        static double claim1Size = 1000.0;
        static double claim2Size = 1000.0;
        static double pCosts = 200;
        static double dCosts = 200;
        static double claim2ProbabilityMultiplier = 1.0;
        static bool assumePShouldWinWhenMostJudgesAgree = true;
        static double withThreeQuartersAgreementPShouldWinProbability = 0.95;
        static bool pMayDropAtBeginning = true;
        static bool pMayDropAtEnd = true;
        static bool dMayStopNegotiatingBasedOnPEndDrop = true;
        static bool adjustOffersSoThatTheyAreWithinRange = false;
        static bool countTrialCostsInErrorCostCalculation = true;
        static bool forceDropClaim2 = false; // for testing

        public void FindSymmetricSurplusesWithDifferentStructures()
        {
            //EffectOfSeveringOnSettlementRates();
            EffectOfDropOptions();
        }

        public void EffectOfSeveringOnSettlementRates()
        {
            pMayDropAtBeginning = false;
            pMayDropAtEnd = false;
            dMayStopNegotiatingBasedOnPEndDrop = false;

            int iterationsToPlay = 1000000;

            // alter parties' information quality
            standardDeviationOfObfuscationParties = 0.05;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            standardDeviationOfObfuscationParties = 0.15;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            standardDeviationOfObfuscationParties = 0.1;
            // alter costs
            pCosts = dCosts = 300.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            pCosts = dCosts = 200.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            pCosts = dCosts = 100.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            pCosts = dCosts = 50.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
        }

        public void EffectOfDropOptions()
        {
            int iterationsToPlay = 1000000;
            adjustOffersSoThatTheyAreWithinRange = true; // no reason for a plaintiff to offer neg money when it can drop, and we'll do this symetrically
            tasteForOutOfCourtResolution = 0; 
            countTrialCostsInErrorCostCalculation = true;

            pMayDropAtBeginning = false; // no point in modeling begin drops when information isn't changing over time and end drop is mechanical calculation
            pMayDropAtEnd = true;
            dMayStopNegotiatingBasedOnPEndDrop = true;


            TabbedText.WriteLine("Equal claims");
            TabbedText.Tabs++;
            standardDeviationOfObfuscationParties = 0.1;
            pCosts = dCosts = 200.0;
            claim1Size = 1000.0;
            claim2Size = 1000.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            TabbedText.Tabs--;

            TabbedText.WriteLine("Claim 2 is 1000 but lower probability");
            TabbedText.Tabs++;
            standardDeviationOfObfuscationParties = 0.1;
            pCosts = dCosts = 200.0;
            claim1Size = 1000.0;
            claim2Size = 1000.0;
            claim2ProbabilityMultiplier = 0.10;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            TabbedText.Tabs--;
            claim2ProbabilityMultiplier = 1.0; // reset that

            TabbedText.WriteLine("Claim 2 is only 200");
            TabbedText.Tabs++;
            standardDeviationOfObfuscationParties = 0.1;
            pCosts = dCosts = 200.0;
            claim1Size = 1000.0;
            claim2Size = 200.0;
            FindSymmetricSurplusesForDifferentSeveringApproaches(iterationsToPlay);
            TabbedText.Tabs--;

        }

        public Outcome PlayOnce(
            int iterationToPlay,
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2,
            double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether)
        {
            // ignore the stuff about cutoffs
            double ignore;
            return PlayOnceAndReturnCutoff(iterationToPlay, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, false /* irrelevant */, true /* irrelevant */, null, null, out ignore);
        }

        public Outcome PlayOnceAndReturnCutoff(
            int iterationToPlay,
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2,
            double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            bool findDNegotiationCutoff, bool findForClaim1, double? tentativeCutoff, double? maxDistanceFromCutoff, out double cutoff)
        {
            ClaimAssessment claim1 = GetClaimAssessment(iterationToPlay * 2, false);
            ClaimAssessment claim2 = GetClaimAssessment(iterationToPlay * 2 + 1, true);

            cutoff = 0;
            if (!findDNegotiationCutoff)
                cutoff = (findForClaim1 ? claim1.pEstimateStrengthLiability : claim2.pEstimateStrengthLiability);

            if (tentativeCutoff != null &&
                Math.Abs(cutoff - (double)tentativeCutoff) > (double)maxDistanceFromCutoff
                )
                return null; // this isn't near the cutoff, and we only want to play games near the cutoff right now

            bool dropClaim1 = pMayDropAtBeginning && claim1.pEstimateStrengthLiability < dropCutoffClaim1;
            bool dropClaim2 = pMayDropAtBeginning && claim2.pEstimateStrengthLiability < dropCutoffClaim2;
            if (forceDropClaim2)
                dropClaim2 = true;
            if (dropClaim1 && aggregateTogether)
                dropClaim2 = true;

            ClaimAssessment aggregate = null;
            if (aggregateTogether)
            {
                double weightClaim1 = claim1Size / (claim1Size + claim2Size);
                double weightClaim2 = 1.0 - weightClaim1;
                if (dropClaim1)
                {
                    weightClaim1 = 0;
                    weightClaim2 = 1.0;
                }
                else if (dropClaim2)
                {
                    weightClaim1 = 1.0;
                    weightClaim2 = 0;
                }
                aggregate = new ClaimAssessment()
                {
                    actualLitigationQuality = (weightClaim1 * (dropClaim1 ? 0 : claim1.actualLitigationQuality) + weightClaim2 * (dropClaim2 ? 0 : claim2.actualLitigationQuality)),
                    pEstimateStrengthLiability = (weightClaim1 * (dropClaim1 ? 0 : claim1.pEstimateStrengthLiability) + weightClaim2 * (dropClaim2 ? 0 : claim2.pEstimateStrengthLiability)),
                    dEstimateStrengthLiability = (weightClaim1 * (dropClaim1 ? 0 : claim1.dEstimateStrengthLiability) + weightClaim2 * (dropClaim2 ? 0 : claim2.dEstimateStrengthLiability))
                };
                // surplus insisted upon from claim 1 applies to the entire aggregate
                if (dropClaim1 && dropClaim2)
                    return new Outcome() { pOutcome = tasteForOutOfCourtResolution, dOutcome = tasteForOutOfCourtResolution, claim1Dropped = dropClaim1 ? 1.0 : 0, claim2Dropped = dropClaim2 ? 1.0 : 0, bothClaimsDropped = dropClaim1 && dropClaim2 ? 1.0 : 0, claim1Settled = 0.0, claim2Settled = 0.0, bothClaimsSettled = 0.0, noTrial = 1.0, trial = 0.0 }.WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, tasteForOutOfCourtResolution);
                double claimSize = (dropClaim1 ? 0 : claim1Size) + (dropClaim2 ? 0 : claim2Size);
                double pAggOffer = GetOffer(aggregate, true, pPortionOfSurplusInsistedUponClaim1, claimSize);
                double dAggOffer;

                if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop)
                    cutoff = aggregate.dEstimateStrengthLiability;
                if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop && aggregate.dEstimateStrengthLiability < dNegotiationCutoffClaim1)
                    dAggOffer = 0;
                else
                    dAggOffer = GetOffer(aggregate, false, dPortionOfSurplusInsistedUponClaim1, claimSize);
                if (pMayDropAtEnd && pAggOffer > dAggOffer && aggregate.pEstimateStrengthLiability * claimSize < pCosts)
                { // we don't have a settlement, and it looks like p should give up
                    dropClaim1 = true;
                    dropClaim2 = true;
                    return new Outcome() { pOutcome = tasteForOutOfCourtResolution, dOutcome = tasteForOutOfCourtResolution, claim1Dropped = 1.0, claim2Dropped = 1.0, bothClaimsDropped = 1.0, claim1Settled = 0.0, claim2Settled = 0.0, bothClaimsSettled = 0.0, noTrial = 1.0, trial = 0.0 }.WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, tasteForOutOfCourtResolution);
                }

                if (dAggOffer > pAggOffer)
                    return new Outcome() { pOutcome = (pAggOffer + dAggOffer) / 2.0 + tasteForOutOfCourtResolution, dOutcome = 0 - (pAggOffer + dAggOffer) / 2.0 + tasteForOutOfCourtResolution, claim1Dropped = dropClaim1 ? 1.0 : 0, claim2Dropped = dropClaim2 ? 1.0 : 0, claim1Settled = dropClaim1 ? 0 : 1.0, claim2Settled = dropClaim2 ? 0 : 1, bothClaimsSettled = !dropClaim1 && !dropClaim2 ? 1.0 : 0.0, noTrial = 1.0, trial = 0.0 }.WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, tasteForOutOfCourtResolution).WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, tasteForOutOfCourtResolution);
                else
                    return new Outcome()
                    {
                        pOutcome = (claim1.pWinsAtTrial && !dropClaim1 ? claim1Size : 0) + (claim2.pWinsAtTrial && !dropClaim2 ? claim2Size : 0) - pCosts,
                        dOutcome = (claim1.pWinsAtTrial && !dropClaim1 ? 0 - claim1Size : 0) + (claim2.pWinsAtTrial && !dropClaim2 ? 0 - claim2Size : 0) - dCosts,
                        claim1Dropped = dropClaim1 ? 1.0 : 0,
                        claim2Dropped = dropClaim2 ? 1.0 : 0,
                        bothClaimsDropped = dropClaim1 && dropClaim2 ? 1.0 : 0,
                        claim1Settled = 0.0,
                        claim2Settled = 0.0,
                        bothClaimsSettled = 0.0,
                        noTrial = 0.0,
                        trial = 1.0
                    }.WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, countTrialCostsInErrorCostCalculation ? 0 : 0 - (pCosts + dCosts) / 2.0);
            }

            double pOfferClaim1 = GetOffer(claim1, true, pPortionOfSurplusInsistedUponClaim1, claim1Size);
            double dOfferClaim1;
            if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop && findDNegotiationCutoff && findForClaim1)
                cutoff = claim1.dEstimateStrengthLiability;
            if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop && claim1.dEstimateStrengthLiability < dNegotiationCutoffClaim1)
                dOfferClaim1 = 0;
            else
                dOfferClaim1 = GetOffer(claim1, false, dPortionOfSurplusInsistedUponClaim1, claim1Size);
            double pOfferClaim2 = GetOffer(claim2, true, pPortionOfSurplusInsistedUponClaim2, claim2Size);
            double dOfferClaim2;
            if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop && findDNegotiationCutoff && !findForClaim1)
                cutoff = claim2.dEstimateStrengthLiability;
            if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop && claim2.dEstimateStrengthLiability < dNegotiationCutoffClaim2)
                dOfferClaim2 = 0;
            else
                dOfferClaim2 = GetOffer(claim2, false, dPortionOfSurplusInsistedUponClaim2, claim2Size);

            double? claim1OutOfCourtResolution = dOfferClaim1 > pOfferClaim1 ? (double?)((pOfferClaim1 + dOfferClaim1) / 2.0) : (double?)null;
            double? claim2OutOfCourtResolution = dOfferClaim2 > pOfferClaim2 ? (double?)((pOfferClaim2 + dOfferClaim2) / 2.0) : (double?)null;
            if (dropClaim1)
            {
                claim1OutOfCourtResolution = 0.0;
                resolutionOfClaim1Sticks = true;
            }
            if (dropClaim2)
            {
                claim2OutOfCourtResolution = 0.0;
                resolutionOfClaim2Sticks = true;
            }

            bool claim1ResolvedBeforeEndDrop = claim1OutOfCourtResolution != null && (resolutionOfClaim1Sticks || claim2OutOfCourtResolution != null);
            bool claim2ResolvedBeforeEndDrop = claim2OutOfCourtResolution != null && (resolutionOfClaim2Sticks || claim1OutOfCourtResolution != null);
            bool atLeastOneClaimDidntSettle = !claim1ResolvedBeforeEndDrop || !claim2ResolvedBeforeEndDrop;
            if (pMayDropAtEnd && atLeastOneClaimDidntSettle)
            { // p needs to consider whether to give up claim that didn't settle
                ClaimAssessment remaining;
                double remainingClaimSize;
                if (!claim1ResolvedBeforeEndDrop && claim2ResolvedBeforeEndDrop)
                {
                    remaining = claim1;
                    remainingClaimSize = claim1Size;
                }
                else if (!claim2ResolvedBeforeEndDrop && claim1ResolvedBeforeEndDrop)
                {
                    remaining = claim2;
                    remainingClaimSize = claim2Size;
                }
                else /* we didn't resolve either */
                {
                    double weightClaim1 = claim1Size / (claim1Size + claim2Size);
                    double weightClaim2 = 1.0 - weightClaim1;
                    remaining = new ClaimAssessment()
                    {
                        actualLitigationQuality = (weightClaim1 * claim1.actualLitigationQuality + weightClaim2 * claim2.actualLitigationQuality),
                        pEstimateStrengthLiability = (weightClaim1 * claim1.pEstimateStrengthLiability + weightClaim2 * claim2.pEstimateStrengthLiability),
                        dEstimateStrengthLiability = (weightClaim1 * claim1.dEstimateStrengthLiability + weightClaim2 * claim2.dEstimateStrengthLiability)
                    };
                    remainingClaimSize = claim1Size + claim2Size;
                }
                bool pShouldGiveUpRemainingClaims = remaining.pEstimateStrengthLiability * remainingClaimSize < pCosts;
                if (pShouldGiveUpRemainingClaims)
                {
                    if (!claim1ResolvedBeforeEndDrop)
                        dropClaim1 = true;
                    if (!claim2ResolvedBeforeEndDrop)
                        dropClaim2 = true;
                }
            }

            if (dropClaim1)
            {
                claim1OutOfCourtResolution = 0.0;
                resolutionOfClaim1Sticks = true;
            }
            if (dropClaim2)
            {
                claim2OutOfCourtResolution = 0.0;
                resolutionOfClaim2Sticks = true;
            }

            double pOutcome, dOutcome, settled = 0.0, trial = 1.0;
            if (
                (claim1OutOfCourtResolution == null && claim2OutOfCourtResolution == null) ||
                (claim1OutOfCourtResolution == null && !resolutionOfClaim2Sticks) ||
                (claim2OutOfCourtResolution == null && !resolutionOfClaim1Sticks)
                )
            { // go to trial on both
                pOutcome = (claim1.pWinsAtTrial ? claim1Size : 0) + (claim2.pWinsAtTrial ? claim2Size : 0) - pCosts;
                dOutcome = (claim1.pWinsAtTrial ? 0 - claim1Size : 0) + (claim2.pWinsAtTrial ? 0 - claim2Size : 0) - dCosts;
            }
            else if (claim1OutOfCourtResolution != null && claim2OutOfCourtResolution != null)
            { // settle on both
                trial = 0.0;
                settled = 1.0;
                pOutcome = (double)(claim1OutOfCourtResolution + claim2OutOfCourtResolution) + tasteForOutOfCourtResolution;
                dOutcome = (double)(0 - claim1OutOfCourtResolution - claim2OutOfCourtResolution) + tasteForOutOfCourtResolution;
            }
            else if (claim1OutOfCourtResolution == null || !resolutionOfClaim1Sticks)
            { // claim 2 settled, but claim 1 must go to trial
                pOutcome = (claim1.pWinsAtTrial ? claim1Size : 0) + (double)claim2OutOfCourtResolution - pCosts;
                dOutcome = (claim1.pWinsAtTrial ? 0 - claim1Size : 0) - (double)claim2OutOfCourtResolution - dCosts;
            }
            else
            { // claim 1 settled, but claim 2 must go to trial
                pOutcome = (claim2.pWinsAtTrial ? claim2Size : 0) + (double)claim1OutOfCourtResolution - pCosts;
                dOutcome = (claim2.pWinsAtTrial ? 0 - claim2Size : 0) - (double)claim1OutOfCourtResolution - dCosts;
            }

            return new Outcome() { pOutcome = pOutcome, dOutcome = dOutcome, claim1Dropped = dropClaim1 ? 1.0 : 0, claim2Dropped = dropClaim2 ? 1.0 : 0, bothClaimsDropped = dropClaim1 && dropClaim2 ? 1.0 : 0, claim1Settled = (!dropClaim1 && claim1OutOfCourtResolution != null) ? 1.0 : 0, claim2Settled = (!dropClaim2 && claim2OutOfCourtResolution != null) ? 1.0 : 0, bothClaimsSettled = !dropClaim1 && !dropClaim2 && claim1OutOfCourtResolution != null && claim2OutOfCourtResolution != null ? 1.0 : 0, noTrial = 1.0 - trial, trial = trial }.
                WithErrorsCalculated(claim1, claim2, claim1Size, claim2Size, 
                    trial == 1.0 
                        ? (countTrialCostsInErrorCostCalculation ? 0 : -(pCosts + dCosts) / 2.0) 
                        : tasteForOutOfCourtResolution);
        }


        public class ClaimAssessment
        {
            public double actualLitigationQuality;
            public double pEstimateStrengthLiability;
            public double dEstimateStrengthLiability;
            public bool pWinsAtTrial;
            public bool pShouldWin;
        }

        int randomVariant = 0;

        public ClaimAssessment GetClaimAssessment(int i, bool isClaim2)
        {

            double ran0 = FastPseudoRandom.GetRandom(i * 10 + 0, 373);
            double ran1 = FastPseudoRandom.GetRandom(i * 10 + 1, 373);
            double ran2 = FastPseudoRandom.GetRandom(i * 10 + 2, 373);
            double ran3 = FastPseudoRandom.GetRandom(i * 10 + 3, 373);
            double ran4 = FastPseudoRandom.GetRandom(i * 10 + 4, 373);

            //Random r = new Random(randomVariant * 373 + i);
            double actualLitigationQuality = ran0; // r.NextDouble();

            double pNoise = standardDeviationOfObfuscationParties * alglib.normaldistr.invnormaldistribution(ran2);
            double dNoise = standardDeviationOfObfuscationParties * alglib.normaldistr.invnormaldistribution(ran3);

            double pSignal = actualLitigationQuality + pNoise;
            double dSignal = actualLitigationQuality + dNoise;

            double pEstimateStrengthLiability; 
            double dEstimateStrengthLiability; 
            if (standardDeviationOfObfuscationParties == 0)
                pEstimateStrengthLiability = dEstimateStrengthLiability = actualLitigationQuality;
            else
            {
                pEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscationParties, pSignal);
                dEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscationParties, dSignal);
            }

            if (claim2ProbabilityMultiplier != 1.0 && isClaim2)
            {
                actualLitigationQuality *= claim2ProbabilityMultiplier;
                pEstimateStrengthLiability *= claim2ProbabilityMultiplier;
                dEstimateStrengthLiability *= claim2ProbabilityMultiplier;
            }

            bool pShouldWin;
            if (assumePShouldWinWhenMostJudgesAgree)
                pShouldWin = actualLitigationQuality > 0.5;
            else
            {
                double probabilityPShouldWinForThisQuality = HyperbolicTangentCurve.GetYValue(0.5, 0.5, 1.0, 0.75, withThreeQuartersAgreementPShouldWinProbability, actualLitigationQuality);
                pShouldWin = ran1 < probabilityPShouldWinForThisQuality;
            }

            return new ClaimAssessment() { actualLitigationQuality = actualLitigationQuality, pEstimateStrengthLiability = pEstimateStrengthLiability, dEstimateStrengthLiability = dEstimateStrengthLiability, pWinsAtTrial = ran4 < actualLitigationQuality, pShouldWin = pShouldWin };
        }

        public class Outcome
        {
            public double pOutcome;
            public double dOutcome;
            public double claim1Dropped;
            public double claim2Dropped;
            public double bothClaimsDropped;
            public double claim1Settled;
            public double claim2Settled;
            public double bothClaimsSettled;
            public double noTrial;
            public double trial;
            public double compensationError;
            public double deterrenceError;

            public double absCompensationError;
            public double absDeterrenceError;

            public Outcome WithErrorsCalculated(ClaimAssessment claim1, ClaimAssessment claim2, double claim1Size, double claim2Size, double subtractFromEachOutcome)
            {
                double correctPCompensation = (claim1.pShouldWin ? claim1Size : 0) + (claim2.pShouldWin ? claim2Size : 0);
                double correctDPayment = -correctPCompensation;
                compensationError = (pOutcome - subtractFromEachOutcome) - correctPCompensation;
                deterrenceError = correctDPayment - (dOutcome - subtractFromEachOutcome);
                absCompensationError = Math.Abs((pOutcome - subtractFromEachOutcome) - correctPCompensation);
                absDeterrenceError = Math.Abs((dOutcome - subtractFromEachOutcome) - correctDPayment);
                return this;
            }

            public void OutputResolutionSummary()
            {
                TabbedText.WriteLine(GetResolutionSummary1());
                TabbedText.WriteLine(GetResolutionSummary2());
            }

            public string GetResolutionSummary1()
            {
                double total = noTrial + trial;
                return String.Format("NoTrial: {0}, Trial: {1}, CompensationError: {2}, DeterrenceError: {3}, AbsCompensationError: {4}, AbsDeterrenceError: {5}, POutcome: {6}, DOutcome: {7}", noTrial * 100.0 / total, trial * 100.0 / total, compensationError / total, deterrenceError / total, absCompensationError / total, absDeterrenceError / total, pOutcome / total, dOutcome / total);
            }

            public string GetResolutionSummary2()
            {
                double total = noTrial + trial;
                return String.Format("Claim1Dropped: {0}, Claim2Dropped: {1}, BothDropped: {2}, Claim1Settled: {3}, Claim2Settled: {4}, BothClaimsSettled: {5}", claim1Dropped * 100.0 / total, claim2Dropped * 100.0 / total, bothClaimsDropped * 100.0 / total, claim1Settled * 100.0 / total, claim2Settled * 100.0 / total, bothClaimsSettled * 100.0 / total);
            }
        }

        public double GetPerceivedBreakEvenPoint(ClaimAssessment ca, bool plaintiff, double claimSize)
        {
            if (plaintiff)
                return ca.pEstimateStrengthLiability * claimSize - pCosts;
            else
                return ca.dEstimateStrengthLiability * claimSize + dCosts;
        }

        public double GetOffer(ClaimAssessment ca, bool plaintiff, double portionOfSurplusInsistedUpon, double claimSize)
        {
            double breakEven = GetPerceivedBreakEvenPoint(ca, plaintiff, claimSize);
            double calculatedOffer;
            if (plaintiff)
            {
                calculatedOffer = breakEven + portionOfSurplusInsistedUpon * (pCosts + dCosts);
                if (adjustOffersSoThatTheyAreWithinRange && calculatedOffer < 0)
                    calculatedOffer = 0;
                return calculatedOffer;
            }
            else
            {
                calculatedOffer = breakEven - portionOfSurplusInsistedUpon * (pCosts + dCosts);
                if (adjustOffersSoThatTheyAreWithinRange && calculatedOffer > claimSize)
                    calculatedOffer = claimSize;
                return calculatedOffer;
            }
        }

        public void FindSymmetricSurplusesForDifferentSeveringApproaches(int numberOfTimesToPlay)
        {
            TabbedText.WriteLine("Claim size: " + claim1Size + "," + claim2Size + " litigation costs: " + pCosts + "," + dCosts + " playing: " + numberOfTimesToPlay + " each test");
            TabbedText.Tabs++;

            TabbedText.WriteLine("Aggregated together into single negotiation round");
            FindSymmetricSurplusesFromSingleStartingPoint(false, false, true, numberOfTimesToPlay, false);

            TabbedText.WriteLine("Separately negotiated; no effect of partial settlements");
            FindSymmetricSurplusesFromSingleStartingPoint(false, false, false, numberOfTimesToPlay, false);

            TabbedText.WriteLine("Separately negotiated; claim 1 sticks if claim 2 fails (severed)");
            FindSymmetricSurplusesFromSingleStartingPoint(true, false, false, numberOfTimesToPlay, false);

            TabbedText.WriteLine("Separately negotiated; either claim sticks if other fails (severable)");
            FindSymmetricSurplusesFromSingleStartingPoint(true, true, false, numberOfTimesToPlay, false);

            TabbedText.Tabs--;
        }

        public void FindSymmetricSurplusesFromSingleStartingPoint(
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool report = true)
        {
            if (report)
                TabbedText.WriteLine("Claim size: " + claim1Size + "," + claim2Size + " litigation costs: " + pCosts + "," + dCosts + " resolution of claims sticks: " + resolutionOfClaim1Sticks + " and " + resolutionOfClaim2Sticks + " playing: " + numberOfTimesToPlay + " each test");
            TabbedText.Tabs++;
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, 0, 0, true);
            TabbedText.Tabs--;
        }

        public void FindSymmetricSurplusesFromDifferentStartingPoints(
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay)
        {
            TabbedText.WriteLine("Claim size: " + claim1Size + "," + claim2Size + " litigation costs: " + pCosts + "," + dCosts + " resolution of claims sticks: " + resolutionOfClaim1Sticks + " and " + resolutionOfClaim2Sticks + " playing: " + numberOfTimesToPlay + " each test");
            TabbedText.Tabs++;
            TabbedText.WriteLine("Starting to optimize claim 1 surplus based on claim 2 of 0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, 0, 0, true);
            TabbedText.WriteLine("Starting to optimize claim 1 surplus based on claim 2 of -1.0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, 0, -1.0, true);
            TabbedText.WriteLine("Starting to optimize claim 1 surplus based on claim 2 of 1.0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, 0, 1.0, true);
            TabbedText.WriteLine("Starting to optimize claim 2 surplus based on claim 1 of 0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, -1.0, 0, false);
            TabbedText.WriteLine("Starting to optimize claim 2 surplus based on claim 1 of -1.0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, -1.0, 0, false);
            TabbedText.WriteLine("Starting to optimize claim 2 surplus based on claim 1 of 1.0");
            RepeatedlyFindSymmetricSurplusAndDropCutoffs(resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, 1.0, 0, false);
            TabbedText.Tabs--;
        }

        public void RepeatedlyFindSymmetricSurplusAndDropCutoffs(
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, double startValueClaim1, double startValueClaim2, bool startWithClaim1)
        {
            TabbedText.Tabs++;
            Tuple<double, double> surplusForClaims = new Tuple<double, double>(startValueClaim1, startValueClaim2);
            Tuple<double, double> dropCutoffs = new Tuple<double, double>(0, 0);
            Tuple<double, double> dNegotiationCutoffs = new Tuple<double, double>(0, 0);
            randomVariant += 100;
            TabbedText.WriteLine("Initial status: ");
            PlayManyTimesAndReport(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
            for (int i = 0; i < 8; i++)
            {
                randomVariant++;
                bool optimizeForClaim1 = i % 2 == (startWithClaim1 ? 0 : 1);
                if (aggregateTogether)
                    optimizeForClaim1 = true; // claim 2 is ignored in this scenario

                //double result_orig = FindSymmetricSurplusPortion_3(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, optimizeForClaim1);
                //randomVariant++;

                double symmetricSurplusResult = FindSymmetricSurplusPortion_3(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, optimizeForClaim1);
                if (optimizeForClaim1)
                    surplusForClaims = new Tuple<double, double>(symmetricSurplusResult, surplusForClaims.Item2);
                else
                    surplusForClaims = new Tuple<double, double>(surplusForClaims.Item1, symmetricSurplusResult);
                TabbedText.WriteLine("Surplus: " + surplusForClaims.Item1 + ", " + surplusForClaims.Item2);
                PlayManyTimesAndReport(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                if (pMayDropAtBeginning)
                {
                    double dropCutoffResult = FindDropCutoff(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, optimizeForClaim1);
                    if (optimizeForClaim1)
                        dropCutoffs = new Tuple<double, double>(dropCutoffResult, dropCutoffs.Item2);
                    else
                        dropCutoffs = new Tuple<double, double>(dropCutoffs.Item1, dropCutoffResult);
                    TabbedText.WriteLine("Drop cutoffs: " + dropCutoffs.Item1 + ", " + dropCutoffs.Item2);
                    PlayManyTimesAndReport(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                }
                if (pMayDropAtEnd && dMayStopNegotiatingBasedOnPEndDrop)
                {
                    double negotiationCutoff = FindDNegotiationCutoff(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay, optimizeForClaim1);
                    if (optimizeForClaim1)
                        dNegotiationCutoffs = new Tuple<double, double>(negotiationCutoff, dNegotiationCutoffs.Item2);
                    else
                        dNegotiationCutoffs = new Tuple<double, double>(dNegotiationCutoffs.Item1, negotiationCutoff);
                    TabbedText.WriteLine("D negotiation cutoffs: " + dNegotiationCutoffs.Item1 + ", " + dNegotiationCutoffs.Item2);
                    PlayManyTimesAndReport(surplusForClaims.Item1, surplusForClaims.Item2, surplusForClaims.Item1, surplusForClaims.Item2, dropCutoffs.Item1, dropCutoffs.Item2, dNegotiationCutoffs.Item1, dNegotiationCutoffs.Item2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                }
            }
            TabbedText.Tabs--;
        }

        public double FindDropCutoff(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1)
        {
            double cutoff = StochasticCutoffFinder.FindCutoff(true, 0.0, 1.0, numberOfTimesToPlay, Int32.MaxValue, true, 10, 0.10, true, true,
                (StochasticCutoffFinderInputs scfi, long iter) =>
                {
                    double dropCutoffClaim1ToTest = dropCutoffClaim1;
                    double dropCutoffClaim2ToTest = dropCutoffClaim2;
                    if (findForClaim1)
                        dropCutoffClaim1ToTest = 1.0;
                    else
                        dropCutoffClaim2ToTest = 1.0;

                    double cutoffVariableValue;
                    Outcome cuttingOff = PlayOnceAndReturnCutoff((int)iter, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1ToTest, dropCutoffClaim2ToTest, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, false /* findDNegotiationCutoff */, findForClaim1, scfi.TentativeCutoff, scfi.MaxRangeFromCutoff, out cutoffVariableValue);
                    if (cuttingOff == null)
                        return null;
                    else
                    {
                        if (findForClaim1)
                            dropCutoffClaim1ToTest = 0.0;
                        else
                            dropCutoffClaim2ToTest = 0.0;

                        Outcome notCuttingOff = PlayOnceAndReturnCutoff((int)iter, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1ToTest, dropCutoffClaim2ToTest, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, false /* findDNegotiationCutoff */, findForClaim1, scfi.TentativeCutoff, scfi.MaxRangeFromCutoff, out cutoffVariableValue);
                        return new StochasticCutoffFinderOutputs() { InputVariable = cutoffVariableValue, Weight = 1.0, Score = cuttingOff.pOutcome - notCuttingOff.pOutcome };
                    }
                });
            return cutoff;
        }


        public double FindDNegotiationCutoff(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1)
        {
            double cutoff = StochasticCutoffFinder.FindCutoff(true, 0.0, 1.0, numberOfTimesToPlay, Int32.MaxValue, true, 10, 0.10, true, true,
                (StochasticCutoffFinderInputs scfi, long iter) =>
                {
                    double negotiationCutoffClaim1ToTest = dNegotiationCutoffClaim1;
                    double negotiationCutoffClaim2ToTest = dNegotiationCutoffClaim2;
                    // test with cutting off
                    if (findForClaim1)
                        negotiationCutoffClaim1ToTest = 1.0;
                    else
                        negotiationCutoffClaim2ToTest = 1.0;
                    double cutoffVariableValue;
                    Outcome cuttingOff = PlayOnceAndReturnCutoff((int)iter, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, negotiationCutoffClaim1ToTest, negotiationCutoffClaim2ToTest, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, true /* findDNegotiationCutoff */, findForClaim1, scfi.TentativeCutoff, scfi.MaxRangeFromCutoff, out cutoffVariableValue);
                    if (cuttingOff == null)
                        return null; // not close enough to cutoff to be worth testing
                    else
                    {
                        if (findForClaim1)
                            negotiationCutoffClaim1ToTest = 0.0;
                        else
                            negotiationCutoffClaim2ToTest = 0.0;

                        Outcome notCuttingOff = PlayOnceAndReturnCutoff((int)iter, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, negotiationCutoffClaim1ToTest, negotiationCutoffClaim2ToTest, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, true /* findDNegotiationCutoff */, findForClaim1, scfi.TentativeCutoff, scfi.MaxRangeFromCutoff, out cutoffVariableValue);
                        return new StochasticCutoffFinderOutputs() { InputVariable = cutoffVariableValue, Weight = 1.0, Score = cuttingOff.dOutcome - notCuttingOff.dOutcome };
                    }
                });
            return cutoff;
        }

        public double FindSymmetricSurplusPortion(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1)
        {
            double currentSurplusToCheck = findForClaim1 ? pPortionOfSurplusInsistedUponClaim1 : pPortionOfSurplusInsistedUponClaim2;
            int repetitions = 1000;
            double curvature = CurveDistribution.CalculateCurvature(0.3, 0.005, 0.1);
            bool positive = true;
            bool plaintiff = true;
            for (int i = 0; i < repetitions; i++)
            {
                Outcome asBefore = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                double pIncrementClaim1 = 0, pIncrementClaim2 = 0, dIncrementClaim1 = 0, dIncrementClaim2 = 0;
                double increment = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(0.3, 0.01, curvature, ((double)i / (double)repetitions));
                if (plaintiff && findForClaim1)
                    pIncrementClaim1 = increment;
                else if (plaintiff)
                    pIncrementClaim2 = increment;
                else if (findForClaim1)
                    dIncrementClaim1 = increment;
                else
                    dIncrementClaim2 = increment;
                Outcome higher = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1 + pIncrementClaim1, pPortionOfSurplusInsistedUponClaim2 + pIncrementClaim2, dPortionOfSurplusInsistedUponClaim1 + dIncrementClaim1, dPortionOfSurplusInsistedUponClaim2 + dIncrementClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                Outcome lower = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1 - pIncrementClaim1, pPortionOfSurplusInsistedUponClaim2 - pIncrementClaim2, dPortionOfSurplusInsistedUponClaim1 - dIncrementClaim1, dPortionOfSurplusInsistedUponClaim2 - dIncrementClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                if (plaintiff)
                {
                    if (higher.pOutcome > asBefore.pOutcome && higher.pOutcome > lower.pOutcome)
                        ; // we will increment
                    else if (lower.pOutcome > asBefore.pOutcome && lower.pOutcome > higher.pOutcome)
                        increment = 0 - increment; // we will decrement
                    else
                        increment = 0;
                }
                else
                {
                    if (higher.dOutcome > asBefore.dOutcome && higher.dOutcome > lower.dOutcome)
                        ; // we will increment
                    else if (lower.dOutcome > asBefore.dOutcome && lower.dOutcome > higher.dOutcome)
                        increment = 0 - increment; // we will decrement
                    else
                        increment = 0;
                }
                if (plaintiff && findForClaim1)
                    pPortionOfSurplusInsistedUponClaim1 += increment;
                else if (plaintiff)
                    pPortionOfSurplusInsistedUponClaim2 += increment;
                else if (findForClaim1)
                    dPortionOfSurplusInsistedUponClaim1 += increment;
                else
                    dPortionOfSurplusInsistedUponClaim2 += increment;

                if (plaintiff && findForClaim1)
                    dPortionOfSurplusInsistedUponClaim1 = pPortionOfSurplusInsistedUponClaim1;
                else if (plaintiff)
                    dPortionOfSurplusInsistedUponClaim2 = pPortionOfSurplusInsistedUponClaim2;
                else if (findForClaim1)
                    pPortionOfSurplusInsistedUponClaim1 = dPortionOfSurplusInsistedUponClaim1;
                else
                    pPortionOfSurplusInsistedUponClaim2 = dPortionOfSurplusInsistedUponClaim2;
                plaintiff = !plaintiff; // check for other party
            }
            double returnVal = findForClaim1 ? (pPortionOfSurplusInsistedUponClaim1 + dPortionOfSurplusInsistedUponClaim1) / 2.0 : (pPortionOfSurplusInsistedUponClaim2 + dPortionOfSurplusInsistedUponClaim2) / 2.0;
            TabbedText.WriteLine("Symmetric surplus for claim " + (findForClaim1 ? "1 " : "2 ") + ": " + returnVal);
            return returnVal;
        }

        public double FindSymmetricSurplusPortion_2(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1)
        {
            TabbedText.Tabs++;
            double currentSurplusToCheck = findForClaim1 ? pPortionOfSurplusInsistedUponClaim1 : pPortionOfSurplusInsistedUponClaim2;
            double increment = 0.01;
            bool keepGoing = true;
            bool firstTime = true;
            int consecutiveFailures = 0;
            double bestSoFar = currentSurplusToCheck;
            Outcome bothPartiesSame = null;
            while (keepGoing)
            {
                // both parties insist on current surplus
                if (findForClaim1)
                {
                    pPortionOfSurplusInsistedUponClaim1 = currentSurplusToCheck;
                    dPortionOfSurplusInsistedUponClaim1 = currentSurplusToCheck;
                }
                else
                {
                    pPortionOfSurplusInsistedUponClaim2 = currentSurplusToCheck;
                    dPortionOfSurplusInsistedUponClaim2 = currentSurplusToCheck;
                }
                bothPartiesSame = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                // now we see if changing the surplus sought makes for a better result.
                if (findForClaim1)
                    pPortionOfSurplusInsistedUponClaim1 += increment;
                else
                    pPortionOfSurplusInsistedUponClaim2 += increment;
                Outcome pIncrements = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                bool improvement = pIncrements.pOutcome > bothPartiesSame.pOutcome;
                if (improvement)
                {
                    if (consecutiveFailures == 0) // we've had at least two straight successes, so that's good
                        bestSoFar = currentSurplusToCheck;
                    else
                        consecutiveFailures = 0;
                    keepGoing = true;
                }
                else
                {
                    consecutiveFailures++;
                    keepGoing = consecutiveFailures < 2;
                }
                if (firstTime && !improvement)
                {
                    increment = 0 - increment; // go downward instead of upward
                    keepGoing = true;
                    consecutiveFailures = 0;
                }
                firstTime = false;
                if (keepGoing)
                    currentSurplusToCheck += increment;
            }
            double returnVal = bestSoFar;
            TabbedText.Tabs--;

            return returnVal;
        }


        public double FindSymmetricSurplusPortion_3(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1)
        {
            double result = FindSymmetricSurplusPortion_3_helper(
                pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2,
                dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2,
                dropCutoffClaim1, dropCutoffClaim2,
                dNegotiationCutoffClaim1, dNegotiationCutoffClaim2,
                resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether,
                numberOfTimesToPlay, findForClaim1, -2.0, 0.50, 9
                    );
            result = FindSymmetricSurplusPortion_3_helper(
                pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2,
                dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2,
                dropCutoffClaim1, dropCutoffClaim2,
                dNegotiationCutoffClaim1, dNegotiationCutoffClaim2,
                resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether,
                numberOfTimesToPlay, findForClaim1, result - 0.25, 0.10, 6
                    );
            result = FindSymmetricSurplusPortion_3_helper(
                pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2,
                dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2,
                dropCutoffClaim1, dropCutoffClaim2,
                dNegotiationCutoffClaim1, dNegotiationCutoffClaim2,
                resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether,
                numberOfTimesToPlay, findForClaim1, result - .125, 0.025, 11
                    );
            result = FindSymmetricSurplusPortion_3_helper(
                pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2,
                dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2,
                dropCutoffClaim1, dropCutoffClaim2,
                dNegotiationCutoffClaim1, dNegotiationCutoffClaim2,
                resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether,
                numberOfTimesToPlay, findForClaim1, result - .0125, 0.005, 6, true
                    );
            return result;

        }

        public double FindSymmetricSurplusPortion_3_helper(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay, bool findForClaim1, double minSurplusToCheck, double increment, int numSurplusToCheck, bool reportResult = false)
        {
            TabbedText.Tabs++;
            double currentSurplusToCheck = minSurplusToCheck;
            double[] benefitOfCheating = new double[numSurplusToCheck];
            List<double> equilibria = new List<double>();
            Outcome bothPartiesSame = null;
            bool currentlyAboveLine = false, previouslyAboveLine = false;
            for (int s = 0; s < numSurplusToCheck; s++)
            {
                currentSurplusToCheck = minSurplusToCheck + ((double)s) * increment;
                // both parties insist on current surplus
                if (findForClaim1)
                {
                    pPortionOfSurplusInsistedUponClaim1 = currentSurplusToCheck;
                    dPortionOfSurplusInsistedUponClaim1 = currentSurplusToCheck;
                }
                else
                {
                    pPortionOfSurplusInsistedUponClaim2 = currentSurplusToCheck;
                    dPortionOfSurplusInsistedUponClaim2 = currentSurplusToCheck;
                }
                bothPartiesSame = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                // now we see if changing the surplus sought makes for a better result.
                if (findForClaim1)
                    pPortionOfSurplusInsistedUponClaim1 += increment;
                else
                    pPortionOfSurplusInsistedUponClaim2 += increment;
                Outcome pIncrements = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
                TabbedText.Tabs++;
                benefitOfCheating[s] = pIncrements.pOutcome - bothPartiesSame.pOutcome;
                //TabbedText.WriteLine(currentSurplusToCheck + ": " + benefitOfCheating[s]);
                TabbedText.Tabs--;
                bool thisAboveLine = benefitOfCheating[s] > currentSurplusToCheck;
                if (s == 0)
                    currentlyAboveLine = thisAboveLine;
                else
                {
                    previouslyAboveLine = currentlyAboveLine;
                    currentlyAboveLine = thisAboveLine;
                    if (currentlyAboveLine != previouslyAboveLine)
                        equilibria.Add(currentSurplusToCheck - 0.5 * increment);
                }
            }
            double returnVal = equilibria.Any() ? equilibria.First() :
                (currentlyAboveLine ? currentSurplusToCheck : minSurplusToCheck);
            // (minSurplusToCheck + ((double)numSurplusToCheck / 2.0) * increment);
            TabbedText.Tabs--;
            if (reportResult)
            {
                if (findForClaim1)
                {
                    pPortionOfSurplusInsistedUponClaim1 = returnVal;
                    dPortionOfSurplusInsistedUponClaim1 = returnVal;
                }
                else
                {
                    pPortionOfSurplusInsistedUponClaim2 = returnVal;
                    dPortionOfSurplusInsistedUponClaim2 = returnVal;
                }
            }
            return returnVal;
        }

        public void PlayManyTimesAndReport(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay)
        {
            TabbedText.Tabs++;
            var results = PlayManyTimes(pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether, numberOfTimesToPlay);
            results.OutputResolutionSummary();
            TabbedText.Tabs--;
        }

        public Outcome PlayManyTimes(
            double pPortionOfSurplusInsistedUponClaim1, double pPortionOfSurplusInsistedUponClaim2,
            double dPortionOfSurplusInsistedUponClaim1, double dPortionOfSurplusInsistedUponClaim2,
            double dropCutoffClaim1, double dropCutoffClaim2, double dNegotiationCutoffClaim1, double dNegotiationCutoffClaim2,
            bool resolutionOfClaim1Sticks, bool resolutionOfClaim2Sticks, bool aggregateTogether,
            int numberOfTimesToPlay)
        {
            RandomGeneratorInstanceManager.Reset(false, false); // always come back to same set of random numbers
            Outcome combined = new Outcome();
            object lockObj = new object();
            Parallel.For(0, numberOfTimesToPlay, i =>
            { // not worth parallelizing, and that would mess up random numbers
                Outcome o = PlayOnce(i, pPortionOfSurplusInsistedUponClaim1, pPortionOfSurplusInsistedUponClaim2, dPortionOfSurplusInsistedUponClaim1, dPortionOfSurplusInsistedUponClaim2, dropCutoffClaim1, dropCutoffClaim2, dNegotiationCutoffClaim1, dNegotiationCutoffClaim2, resolutionOfClaim1Sticks, resolutionOfClaim2Sticks, aggregateTogether);
                lock (lockObj)
                {
                    combined.pOutcome += o.pOutcome;
                    combined.dOutcome += o.dOutcome;
                    combined.claim1Dropped += o.claim1Dropped;
                    combined.claim2Dropped += o.claim2Dropped;
                    combined.bothClaimsDropped += o.bothClaimsDropped;
                    combined.claim1Settled += o.claim1Settled;
                    combined.claim2Settled += o.claim2Settled;
                    combined.bothClaimsSettled += o.bothClaimsSettled;
                    combined.noTrial += o.noTrial;
                    combined.trial += o.trial;
                    combined.compensationError += o.compensationError;
                    combined.deterrenceError += o.deterrenceError;
                    combined.absCompensationError += o.absCompensationError;
                    combined.absDeterrenceError += o.absDeterrenceError;
                }
            });
            //TabbedText.WriteLine("p1 p2 d1 d2 " + pPortionOfSurplusInsistedUponClaim1 + " " + pPortionOfSurplusInsistedUponClaim2 + " " + dPortionOfSurplusInsistedUponClaim1 + " " + dPortionOfSurplusInsistedUponClaim2 + ": " + combined.pOutcome + ", " + combined.dOutcome);
            return combined;
        }

    }

}

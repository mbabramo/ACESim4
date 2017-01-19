using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public class LitigationGameProbMagnitudeSimpleModel
    {
        //const int numStrategyPointsEachDimension = 10;
        //const int numRepetitionsPerOptimizationRound = 100000;
        //const int numOptimizationRoundsPerStage = 50;
        //const int numRepetitionsPerReportingRound = 1000000;
        const int numStrategyPointsEachDimension = 30;
        const int numValuesToTestEachDimensionForContinuousStrategies = 100;
        const int numRepetitionsPerOptimizationRound = 3000000;
        const int numOptimizationRoundsPerStage = 50;
        const int numRepetitionsPerReportingRound = 1000000;
        static double? alwaysMakeMagnitude = null;
        static double? alwaysMakeProbability = null;
        static double? alwaysDefinitivelySettleMagnitudeAt = null; // note: this must be between 0 and 1.0
        static double? alwaysDefinitivelySettleProbabilityAt = null; 
        static bool optimizeEverythingInStage0 = false;

        const double stepSizeForTestingContinuousStrategies = 1.0 / (double)numValuesToTestEachDimensionForContinuousStrategies;
        const double firstValueToTestContinuousStrategies = stepSizeForTestingContinuousStrategies / 2.0;

        static double? lockPProbabilityOfferForTesting = null;
        static double? lockDProbabilityOfferForTesting = null;
        static double? lockPMagnitudeOfferForTesting = null;
        static double? lockDMagnitudeOfferForTesting = null;

        public class SeveringRules
        {
            public bool probabilitySettlementSticks;
            public bool magnitudeSettlementSticks;
        }

        public class MiscGameParameters
        {
            public double maxMagnitudeInDollars;
            public double minMagnitudeInDollars;
            public double? minMaxRatio;
            public double eachPartyTrialCost;
            public double pNoise;
            public double dNoise;
            public double tasteForSettlement;
            public double? blockSettlementsBelow;
            public double? blockSettlementsAbove;
            public double? blockSettlementsBelowOriginal;
            public double? blockSettlementsAboveOriginal;

            public MiscGameParameters DeepCopy()
            {
                return new MiscGameParameters()
                {
                    maxMagnitudeInDollars = maxMagnitudeInDollars,
                    minMagnitudeInDollars = minMagnitudeInDollars,
                    minMaxRatio = minMaxRatio,
                    eachPartyTrialCost = eachPartyTrialCost,
                    pNoise = pNoise,
                    dNoise = dNoise,
                    tasteForSettlement = tasteForSettlement,
                    blockSettlementsBelow = blockSettlementsBelow,
                    blockSettlementsAbove = blockSettlementsAbove,
                    blockSettlementsBelowOriginal = blockSettlementsBelowOriginal,
                    blockSettlementsAboveOriginal = blockSettlementsAboveOriginal
                };
            }
        }

        public class OptimizationParameters
        {
            public long numIterationsEachRepeatedPlay = 1000000;
            public double probabilityRandomStrategy = 0;
            public bool moveOnlyHalfWay = true;
            public bool moveSizeIsMaximum = true;
            public double moveSize = 0;
            public double weightToPlaceOnRandomOffer = 0;
            public bool reportMagnitudeOffers = false;
            public bool reportProbabilityOffers = false;
            public int stage;
        }

        public class CaseQualityAndEstimates
        {
            public double actualProbability;
            public double pEstProbability;
            public double dEstProbability;
            public double actualMagnitude;
            public double pEstMagnitude;
            public double dEstMagnitude;
            const int numRan = 6;
            double[] rans = new double[numRan];

            public void SetBasedOnIteration(long iteration, MiscGameParameters mgp)
            {
                for (int i = 0; i < numRan; i++)
                    rans[i] = RandomGenerator.NextDouble(); // FastPseudoRandom.GetRandom(iteration * 10 + i, 419 /* arbitrary prime number */);

                actualProbability = alwaysMakeProbability ?? rans[0];
                double pNoise = mgp.pNoise * alglib.normaldistr.invnormaldistribution(rans[1]);
                double dNoise = mgp.dNoise * alglib.normaldistr.invnormaldistribution(rans[2]);
                double pSignal = actualProbability + pNoise;
                double dSignal = actualProbability + dNoise;
                if (pNoise == 0)
                    pEstProbability = actualProbability;
                else
                    pEstProbability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(pNoise, pSignal);
                if (dNoise == 0)
                    dEstProbability = actualProbability;
                else
                    dEstProbability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(dNoise, dSignal);
                
                actualMagnitude = alwaysMakeMagnitude ?? rans[3];
                pNoise = mgp.pNoise * alglib.normaldistr.invnormaldistribution(rans[4]);
                dNoise = mgp.dNoise * alglib.normaldistr.invnormaldistribution(rans[5]);
                pSignal = actualMagnitude + pNoise;
                dSignal = actualMagnitude + dNoise;
                if (pNoise == 0)
                    pEstMagnitude = actualMagnitude;
                else
                    pEstMagnitude = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(pNoise, pSignal);
                if (dNoise == 0)
                    dEstMagnitude = actualMagnitude;
                else
                    dEstMagnitude = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(dNoise, dSignal);
                if (mgp.minMagnitudeInDollars > 0)
                {
                    double minMaxRatio = (double) mgp.minMaxRatio;
                    double range = 1.0 - minMaxRatio;
                    actualMagnitude = minMaxRatio + actualMagnitude * range;
                    pEstMagnitude = minMaxRatio + pEstMagnitude * range;
                    dEstMagnitude = minMaxRatio + dEstMagnitude * range;
                }
            }
        }

        public class StrategiesGivenEstimates
        {
            public bool pRefusesToNegotiateProbability;
            public bool dRefusesToNegotiateProbability;
            public double pProbabilityOfferIfNegotiating;
            public double dProbabilityOfferIfNegotiating;
            public double pMagnitudeOffer;
            public double dMagnitudeOffer;
            public bool pDropsAbsentSettlementIfNeitherSettled;
            public bool dDefaultsAbsentSettlementIfNeitherSettled;
            public bool pDropsAbsentSettlementIfProbabilityOnlySettled;
            public bool dDefaultsAbsentSettlementIfProbabilityOnlySettled;
            public bool pDropsAbsentSettlementIfMagnitudeOnlySettled;
            public bool dDefaultsAbsentSettlementIfMagnitudeOnlySettled;
            public bool pDowngradesProbabilityIfNecessary;
            public bool dUpgradesProbabilityIfNecessary;
            public double probabilityOfferBeforeNoiseAdded;
            public double magnitudeOfferBeforeNoiseAdded;
            public bool originalMagnitudeOnlySettledBeforePrevent;
            public bool originalProbabilityOnlySettledBeforePrevent;
            public bool originalNeitherSettledBeforePrevent;


            public double GetFieldByIndex(int fieldIndex)
            {
                double currentValue;
                switch (fieldIndex)
                {
                    case 0:
                        currentValue = pProbabilityOfferIfNegotiating;
                        break;
                    case 1:
                        currentValue = dProbabilityOfferIfNegotiating;
                        break;
                    case 2:
                        currentValue = pMagnitudeOffer;
                        break;
                    case 3:
                        currentValue = dMagnitudeOffer;
                        break;
                    case 4:
                        currentValue = pRefusesToNegotiateProbability ? 1.0 : 0.0;
                        break;
                    case 5:
                        currentValue = dRefusesToNegotiateProbability ? 1.0 : 0.0;
                        break;
                    case 6:
                        currentValue = pDropsAbsentSettlementIfNeitherSettled ? 1.0 : 0.0;
                        break;
                    case 7:
                        currentValue = dDefaultsAbsentSettlementIfNeitherSettled ? 1.0 : 0.0;
                        break;
                    case 8:
                        currentValue = pDropsAbsentSettlementIfProbabilityOnlySettled ? 1.0 : 0.0;
                        break;
                    case 9:
                        currentValue = dDefaultsAbsentSettlementIfProbabilityOnlySettled ? 1.0 : 0.0;
                        break;
                    case 10:
                        currentValue = pDropsAbsentSettlementIfMagnitudeOnlySettled ? 1.0 : 0.0;
                        break;
                    case 11:
                        currentValue = dDefaultsAbsentSettlementIfMagnitudeOnlySettled ? 1.0 : 0.0;
                        break;
                    case 12:
                        currentValue = pDowngradesProbabilityIfNecessary ? 1.0 : 0.0;
                        break;
                    case 13:
                        currentValue = dUpgradesProbabilityIfNecessary ? 1.0 : 0.0;
                        break;
                    default:
                        throw new Exception();
                }
                return currentValue;
            }

            public void SetFieldByIndex(int fieldIndex, double fieldValue)
            {
                double oldValue;
                switch (fieldIndex)
                {
                    case 0:
                        oldValue = pProbabilityOfferIfNegotiating;
                        pProbabilityOfferIfNegotiating = fieldValue;
                        break;
                    case 1:
                        oldValue = dProbabilityOfferIfNegotiating;
                        dProbabilityOfferIfNegotiating = fieldValue;
                        break;
                    case 2:
                        oldValue = pMagnitudeOffer;
                        pMagnitudeOffer = fieldValue;
                        break;
                    case 3:
                        oldValue = dMagnitudeOffer;
                        dMagnitudeOffer = fieldValue;
                        break;
                    case 4:
                        oldValue = pRefusesToNegotiateProbability ? 1.0 : 0.0;
                        pRefusesToNegotiateProbability = fieldValue > 0.5;
                        break;
                    case 5:
                        oldValue = dRefusesToNegotiateProbability ? 1.0 : 0.0;
                        dRefusesToNegotiateProbability = fieldValue > 0.5;
                        break;
                    case 6:
                        oldValue = pDropsAbsentSettlementIfNeitherSettled ? 1.0 : 0.0;
                        pDropsAbsentSettlementIfNeitherSettled = fieldValue > 0.5;
                        break;
                    case 7:
                        oldValue = dDefaultsAbsentSettlementIfNeitherSettled ? 1.0 : 0.0;
                        dDefaultsAbsentSettlementIfNeitherSettled = fieldValue > 0.5;
                        break;
                    case 8:
                        oldValue = pDropsAbsentSettlementIfProbabilityOnlySettled ? 1.0 : 0.0;
                        pDropsAbsentSettlementIfProbabilityOnlySettled = fieldValue > 0.5;
                        break;
                    case 9:
                        oldValue = dDefaultsAbsentSettlementIfProbabilityOnlySettled ? 1.0 : 0.0;
                        dDefaultsAbsentSettlementIfProbabilityOnlySettled = fieldValue > 0.5;
                        break;
                    case 10:
                        oldValue = pDropsAbsentSettlementIfMagnitudeOnlySettled ? 1.0 : 0.0;
                        pDropsAbsentSettlementIfMagnitudeOnlySettled = fieldValue > 0.5;
                        break;
                    case 11:
                        oldValue = dDefaultsAbsentSettlementIfMagnitudeOnlySettled ? 1.0 : 0.0;
                        dDefaultsAbsentSettlementIfMagnitudeOnlySettled = fieldValue > 0.5;
                        break;
                    case 12:
                        oldValue = pDowngradesProbabilityIfNecessary ? 1.0 : 0.0;
                        pDowngradesProbabilityIfNecessary = fieldValue > 0.5;
                        break;
                    case 13:
                        oldValue = dUpgradesProbabilityIfNecessary ? 1.0 : 0.0;
                        dUpgradesProbabilityIfNecessary = fieldValue > 0.5;
                        break;
                    default:
                        throw new Exception();
                }
                //return oldValue;
            }

            public double SetFieldByIndexToHigherOrLowerValue(int fieldIndex, bool higher, bool isBoolean, double moveSize, bool moveSizeIsMaximum)
            {
                double oldValue;
                if (isBoolean)
                {
                    oldValue = GetFieldByIndex(fieldIndex);
                    if (higher)
                        SetFieldByIndex(fieldIndex, 1.0);
                    else
                        SetFieldByIndex(fieldIndex, 0.0);
                    return oldValue;
                }
                else
                {
                    double moveSizeAdjusted = moveSize;
                    if (moveSizeIsMaximum)
                        moveSizeAdjusted = RandomGenerator.NextDouble(0, moveSizeAdjusted);
                    oldValue = GetFieldByIndex(fieldIndex);
                    double newValue = oldValue;
                    if (higher)
                    {
                        newValue = oldValue + moveSizeAdjusted;
                        if (newValue > 1.0)
                            newValue = 1.0;
                    }
                    else
                    {
                        newValue = oldValue - moveSizeAdjusted;
                        if (newValue < 0)
                            newValue = 0.0;
                    }
                    SetFieldByIndex(fieldIndex, newValue);
                    return oldValue;
                }
            }
        }

        public class Strategies
        {
            public Strategy pRefusesToNegotiateProbability = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = true, isProbabilityRelated = true, stageToStartOptimizing = 1 };
            public Strategy dRefusesToNegotiateProbability = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = false, isProbabilityRelated = true, stageToStartOptimizing = 1 };
            public Strategy pProbabilityOfferIfNegotiating = new Strategy(0.5, true) { isBoolStrategy = false, isPlaintiffStrategy = true, isProbabilityRelated = true,  stageToStartOptimizing = 0, stageToStopOptimizing = 1 };
            public Strategy dProbabilityOfferIfNegotiating = new Strategy(0.5, true) { isBoolStrategy = false, isPlaintiffStrategy = false, isProbabilityRelated = true, stageToStartOptimizing = 0, stageToStopOptimizing = 1 };
            public Strategy pMagnitudeOffer = new Strategy(null, true) { isBoolStrategy = false, isPlaintiffStrategy = true, stageToStartOptimizing = 0, stageToStopOptimizing = 1 };
            public Strategy dMagnitudeOffer = new Strategy(null, true) { isBoolStrategy = false, isPlaintiffStrategy = false, stageToStartOptimizing = 0, stageToStopOptimizing = 1 };
            public Strategy pDropsAbsentSettlementIfNeitherSettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = true, stageToStartOptimizing = 1 };
            public Strategy dDefaultsAbsentSettlementIfNeitherSettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = false, stageToStartOptimizing = 1 };
            public Strategy pDropsAbsentSettlementIfProbabilityOnlySettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = true, stageToStartOptimizing = 1 };
            public Strategy dDefaultsAbsentSettlementIfProbabilityOnlySettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = false, stageToStartOptimizing = 1 };
            public Strategy pDropsAbsentSettlementIfMagnitudeOnlySettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = true, stageToStartOptimizing = 1 };
            public Strategy dDefaultsAbsentSettlementIfMagnitudeOnlySettled = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = false, stageToStartOptimizing = 1 };
            public Strategy pDowngradesProbabilityIfNecessary = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = true, isProbabilityRelated = true, stageToStartOptimizing = 1 };
            public Strategy dUpgradesProbabilityIfNecessary = new Strategy(0, false) { isBoolStrategy = true, isPlaintiffStrategy = false, isProbabilityRelated = true, stageToStartOptimizing = 1 };
            public List<Strategy> AllStrategies;
            public List<bool?> NoiseShouldBeAddedToPlaintiffWhenStrategyIsBeingOptimized; // null indicates don't add noise at all
            public List<bool?> AssumePOrDDoesntDefaultWhenStrategyIsBeingOptimized; // null indicates don't change for either party
            public List<bool> AllowTasteForSettlementWhenOptimizing;
            public OptimizationParameters op;

            public Strategies()
            {
                AllStrategies = new List<Strategy> { pProbabilityOfferIfNegotiating, dProbabilityOfferIfNegotiating, pMagnitudeOffer, dMagnitudeOffer, pRefusesToNegotiateProbability, dRefusesToNegotiateProbability, pDropsAbsentSettlementIfNeitherSettled, dDefaultsAbsentSettlementIfNeitherSettled, pDropsAbsentSettlementIfProbabilityOnlySettled, dDefaultsAbsentSettlementIfProbabilityOnlySettled, pDropsAbsentSettlementIfMagnitudeOnlySettled, dDefaultsAbsentSettlementIfMagnitudeOnlySettled, pDowngradesProbabilityIfNecessary, dUpgradesProbabilityIfNecessary };
                NoiseShouldBeAddedToPlaintiffWhenStrategyIsBeingOptimized = new List<bool?>() { false /* when p is optimized, we want to add noise to d's strategy */, true, false, true, null, null, null, null, null, null, null, null, null, null };
                AssumePOrDDoesntDefaultWhenStrategyIsBeingOptimized = new List<bool?>() { null, null, null, null, null, null, false, true, false, true, false, true, null, null };
                AllowTasteForSettlementWhenOptimizing = new List<bool>() { true, true, true, true, false, false, false, false, false, false, false, false, true, true };
            }

            public void OptimizationUpdateReport()
            {
                if (op.reportMagnitudeOffers)
                {
                    TabbedText.WriteLine("pMagnitudeOffer: ");
                    pMagnitudeOffer.Report();
                    TabbedText.WriteLine("dMagnitudeOffer: ");
                    dMagnitudeOffer.Report();
                }
                if (op.reportProbabilityOffers)
                {
                    TabbedText.WriteLine("pProbabilityOfferIfNegotiating: ");
                    pProbabilityOfferIfNegotiating.Report();
                    TabbedText.WriteLine("dProbabilityOfferIfNegotiating: ");
                    dProbabilityOfferIfNegotiating.Report();
                }
            }

            private void RegisterRelativeResults(int strategyIndex, OutputData odHigherValue, OutputData odLowerValue, CaseQualityAndEstimates cqe, MiscGameParameters mgp)
            {
                Strategy s = AllStrategies[strategyIndex];
                int probabilityIndex;
                int magnitudeIndex;
                s.GetIndicesFromEstimates(
                    s.isPlaintiffStrategy ? cqe.pEstProbability : cqe.dEstProbability,
                    s.isPlaintiffStrategy ? cqe.pEstMagnitude : cqe.dEstMagnitude,
                    mgp,
                    out probabilityIndex, out magnitudeIndex);
                double valueToRecord = s.isPlaintiffStrategy ? odHigherValue.pOutcome - odLowerValue.pOutcome : odHigherValue.dOutcome - odLowerValue.dOutcome;
                s.statCollectors[probabilityIndex, magnitudeIndex].Add(valueToRecord);
            }

            private void RegisterResultsForParticularPoint(int strategyIndex, int probabilityIndex, int magnitudeIndex, int valueToTestIndex, double result)
            {
                AllStrategies[strategyIndex].statCollectorsForContinuous[probabilityIndex, magnitudeIndex, valueToTestIndex].Add(result);
            }

            private void RegisterResultsForParticularPoint(int strategyIndex, int valueToTestIndex, double result, CaseQualityAndEstimates cqe, MiscGameParameters mgp)
            {
                Strategy s = AllStrategies[strategyIndex];
                int probabilityIndex;
                int magnitudeIndex;
                s.GetIndicesFromEstimates(
                    s.isPlaintiffStrategy ? cqe.pEstProbability : cqe.dEstProbability,
                    s.isPlaintiffStrategy ? cqe.pEstMagnitude : cqe.dEstMagnitude,
                    mgp,
                    out probabilityIndex, out magnitudeIndex);
                s.statCollectorsForContinuous[probabilityIndex, magnitudeIndex, valueToTestIndex].Add(result);
            }

            public void GetStrategiesGivenEstimates(StrategiesGivenEstimates sge, CaseQualityAndEstimates cqe, MiscGameParameters mgp)
            {
                sge.pRefusesToNegotiateProbability = pRefusesToNegotiateProbability.GetStrategyAsBool(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dRefusesToNegotiateProbability = dRefusesToNegotiateProbability.GetStrategyAsBool(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pProbabilityOfferIfNegotiating = lockPProbabilityOfferForTesting ?? pProbabilityOfferIfNegotiating.GetStrategy(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dProbabilityOfferIfNegotiating = lockDProbabilityOfferForTesting ?? dProbabilityOfferIfNegotiating.GetStrategy(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pMagnitudeOffer = lockPMagnitudeOfferForTesting ?? pMagnitudeOffer.GetStrategy(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dMagnitudeOffer = lockDMagnitudeOfferForTesting ?? dMagnitudeOffer.GetStrategy(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pDropsAbsentSettlementIfNeitherSettled = pDropsAbsentSettlementIfNeitherSettled.GetStrategyAsBool(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dDefaultsAbsentSettlementIfNeitherSettled = dDefaultsAbsentSettlementIfNeitherSettled.GetStrategyAsBool(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pDropsAbsentSettlementIfProbabilityOnlySettled = pDropsAbsentSettlementIfProbabilityOnlySettled.GetStrategyAsBool(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dDefaultsAbsentSettlementIfProbabilityOnlySettled = dDefaultsAbsentSettlementIfProbabilityOnlySettled.GetStrategyAsBool(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pDropsAbsentSettlementIfMagnitudeOnlySettled = pDropsAbsentSettlementIfMagnitudeOnlySettled.GetStrategyAsBool(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dDefaultsAbsentSettlementIfMagnitudeOnlySettled = dDefaultsAbsentSettlementIfMagnitudeOnlySettled.GetStrategyAsBool(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
                sge.pDowngradesProbabilityIfNecessary = pDowngradesProbabilityIfNecessary.GetStrategyAsBool(cqe.pEstProbability, cqe.pEstMagnitude, mgp, op);
                sge.dUpgradesProbabilityIfNecessary = dUpgradesProbabilityIfNecessary.GetStrategyAsBool(cqe.dEstProbability, cqe.dEstMagnitude, mgp, op);
            }

            public void DisableCutoffs(MiscGameParameters mgp)
            {
                mgp.blockSettlementsBelowOriginal = mgp.blockSettlementsBelow;
                mgp.blockSettlementsAboveOriginal = mgp.blockSettlementsAbove;
                mgp.blockSettlementsAbove = null;
                mgp.blockSettlementsBelow = null;
            }

            public void ReenableCutoffs(MiscGameParameters mgp)
            {
                mgp.blockSettlementsAbove = mgp.blockSettlementsAboveOriginal;
                mgp.blockSettlementsBelow = mgp.blockSettlementsBelowOriginal;
            }

            public void AddNoiseToOffersNotBeingOptimized(StrategiesGivenEstimates sge, MiscGameParameters mgp, bool addNoiseToPlaintiff)
            {
                if (addNoiseToPlaintiff)
                {
                    sge.probabilityOfferBeforeNoiseAdded = sge.pProbabilityOfferIfNegotiating;
                    sge.magnitudeOfferBeforeNoiseAdded = sge.pMagnitudeOffer;
                    sge.pProbabilityOfferIfNegotiating = (1.0 - op.weightToPlaceOnRandomOffer) * sge.pProbabilityOfferIfNegotiating + op.weightToPlaceOnRandomOffer * RandomGenerator.NextDouble();
                    sge.pMagnitudeOffer = (1.0 - op.weightToPlaceOnRandomOffer) * sge.pMagnitudeOffer + op.weightToPlaceOnRandomOffer * RandomGenerator.NextDouble((double) mgp.minMaxRatio, 1.0);
                }
                else
                {
                    sge.probabilityOfferBeforeNoiseAdded = sge.dProbabilityOfferIfNegotiating;
                    sge.magnitudeOfferBeforeNoiseAdded = sge.dMagnitudeOffer;
                    sge.dProbabilityOfferIfNegotiating = (1.0 - op.weightToPlaceOnRandomOffer) * sge.dProbabilityOfferIfNegotiating + op.weightToPlaceOnRandomOffer * RandomGenerator.NextDouble();
                    sge.dMagnitudeOffer = (1.0 - op.weightToPlaceOnRandomOffer) * sge.dMagnitudeOffer + op.weightToPlaceOnRandomOffer * RandomGenerator.NextDouble((double) mgp.minMaxRatio, 1.0);
                }
            }

            public void RemoveNoiseFromOffersNotBeingOptimized(StrategiesGivenEstimates sge, bool plaintiff)
            {
                if (plaintiff)
                {
                    sge.pProbabilityOfferIfNegotiating = sge.probabilityOfferBeforeNoiseAdded;
                    sge.pMagnitudeOffer = sge.magnitudeOfferBeforeNoiseAdded;
                }
                else
                {
                    sge.dProbabilityOfferIfNegotiating = sge.probabilityOfferBeforeNoiseAdded;
                    sge.dMagnitudeOffer = sge.magnitudeOfferBeforeNoiseAdded;
                }
            }

            public void PreventOpposingDefault(StrategiesGivenEstimates sge, bool plaintiffIsPartyToPrevent)
            {
                if (plaintiffIsPartyToPrevent)
                {
                    sge.originalMagnitudeOnlySettledBeforePrevent = sge.pDropsAbsentSettlementIfMagnitudeOnlySettled;
                    sge.originalNeitherSettledBeforePrevent = sge.pDropsAbsentSettlementIfNeitherSettled;
                    sge.originalProbabilityOnlySettledBeforePrevent = sge.pDropsAbsentSettlementIfProbabilityOnlySettled;
                    sge.pDropsAbsentSettlementIfMagnitudeOnlySettled = false;
                    sge.pDropsAbsentSettlementIfNeitherSettled = false;
                    sge.pDropsAbsentSettlementIfProbabilityOnlySettled = false;
                }
                else
                {
                    sge.originalMagnitudeOnlySettledBeforePrevent = sge.dDefaultsAbsentSettlementIfMagnitudeOnlySettled;
                    sge.originalNeitherSettledBeforePrevent = sge.dDefaultsAbsentSettlementIfNeitherSettled;
                    sge.originalProbabilityOnlySettledBeforePrevent = sge.dDefaultsAbsentSettlementIfProbabilityOnlySettled;
                    sge.dDefaultsAbsentSettlementIfMagnitudeOnlySettled = false;
                    sge.dDefaultsAbsentSettlementIfNeitherSettled = false;
                    sge.dDefaultsAbsentSettlementIfProbabilityOnlySettled = false;
                }
            }

            public void PreventOpposingDefaultReverse(StrategiesGivenEstimates sge, bool plaintiffIsPartyToPrevent)
            {
                if (plaintiffIsPartyToPrevent)
                {
                    sge.pDropsAbsentSettlementIfMagnitudeOnlySettled = sge.originalMagnitudeOnlySettledBeforePrevent;
                    sge.pDropsAbsentSettlementIfNeitherSettled = sge.originalNeitherSettledBeforePrevent;
                    sge.pDropsAbsentSettlementIfProbabilityOnlySettled = sge.originalProbabilityOnlySettledBeforePrevent;
                }
                else
                {
                    sge.dDefaultsAbsentSettlementIfMagnitudeOnlySettled = sge.originalMagnitudeOnlySettledBeforePrevent;
                    sge.dDefaultsAbsentSettlementIfNeitherSettled = sge.originalNeitherSettledBeforePrevent;
                    sge.dDefaultsAbsentSettlementIfProbabilityOnlySettled = sge.originalProbabilityOnlySettledBeforePrevent;
                }
            }


            public void PickBetterApproaches(int? pickOnlyForSpecifiedStrategy)
            {
                for (int i = 0; i < AllStrategies.Count; i++)
                {
                    if (pickOnlyForSpecifiedStrategy == null || pickOnlyForSpecifiedStrategy == i)
                        if ((AllStrategies[i].stageToStartOptimizing <= op.stage && AllStrategies[i].stageToStopOptimizing > op.stage) || optimizeEverythingInStage0)
                            AllStrategies[i].PickBetterApproaches(op.moveSizeIsMaximum || op.moveOnlyHalfWay ? op.moveSize / 2.0 : op.moveSize);
                }
            }

            public void PlayGameForEachStrategyVariation(LitigationGameProbMagnitudeSimpleModel game, GameData gameData, long iterationNumber, int? playOnlyForSpecifiedStrategy)
            {
                gameData.CaseQualityAndEstimates.SetBasedOnIteration(iterationNumber, gameData.MiscGameParameters);
                GetStrategiesGivenEstimates(gameData.PartiesStrategies, gameData.CaseQualityAndEstimates, gameData.MiscGameParameters);
                int strategiesCount = AllStrategies.Count;
                if (playOnlyForSpecifiedStrategy == null)
                {
                    for (int i = 0; i < strategiesCount; i++)
                        PlayGameForSpecificStrategy(game, gameData, i);
                }
                else
                    PlayGameForSpecificStrategy(game, gameData, (int)playOnlyForSpecifiedStrategy);

            }

            private void PlayGameForSpecificStrategy(LitigationGameProbMagnitudeSimpleModel game, GameData gameData, int i)
            {
                if ((AllStrategies[i].stageToStartOptimizing <= op.stage && AllStrategies[i].stageToStopOptimizing > op.stage) || optimizeEverythingInStage0)
                {
                    bool forOffersFigureOutScoreAcrossEntireSpectrum = true;

                    double tasteForSettlementValue = gameData.MiscGameParameters.tasteForSettlement;
                    if (!AllowTasteForSettlementWhenOptimizing[i])
                        gameData.MiscGameParameters.tasteForSettlement = 0;
                    bool? preventOpposingDefault = AssumePOrDDoesntDefaultWhenStrategyIsBeingOptimized[i];
                    if (preventOpposingDefault != null)
                        PreventOpposingDefault(gameData.PartiesStrategies, (bool)preventOpposingDefault);
                    bool? addNoise = NoiseShouldBeAddedToPlaintiffWhenStrategyIsBeingOptimized[i];
                    if (addNoise != null)
                        AddNoiseToOffersNotBeingOptimized(gameData.PartiesStrategies, gameData.MiscGameParameters, (bool)addNoise);
                    if (!optimizeEverythingInStage0 && AllStrategies[6].stageToStartOptimizing > op.stage) // not dropping yet
                        DisableCutoffs(gameData.MiscGameParameters);
                    Strategy s = AllStrategies[i];

                    if (i <= 3 && forOffersFigureOutScoreAcrossEntireSpectrum)
                        CompareResultsAcrossFullProbabilitySpectrum(game, gameData, i, s);
                    else
                        ComparePlayingHigherAndLowerValue(game, gameData, i, s);

                    if (preventOpposingDefault != null)
                        PreventOpposingDefaultReverse(gameData.PartiesStrategies, (bool)preventOpposingDefault);
                    if (addNoise != null)
                        RemoveNoiseFromOffersNotBeingOptimized(gameData.PartiesStrategies, (bool)addNoise);

                    if (!optimizeEverythingInStage0 && AllStrategies[6].stageToStartOptimizing > op.stage) // not dropping yet
                        ReenableCutoffs(gameData.MiscGameParameters);
                    gameData.MiscGameParameters.tasteForSettlement = tasteForSettlementValue; // restore original
                }
            }

            ThreadLocal<double[]> keyPoints = new ThreadLocal<double[]>(), outcomeIfMoreThanKeyPoint = new ThreadLocal<double[]>(), outcomeIfLessThanKeyPoint = new ThreadLocal<double[]>();
            private void CompareResultsAcrossFullProbabilitySpectrum(LitigationGameProbMagnitudeSimpleModel game, GameData gameData, int strategyIndex, Strategy s)
            {
                // The continuum of possible offers is from 0 to 1. But there may be points in the middle that lead to discontinuities, namely
                // where we have an offer that would just lead to settlement or just lead to one of the thresholds being hit, given the other side's offer.
                // Figure out these points in the middle.
                StrategiesGivenEstimates sge = gameData.PartiesStrategies;
                
                double? offerJustEnoughToProduceSettlement;
                if (s.isPlaintiffStrategy)
                    offerJustEnoughToProduceSettlement = s.isProbabilityRelated ? sge.dProbabilityOfferIfNegotiating : sge.dMagnitudeOffer;
                else
                    offerJustEnoughToProduceSettlement = s.isProbabilityRelated ? sge.pProbabilityOfferIfNegotiating : sge.pMagnitudeOffer;

                double? offerJustEnoughToHitBlockingThresholdOnOwnSide = null;
                double? offerJustEnoughToHitBlockingThresholdOnOtherSide = null;
                if (gameData.MiscGameParameters.blockSettlementsBelow != null)
                {
                    // Note: other sides offer == offer just enough to produce settlement
                    // So, to hit a threshold, (other sides offer + my offer) / 2.0 = threshold, so my Offer = threshold * 2.0 - other sides offer
                    if (s.isPlaintiffStrategy)
                        offerJustEnoughToHitBlockingThresholdOnOwnSide = gameData.MiscGameParameters.blockSettlementsBelow * 2.0 - offerJustEnoughToProduceSettlement;
                    else
                        offerJustEnoughToHitBlockingThresholdOnOtherSide = gameData.MiscGameParameters.blockSettlementsBelow * 2.0 - offerJustEnoughToProduceSettlement;
                }
                if (gameData.MiscGameParameters.blockSettlementsAbove != null)
                {
                    if (s.isPlaintiffStrategy)
                        offerJustEnoughToHitBlockingThresholdOnOtherSide = gameData.MiscGameParameters.blockSettlementsAbove * 2.0 - offerJustEnoughToProduceSettlement;
                    else
                        offerJustEnoughToHitBlockingThresholdOnOwnSide = gameData.MiscGameParameters.blockSettlementsAbove * 2.0 - offerJustEnoughToProduceSettlement;
                }
                if (offerJustEnoughToProduceSettlement <= 0 || offerJustEnoughToProduceSettlement >= 1)
                    offerJustEnoughToProduceSettlement = null;
                if (offerJustEnoughToHitBlockingThresholdOnOwnSide <= 0 || offerJustEnoughToHitBlockingThresholdOnOwnSide >= 1)
                    offerJustEnoughToHitBlockingThresholdOnOwnSide = null;
                if (offerJustEnoughToHitBlockingThresholdOnOtherSide <= 0 || offerJustEnoughToHitBlockingThresholdOnOtherSide >= 1)
                    offerJustEnoughToHitBlockingThresholdOnOtherSide = null;

                double[] keyPointsArray = keyPoints.Value;
                double[] outcomeIfMoreArray = outcomeIfMoreThanKeyPoint.Value;
                double[] outcomeIfLessArray = outcomeIfLessThanKeyPoint.Value;
                // Now, put the points in order. It doesn't matter whether we're dealing with plaintiff or defendant here.
                if (keyPointsArray == null)
                {
                    keyPoints.Value = keyPointsArray = new double[5];
                    outcomeIfLessThanKeyPoint.Value = outcomeIfLessArray = new double[5];
                    outcomeIfMoreThanKeyPoint.Value = outcomeIfMoreArray = new double[5];
                }
                keyPointsArray[0] = 0;
                keyPointsArray[1] = offerJustEnoughToProduceSettlement ?? 2.0; // use 2.0 to represent an out of range value, which we'll ignore
                keyPointsArray[2] = offerJustEnoughToHitBlockingThresholdOnOwnSide ?? 2.0;
                keyPointsArray[3] = offerJustEnoughToHitBlockingThresholdOnOtherSide ?? 2.0;
                keyPointsArray[4] = 1.0;
                BubbleSort(keyPointsArray);

                // Now figure out the outcome that is just above and just below each point (for end points, we just figure out one value)
                Func<double, double> getOutcomeAtPointByPlayingGame = (pt =>
                {
                    double originalValue = sge.GetFieldByIndex(strategyIndex);
                    sge.SetFieldByIndex(strategyIndex, pt);
                    game.PlayGameOnce(gameData);
                    sge.SetFieldByIndex(strategyIndex, originalValue);
                    return s.isPlaintiffStrategy ? gameData.OutputData.pOutcome : gameData.OutputData.dOutcome;
                }
                );
                for (int i = 0; i < 5; i++)
                {
                    double keyPointValue = keyPointsArray[i];
                    if (keyPointValue == 0 || keyPointValue == 1)
                    {
                        double outcome = getOutcomeAtPointByPlayingGame(keyPointValue);
                        outcomeIfLessArray[i] = outcomeIfMoreThanKeyPoint.Value[i] = outcome;
                    }
                    else if (keyPointValue > 0 && keyPointValue < 1)
                    {
                        outcomeIfLessArray[i] = getOutcomeAtPointByPlayingGame(keyPointValue - 0.0000001);
                        outcomeIfMoreArray[i] = getOutcomeAtPointByPlayingGame(keyPointValue + 0.0000001);
                    }
                }
                Func<double, double> getOutcomeAtPointByConsideringPlayedGames = (pt =>
                { // this is the whole point of the exercise -- now we can figure out outcomes without completely replaying the games.
                    int nextHigherPointIndex = 0;
                    while (nextHigherPointIndex < 4 && pt > keyPointsArray[nextHigherPointIndex])
                        nextHigherPointIndex++;
                    int nextLowerPointIndex = nextHigherPointIndex - 1;
                    if (nextLowerPointIndex == -1)
                        return outcomeIfLessArray[0];
                    double higherPoint = keyPointsArray[nextHigherPointIndex];
                    double lowerPoint = keyPointsArray[nextLowerPointIndex];
                    double higherPointResult = outcomeIfLessArray[nextHigherPointIndex];
                    double lowerPointResult = outcomeIfMoreArray[nextLowerPointIndex];
                    if (higherPointResult == lowerPointResult)
                        return higherPointResult;
                    double proportion = (pt - lowerPoint) / (higherPoint - lowerPoint); // the proportion is the weight to place on the higher point
                    double outcome = proportion * higherPointResult + (1.0 - proportion) * lowerPointResult;
                    return outcome;
                }
                );

                int probabilityIndex;
                int magnitudeIndex;
                s.GetIndicesFromEstimates(
                    s.isPlaintiffStrategy ? gameData.CaseQualityAndEstimates.pEstProbability : gameData.CaseQualityAndEstimates.dEstProbability,
                    s.isPlaintiffStrategy ? gameData.CaseQualityAndEstimates.pEstMagnitude : gameData.CaseQualityAndEstimates.dEstMagnitude,
                    gameData.MiscGameParameters,
                    out probabilityIndex, out magnitudeIndex);
                int bestV = -1;
                double bestOutcome = -99999999;
                double valueToTest = firstValueToTestContinuousStrategies;
                for (int v = 0; v < numValuesToTestEachDimensionForContinuousStrategies; v++)
                {
                    double outcome = getOutcomeAtPointByConsideringPlayedGames(valueToTest);
                    if (outcome > bestOutcome)
                    {
                        bestV = v;
                        bestOutcome = outcome;
                    }
                    RegisterResultsForParticularPoint(strategyIndex, probabilityIndex, magnitudeIndex, v, outcome); // faster than passing cqe etc.
                    valueToTest += stepSizeForTestingContinuousStrategies;
                }

            }

            private void BubbleSort(double[] array)
            {
                bool sortNeeded = true;
                while (sortNeeded)
                {
                    sortNeeded = false;
                    for (int i = 0; i < array.Length - 1; i++)
                        if (array[i] > array[i + 1])
                        {
                            double temp = array[i];
                            array[i] = array[i + 1];
                            array[i + 1] = temp;
                            sortNeeded = true;
                        }
                }
            }

            private void ComparePlayingHigherAndLowerValue(LitigationGameProbMagnitudeSimpleModel game, GameData gameData, int strategyIndex, Strategy s)
            {
                
                double originalValue = gameData.PartiesStrategies.SetFieldByIndexToHigherOrLowerValue(strategyIndex, true, s.isBoolStrategy, op.moveSize, op.moveSizeIsMaximum);
                game.PlayGameOnce(gameData);
                gameData.CopyOfOutputData.CopyFrom(gameData.OutputData); // copy now contains higher value
                gameData.PartiesStrategies.SetFieldByIndex(strategyIndex, originalValue); // must do this so can set to lower value
                gameData.PartiesStrategies.SetFieldByIndexToHigherOrLowerValue(strategyIndex, false, s.isBoolStrategy, op.moveSize, op.moveSizeIsMaximum);
                game.PlayGameOnce(gameData);
                RegisterRelativeResults(strategyIndex, gameData.CopyOfOutputData, gameData.OutputData, gameData.CaseQualityAndEstimates, gameData.MiscGameParameters);
                gameData.PartiesStrategies.SetFieldByIndex(strategyIndex, originalValue); // restore this, since we will continue to use the same parties strategies again
            }

            public void PlayGameAndAccumulateOutput(LitigationGameProbMagnitudeSimpleModel game, GameData gameData, long iterationNumber, CumulativeOutputData cod)
            {
                gameData.CaseQualityAndEstimates.SetBasedOnIteration(iterationNumber, gameData.MiscGameParameters);
                GetStrategiesGivenEstimates(gameData.PartiesStrategies, gameData.CaseQualityAndEstimates, gameData.MiscGameParameters);
                game.PlayGameOnce(gameData);
                cod.AddOutputData(gameData.OutputData);
            }

            internal void RememberCurrentSettings(int s)
            {
                AllStrategies[s].RememberCurrentSettings();
            }

            internal void SwapCurrentAndRememberedSettings(int s)
            {
                AllStrategies[s].SwapCurrentAndRememberedSettings();
            }
        }

        public class Strategy
        {
            const double stepSize = 1.0 / numStrategyPointsEachDimension;
            const double halfStepSize = stepSize / 2.0;
            double[,] strategyPoints = new double[numStrategyPointsEachDimension,numStrategyPointsEachDimension];
            double[,] rememberedStrategyPoints = new double[numStrategyPointsEachDimension, numStrategyPointsEachDimension];
            public StatCollector[,] statCollectors;
            public StatCollector[, ,] statCollectorsForContinuous;
            public bool isBoolStrategy = false;
            public bool isPlaintiffStrategy = false;
            public bool isProbabilityRelated = false;
            bool isContinuous = false;
            double? initializationValue = null;
            public int stageToStartOptimizing = 0;
            public int stageToStopOptimizing = 99999;

            public Strategy(double? initValue, bool continuous)
            {
                initializationValue = initValue;
                isContinuous = continuous;
                if (isContinuous)
                    statCollectorsForContinuous = new StatCollector[numStrategyPointsEachDimension, numStrategyPointsEachDimension, numValuesToTestEachDimensionForContinuousStrategies];
                else
                    statCollectors = new StatCollector[numStrategyPointsEachDimension, numStrategyPointsEachDimension];
                InitializeStrategyPoints();
            }

            public void Report()
            {
                int everyNth = 1;
                for (int probabilityIndex = 0; probabilityIndex < numStrategyPointsEachDimension; probabilityIndex += everyNth)
                {
                    string theString = "";
                    for (int magnitudeIndex = 0; magnitudeIndex < numStrategyPointsEachDimension; magnitudeIndex += everyNth) 
                    {
                        theString += strategyPoints[probabilityIndex,magnitudeIndex].ToString("0.000") + ", ";
                    }
                    TabbedText.WriteLine(theString);
                }
                
            }

            public void InitializeStrategyPoints()
            {
                for (int probabilityIndex = 0; probabilityIndex < numStrategyPointsEachDimension; probabilityIndex++)
                    for (int magnitudeIndex = 0; magnitudeIndex < numStrategyPointsEachDimension; magnitudeIndex++)
                    {
                        strategyPoints[probabilityIndex, magnitudeIndex] = initializationValue ?? RandomGenerator.NextDouble();
                        if (isContinuous)
                        {
                            for (int valueToTestIndex = 0; valueToTestIndex < numValuesToTestEachDimensionForContinuousStrategies; valueToTestIndex++)
                                statCollectorsForContinuous[probabilityIndex, magnitudeIndex, valueToTestIndex] = new StatCollector();
                        }
                        else
                            statCollectors[probabilityIndex, magnitudeIndex] = new StatCollector();
                    }
            }

            public void PickBetterApproaches(double moveSize)
            {
                if (isContinuous)
                    PickBetterApproachesContinuous(moveSize);
                else
                    PickBetterApproachesNoncontinuous(moveSize);
            }

            private void PickBetterApproachesContinuous(double moveSize)
            {
                for (int probabilityIndex = 0; probabilityIndex < numStrategyPointsEachDimension; probabilityIndex++)
                    for (int magnitudeIndex = 0; magnitudeIndex < numStrategyPointsEachDimension; magnitudeIndex++)
                    {
                        int bestIndexSoFar = -1;
                        double bestOutcomeSoFar = -999999999999999;
                        for (int valueToTestIndex = 0; valueToTestIndex < numValuesToTestEachDimensionForContinuousStrategies; valueToTestIndex++)
                        {
                            double result = statCollectorsForContinuous[probabilityIndex, magnitudeIndex, valueToTestIndex].Average();
                            statCollectorsForContinuous[probabilityIndex, magnitudeIndex, valueToTestIndex].Reset();
                            double margin = isPlaintiffStrategy ? 0.000001 : -0.000001;
                            if (result > bestOutcomeSoFar + margin)
                            {
                                bestIndexSoFar = valueToTestIndex;
                                bestOutcomeSoFar = result;
                            }
                        }
                        double newValue = firstValueToTestContinuousStrategies + bestIndexSoFar * stepSizeForTestingContinuousStrategies;
                        double currentValue = strategyPoints[probabilityIndex, magnitudeIndex];
                        if (newValue > currentValue && newValue - currentValue > moveSize)
                            newValue = currentValue + moveSize;
                        else if (newValue < currentValue && currentValue - newValue > moveSize)
                            newValue = currentValue - moveSize;
                        strategyPoints[probabilityIndex, magnitudeIndex] = newValue;
                    }
            }

            private void PickBetterApproachesNoncontinuous(double moveSize)
            {
                bool smooth = false;
                double[][] smoothedScores = null;
                if (smooth)
                    smoothedScores = SmoothScores();
                for (int probabilityIndex = 0; probabilityIndex < numStrategyPointsEachDimension; probabilityIndex++)
                    for (int magnitudeIndex = 0; magnitudeIndex < numStrategyPointsEachDimension; magnitudeIndex++)
                    {
                        double higherApproachScoreMinusLowerApproachScore = smooth ? smoothedScores[probabilityIndex][magnitudeIndex] : statCollectors[probabilityIndex, magnitudeIndex].Average();
                        bool randomlyAssignDirectionifZero = true;
                        if (randomlyAssignDirectionifZero && higherApproachScoreMinusLowerApproachScore == 0)
                            higherApproachScoreMinusLowerApproachScore = RandomGenerator.NextDouble() > 0.5 ? 1.0 : -1.0;
                        if (higherApproachScoreMinusLowerApproachScore != 0) // we might not have any results implicating a particular prob/mag combo
                        {
                            bool moveHigher = (higherApproachScoreMinusLowerApproachScore > 0);
                            double oldValue = strategyPoints[probabilityIndex, magnitudeIndex];
                            double newValue;
                            if (moveHigher)
                            {
                                if (isBoolStrategy)
                                    newValue = 1.0;
                                else
                                    newValue = oldValue + moveSize;
                                if (newValue > 1.0)
                                    newValue = 1.0;
                            }
                            else
                            {
                                if (isBoolStrategy)
                                    newValue = 0;
                                else
                                    newValue = oldValue - moveSize;
                                if (newValue < 0)
                                    newValue = 0.0;
                            }
                            strategyPoints[probabilityIndex, magnitudeIndex] = newValue;
                        }
                        statCollectors[probabilityIndex, magnitudeIndex].Reset();
                    }
            }

            List<List<int>> nearestNeighbors = new List<List<int>>();
            public double[][] SmoothScores()
            {
                List<double[]> trainingInputs = new List<double[]>();
                List<double> trainingOutputs = new List<double>();
                List<double> weights = new List<double>();
                for (int i = 0; i < numStrategyPointsEachDimension; i++)
                    for (int j = 0; j < numStrategyPointsEachDimension; j++)
                    {
                        double higherApproachScoreMinusLowerApproachScore = statCollectors[i, j].Average();
                        trainingInputs.Add(new double[] { i, j });
                        trainingOutputs.Add(higherApproachScoreMinusLowerApproachScore);
                        weights.Add(statCollectors[i, j].Num());
                    }
                if (!nearestNeighbors.Any())
                {
                    List<Tuple<int, Tuple<double, double>>> pairs = new List<Tuple<int,Tuple<double,double>>>();
                    int overallIndex = 0;
                    for (int i = 0; i < numStrategyPointsEachDimension; i++)
                        for (int j = 0; j < numStrategyPointsEachDimension; j++)
                        {
                            pairs.Add(new Tuple<int,Tuple<double,double>>(overallIndex, new Tuple<double,double>(i, j)));
                            overallIndex++;
                        }
                    overallIndex = 0;
                    for (int i = 0; i < numStrategyPointsEachDimension; i++)
                        for (int j = 0; j < numStrategyPointsEachDimension; j++)
                        {
                            List<int> nearest = pairs.Where(x => x.Item1 != overallIndex).OrderBy(x => (x.Item2.Item1 - i) * (x.Item2.Item1 - i) + (x.Item2.Item2 - j) * (x.Item2.Item2 - j)).Select(x => x.Item1).Take(30).ToList();
                            nearestNeighbors.Add(nearest);
                            overallIndex++;
                        }
                }
                GRNN g = new GRNN(trainingInputs, trainingOutputs, weights, nearestNeighbors, null, null);
                double[][] output = new double[numStrategyPointsEachDimension][];
                for (int o = 0; o < numStrategyPointsEachDimension; o++)
                    output[o] = new double[numStrategyPointsEachDimension];
                
                int overallIndex2 = 0;
                for (int i = 0; i < numStrategyPointsEachDimension; i++)
                    for (int j = 0; j < numStrategyPointsEachDimension; j++)
                    {
                        int nearestNeighborOfThisPoint = nearestNeighbors[overallIndex2][0];
                        output[i][j] = g.CalculateOutputNearestNeighborsOnly(new double[] { i, j }, nearestNeighborOfThisPoint, nearestNeighbors[nearestNeighborOfThisPoint]);
                        overallIndex2++;
                    }
                return output;
            }

            public double GetStrategy(double probabilityValue, double magnitudeValue, MiscGameParameters mgp, OptimizationParameters op)
            {
                int probabilityIndex;
                int magnitudeIndex;
                GetIndicesFromEstimates(probabilityValue, magnitudeValue, mgp, out probabilityIndex, out magnitudeIndex);
                if (op.probabilityRandomStrategy != 0 && RandomGenerator.NextDouble() < op.probabilityRandomStrategy)
                    return RandomGenerator.NextDouble();
                return strategyPoints[probabilityIndex, magnitudeIndex];
            }

            public void GetIndicesFromEstimates(double probabilityValue, double magnitudeValue, MiscGameParameters mgp, out int probabilityIndex, out int magnitudeIndex)
            {
                // center of region i is halfStepSize + (i * stepSize) = v
                // so, i = (v - halfStepSize) / stepSize. round off to nearest.
                probabilityIndex = (int)Math.Round((probabilityValue - halfStepSize) / stepSize);
                double bottomOfMagnitudeScale = (double) mgp.minMaxRatio;
                double magnitudeValueScaled = (magnitudeValue - bottomOfMagnitudeScale) / (1.0 - bottomOfMagnitudeScale);
                magnitudeIndex = (int)Math.Round((magnitudeValueScaled - halfStepSize) / stepSize);
                if (probabilityIndex < 0)
                    probabilityIndex = 0;
                if (probabilityIndex == numStrategyPointsEachDimension)
                    probabilityIndex = numStrategyPointsEachDimension - 1;
                if (magnitudeIndex < 0)
                    magnitudeIndex = 0;
                if (magnitudeIndex == numStrategyPointsEachDimension)
                    magnitudeIndex = numStrategyPointsEachDimension - 1;
            }

            public bool GetStrategyAsBool(double probabilityValue, double magnitudeValue, MiscGameParameters mgp, OptimizationParameters op)
            {
                return GetStrategy(probabilityValue, magnitudeValue, mgp, op) > 0.5;
            }

            internal void RememberCurrentSettings()
            {
                for (int i = 0; i < numStrategyPointsEachDimension; i++)
                    for (int j = 0; j < numStrategyPointsEachDimension; j++)
                        rememberedStrategyPoints[i, j] = strategyPoints[i, j];
            }

            internal void SwapCurrentAndRememberedSettings()
            {
                for (int i = 0; i < numStrategyPointsEachDimension; i++)
                    for (int j = 0; j < numStrategyPointsEachDimension; j++)
                    {
                        double temp = rememberedStrategyPoints[i, j];
                        rememberedStrategyPoints[i, j] = strategyPoints[i, j];
                        strategyPoints[i, j] = temp;
                    }
            }
        }

        public class CumulativeOutputData
        {
            public int numCases;
            public double resolutionBeforeCosts;
            public double pOutcome;
            public double dOutcome;
            public double compensationError;
            public double deterrenceError;
            public double absCompensationError;
            public double absDeterrenceError;
            public double entirelySettled;
            public double entirelyResolvedBeforeTrial;
            public double probabilityInitiallySettled;
            public double magnitudeInitiallySettled;
            public double pDroppedRemainderOfCase;
            public double dDefaultedRemainderOfCase;
            public double pDroppedMagnitudeOnly;
            public double dDroppedMagnitudeOnly;
            public double pDroppedProbabilityOnly;
            public double dDroppedProbabilityOnly;
            public double pRefusesToNegotiate;
            public double dRefusesToNegotiate;
            public double probabilityTried;
            public double magnitudeTried;
            public double bothIssuesTried;
            public double pDowngradesProbability;
            public double dUpgradesProbability;
            public object lockObj = new object();

            public void AddOutputData(OutputData od)
            {
                lock (lockObj)
                {
                    numCases++;
                    resolutionBeforeCosts += od.resolutionBeforeCosts;
                    pOutcome += od.pOutcome;
                    dOutcome += od.dOutcome;
                    //if (tasteForOutOfCourtResolution > 0 && od.entirelySettled) /* only have taste for settling, not for dropping */
                    //{ // don't count taste for settlement in reporting, even though we are counting it in other ways
                    //    pOutcome -= tasteForOutOfCourtResolution;
                    //    dOutcome -= tasteForOutOfCourtResolution;
                    //}
                    compensationError += od.compensationError;
                    deterrenceError += od.deterrenceError;
                    absCompensationError += od.absCompensationError;
                    absDeterrenceError += od.absDeterrenceError;
                    if (od.entirelySettled)
                        entirelySettled++;
                    if (od.entirelyResolvedBeforeTrial)
                        entirelyResolvedBeforeTrial++;
                    if (od.probabilityInitiallySettled)
                        probabilityInitiallySettled++;
                    if (od.magnitudeInitiallySettled)
                        magnitudeInitiallySettled++;
                    if (od.pDroppedRemainderOfCase)
                        pDroppedRemainderOfCase++;
                    if (od.dDefaultedRemainderOfCase)
                        dDefaultedRemainderOfCase++;
                    if (od.pDroppedMagnitudeOnly)
                        pDroppedMagnitudeOnly++;
                    if (od.dDroppedMagnitudeOnly)
                        dDroppedMagnitudeOnly++;
                    if (od.pDroppedProbabilityOnly)
                        pDroppedProbabilityOnly++;
                    if (od.dDroppedProbabilityOnly)
                        dDroppedProbabilityOnly++;
                    if (od.pRefusesToNegotiate)
                        pRefusesToNegotiate++;
                    if (od.dRefusesToNegotiate)
                        dRefusesToNegotiate++;
                    if (od.pDowngradesProbability)
                        pDowngradesProbability++;
                    if (od.dUpgradesProbability)
                        dUpgradesProbability++;
                    if (od.probabilityTried)
                        probabilityTried++;
                    if (od.magnitudeTried)
                        magnitudeTried++;
                    if (od.bothIssuesTried)
                        bothIssuesTried++;
                }
            }

            public void ReportResults()
            {
                TabbedText.WriteLine(
                    String.Format(
@"
            resolutionBeforeCosts {0}
            pOutcome {1}
            dOutcome {2}
            compensationError {3}
            deterrenceError {4}
            absCompensationError {5}
            absDeterrenceError {6}
            entirelySettled {7}
            entirelyResolvedBeforeTrial {8}
            probabilityInitiallySettled {9}
            magnitudeInitiallySettled {10}
            pDroppedRemainderOfCase {11}
            dDefaultedRemainderOfCase {12}
            pDroppedMagnitudeOnly {13}
            dDefaultedMagnitudeOnly {14}
            pDroppedProbabilityOnly {15}
            dDefaultedProbabilityOnly {16}
            pRefusesToNegotiate {17}
            dRefusesToNegotiate {18}
            pDowngradesProbability {19}
            dUpgradesProbability {20}
            probabilityTried {21}
            magnitudeTried {22}
            bothIssuesTried {23}
",  
            resolutionBeforeCosts / (double) numCases,
            pOutcome / (double)numCases,
            dOutcome / (double)numCases,
            compensationError / (double)numCases,
            deterrenceError / (double)numCases,
            absCompensationError / (double)numCases,
            absDeterrenceError / (double)numCases,
            entirelySettled / (double)numCases,
            entirelyResolvedBeforeTrial / (double)numCases,
            probabilityInitiallySettled / (double)numCases,
            magnitudeInitiallySettled / (double)numCases,
            pDroppedRemainderOfCase / (double)numCases,
            dDefaultedRemainderOfCase / (double)numCases,
            pDroppedMagnitudeOnly / (double)numCases,
            dDroppedMagnitudeOnly / (double)numCases,
            pDroppedProbabilityOnly / (double)numCases,
            dDroppedProbabilityOnly / (double)numCases,
            pRefusesToNegotiate / (double)numCases,
            dRefusesToNegotiate / (double)numCases,
            pDowngradesProbability / (double)numCases,
            dUpgradesProbability / (double)numCases,
            probabilityTried / (double)numCases,
            magnitudeTried / (double)numCases,
            bothIssuesTried / (double)numCases
            ));
            }
        }

        public class OutputData
        {
            public double resolutionBeforeCosts;
            public double pOutcome;
            public double dOutcome;
            public double compensationError;
            public double deterrenceError;
            public double absCompensationError;
            public double absDeterrenceError;
            public bool entirelySettled;
            public bool entirelyResolvedBeforeTrial;
            public bool probabilityInitiallySettled;
            public bool magnitudeInitiallySettled;
            public bool pDroppedRemainderOfCase;
            public bool dDefaultedRemainderOfCase;
            public bool pDroppedMagnitudeOnly;
            public bool dDroppedMagnitudeOnly;
            public bool pDroppedProbabilityOnly;
            public bool dDroppedProbabilityOnly;
            public bool pRefusesToNegotiate;
            public bool dRefusesToNegotiate;
            public bool pDowngradesProbability;
            public bool dUpgradesProbability;
            public bool probabilityTried;
            public bool magnitudeTried;
            public bool bothIssuesTried;

            public void CopyFrom(OutputData source)
            {
                resolutionBeforeCosts = source.resolutionBeforeCosts;
                pOutcome = source.pOutcome;
                dOutcome = source.dOutcome;
                compensationError = source.compensationError;
                deterrenceError = source.deterrenceError;
                absCompensationError = source.absCompensationError;
                absDeterrenceError = source.absDeterrenceError;
                entirelySettled = source.entirelySettled;
                entirelyResolvedBeforeTrial = source.entirelyResolvedBeforeTrial;
                probabilityInitiallySettled = source.probabilityInitiallySettled;
                magnitudeInitiallySettled = source.magnitudeInitiallySettled;
                pDroppedRemainderOfCase = source.pDroppedRemainderOfCase;
                dDefaultedRemainderOfCase = source.dDefaultedRemainderOfCase;
                pDroppedMagnitudeOnly = source.pDroppedMagnitudeOnly;
                dDroppedMagnitudeOnly = source.dDroppedMagnitudeOnly;
                pDroppedProbabilityOnly = source.pDroppedProbabilityOnly;
                dDroppedProbabilityOnly = source.dDroppedProbabilityOnly;
                probabilityTried = source.probabilityTried;
                magnitudeTried = source.magnitudeTried;
                bothIssuesTried = source.bothIssuesTried;
                pDowngradesProbability = source.pDowngradesProbability;
                dUpgradesProbability = source.dUpgradesProbability;
            }
        }

        public class GameData
        {
            // we will not repeatedly allocate memory
            // set overall
            public MiscGameParameters MiscGameParameters;
            public SeveringRules SeveringRules;
            // set before playing the game (once for all the strategy variations)
            public CaseQualityAndEstimates CaseQualityAndEstimates = new CaseQualityAndEstimates();
            // set before playing the game and then adjusted for each strategy variation
            public StrategiesGivenEstimates PartiesStrategies = new StrategiesGivenEstimates();
            // produced by the game
            public OutputData OutputData = new OutputData();
            public OutputData CopyOfOutputData = new OutputData(); // can copy output data here for comparison purposes

            public GameData(MiscGameParameters mgp, SeveringRules sr)
            {
                MiscGameParameters = mgp;
                SeveringRules = sr;
            }
        }

        public void RunSimulation()
        {
            //RunSimulationWithSettings(0.4, null, false, false);
            //RunSimulationWithSettings(null, 0.6, false, false);
            RunSimulationWithSettings(0.4, 0.6, true, true);
            //RunSimulationWithSettings(0.4, 0.6, true, false);
            //RunSimulationWithSettings(0.4, 0.6, false, true);
            //RunSimulationWithSettings(0.4, 0.6, false, false);
            //RunSimulationWithSettings(null, null, true, true); // after stage 0, probability and magnitude are individually roughly correct 
            //RunSimulationWithSettings(null, null, true, false);
            //RunSimulationWithSettings(null, null, false, true);
            //RunSimulationWithSettings(null, null, false, false); 
        }

        public void RunSimulationWithSettings(double? blockSettlementsBelow, double? blockSettlementsAbove, bool magnitudeSettlementSticks, bool probabilitySettlementSticks)
        {
            TabbedText.WriteLine("Simulation: " + blockSettlementsBelow + ", " + blockSettlementsAbove + ", " + " mag sticks: " + magnitudeSettlementSticks + " prob sticks: " + probabilitySettlementSticks);
            TabbedText.Tabs++;
            MiscGameParameters mgp = new MiscGameParameters() { 
                minMagnitudeInDollars = 900, maxMagnitudeInDollars = 1100, 
                dNoise = 0.03, pNoise = 0.03,
                eachPartyTrialCost = 100, tasteForSettlement = 0, 
                blockSettlementsBelow = blockSettlementsBelow, blockSettlementsAbove = blockSettlementsAbove 
            };
            mgp.minMaxRatio = mgp.minMagnitudeInDollars / mgp.maxMagnitudeInDollars;
            SeveringRules sr = new SeveringRules() { magnitudeSettlementSticks = magnitudeSettlementSticks, probabilitySettlementSticks = probabilitySettlementSticks };
            Strategies theStrategies = new Strategies();
            RepeatedlyPickBestApproaches(mgp, sr, theStrategies); 
            TabbedText.WriteLine("Final report: " + blockSettlementsBelow + ", " + blockSettlementsAbove + ", " + " mag sticks: " + magnitudeSettlementSticks + " prob sticks: " + probabilitySettlementSticks);
            RepeatedlyPlayToReport(mgp, sr, theStrategies, numRepetitionsPerReportingRound); 
            TabbedText.Tabs--;
        }


        public void RepeatedlyPickBestApproaches(MiscGameParameters mgp, SeveringRules sr, Strategies theStrategies)
        {
            OptimizationParameters op = new OptimizationParameters() { stage = 0, moveSizeIsMaximum = false, moveOnlyHalfWay = true };

            TabbedText.WriteLine("Initial state");
            TabbedText.Tabs++;
            RepeatedlyPlayToReport(mgp, sr, theStrategies, numRepetitionsPerReportingRound);
            TabbedText.Tabs--;

            long startIteration = 0;
            int maxStage = 1; 
            if (optimizeEverythingInStage0)
                maxStage = 0;
            for (int stage = 0; stage <= maxStage; stage++)
            {
                op.stage = stage;
                TabbedText.WriteLine("Stage " + stage);
                TabbedText.Tabs++;
                RepeatedlyPickBestApproaches_SingleStage(mgp, sr, theStrategies, op, ref startIteration);
                TabbedText.Tabs--;
            }
        }

        public void RepeatedlyPickBestApproaches_SingleStage(MiscGameParameters mgp, SeveringRules sr, Strategies theStrategies, OptimizationParameters op, ref long startIteration)
        {
            int repetitions = numOptimizationRoundsPerStage;
            for (int r = 0; r < repetitions; r++)
            {
                double reachFinalMoveSizeAfterThisProportionofRepetitions = 0.2;
                double finalMoveSize = 0.005;
                double curvatureMoveSize = MonotonicCurve.CalculateCurvatureForThreePoints(0, 0.25, 0.5 * reachFinalMoveSizeAfterThisProportionofRepetitions, 0.1, reachFinalMoveSizeAfterThisProportionofRepetitions, finalMoveSize);//  CurveDistribution.CalculateCurvature(0.25, 0.01, 0.1);

                double proportionOfRepetitions = ((double)r / (double)repetitions);
                double moveSize;
                if (proportionOfRepetitions <= reachFinalMoveSizeAfterThisProportionofRepetitions)
                    moveSize = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(0.25, finalMoveSize, curvatureMoveSize, proportionOfRepetitions);
                else
                    moveSize = finalMoveSize;

                double reachZeroRandomnessAfterThisProportionRepetitions = 0.2;
                double curvatureRandomWeight = MonotonicCurve.CalculateCurvatureForThreePoints(0, 0.998, 0.5, 0.05, 1.0, 0.0001); // CurveDistribution.CalculateCurvature(0.998, 0.0001, 0.01);
                double weightToPlaceOnRandomOffer = op.stage == 0 && proportionOfRepetitions < reachZeroRandomnessAfterThisProportionRepetitions ? MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(0.998, 0.0001, curvatureRandomWeight, proportionOfRepetitions) : 0;

                TabbedText.WriteLine("Stage: " + op.stage + " Repetition within stage: " + r + " move size: " + moveSize + " weight on random: " + weightToPlaceOnRandomOffer);
                op.moveSize = moveSize;
                op.weightToPlaceOnRandomOffer = weightToPlaceOnRandomOffer;
                op.numIterationsEachRepeatedPlay = numRepetitionsPerOptimizationRound;
                RepeatedlyPlayToPickBestApproaches(mgp, sr, theStrategies, startIteration, op);
                startIteration += numRepetitionsPerOptimizationRound;
                RepeatedlyPlayToReport(mgp, sr, theStrategies, numRepetitionsPerReportingRound);
                theStrategies.OptimizationUpdateReport();
            }
        }

        public void RepeatedlyPlayToReport(MiscGameParameters mgp, SeveringRules sr, Strategies theStrategies, int repetitions)
        {
            CumulativeOutputData cod = new CumulativeOutputData();
            ThreadLocal<GameData> gameData = new ThreadLocal<GameData>();

            theStrategies.op = new OptimizationParameters() { probabilityRandomStrategy = 0, weightToPlaceOnRandomOffer = 0 };
            double tasteForSettlementValue = 0;
            bool doParallel = true;
            Parallelizer.Go(doParallel, 0, repetitions, iteration =>
            {
                if (gameData.Value == null)
                {
                    gameData.Value = new GameData(mgp.DeepCopy() /* Must copy it since we are changing tasteForSettlement */, sr);
                    gameData.Value.MiscGameParameters.tasteForSettlement = 0;
                }
                theStrategies.PlayGameAndAccumulateOutput(this, gameData.Value, iteration, cod);
            }
            );
            cod.ReportResults();
        }

        public void RepeatedlyPlayToPickBestApproaches(MiscGameParameters mgp, SeveringRules sr, Strategies theStrategies, long startIteration, OptimizationParameters op)
        {
            ThreadLocal<GameData> gameData = new ThreadLocal<GameData>();
            theStrategies.op = op;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            const int numStrategyPointsEachDimension = 25;
            bool doAllStrategiesSimultaneously = false;
            if (doAllStrategiesSimultaneously)
            {
                Parallel.For(startIteration, startIteration + op.numIterationsEachRepeatedPlay, iteration =>
                    {
                        if (gameData.Value == null)
                            gameData.Value = new GameData(mgp.DeepCopy(), sr);
                        theStrategies.PlayGameForEachStrategyVariation(this, gameData.Value, iteration, null);
                    }
                );
                theStrategies.PickBetterApproaches(null);
            }
            else
            {
                for (int s = 0; s < theStrategies.AllStrategies.Count(); s++)
                {
                    theStrategies.RememberCurrentSettings(s);
                    Parallel.For(startIteration, startIteration + op.numIterationsEachRepeatedPlay, iteration =>
                    {
                        if (gameData.Value == null)
                            gameData.Value = new GameData(mgp.DeepCopy(), sr);
                        theStrategies.PlayGameForEachStrategyVariation(this, gameData.Value, iteration, s);
                    }
                        );
                    theStrategies.PickBetterApproaches(s);
                    theStrategies.SwapCurrentAndRememberedSettings(s);
                }
                for (int s = 0; s < theStrategies.AllStrategies.Count(); s++)
                    theStrategies.SwapCurrentAndRememberedSettings(s);
            }
            sw.Stop();
            TabbedText.WriteLine("Elapsed ms: " + sw.ElapsedMilliseconds);
        }

        public void PlayGameOnce(GameData gameData)
        {
            #region initialization
            StrategiesGivenEstimates s = gameData.PartiesStrategies;
            OutputData o = gameData.OutputData;
            SeveringRules r = gameData.SeveringRules;
            CaseQualityAndEstimates q = gameData.CaseQualityAndEstimates;
            MiscGameParameters m = gameData.MiscGameParameters;

            double? probabilityResolution = null;
            double? magnitudeResolution = null;
            #endregion initialization

            #region initialSettlement

            o.pRefusesToNegotiate = false;
            o.dRefusesToNegotiate = false;

            double? probabilitySettlement = null;
            if (alwaysDefinitivelySettleProbabilityAt != null)
            {
                probabilityResolution = probabilitySettlement = alwaysDefinitivelySettleProbabilityAt;
                o.probabilityInitiallySettled = true;
            }
            else
            {
                if (!s.pRefusesToNegotiateProbability && !s.dRefusesToNegotiateProbability && s.pProbabilityOfferIfNegotiating <= s.dProbabilityOfferIfNegotiating)
                {
                    probabilitySettlement = (s.dProbabilityOfferIfNegotiating + s.pProbabilityOfferIfNegotiating) / 2.0;
                    o.probabilityInitiallySettled = true;
                    probabilityResolution = probabilitySettlement;
                }
                else
                {
                    o.probabilityInitiallySettled = false;
                    if (s.pRefusesToNegotiateProbability)
                        o.pRefusesToNegotiate = true;
                    if (s.dRefusesToNegotiateProbability)
                        o.dRefusesToNegotiate = true;
                }
            }

            double? magnitudeSettlement = null;
            if (alwaysDefinitivelySettleMagnitudeAt != null)
            {
                magnitudeResolution = magnitudeSettlement = alwaysDefinitivelySettleMagnitudeAt;
                o.magnitudeInitiallySettled = true;
            }
            else
            {
                if (s.pMagnitudeOffer <= s.dMagnitudeOffer)
                {
                    magnitudeSettlement = (s.pMagnitudeOffer + s.dMagnitudeOffer) / 2.0;
                    o.magnitudeInitiallySettled = true;
                    magnitudeResolution = magnitudeSettlement;
                }
                else
                    o.magnitudeInitiallySettled = false;
            }

            #endregion initialSettlement

            #region probability constraint
            o.pDowngradesProbability = false;
            o.dUpgradesProbability = false;
            if (o.probabilityInitiallySettled /* && o.magnitudeInitiallySettled -- we don't want to require this b/c we are separately negotiating probability and magnitude, and here we're just asking the question of what we wish to do with the probability negotiation if a result was invalid */ && ((m.blockSettlementsAbove != null && probabilitySettlement > m.blockSettlementsAbove) || (m.blockSettlementsBelow != null && probabilitySettlement < m.blockSettlementsBelow))) 
            {
                if (m.blockSettlementsAbove != null && probabilitySettlement > m.blockSettlementsAbove)
                {
                    if (s.pDowngradesProbabilityIfNecessary)
                    {
                        probabilityResolution = probabilitySettlement = (double)m.blockSettlementsAbove;
                        o.pDowngradesProbability = true;
                    }
                    else
                    {
                        probabilityResolution = probabilitySettlement = null;
                        o.probabilityInitiallySettled = false;
                    }
                }
                else if (m.blockSettlementsBelow != null && probabilitySettlement < m.blockSettlementsBelow)
                {
                    if (s.dUpgradesProbabilityIfNecessary)
                    {
                        probabilityResolution = probabilitySettlement = (double)m.blockSettlementsBelow;
                        o.dUpgradesProbability = true;
                    }
                    else
                    {
                        probabilityResolution = probabilitySettlement = null;
                        o.probabilityInitiallySettled = false;
                    }
                }
            }
            #endregion probability constraint

            #region settlementStickingAndDropping
            o.entirelySettled = o.probabilityInitiallySettled && o.magnitudeInitiallySettled;
            o.pDroppedRemainderOfCase = false;
            o.dDefaultedRemainderOfCase = false;
            o.pDroppedMagnitudeOnly = false;
            o.dDroppedMagnitudeOnly = false;
            o.pDroppedProbabilityOnly = false;
            o.dDroppedProbabilityOnly = false;
            if (o.entirelySettled)
            {
                o.entirelyResolvedBeforeTrial = true;
                o.probabilityTried = false;
                o.magnitudeTried = false;
                o.bothIssuesTried = false;
                o.resolutionBeforeCosts = (double)probabilityResolution * (double)magnitudeResolution * m.maxMagnitudeInDollars;
                o.pOutcome = o.resolutionBeforeCosts + gameData.MiscGameParameters.tasteForSettlement;
                o.dOutcome = 0 - o.resolutionBeforeCosts + gameData.MiscGameParameters.tasteForSettlement;
            }
            else
            {
                o.entirelyResolvedBeforeTrial = false;
                // void parts of settlement depending on rules, since we don't have a complete settlement
                if (!r.magnitudeSettlementSticks && o.magnitudeInitiallySettled && alwaysDefinitivelySettleMagnitudeAt == null)
                {
                    magnitudeResolution = null;
                    // magnitudeSettlement = null;
                }
                if (!r.probabilitySettlementSticks && o.probabilityInitiallySettled && alwaysDefinitivelySettleProbabilityAt == null)
                {
                    probabilityResolution = null;
                    // probabilitySettlement = null;
                }

                // give parties chance to drop depending on scenario
                // if both would give up, neither does
                if (magnitudeResolution == null && probabilityResolution == null)
                {
                    if (s.pDropsAbsentSettlementIfNeitherSettled && s.dDefaultsAbsentSettlementIfNeitherSettled)
                    {
                        magnitudeResolution = (m.minMaxRatio + 1.0) / 2;
                        probabilityResolution = 0.5;
                        o.entirelyResolvedBeforeTrial = o.entirelySettled = true;
                    }
                    else if (s.pDropsAbsentSettlementIfNeitherSettled && !s.dDefaultsAbsentSettlementIfNeitherSettled)
                    {
                        magnitudeResolution = m.minMaxRatio; // doesn't matter given 0 probability
                        probabilityResolution = 0.0;
                        o.pDroppedRemainderOfCase = true;
                    }
                    else if (s.dDefaultsAbsentSettlementIfNeitherSettled && !s.pDropsAbsentSettlementIfNeitherSettled)
                    {
                        magnitudeResolution = 1.0;
                        probabilityResolution = 1.0;
                        o.dDefaultedRemainderOfCase = true;
                    }
                }
                else if (magnitudeResolution != null)
                {
                    if (s.pDropsAbsentSettlementIfMagnitudeOnlySettled && s.dDefaultsAbsentSettlementIfMagnitudeOnlySettled)
                    {
                        probabilityResolution = 0.5;
                        o.entirelyResolvedBeforeTrial = o.entirelySettled = true;
                    }
                    else if (s.pDropsAbsentSettlementIfMagnitudeOnlySettled && !s.dDefaultsAbsentSettlementIfMagnitudeOnlySettled)
                    {
                        probabilityResolution = 0.0;
                        o.pDroppedRemainderOfCase = true;
                        o.pDroppedProbabilityOnly = true;
                    }
                    else if (s.dDefaultsAbsentSettlementIfMagnitudeOnlySettled && !s.pDropsAbsentSettlementIfMagnitudeOnlySettled)
                    {
                        probabilityResolution = 1.0;
                        o.dDefaultedRemainderOfCase = true;
                        o.dDroppedProbabilityOnly = true;
                    }
                }
                else if (probabilityResolution != null)
                {
                    if (s.pDropsAbsentSettlementIfProbabilityOnlySettled && s.dDefaultsAbsentSettlementIfProbabilityOnlySettled)
                    {
                        magnitudeResolution = (m.minMaxRatio + 1.0) / 2.0;
                        o.entirelyResolvedBeforeTrial = o.entirelySettled = true;
                    }
                    else if (s.pDropsAbsentSettlementIfProbabilityOnlySettled && !s.dDefaultsAbsentSettlementIfProbabilityOnlySettled)
                    {
                        magnitudeResolution = m.minMaxRatio;
                        o.pDroppedRemainderOfCase = true;
                        o.pDroppedMagnitudeOnly = true;
                    }
                    else if (s.dDefaultsAbsentSettlementIfProbabilityOnlySettled && !s.pDropsAbsentSettlementIfProbabilityOnlySettled)
                    {
                        magnitudeResolution = 1.0;
                        o.dDefaultedRemainderOfCase = true;
                        o.dDroppedMagnitudeOnly = true;
                    }
                }
                else
                    throw new Exception("Internal error. Should not reach this code.");

                #endregion settlementStickingAndDropping

            #region resolution for cases that did not entirely settle initially
                if (magnitudeResolution != null && probabilityResolution != null)
                { // trial averted by dropping
                    o.entirelyResolvedBeforeTrial = true;
                    o.probabilityTried = false;
                    o.magnitudeTried = false;
                    o.bothIssuesTried = false;
                    o.resolutionBeforeCosts = (double)probabilityResolution * (double)magnitudeResolution * m.maxMagnitudeInDollars;
                    o.pOutcome = o.resolutionBeforeCosts;
                    o.dOutcome = 0 - o.resolutionBeforeCosts;
                }
                else
                {
                    // we're going to trial on at least one issue
                    if (probabilityResolution == null)
                    {
                        probabilityResolution = q.actualProbability;
                        o.probabilityTried = true;
                    }
                    else
                        o.probabilityTried = false;
                    if (magnitudeResolution == null)
                    {
                        magnitudeResolution = q.actualMagnitude;
                        o.magnitudeTried = true;
                    }
                    else
                        o.magnitudeTried = false;
                    o.bothIssuesTried = o.probabilityTried && o.magnitudeTried;

                    o.resolutionBeforeCosts = (double)probabilityResolution * (double)magnitudeResolution * m.maxMagnitudeInDollars;
                    o.pOutcome = o.resolutionBeforeCosts - m.eachPartyTrialCost;
                    o.dOutcome = 0 - o.resolutionBeforeCosts - m.eachPartyTrialCost;
                } // trial
            } // not initially entirely settled
                
            #endregion resolution

            #region errorCalc
            // figure out errors
            double correctPCompensation = (q.actualProbability > 0.5 ? 1.0 : 0.0) * q.actualMagnitude * m.maxMagnitudeInDollars;
            double correctDPayment = -correctPCompensation;
            double subtractFromEachOutcome = 0;
            o.compensationError = (o.pOutcome - subtractFromEachOutcome) - correctPCompensation;
            o.deterrenceError = correctDPayment - (o.dOutcome - subtractFromEachOutcome);
            o.absCompensationError = Math.Abs((o.pOutcome - subtractFromEachOutcome) - correctPCompensation);
            o.absDeterrenceError = Math.Abs((o.dOutcome - subtractFromEachOutcome) - correctDPayment);
            #endregion errorCalc
        }
    }
}

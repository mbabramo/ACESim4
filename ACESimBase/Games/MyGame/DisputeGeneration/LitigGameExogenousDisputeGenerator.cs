using ACESim.Util;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameExogenousDisputeGenerator : ILitigGameDisputeGenerator
    {
        public string GetGeneratorName() => "Exog";
        /// <summary>
        /// If litigation quality is generated from truly-liable status, and truly-liable status is exogenously determined, then the probability that the correct outcome is that the defendant truly is liable.
        /// </summary>
        public double ExogenousProbabilityTrulyLiable;
        /// <summary>
        /// If generating LiabilityStrength based on true case value, this is the standard deviation of the noise used to obscure the true litigation quality value (0 or 1). Each litigation quality level will be equally likely if there is an equal chance of either true litigation quality value.
        /// </summary>
        public double StdevNoiseToProduceLiabilityStrength;

        private double[] ProbabilityOfTrulyLiableValues, ProbabilitiesLiabilityStrength_TrulyNotLiable, ProbabilitiesLiabilityStrength_TrulyLiable;
        private double[] ProbabilityOfDamagesStrengthValues;

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition litigGameDefinition)
        {
            LitigGameDefinition = litigGameDefinition;
            ProbabilityOfTrulyLiableValues = new double[] { 1.0 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable };
            // A case is assigned a "true" value of 1 (should not be liable) or 2 (should be liable).
            // Based on the litigation quality noise parameter, we then collect a distribution of possible realized values, on the assumption
            // that the true values are equally likely. We then break this distribution into evenly sized buckets (based on a separate standard
            // deviation value) to get cutoff points.
            // Given this approach, the exogenous probability has no effect on ProbabilitiesLiabilityStrength_TrulyNotLiable and ProbabilitiesLiabilityStrength_TrulyLiable; both of these are conditional on whether a case is liable or not.
           DiscreteValueSignalParameters liabilityParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = 2, NumSignals = litigGameDefinition.Options.NumLiabilityStrengthPoints, StdevOfNormalDistribution = StdevNoiseToProduceLiabilityStrength, SourcePointsIncludeExtremes = true };
            ProbabilitiesLiabilityStrength_TrulyNotLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, liabilityParams);
            ProbabilitiesLiabilityStrength_TrulyLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, liabilityParams);

            // damages is simpler -- each damages level is equally likely. A case's damages strength is assumed to be equal to the true damages value. Of course, parties may still misestimate the damages strength.
            ProbabilityOfDamagesStrengthValues = new double[litigGameDefinition.Options.NumDamagesStrengthPoints];
            for (int i = 0; i < litigGameDefinition.Options.NumDamagesStrengthPoints; i++)
                ProbabilityOfDamagesStrengthValues[i] = 1.0 / (double)litigGameDefinition.Options.NumDamagesStrengthPoints;

            SetupInverted(litigGameDefinition);
        }


        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 0;
            primaryActions = 0;
            postPrimaryChanceActions = (byte) 2; // not truly liable or truly liable
            prePrimaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Defendant }; // if we did have a pre-primary chance, we must notify defendant
            primaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Resolution}; // not relevant for this dispute generator
            postPrimaryPlayersToInform = new byte[] {(byte) LitigGamePlayers.LiabilityStrengthChance, (byte) LitigGamePlayers.Resolution};
            prePrimaryUnevenChance = true; // though not used
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return 0;
        }

        double[] NoWealthEffects = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return NoWealthEffects; 
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            bool isTrulyLiable = IsTrulyLiable(myGameDefinition, disputeGeneratorActions, null);
            if (isTrulyLiable)
                return ProbabilitiesLiabilityStrength_TrulyLiable;
            else
                return ProbabilitiesLiabilityStrength_TrulyNotLiable;
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => ProbabilityOfDamagesStrengthValues;

        
        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return disputeGeneratorActions.PostPrimaryChanceAction == 2;
        }

        public void ModifyOptions(ref LitigGameOptions myGameOptions)
        {
            // nothing to modify
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true;
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            throw new NotImplementedException();
        }

        public double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // no pre primary chance function
        }

        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityOfTrulyLiableValues;
        }

        public bool PostPrimaryDoesNotAffectStrategy() => true;

        public bool SupportsSymmetry() => true;

        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);

        // INVERTED CALCULATIONS
        // See ILitigGameDisputeGenerator for full explanation

        // The following indices are for our inverted calculations
        const int trueLiabilityIndex = 0, liabilityStrengthIndex = 1, pLiabilitySignalIndex = 2, dLiabilitySignalIndex = 3, cLiabilitySignalIndex = 4;
        const int dLiabilitySignalCalculatorIndex = 0, cLiabilitySignalCalculatorIndex = 1, liabilityStrengthCalculatorIndex = 2, trueLiabilityCalculatorIndex = 3, liabilityStrengthWithoutTrialCalculatorIndex = 4, trueLiabilityWithoutTrialCalculatorIndex = 5;
        const int damagesStrengthIndex = 0, pDamagesSignalIndex = 1, dDamagesSignalIndex = 2, cDamagesSignalIndex = 3;
        const int dDamagesSignalCalculatorIndex = 0, cDamagesSignalCalculatorIndex = 1, damagesStrengthCalculatorIndex = 2, damagesStrengthWithoutTrialCalculatorIndex = 3;
        double[] pLiabilitySignalProbabilitiesUnconditional = null, pDamagesSignalProbabilitiesUnconditional = null;
        List<Func<List<int>, double[]>> LiabilityCalculators = null, DamagesCalculators = null;

        public void SetupInverted(LitigGameDefinition myGameDefinition)
        {
            var o = myGameDefinition.Options;
            if (!o.InvertChanceDecisions)
                return;

            const int numTrueLiabilityValues = 2;
            const int numCourtLiabilitySignals = 2;
            int[] liabilityDimensions = new int[] { numTrueLiabilityValues, o.NumLiabilityStrengthPoints, o.NumLiabilitySignals, o.NumLiabilitySignals, numCourtLiabilitySignals };
            double[] liabilityPrior = ProbabilityOfTrulyLiableValues;
            var liabilitySignalsProducer = new List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)>()
                {
                    // true liability is given by the prior
                    /* liability strength */ (trueLiabilityIndex /* taking from two initial values */, true /* which map onto extreme values of 0 and 1 */, StdevNoiseToProduceLiabilityStrength /* adding noise */), 
                    /* p liability signal */ (liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.PLiabilityNoiseStdev /* noise */),
                    /* d liability signal */ (liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.DLiabilityNoiseStdev /* noise */),
                    /* c liability signal */ (liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.CourtLiabilityNoiseStdev /* noise */),
                };
            pLiabilitySignalProbabilitiesUnconditional = DiscreteProbabilityDistribution.GetUnconditionalProbabilities(liabilityDimensions, liabilityPrior, liabilitySignalsProducer, pLiabilitySignalIndex);
            var liabilityCalculatorsToProduce = new List<(int distributionVariableIndex, List<int> fixedVariableIndices)>()
                {
                    (dLiabilitySignalIndex, new List<int>() { pLiabilitySignalIndex }), // defendant's signal based on plaintiff's signal
                    (cLiabilitySignalIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex}), // court liability based on plaintiff's and defendant's signals
                    (liabilityStrengthIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, cLiabilitySignalIndex }), // liability strength based on all of above
                    (trueLiabilityIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, cLiabilitySignalIndex, liabilityStrengthIndex, }), // true value based on all of the above
                    (liabilityStrengthIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, }), // liability strength based on everything but court info
                    (trueLiabilityIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, liabilityStrengthIndex, }) // true value based on everything but court info
                };
            LiabilityCalculators = DiscreteProbabilityDistribution.GetProbabilityMapCalculators(liabilityDimensions, liabilityPrior, liabilitySignalsProducer, liabilityCalculatorsToProduce);

            int[] damagesDimensions = new int[] { o.NumDamagesStrengthPoints, o.NumDamagesSignals, o.NumDamagesSignals, o.NumDamagesSignals };
            double[] damagesPrior = Enumerable.Range(0, o.NumDamagesStrengthPoints).Select(x => 1.0 / o.NumDamagesStrengthPoints).ToArray();
            var damagesSignalsProducer = new List<(int sourceSignalIndex, bool sourceIncludesExtremes, double stdev)>()
                {
                    // damages strength is given by the prior
                    /* p damages signal */ (damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.PDamagesNoiseStdev /* noise */),
                    /* d damages signal */ (damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.DDamagesNoiseStdev /* noise */),
                    /* c damages signal */ (damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.CourtDamagesNoiseStdev /* noise */),
                };
            pDamagesSignalProbabilitiesUnconditional = DiscreteProbabilityDistribution.GetUnconditionalProbabilities(damagesDimensions, damagesPrior, damagesSignalsProducer, pDamagesSignalIndex);
            var damagesCalculatorsToProduce = new List<(int distributionVariableIndex, List<int> fixedVariableIndices)>()
                {
                    (dDamagesSignalIndex, new List<int>() { pDamagesSignalIndex }), // defendant's signal based on plaintiff's signal
                    (cDamagesSignalIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex}), // court damages based on plaintiff's and defendant's signals
                    (damagesStrengthIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex, cDamagesSignalIndex }), // damages strength based on all of above
                    (damagesStrengthWithoutTrialCalculatorIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex }), // damages strength when no trial has occurred
                };
            DamagesCalculators = DiscreteProbabilityDistribution.GetProbabilityMapCalculators(damagesDimensions, damagesPrior, damagesSignalsProducer, damagesCalculatorsToProduce);
        }

        // Interface implementations
        public double[] InvertedCalculations_GetPLiabilitySignalProbabilities() => pLiabilitySignalProbabilitiesUnconditional;
        public double[] InvertedCalculations_GetDLiabilitySignalProbabilities(byte pLiabilitySignal) => LiabilityCalculators[dLiabilitySignalCalculatorIndex](new List<int>() { pLiabilitySignal - 1});
        public double[] InvertedCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal) => LiabilityCalculators[cLiabilitySignalCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1 });

        public double[] InvertedCalculations_GetPDamagesSignalProbabilities() => pDamagesSignalProbabilitiesUnconditional;
        public double[] InvertedCalculations_GetDDamagesSignalProbabilities(byte pDamagesSignal) => DamagesCalculators[dDamagesSignalCalculatorIndex](new List<int>() { pDamagesSignal - 1 });
        public double[] InvertedCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal) => DamagesCalculators[cDamagesSignalCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1}); 
        public double[] InvertedCalculations_GetLiabilityStrengthProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal) => cLiabilitySignal is byte cLiabilitySignalNotNull ? LiabilityCalculators[liabilityStrengthCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, cLiabilitySignalNotNull - 1 }) : LiabilityCalculators[liabilityStrengthWithoutTrialCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1 });
        public double[] InvertedCalculations_GetDamagesStrengthProbabilities(byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal) => cDamagesSignal is byte cDamagesSignalNotNull ? DamagesCalculators[liabilityStrengthCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1, cDamagesSignalNotNull - 1 }) : DamagesCalculators[liabilityStrengthWithoutTrialCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1 });

        // Additional implementations. This dispute generator calculates 
        public double[] InvertedCalculations_GetLiabilityTrueValueProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte liabilityStrength) => cLiabilitySignal is byte cLiabilitySignalNotNull ? LiabilityCalculators[trueLiabilityCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, cLiabilitySignalNotNull - 1, liabilityStrength - 1 }) : LiabilityCalculators[trueLiabilityWithoutTrialCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, liabilityStrength - 1 });
        public (bool trulyLiable, byte liabilityStrength, byte damagesStrength) InvertedCalculations_WorkBackwardsFromSignals(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed)
        {
            Random r = new Random(randomSeed);
            double[] liabilityStrengthProbabilities = InvertedCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
            byte liabilityStrength = ArrayUtilities.ChooseIndex_OneBasedByte(liabilityStrengthProbabilities, r.NextDouble());
            double[] trulyLiableProbabilities = InvertedCalculations_GetLiabilityTrueValueProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal, liabilityStrength);
            byte trulyLiableIndex = ArrayUtilities.ChooseIndex_OneBasedByte(trulyLiableProbabilities, r.NextDouble());
            bool trulyLiable = trulyLiableIndex == 2;
            double[] damagesStrengthProbabilities = InvertedCalculations_GetDamagesStrengthProbabilities(pDamagesSignal, dDamagesSignal, cDamagesSignal);
            byte damagesStrength = ArrayUtilities.ChooseIndex_OneBasedByte(damagesStrengthProbabilities, r.NextDouble());
            return (trulyLiable, liabilityStrength, damagesStrength);
        }

        public List<(GameProgress progress, double weight)> InvertedCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress gameProgress)
        {
            List<(GameProgress progress, double weight)> withLiabilityStrength = new List<(GameProgress progress, double weight)>();
            double[] liabilityStrengthProbabilities = InvertedCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
            for (byte p = 1; p <= liabilityStrengthProbabilities.Length; p++)
            {
                double probability = liabilityStrengthProbabilities[p - 1];
                var copy = gameProgress.DeepCopy();
                copy.LiabilityStrengthDiscrete = p;
                withLiabilityStrength.Add((copy, probability));
            }

            List<(GameProgress progress, double weight)> withTrulyLiable = new List<(GameProgress progress, double weight)>();
            foreach (var gp in withLiabilityStrength)
            {
                double[] trulyLiableProbabilities = InvertedCalculations_GetLiabilityTrueValueProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal, ((LitigGameProgress)gp.progress).LiabilityStrengthDiscrete);
                for (byte p = 1; p <= trulyLiableProbabilities.Length; p++)
                {
                    double probability = trulyLiableProbabilities[p - 1];
                    var copy = (LitigGameProgress) gp.progress.DeepCopy();
                    copy.IsTrulyLiable = p == 2;
                    withTrulyLiable.Add((copy, probability));
                }
            }

            List<(GameProgress progress, double weight)> withDamagesStrength = new List<(GameProgress progress, double weight)>();
            foreach (var gp in withTrulyLiable)
            {
                double[] damagesStrengthProbabilities = InvertedCalculations_GetDamagesStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
                for (byte p = 1; p <= damagesStrengthProbabilities.Length; p++)
                {
                    double probability = damagesStrengthProbabilities[p - 1];
                    var copy = (LitigGameProgress)gp.progress.DeepCopy();
                    copy.DamagesStrengthDiscrete = p;
                    withTrulyLiable.Add((copy, probability));
                }
            }

            return withDamagesStrength;
        }


    }
}

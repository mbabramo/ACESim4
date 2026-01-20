using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.ArrayManipulation;
using ACESimBase.Util.DiscreteProbabilities;
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
    public class LitigGameExogenousDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public override string GetGeneratorName() => "Exog";
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

        public override void Setup(LitigGameDefinition litigGameDefinition)
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
        public override string OptionsString => $"ExogenousProbabilityTrulyLiable {ExogenousProbabilityTrulyLiable} StdevNoiseToProduceLiabilityStrength {StdevNoiseToProduceLiabilityStrength}";
        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("PrePrimaryChanceActions", "Pre Primary");
        public override (string name, string abbreviation) PrimaryNameAndAbbreviation => ("PrimaryChanceActions", "Primary");
        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("Truly Liable", "TruLiab");
        public override string GetActionString(byte action, byte decisionByteCode)
        {
            if (decisionByteCode == (byte)LitigGameDecisions.PostPrimaryActionChance)
                return action == 1 ? "Truly Not Liable" : "Truly Liable";
            return action.ToString();
        }

        public override void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
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

        public override double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            return 0;
        }

        double[] NoWealthEffects = new double[] { 0, 0 };
        public override double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            return NoWealthEffects; 
        }

        public override (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            return (0.0, 0.0);  // inapplicable
        }

        public override double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions)
        {
            bool isTrulyLiable = IsTrulyLiable(myGameDefinition, disputeGeneratorActions, null);
            if (isTrulyLiable)
                return ProbabilitiesLiabilityStrength_TrulyLiable;
            else
                return ProbabilitiesLiabilityStrength_TrulyNotLiable;
        }

        public override double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions) => ProbabilityOfDamagesStrengthValues;

        
        public override bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            if (gameProgress is LitigGameProgress litigGameProgress && litigGameProgress.IsTrulyLiable is bool trulyLiable)
                return trulyLiable;

            return disputeGeneratorActions.PostPrimaryChanceAction == 2;
        }

        public void ModifyOptions(ref LitigGameOptions myGameOptions)
        {
            // nothing to modify
        }

        public override bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress p)
        {
            return true;
        }

        public override bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public override bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            throw new NotImplementedException();
        }

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // no pre primary chance function
        }

        public override double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityOfTrulyLiableValues;
        }

        public override bool PostPrimaryDoesNotAffectStrategy() => true;

        public override bool SupportsSymmetry() => true;

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings() =>
            (true, SymmetryMapInput.SameInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings() =>
            (true, SymmetryMapInput.SameInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);

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
            if (!o.CollapseChanceDecisions)
                return;

            const int numTrueLiabilityValues = 2;
            int numCourtLiabilitySignals = o.NumCourtLiabilitySignals;
            int[] liabilityDimensions = new int[] { numTrueLiabilityValues, o.NumLiabilityStrengthPoints, o.NumLiabilitySignals, o.NumLiabilitySignals, numCourtLiabilitySignals };
            double[] liabilityPrior = ProbabilityOfTrulyLiableValues;

            List<VariableProductionInstruction> liabilitySignalsInstructions = new List<VariableProductionInstruction>()
                {
                    new IndependentVariableProductionInstruction(liabilityDimensions, 0, liabilityPrior), // true liability is given by the prior
                    new DiscreteValueParametersVariableProductionInstruction(liabilityDimensions, trueLiabilityIndex /* taking from two initial values */, true /* which map onto extreme values of 0 and 1 */, StdevNoiseToProduceLiabilityStrength /* adding noise */, 1), /* liability strength */
                    new DiscreteValueParametersVariableProductionInstruction(liabilityDimensions, liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.PLiabilityNoiseStdev /* noise */, 2),  /* p liability signal */ 
                    new DiscreteValueParametersVariableProductionInstruction(liabilityDimensions, liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.DLiabilityNoiseStdev /* noise */, 3),  /* d liability signal */
                    new DiscreteValueParametersVariableProductionInstruction(liabilityDimensions, liabilityStrengthIndex /* liability strength */, false /* from zero to one but excluding extremes */, o.CourtLiabilityNoiseStdev /* noise */, 4),  /* c liability signal */
                };
            pLiabilitySignalProbabilitiesUnconditional = DiscreteProbabilityDistribution.GetUnconditionalProbabilities(liabilityDimensions, liabilitySignalsInstructions, pLiabilitySignalIndex);
            var liabilityCalculatorsToProduce = new List<(int distributionVariableIndex, List<int> fixedVariableIndices)>()
                {
                    (dLiabilitySignalIndex, new List<int>() { pLiabilitySignalIndex }), // defendant's signal based on plaintiff's signal
                    (cLiabilitySignalIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex}), // court liability based on plaintiff's and defendant's signals
                    (liabilityStrengthIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, cLiabilitySignalIndex }), // liability strength based on all of above
                    (trueLiabilityIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, cLiabilitySignalIndex, liabilityStrengthIndex, }), // true value based on all of the above
                    (liabilityStrengthIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, }), // liability strength based on everything but court info
                    (trueLiabilityIndex, new List<int>() { pLiabilitySignalIndex, dLiabilitySignalIndex, liabilityStrengthIndex, }) // true value based on everything but court info
                };
            LiabilityCalculators = DiscreteProbabilityDistribution.GetProbabilityMapCalculators(liabilityDimensions, liabilitySignalsInstructions, liabilityCalculatorsToProduce);

            int[] damagesDimensions = new int[] { o.NumDamagesStrengthPoints, o.NumDamagesSignals, o.NumDamagesSignals, o.NumDamagesSignals };
            double[] damagesPrior = Enumerable.Range(0, o.NumDamagesStrengthPoints).Select(x => 1.0 / o.NumDamagesStrengthPoints).ToArray();

            List<VariableProductionInstruction> damagesSignalsInstructions = new List<VariableProductionInstruction>()
                {
                    new IndependentVariableProductionInstruction(damagesDimensions, 0, damagesPrior), // damages strength is given by the prior
                    new DiscreteValueParametersVariableProductionInstruction(damagesDimensions, damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.PDamagesNoiseStdev /* noise */, 1),  /* p damages signal */ 
                    new DiscreteValueParametersVariableProductionInstruction(damagesDimensions, damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.DDamagesNoiseStdev /* noise */, 2),  /* d damages signal */
                    new DiscreteValueParametersVariableProductionInstruction(damagesDimensions, damagesStrengthIndex /* damages strength */, false /* from zero to one but excluding extremes */, o.CourtDamagesNoiseStdev /* noise */, 3),  /* c damages signal */
                };
            pDamagesSignalProbabilitiesUnconditional = DiscreteProbabilityDistribution.GetUnconditionalProbabilities(damagesDimensions, damagesSignalsInstructions, pDamagesSignalIndex);
            var damagesCalculatorsToProduce = new List<(int distributionVariableIndex, List<int> fixedVariableIndices)>()
                {
                    (dDamagesSignalIndex, new List<int>() { pDamagesSignalIndex }), // defendant's signal based on plaintiff's signal
                    (cDamagesSignalIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex}), // court damages based on plaintiff's and defendant's signals
                    (damagesStrengthIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex, cDamagesSignalIndex }), // damages strength based on all of above
                    (damagesStrengthIndex, new List<int>() { pDamagesSignalIndex, dDamagesSignalIndex }), // damages strength when no trial has occurred
                };
            DamagesCalculators = DiscreteProbabilityDistribution.GetProbabilityMapCalculators(damagesDimensions, damagesSignalsInstructions, damagesCalculatorsToProduce);
        }

        // Interface implementations
        public override double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal) => pLiabilitySignalProbabilitiesUnconditional;
        public override double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal) => LiabilityCalculators[dLiabilitySignalCalculatorIndex](new List<int>() { ((byte)pLiabilitySignal) - 1});
        public override double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal) => LiabilityCalculators[cLiabilitySignalCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1 });

        public override double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal) => pDamagesSignalProbabilitiesUnconditional;
        public override double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal) => DamagesCalculators[dDamagesSignalCalculatorIndex](new List<int>() { ((byte)pDamagesSignal) - 1 });
        public override double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal) => DamagesCalculators[cDamagesSignalCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1}); 
        public override double[] BayesianCalculations_GetLiabilityStrengthProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal) => cLiabilitySignal is byte cLiabilitySignalNotNull ? LiabilityCalculators[liabilityStrengthCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, cLiabilitySignalNotNull - 1 }) : LiabilityCalculators[liabilityStrengthWithoutTrialCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1 });
        public override double[] BayesianCalculations_GetDamagesStrengthProbabilities(byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal) => cDamagesSignal is byte cDamagesSignalNotNull ? DamagesCalculators[damagesStrengthCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1, cDamagesSignalNotNull - 1 }) : DamagesCalculators[damagesStrengthWithoutTrialCalculatorIndex](new List<int>() { pDamagesSignal - 1, dDamagesSignal - 1 });

        // Additional implementations. This dispute generator calculates 
        public double[] BayesianCalculations_GetLiabilityTrueValueProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte liabilityStrength) => cLiabilitySignal is byte cLiabilitySignalNotNull ? LiabilityCalculators[trueLiabilityCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, cLiabilitySignalNotNull - 1, liabilityStrength - 1 }) : LiabilityCalculators[trueLiabilityWithoutTrialCalculatorIndex](new List<int>() { pLiabilitySignal - 1, dLiabilitySignal - 1, liabilityStrength - 1 });
        public override void BayesianCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed)
        {
            Random r = new Random(randomSeed);
            double[] liabilityStrengthProbabilities = BayesianCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
            byte liabilityStrength = ArrayUtilities.ChooseIndex_OneBasedByte(liabilityStrengthProbabilities, r.NextDouble());
            double[] trulyLiableProbabilities = BayesianCalculations_GetLiabilityTrueValueProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal, liabilityStrength);
            byte trulyLiableIndex = ArrayUtilities.ChooseIndex_OneBasedByte(trulyLiableProbabilities, r.NextDouble());
            bool trulyLiable = trulyLiableIndex == 2;
            double[] damagesStrengthProbabilities = BayesianCalculations_GetDamagesStrengthProbabilities(pDamagesSignal, dDamagesSignal, cDamagesSignal);
            byte damagesStrength = ArrayUtilities.ChooseIndex_OneBasedByte(damagesStrengthProbabilities, r.NextDouble());
            (gameProgress.IsTrulyLiable, gameProgress.LiabilityStrengthDiscrete, gameProgress.DamagesStrengthDiscrete) = (trulyLiable, liabilityStrength, damagesStrength);
        }

        public override List<(GameProgress progress, double weight)> BayesianCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress gameProgress)
        {
            List<(GameProgress progress, double weight)> withLiabilityStrength = new List<(GameProgress progress, double weight)>();
            double[] liabilityStrengthProbabilities = BayesianCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
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
                double[] trulyLiableProbabilities = BayesianCalculations_GetLiabilityTrueValueProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal, ((LitigGameProgress)gp.progress).LiabilityStrengthDiscrete);
                for (byte p = 1; p <= trulyLiableProbabilities.Length; p++)
                {
                    double probability = trulyLiableProbabilities[p - 1] * gp.weight;
                    var copy = (LitigGameProgress) gp.progress.DeepCopy();
                    copy.IsTrulyLiable = p == 2;
                    withTrulyLiable.Add((copy, probability));
                }
            }

            List<(GameProgress progress, double weight)> withDamagesStrength = new List<(GameProgress progress, double weight)>();
            foreach (var gp in withTrulyLiable)
            {
                double[] damagesStrengthProbabilities = BayesianCalculations_GetDamagesStrengthProbabilities(pDamagesSignal, dDamagesSignal, cDamagesSignal);
                for (byte p = 1; p <= damagesStrengthProbabilities.Length; p++)
                {
                    double probability = damagesStrengthProbabilities[p - 1];
                    var copy = (LitigGameProgress)gp.progress.DeepCopy();
                    copy.DamagesStrengthDiscrete = p;
                    withDamagesStrength.Add((copy, probability * gp.weight));
                }
            }

            return withDamagesStrength;
        }


    }
}





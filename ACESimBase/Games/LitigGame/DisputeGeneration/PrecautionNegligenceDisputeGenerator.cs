using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.ArrayManipulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static HDF.PInvoke.H5T;

namespace ACESim // Assuming the ACESim base namespace; adjust if needed
{
    /// <summary>
    /// Dispute generator for a negligence case with precaution investment and accident chance.
    /// Simulates the sequence from precaution choice through accident, engagement, settlement, and trial.
    /// Supports full simulation mode (explicit chance nodes for accident/trial) and collapsed chance mode (integrated probabilities).
    /// </summary>
    public class PrecautionNegligenceDisputeGenerator : ILitigGameDisputeGenerator
    {
        public LitigGameDefinition LitigGameDefinition { get; set; }
        public LitigGameProgress CreateGameProgress(bool fullHistoryRequired) => new PrecautionNegligenceProgress(fullHistoryRequired);

        public LitigGameOptions Options => LitigGameDefinition.Options;

        public string OptionsString => $"{nameof(CostOfAccident)}: {CostOfAccident} {nameof(MarginalPrecautionCost)}: {MarginalPrecautionCost} {nameof(PrecautionPowerLevels)}: {PrecautionPowerLevels} {nameof(PrecautionLevels)}: {PrecautionLevels} {nameof(PrecautionPowerFactor)}: {PrecautionPowerFactor} {nameof(ProbabilityAccidentNoActivity)}: {ProbabilityAccidentNoActivity} {nameof(ProbabilityAccidentNoPrecaution)}: {ProbabilityAccidentNoPrecaution} {nameof(ProbabilityAccidentWrongfulAttribution)}: {ProbabilityAccidentWrongfulAttribution} {nameof(LiabilityThreshold)}: {LiabilityThreshold} ";

        public double BenefitToDefendantOfActivity = 3.0; // back of the envelope suggests 0.00005 might make the defendant on the borderline of whether to engage in the activity. Set to much higher value to make it so that defendant always engages in the activity
        public double CostOfAccident = 1.0; // normalized harm in the event of an accident
        public double MarginalPrecautionCost = 0.00001;
        public byte PrecautionPowerLevels = 10; // can be high if we're collapsing chance decisions, since this is the decision that gets collapsed
        public byte PrecautionLevels = 5;
        public double PrecautionPowerFactor = 0.8;
        public double ProbabilityAccidentNoActivity = 0.0;
        public double ProbabilityAccidentNoPrecaution = 0.0001;
        public double ProbabilityAccidentWrongfulAttribution = 0.000025;
        public double LiabilityThreshold = 1.0; // liability if benefit/cost ratio for marginal forsaken precaution > 1


        // Models for domain-specific logic
        private PrecautionImpactModel _impactModel;
        private PrecautionSignalModel _signalModel;
        private PrecautionCourtDecisionModel _courtDecisionModel;

        // Precomputed lookup tables
        private double[] _precautionPowerProbabilities; // Probability of each precaution level
        private double[] _dSignalProbabilities; // Probability of each defendant signal, when NOT conditioned on the precaution power level (in collapse chance mode)
        private double[][] _pSignalProbabilitiesGivenDSignal;
        private double[][] _pSignalProbabilitiesGivenPrecautionPower;

        /// <summary>
        /// Initializes a new PrecautionNegligenceDisputeGenerator with the given models and settings.
        /// </summary>
        /// <param name="impactModel">Model mapping precaution level to accident probability (and possibly related parameters).</param>
        /// <param name="signalModel">Model for generating private precaution signals for each party.</param>
        /// <param name="courtDecisionModel">Model defining the court's liability threshold and decision logic.</param>
        /// <param name="collapseChanceDecisions">If true, use collapsed chance mode; if false, use full simulation mode.</param>
        /// <param name="enableSettlement">If true, include a settlement negotiation stage before trial.</param>
        /// <param name="random">Optional random number generator for chance events (useful for simulation runs).</param>
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            var options = LitigGameDefinition.Options;
            options.DamagesMax = options.DamagesMin = CostOfAccident;
            options.NumDamagesStrengthPoints = 1;
            _impactModel = new PrecautionImpactModel(PrecautionPowerLevels, PrecautionLevels, ProbabilityAccidentNoActivity, ProbabilityAccidentNoPrecaution, MarginalPrecautionCost, CostOfAccident, null, PrecautionPowerFactor, PrecautionPowerFactor, LiabilityThreshold, ProbabilityAccidentWrongfulAttribution, null);
            int numSamplesToMakeForCourtLiablityDetermination = 1000; // Note: This is different from NumCourtLiabilitySignals, which indicates the number of different branches that the court will receive and will thus generally be set to 2, for liability and no liability. Instead, this affects the fineness of the calculation of the probability of liability.
            _signalModel = new PrecautionSignalModel(PrecautionPowerLevels, options.NumLiabilitySignals, options.NumLiabilitySignals, numSamplesToMakeForCourtLiablityDetermination, options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev, options.CourtLiabilityNoiseStdev, includeExtremes: false);
            _courtDecisionModel = new PrecautionCourtDecisionModel(_impactModel, _signalModel);
            _precautionPowerProbabilities = Enumerable.Range(1, PrecautionPowerLevels).Select(x => 1.0 / PrecautionPowerLevels).ToArray();
            if (Options.CollapseChanceDecisions)
            {
                _dSignalProbabilities = _signalModel.GetUnconditionalDefendantSignalDistribution();
                _pSignalProbabilitiesGivenDSignal = _signalModel.GetPlaintiffSignalDistributionGivenDefendantSignal();
                _pSignalProbabilitiesGivenPrecautionPower = _signalModel.GetPlaintiffSignalProbabilityTable();
    }
        }

        public List<Decision> GenerateDisputeDecisions(LitigGameDefinition litigGameDefinition)
        {
            var list = new List<Decision>();
            bool collapse = litigGameDefinition.Options.CollapseChanceDecisions;

            if (!collapse)
                list.Add(new(
                    "Precaution Power", "PPow", true,
                    (byte)LitigGamePlayers.LiabilityStrengthChance,
                    new byte[] { (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                    PrecautionPowerLevels,
                    (byte)LitigGameDecisions.LiabilityStrength,
                    unevenChanceActions: false)
                {
                    StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_LiabilityStrength,
                    IsReversible = true,
                    DistributedChanceDecision = false, // using collapse chance instead
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = false,
                    SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
                }
                );

            list.Add(new("Defendant Signal", "DLS", true,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.DLiabilitySignal,
                unevenChanceActions: Options.CollapseChanceDecisions) 
            {
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            });

            var plaintiffSignalDecision = new Decision("Plaintiff Signal", "PLS", true,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.PLiabilitySignal,
                unevenChanceActions: Options.CollapseChanceDecisions) 
            {
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            };
            if (!Options.CollapseChanceDecisions)
                list.Add(plaintiffSignalDecision); // when not collapsing chance decisions, we want the game tree to be as straightforward as possible, and don't want to worry about Bayesian calculations. So, we give the plaintiff it's signal (conditional on the underlying precaution power level) early.

            list.Add(new("Engage in Activity", "ENG", false,
                (byte)LitigGamePlayers.Defendant,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                2, // engage or don't
                (byte)LitigGameDecisions.EngageInActivity)
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_EngagesInActivity,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric),
                CanTerminateGame = true
            }); // 1 = yes, 2 = no

            list.Add(new("Precaution", "PREC", false,
                (byte)LitigGamePlayers.Defendant,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.AccidentChance, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                PrecautionLevels,
                (byte)LitigGameDecisions.TakePrecaution)
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_PrecautionLevel,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric)
            }); // 1 = no precaution, ...

            list.Add(new("Accident", "ACC", true,
                (byte)LitigGamePlayers.AccidentChance,
                new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.CourtLiabilityChance, (byte)LitigGamePlayers.Resolution },
                (byte)2, /* accident or no accident */
                (byte)LitigGameDecisions.Accident,
                unevenChanceActions: true
                )
            {
                StoreActionInGameCacheItem = litigGameDefinition.GameHistoryCacheIndex_Accident,
                IsReversible = true,
                DistributedChanceDecision = false,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = false,
                SymmetryMap = (SymmetryMapInput.NotCompatibleWithSymmetry, SymmetryMapOutput.CantBeSymmetric),
                CanTerminateGame = true // only when probability of wrongful attribution is 0

            }); // 1 --> accident, 2 --> no accident


            if (Options.CollapseChanceDecisions)
                list.Add(plaintiffSignalDecision); // when collapsing chance decisions, we can add the plaintiff's signal here, so we don't need to even deal with it if no accident occurs. We'll have to deal with the Bayesian calculations.

            // The liability signals tables are used when NOT collapsing decisions. This logic is already built in, so we don't need to enhance it.
            litigGameDefinition.CreateLiabilitySignalsTables();
            if (litigGameDefinition.Options.NumDamagesStrengthPoints > 1)
                throw new NotImplementedException(); // gameDefinition.CreateDamagesSignalsTables();

            if (litigGameDefinition.Options.LoserPaysOnlyLargeMarginOfVictory || litigGameDefinition.Options.NumCourtLiabilitySignals != 2)
                throw new NotImplementedException(); // we are generally implementing only 2 signals for the court (plaintiff wins and plaintiff loses) -- margin of victory fee shifting requires more.

            return list;
        }

        public bool PotentialDisputeArises(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            return precautionProgress.EngagesInActivity && precautionProgress.AccidentOccurs;
        }

        public bool MarkCompleteAfterEngageInActivity(LitigGameDefinition g, byte engagesInActivityCode) => engagesInActivityCode == 2;
        public bool MarkCompleteAfterAccidentDecision(LitigGameDefinition g, byte accidentCode) => accidentCode == 2 /* no accident */; // Note that no accident means no accident of any kind, including a wrongfully attributed accident

        public bool HandleUpdatingGameProgress(LitigGameProgress gameProgress, byte currentDecisionByteCode, byte action)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;

            switch (currentDecisionByteCode)
            {
                case (byte)LitigGameDecisions.EngageInActivity:
                    bool engagesInActivity = action == 1;
                    precautionProgress.EngagesInActivity = engagesInActivity;
                    if (!engagesInActivity)
                        gameProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.TakePrecaution:
                    precautionProgress.RelativePrecautionLevel = action - 1;
                    // Note: we don't set the opportunity costs until after the game
                    break;
                case (byte)LitigGameDecisions.Accident:
                    bool accidentOccurs = action == 1;
                    precautionProgress.AccidentOccurs = accidentOccurs;
                    // We're not going to set HarmCost yet --> instead, we'll do that when we generate consistent game progresses.
                    // The reason for this is that if there is an accident, there is some probability that the accident was
                    // wrongfully attributed to the defendant. But when reporting, we would like to separate out the cases in 
                    // which there was and wasn't wrongful attribution, so that we can have graphics that separate out these
                    // two sets of cases. We can do that based on information later, taking into account the precaution level.
                    if (!accidentOccurs)
                        gameProgress.GameComplete = true;
                    break;
                default:
                    return false;
            }
            return true;
        }

        public bool IsTrulyLiable(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions,
            GameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            bool isTrulyLiable = _impactModel.IsTrulyLiable(precautionProgress.LiabilityStrengthDiscrete - 1, precautionProgress.RelativePrecautionLevel);
            return isTrulyLiable;
        }

        public double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions) => _precautionPowerProbabilities;

        public double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions) => [1.0];

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            var costs = GetOpportunityAndHarmCosts(gameDefinition, disputeGeneratorActions, gameProgress);
            return 0 - costs.harmCost - costs.opportunityCost;
        }

        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            var costs = GetOpportunityAndHarmCosts(gameDefinition, disputeGeneratorActions, gameProgress);
            return [0 - costs.harmCost, 0 - costs.opportunityCost];
        }

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGameProgress gameProgress)
        {
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;

            // recalculate this from scratch here, in case there have been changes based on post-game info beng reset
            var activityForegoneCost = precautionProgress.EngagesInActivity ? 0 : BenefitToDefendantOfActivity;
            var precautionTaken = precautionProgress.RelativePrecautionLevel * MarginalPrecautionCost;
            var harmCost = precautionProgress.AccidentProperlyCausallyAttributedToDefendant ? CostOfAccident : 0; // an accident that is not causally attributable to the defendant is not counted as a cost here, since it's exogenous to the model.

            return (activityForegoneCost + precautionTaken, harmCost);
        }

        public bool SupportsSymmetry() => false;

        public string GetGeneratorName() => "PrecautionNegligence";

        public string GetActionString(byte action, byte decisionByteCode) => action.ToString();


        public double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            // Defendant goes first, so we can just use the original liability signal tables.
            if (pLiabilitySignal is not (null or 0))
                throw new NotSupportedException(); // This should only be called in collapse decision mode, as the first signal to be computed
            return _dSignalProbabilities; // not conditioned on anything
        }

        public double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal)
        {
            // Note: This is used ONLY when collapsing chance decisions. We know the accident occurred, and that depends
            // on the level of precaution chosen by the defendant, but that decision is solely a function of the defendant's
            // liability signal, so it doesn't add any information to this signal calculation. However, whether an accident
            // occurred also depends on the hidden state itself. 
            byte dSignal = (byte)dLiabilitySignal; // should not be null, because defendant gets signal first in this game
            return _pSignalProbabilitiesGivenDSignal[dSignal - 1];
        }

        public double GetAccidentProbability(
            byte? precautionPowerLevel,
            byte dLiabilitySignal,
            byte chosenPrecautionLevel // zero-based, unlike others
            )
        {
            if ((uint)(dLiabilitySignal - 1) >= _signalModel.NumDSignals)
                throw new ArgumentOutOfRangeException(nameof(dLiabilitySignal));

            // Collapsed chance mode → integrate over hidden states.
            if (Options.CollapseChanceDecisions)
            {
                return _impactModel.GetAccidentProbabilityGivenDSignalAndPrecautionLevel(
                    (int)dLiabilitySignal - 1,
                    chosenPrecautionLevel,
                    _signalModel);
            }

            // Full tree mode → hidden state is known, signals add no extra information.
            return _impactModel.GetAccidentProbability(
                (int)precautionPowerLevel.Value - 1,
                chosenPrecautionLevel);
        }

        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(PrecautionNegligenceProgress gameProgress)
        {
            if (Options.CollapseChanceDecisions)
            {
                double[] pr = _courtDecisionModel.GetLiabilityOutcomeProbabilities(
                     gameProgress.PLiabilitySignalDiscrete - 1,   // make zero-based
                     gameProgress.DLiabilitySignalDiscrete - 1,
                     gameProgress.AccidentOccurs,
                     gameProgress.RelativePrecautionLevel // already zero-based
                     );

                return pr;
            }
            else
            {
                double[] pr = _courtDecisionModel.GetLiabilityOutcomeProbabilities(gameProgress.LiabilityStrengthDiscrete - 1, gameProgress.RelativePrecautionLevel /* already zero based */);
                return pr;
            }
        }

        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal)
        {
            throw new NotSupportedException(); // because above is implemented, this won't be called.
        }

        public double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal)
        {
            return [1.0];
        }

        public double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal)
        {
            return [1.0];
        }

        public double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal)
        {
            return [1.0];
        }

        private double[] BayesianCalculationOfPrecautionPowerDistribution(PrecautionNegligenceProgress precautionProgress)
        {
            double[] precautionPowerDistribution;
            if (precautionProgress.EngagesInActivity)
            {
                if (precautionProgress.AccidentOccurs)
                    precautionPowerDistribution = _courtDecisionModel.GetHiddenPosteriorFromPath(precautionProgress.PLiabilitySignalDiscrete - 1, precautionProgress.DLiabilitySignalDiscrete - 1, precautionProgress.AccidentOccurs, precautionProgress.RelativePrecautionLevel /* already zero-based */, precautionProgress.TrialOccurs ? precautionProgress.PWinsAtTrial : null);
                else
                {
                    precautionPowerDistribution = _courtDecisionModel.GetHiddenPosteriorFromNoAccidentScenario(precautionProgress.DLiabilitySignalDiscrete - 1, precautionProgress.RelativePrecautionLevel /* already zero-based */);
                }
            }
            else
                precautionPowerDistribution = _courtDecisionModel.GetHiddenPosteriorFromDefendantSignal(precautionProgress.DLiabilitySignalDiscrete - 1);
            return precautionPowerDistribution;
        }

        public void BayesianCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed)
        {
            Random r = new Random(randomSeed);
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            double[] precautionPowerDistribution = BayesianCalculationOfPrecautionPowerDistribution(precautionProgress);

            byte precautionPowerIndex = ArrayUtilities.ChooseIndex_OneBasedByte(precautionPowerDistribution, r.NextDouble());
            precautionProgress.LiabilityStrengthDiscrete = precautionPowerIndex;

            if (precautionProgress.AccidentOccurs)
            {
                // Note that plaintiff signal will be set, since the accident occurred.
                double probabilityWrongfulCausalAttribution = _impactModel.GetWrongfulAttributionProbabilityGivenHiddenState(precautionProgress.LiabilityStrengthDiscrete - 1, precautionProgress.RelativePrecautionLevel);
                precautionProgress.AccidentWronglyCausallyAttributedToDefendant = r.NextDouble() < probabilityWrongfulCausalAttribution;
            }
            else if (precautionProgress.PLiabilitySignalDiscrete == 0)
            {
                double[] pDist = _pSignalProbabilitiesGivenDSignal[precautionPowerIndex - 1];
                precautionProgress.PLiabilitySignalDiscrete = ArrayUtilities.ChooseIndex_OneBasedByte(pDist, r.NextDouble());
            }

            precautionProgress.ResetPostGameInfo();
        }

        public bool GenerateConsistentGameProgressesWhenNotCollapsing => true;

        public List<(GameProgress progress, double weight)> BayesianCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress baseProgress)
        {

            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)baseProgress;

            if (!Options.CollapseChanceDecisions)
            {
                // Even though we're not collapsing chance decisions, we only had a single chance node to determine
                // whether an accident occurred, so we don't yet know whether an accident that occurred has been 
                // wrongfully attributed to the defendant. We work out those calculations here.
                if (ProbabilityAccidentWrongfulAttribution > 0)
                {
                    return DuplicateProgressWithAndWithoutWrongfulAttribution(precautionProgress, 1.0);
                }
                else
                    return new List<(GameProgress progress, double weight)>() { (baseProgress.DeepCopy(), 1.0) };
            }

            List<(GameProgress progress, double weight)> result = new();

            double[] precautionPowerDistribution = BayesianCalculationOfPrecautionPowerDistribution(precautionProgress);

            baseProgress.ResetPostGameInfo(); // reset this because we're going to figure out wrongful attribution here

            for (int i = 1; i <= precautionPowerDistribution.Length; i++)
            {
                var copy = (PrecautionNegligenceProgress) baseProgress.DeepCopy();
                copy.LiabilityStrengthDiscrete = (byte) i;
                double[] pDist = null;
                if (precautionProgress.PLiabilitySignalDiscrete == 0)
                {
                    pDist = _pSignalProbabilitiesGivenDSignal[copy.LiabilityStrengthDiscrete - 1];
                }
                var withAndWithoutWrongfulAttribution = DuplicateProgressWithAndWithoutWrongfulAttribution(copy, precautionPowerDistribution[i - 1]);
                foreach (var progressWithWeight in withAndWithoutWrongfulAttribution)
                {
                    if (pDist == null)
                        result.Add(progressWithWeight);
                    else
                    { // make a different version for each possible p signal
                        for (int j = 1; i <= pDist.Length; i++)
                        {
                            PrecautionNegligenceProgress withPSignal = (PrecautionNegligenceProgress) progressWithWeight.progress.DeepCopy();
                            withPSignal.PLiabilitySignalDiscrete = (byte)j;
                            double revisedWeight = progressWithWeight.weight * pDist[j - 1];
                            result.Add((withPSignal, revisedWeight));
                        }
                    }
                }
            }

            return result;
        }

        private List<(GameProgress progress, double weight)> DuplicateProgressWithAndWithoutWrongfulAttribution(PrecautionNegligenceProgress precautionProgress, double weight)
        {
            if (precautionProgress.AccidentOccurs)
            {
                double probabilityWrongfulCausalAttribution = _impactModel.GetWrongfulAttributionProbabilityGivenHiddenState(precautionProgress.LiabilityStrengthDiscrete - 1, precautionProgress.RelativePrecautionLevel);
                var withWrongfulAttribution = (PrecautionNegligenceProgress)precautionProgress.DeepCopy();
                withWrongfulAttribution.AccidentWronglyCausallyAttributedToDefendant = true;
                withWrongfulAttribution.ResetPostGameInfo();
                var withoutWrongfulAttribution = (PrecautionNegligenceProgress)precautionProgress.DeepCopy();
                withoutWrongfulAttribution.AccidentWronglyCausallyAttributedToDefendant = false;
                withoutWrongfulAttribution.ResetPostGameInfo();
                return new List<(GameProgress progress, double weight)>()
                {
                    (withWrongfulAttribution, weight * probabilityWrongfulCausalAttribution),
                    (withoutWrongfulAttribution, weight * (1.0 - probabilityWrongfulCausalAttribution))
                };
            }
            else
            {
                return new List<(GameProgress progress, double weight)>()
                {
                    (precautionProgress, weight)
                };
            }
        }
    }
}
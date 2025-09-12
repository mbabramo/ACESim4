using ACESim;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.ArrayManipulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Dispute generator for the precaution-negligence scenario.
    /// Heavy probability tables now come directly from the refactored models;
    /// this class performs no run-time “Build*” calls.
    /// </summary>
    public sealed class PrecautionNegligenceDisputeGenerator : ILitigGameDisputeGenerator
    {
        // ----------------------------------------------------------------  public configuration -- OVERRIDDEN BY LITIGGAMEOPTIONSGENERATOR
        public double BenefitToDefendantOfActivity = 0.001;
        public double CostOfAccident = 1.0;
        public double UnitPrecautionCost = 0.00001;
        public byte PrecautionPowerLevels = 10;
        public byte RelativePrecautionLevels = 5;
        // calibration parameters for the P(t,p) curve
        public double ProbabilityAccidentNoPrecaution = 0.0001;  // pMax
        public double ProbabilityAccidentMaxPrecaution_LowPower = 0.0001; // since the power is not at the extreme, we will still end up with interpolated values
        public double ProbabilityAccidentMaxPrecaution_HighPower = 0.00000;
        public double AlphaLowPower = 3;
        public double AlphaHighPower = 1.5;
        public double ProbabilityAccidentWrongfulAttribution = 0.00001; 
        public double LiabilityThreshold = 1.0;
        int numSamplesToMakeForCourtLiablityDetermination = 1000;
        public CourtDecisionRule CourtDecisionRule = CourtDecisionRule.ProbitThreshold;
        public double CourtProbitScale = 0.1;
        public double CostMisestimationFactor = 1.0;
        private bool ChanceAffectsDPerceptionofPrecautionCost => CostMisestimationFactor != 1.0; // if true, then a chance decision will cause the marginal precaution cost to be multiplied by some number in determining the defendant's utility. But this won't affect the actual amount spent. This is a way of modeling a defendant who incorrectly perceives that the precaution cost is changing. 
        private double[] _PossiblePerceivedPrecautionCostMultipliers = null;
        private double[] PossiblePerceivedPrecautionCostMultipliers
        {
            get
            {
                if (_PossiblePerceivedPrecautionCostMultipliers == null)
                {
                    _PossiblePerceivedPrecautionCostMultipliers = [1.0 / CostMisestimationFactor, 1.0, 1.0 * CostMisestimationFactor];
                }
                return _PossiblePerceivedPrecautionCostMultipliers;
            }
        }

        // ----------------------------------------------------------------  linked models
        public PrecautionImpactModel impact;
        public PrecautionSignalModel signal;
        public PrecautionRiskModel risk;
        public PrecautionCourtDecisionModel court;

        // ----------------------------------------------------------------  cached light vectors/tables
        double[] dSignalProb;            // P(D-signal)
        double[][] dSignalGivenHidden;      // [h][d]
        double[][] pSignalGivenHidden;     // [h][p]
        double[][] pSignalGivenD_NoAct;    // [d][p]
        double[][][] pSignalGivenD_Acc;      // [d][k][p]
        double[][][] pSignalGivenD_NoAcc;    // [d][k][p]

        double[][][][] courtDistLiable;        // [p][d][k][c]
        double[][][][] courtDistNoLiable;      // [p][d][k][c]
        double[][][][] postAccLiable;          // [p][d][k][h]
        double[][][][] postAccNoLiable;        // [p][d][k][h]
        double[][][] postNoAccident;         // [d][k][h]
        double[][] postDefSignal;          // [d][h]

        // ----------------------------------------------------------------  ILitigGameDisputeGenerator plumbing
        public LitigGameDefinition LitigGameDefinition { get; set; }
        public LitigGameOptions Options => LitigGameDefinition.Options;

        public LitigGameProgress CreateGameProgress(bool fullHistoryRequired) =>
            new PrecautionNegligenceProgress(fullHistoryRequired);

        public string OptionsString =>
            $"{nameof(CostOfAccident)} {CostOfAccident}  {nameof(UnitPrecautionCost)} {UnitPrecautionCost} {nameof(PrecautionPowerLevels)} {PrecautionPowerLevels} {nameof(RelativePrecautionLevels)} {RelativePrecautionLevels} {nameof(ProbabilityAccidentNoPrecaution)} {ProbabilityAccidentNoPrecaution} {nameof(ProbabilityAccidentMaxPrecaution_LowPower)} {ProbabilityAccidentMaxPrecaution_LowPower} {nameof(ProbabilityAccidentMaxPrecaution_HighPower)} {ProbabilityAccidentMaxPrecaution_HighPower} {nameof(AlphaLowPower)} {AlphaLowPower} {nameof(AlphaHighPower)} {AlphaHighPower} {nameof(CostMisestimationFactor)} {CostMisestimationFactor} {nameof(ProbabilityAccidentWrongfulAttribution)} {ProbabilityAccidentWrongfulAttribution}";

        public string GetGeneratorName() => "PrecautionNegligence";
        public bool SupportsSymmetry() => false;
        public string GetActionString(byte action, byte decisionByteCode) => action.ToString();

        // ----------------------------------------------------------------  SETUP (creates & links models)
        public void Setup(LitigGameDefinition gameDefinition)
        {
            LitigGameDefinition = gameDefinition;
            var opt = gameDefinition.Options;

            // ----------------- Model chain -----------------
            impact = new PrecautionImpactModel(
                precautionPowerLevels: PrecautionPowerLevels,
                precautionLevels: RelativePrecautionLevels,
                pAccidentNoPrecaution: ProbabilityAccidentNoPrecaution,   // pMax
                pMinLow: ProbabilityAccidentMaxPrecaution_LowPower,
                pMinHigh: ProbabilityAccidentMaxPrecaution_HighPower,
                alphaLow: AlphaLowPower,
                alphaHigh: AlphaHighPower,
                marginalPrecautionCost: UnitPrecautionCost,
                harmCost: CostOfAccident,
                liabilityThreshold: LiabilityThreshold,
                pAccidentWrongfulAttribution: ProbabilityAccidentWrongfulAttribution);

            signal = new PrecautionSignalModel(
                numPrecautionPowerLevels: PrecautionPowerLevels,
                numPlaintiffSignals: opt.NumLiabilitySignals,
                numDefendantSignals: opt.NumLiabilitySignals,
                numCourtSignals: numSamplesToMakeForCourtLiablityDetermination,
                sigmaPlaintiff: opt.PLiabilityNoiseStdev,
                sigmaDefendant: opt.DLiabilityNoiseStdev,
                sigmaCourt: opt.CourtLiabilityNoiseStdev,
                includeExtremes: false);

            risk = new PrecautionRiskModel(impact, signal);
            court = new PrecautionCourtDecisionModel(impact, signal, CourtDecisionRule, CourtProbitScale);

            // ----------------- Cached light tables -----------------
            dSignalProb = signal.GetUnconditionalSignalDistribution(PrecautionSignalModel.DefendantIndex);
            pSignalGivenHidden = Enumerable.Range(0, impact.HiddenCount)
                                           .Select(h => signal.GetPlaintiffSignalDistributionGivenHidden(h))
                                           .ToArray();
            dSignalGivenHidden = Enumerable.Range(0, impact.HiddenCount)
                                           .Select(h => signal.GetDefendantSignalDistributionGivenHidden(h))
                                           .ToArray();
            pSignalGivenD_NoAct = Enumerable.Range(0, signal.NumDSignals)
                                            .Select(d => signal.GetPlaintiffSignalDistributionGivenDefendantSignal(d))
                                            .ToArray();

            int D = signal.NumDSignals;
            int K = impact.PrecautionLevels;
            pSignalGivenD_Acc = new double[D][][];
            pSignalGivenD_NoAcc = new double[D][][];
            for (int d = 0; d < D; d++)
            {
                pSignalGivenD_Acc[d] = new double[K][];
                pSignalGivenD_NoAcc[d] = new double[K][];
                for (int k = 0; k < K; k++)
                {
                    pSignalGivenD_Acc[d][k] = risk.GetPlaintiffSignalDistGivenDefendantAfterAccident(d, k);
                    pSignalGivenD_NoAcc[d][k] = risk.GetPlaintiffSignalDistGivenDefendantNoAccident(d, k);
                }
            }

            courtDistLiable = court.CourtSignalDistGivenLiabilityTable;
            courtDistNoLiable = court.CourtSignalDistGivenNoLiabilityTable;
            postAccLiable = court.HiddenPosteriorAccidentLiabilityTable;
            postAccNoLiable = court.HiddenPosteriorAccidentNoLiabilityTable;
            postNoAccident = court.HiddenPosteriorNoAccidentTable;
            postDefSignal = court.HiddenPosteriorDefendantSignalTable;
        }


        // ----------------------------------------------------------------  GAME-TREE CONSTRUCTION
        public List<Decision> GenerateDisputeDecisions(LitigGameDefinition g)
        {
            var r = new List<Decision>();
            bool collapse = g.Options.CollapseChanceDecisions;

            var chanceRecipients = new HashSet<byte>
            {
                (byte)LitigGamePlayers.LiabilityStrengthChance,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                (byte)LitigGamePlayers.AccidentChance,
                (byte)LitigGamePlayers.CourtLiabilityChance,
                (byte)LitigGamePlayers.PreBargainingRoundChance,
                (byte)LitigGamePlayers.PostBargainingRoundChance,
                (byte)LitigGamePlayers.BothGiveUpChance,
                (byte)LitigGamePlayers.Resolution
            };
            var chanceRecipientsPlusP = new HashSet<byte>(chanceRecipients);
            chanceRecipientsPlusP.Add((byte)LitigGamePlayers.Plaintiff);
            var chanceRecipientsPlusD = new HashSet<byte>(chanceRecipients);
            chanceRecipientsPlusD.Add((byte)LitigGamePlayers.Defendant);
            var everyone = new HashSet<byte>(chanceRecipients);
            everyone.Add((byte)LitigGamePlayers.Plaintiff);
            everyone.Add((byte)LitigGamePlayers.Defendant);
            if (ChanceAffectsDPerceptionofPrecautionCost)
                chanceRecipients.Add((byte)LitigGamePlayers.DPrecautionCostPerceptionChance);

            if (!collapse)
                r.Add(new(
                    "Precaution Power", "PPow", true,
                    (byte)LitigGamePlayers.LiabilityStrengthChance,
                    chanceRecipients.ToArray(),
                    PrecautionPowerLevels,
                    (byte)LitigGameDecisions.LiabilityStrength,
                    unevenChanceActions: false)
                {
                    StoreActionInGameCacheItem = g.GameHistoryCacheIndex_LiabilityStrength,
                    IsReversible = true,
                    Unroll_Parallelize = true
                });

            if (ChanceAffectsDPerceptionofPrecautionCost)
            {
                r.Add(new("Defendant Cost Perception", "DCP", true,
                    (byte)LitigGamePlayers.DPrecautionCostPerceptionChance,
                    chanceRecipientsPlusD.ToArray(),
                    (byte) PossiblePerceivedPrecautionCostMultipliers.Length,
                    (byte)LitigGameDecisions.DPrecautionCostPerceptionChance,
                    unevenChanceActions: false)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true
                });
            }

            r.Add(new("Defendant Signal", "DLS", true,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                chanceRecipientsPlusD.ToArray(),
                g.Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.DLiabilitySignal,
                unevenChanceActions: true)
            {
                IsReversible = true,
                Unroll_Parallelize = true
            });

            var plsDecision = new Decision("Plaintiff Signal", "PLS", true,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                chanceRecipientsPlusP.ToArray(),
                g.Options.NumLiabilitySignals,
                (byte)LitigGameDecisions.PLiabilitySignal,
                unevenChanceActions: true)
            {
                IsReversible = true,
                Unroll_Parallelize = true
            };
            if (!collapse) r.Add(plsDecision);

            r.Add(new("Engage in Activity", "ENG", false,
                (byte)LitigGamePlayers.Defendant,
                everyone.ToArray(),
                2,
                (byte)LitigGameDecisions.EngageInActivity)
            {
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_EngagesInActivity,
                IsReversible = true,
                CanTerminateGame = true
            });

            r.Add(new("Precaution", "PREC", false,
                (byte)LitigGamePlayers.Defendant,
                everyone.ToArray(),
                RelativePrecautionLevels,
                (byte)LitigGameDecisions.TakePrecaution)
            {
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_PrecautionLevel,
                IsReversible = true
            });

            r.Add(new("Accident", "ACC", true,
                (byte)LitigGamePlayers.AccidentChance,
                everyone.ToArray(),
                2,
                (byte)LitigGameDecisions.Accident,
                unevenChanceActions: true)
            {
                StoreActionInGameCacheItem = g.GameHistoryCacheIndex_Accident,
                IsReversible = true,
                CanTerminateGame = true
            });

            if (collapse) r.Add(plsDecision);

            g.CreateLiabilitySignalsTables();
            return r;
        }


        // ----------------------------------------------------------------  BASIC ACCESSORS
        public bool PotentialDisputeArises(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions acts,
                                           LitigGameProgress prog)
            => ((PrecautionNegligenceProgress)prog).EngagesInActivity &&
               ((PrecautionNegligenceProgress)prog).AccidentOccurs;

        public bool MarkCompleteAfterEngageInActivity(LitigGameDefinition g, byte code) => code == 2;
        public bool MarkCompleteAfterAccidentDecision(LitigGameDefinition g, byte code) => code == 2;

        public bool HandleUpdatingGameProgress(LitigGameProgress progress, byte decision, byte action)
        {
            var p = (PrecautionNegligenceProgress)progress;
            switch ((LitigGameDecisions)decision)
            {
                case LitigGameDecisions.DPrecautionCostPerceptionChance:
                    p.DPrecautionCostPerceptionMultiple = PossiblePerceivedPrecautionCostMultipliers[action - 1];
                    break;
                case LitigGameDecisions.EngageInActivity:
                    p.EngagesInActivity = action == 1;
                    progress.GameComplete = !p.EngagesInActivity;
                    break;
                case LitigGameDecisions.TakePrecaution:
                    p.RelativePrecautionLevel = action - 1;
                    break;
                case LitigGameDecisions.Accident:
                    p.AccidentOccurs = action == 1;
                    progress.GameComplete = !p.AccidentOccurs;
                    break;
                default: return false;
            }
            return true;
        }

        public (double PPerceivedWealthChange, double DPerceivedWealthChange) GetSupplementalPerceivedWealthChange(LitigGameProgress gameProgress)
        {
            var precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            double actualPrecautionCosts = precautionProgress.RelativePrecautionLevel * UnitPrecautionCost;
            double perceivedPrecautionCosts = actualPrecautionCosts * precautionProgress.DPrecautionCostPerceptionMultiple;
            double dPerceivedWealthChange = actualPrecautionCosts - perceivedPrecautionCosts; // i.e., if perceived costs are lower, then d's perceived wealth and thus d's utility goes up, even though all other data reported in progress don't reflect this
            return (0, dPerceivedWealthChange);
        }

        public bool IsTrulyLiable(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a,
                                  GameProgress prog)
        {
            var p = (PrecautionNegligenceProgress)prog;
            return impact.IsTrulyLiable(p.LiabilityStrengthDiscrete - 1, p.RelativePrecautionLevel);
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition g,
                                                          LitigGameStandardDisputeGeneratorActions a)
            => Enumerable.Repeat(1.0 / PrecautionPowerLevels, PrecautionPowerLevels).ToArray();

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition g,
                                                        LitigGameStandardDisputeGeneratorActions a) => [1.0];

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition g,
                                                             LitigGameStandardDisputeGeneratorActions a,
                                                             LitigGameProgress prog)
        {
            var (opp, harm) = GetOpportunityAndHarmCosts(g, a, prog);
            return -opp - harm;
        }

        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition g,
                                                              LitigGameStandardDisputeGeneratorActions a,
                                                              LitigGameProgress prog)
        {
            var (opp, harm) = GetOpportunityAndHarmCosts(g, a, prog);
            return [-harm, -opp];
        }

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition g,
                                                                     LitigGameStandardDisputeGeneratorActions a,
                                                                     LitigGameProgress prog)
        {
            var p = (PrecautionNegligenceProgress)prog;
            double opp = p.EngagesInActivity ? 0 : BenefitToDefendantOfActivity;
            opp += p.RelativePrecautionLevel * UnitPrecautionCost;
            double harm = p.AccidentProperlyCausallyAttributedToDefendant ? CostOfAccident : 0;
            return (opp, harm);
        }

        // ----------------------------------------------------------------  BAYESIAN HELPERS
        public double[] GetUnevenChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            // Special handling in this method. Other decisions are handled by default in LitigGameDefinition,
            // often by calling methods here.
            PrecautionNegligenceProgress precautionProgress = (PrecautionNegligenceProgress)gameProgress;
            switch (decisionByteCode)
            {
                case (byte)LitigGameDecisions.DLiabilitySignal:
                    {
                        if (Options.CollapseChanceDecisions)
                            return dSignalProb;                           // unconditional P(D-signal) 

                        byte trueLiabilityStrength = precautionProgress.LiabilityStrengthDiscrete;
                        return dSignalGivenHidden[trueLiabilityStrength - 1];
                    }
                case (byte)LitigGameDecisions.PLiabilitySignal:
                    {
                        double[] probabilities;
                        if (Options.CollapseChanceDecisions)
                        {
                            probabilities = BayesianCalculations_GetPLiabilitySignalProbabilities(precautionProgress.DLiabilitySignalDiscrete, (byte)precautionProgress.RelativePrecautionLevel);
                        }
                        else
                        {
                            byte trueLiabilityStrength = precautionProgress.LiabilityStrengthDiscrete;
                            probabilities = pSignalGivenHidden[trueLiabilityStrength - 1];
                        }
                        return probabilities;
                    }
                case (byte)LitigGameDecisions.CourtDecisionLiability:
                    {
                        if (Options.CollapseChanceDecisions)
                        {
                            double[] probabilities = BayesianCalculations_GetCLiabilitySignalProbabilities(precautionProgress);
                            return probabilities;
                        }
                        else
                        {
                            int hiddenState = precautionProgress.LiabilityStrengthDiscrete - 1;
                            int k = precautionProgress.RelativePrecautionLevel;
                            return court.GetLiabilityOutcomeProbabilities(hiddenState, k);
                        }
                    }

                case (byte)LitigGameDecisions.Accident:
                    {
                        var myGameProgress = ((PrecautionNegligenceProgress)gameProgress);
                        var myDisputeGenerator = (PrecautionNegligenceDisputeGenerator)Options.LitigGameDisputeGenerator;
                        double accidentProbability = myDisputeGenerator.GetAccidentProbability(myGameProgress.LiabilityStrengthDiscrete, myGameProgress.DLiabilitySignalDiscrete, (byte)myGameProgress.RelativePrecautionLevel);
                        return new double[] { accidentProbability, 1 - accidentProbability };
                    }
                default:
                    return null;
            }
        }

        public double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dSignal)
            => throw new NotSupportedException();
        public double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            // Defendant draws first; plaintiff signal must be unknown or zero.
            if (pLiabilitySignal is > 0)
                throw new NotSupportedException("Plaintiff signal must be null or 0 at this stage.");

            return dSignalProb;   // unconditional P(D-signal)
        }

        public double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dSignal, byte level)
            => pSignalGivenD_Acc[dSignal!.Value - 1][level];

        // choose the correct accident probability depending on whether the hidden
        // precaution‑power has already been realised (power > 0) or is still latent
        double GetAccidentProbability(byte? precautionPowerDiscrete,
                                      byte defendantSignalDiscrete,
                                      byte relativePrecautionLevel)
        {
            // Hidden state known: use the per‑state accident chance from the impact model.
            if (precautionPowerDiscrete.HasValue && precautionPowerDiscrete.Value != 0)
                return impact.GetAccidentProbability(
                    precautionPowerDiscrete.Value - 1,           // zero‑based index
                    relativePrecautionLevel);

            // Hidden state latent (collapsed tree): use the average conditional on D‑signal.
            return risk.GetAccidentProbabilityGivenDefendantSignal(
                defendantSignalDiscrete - 1,                     // zero‑based index
                relativePrecautionLevel);
        }


        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(
            PrecautionNegligenceProgress p)
        {
            int k = p.RelativePrecautionLevel;
            return court.GetLiabilityOutcomeProbabilities(
                p.PLiabilitySignalDiscrete - 1,
                p.DLiabilitySignalDiscrete - 1,
                p.AccidentOccurs,
                k);
        }

        public double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pSig, byte dSig)
            => throw new NotImplementedException();

        public double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamages) => [1.0];
        public double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamages) => [1.0];
        public double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamages, byte dDamages) => [1.0];



        public bool GenerateConsistentGameProgressesWhenNotCollapsing => true;

        public void BayesianCalculations_WorkBackwardsFromSignals(
    LitigGameProgress prog,
    byte pSig, byte dSig, byte? cSig,
    byte pDam, byte dDam, byte? cDam, int seed)
        {
            var pr = (PrecautionNegligenceProgress)prog;
            var rng = new Random(seed);

            // ----------------------------------------------------  infer plaintiff signal if still unset
            if (pSig == 0)
            {
                if (!pr.EngagesInActivity)
                {
                    double[] pDist = pSignalGivenD_NoAct[dSig - 1];                // :contentReference[oaicite:6]{index=6}
                    pSig = (byte)ArrayUtilities.ChooseIndex_OneBasedByte(pDist, rng.NextDouble());
                }
                else if (!pr.AccidentOccurs)
                {
                    double[] pDist = pSignalGivenD_NoAcc[dSig - 1][pr.RelativePrecautionLevel];   // :contentReference[oaicite:7]{index=7}
                    pSig = (byte)ArrayUtilities.ChooseIndex_OneBasedByte(pDist, rng.NextDouble());
                }
            }

            // ----------------------------------------------------  posterior over hidden state
            double[] posterior = pr switch
            {
                { EngagesInActivity: true, AccidentOccurs: true, TrialOccurs: true, PWinsAtTrial: true } =>
                    postAccLiable[pSig - 1][dSig - 1][pr.RelativePrecautionLevel],
                { EngagesInActivity: true, AccidentOccurs: true, TrialOccurs: true, PWinsAtTrial: false } =>
                    postAccNoLiable[pSig - 1][dSig - 1][pr.RelativePrecautionLevel],
                { EngagesInActivity: true, AccidentOccurs: true } =>
                    court.GetHiddenPosteriorFromPath(pSig - 1, dSig - 1, true,
                                                     pr.RelativePrecautionLevel, null),
                { EngagesInActivity: true, AccidentOccurs: false } =>
                    postNoAccident[dSig - 1][pr.RelativePrecautionLevel],
                _ =>
                    postDefSignal[dSig - 1]
            };

            pr.LiabilityStrengthDiscrete =
                (byte)ArrayUtilities.ChooseIndex_OneBasedByte(posterior, rng.NextDouble());

            if (pr.AccidentOccurs)
            {
                double probWrong = risk.GetWrongfulAttributionProbabilityGivenSignals(
                                       dSig - 1, pSig - 1, pr.RelativePrecautionLevel);
                pr.AccidentWronglyCausallyAttributedToDefendant = rng.NextDouble() < probWrong;
            }

            // ----------------------------------------------------  store realised signals
            pr.PLiabilitySignalDiscrete = pSig;
            pr.DLiabilitySignalDiscrete = dSig;
            if (cSig.HasValue)
                pr.CLiabilitySignalDiscrete = cSig.Value;
            
            pr.BenefitCostRatio = impact.GetBenefitCostRatio(pr.LiabilityStrengthDiscrete - 1, pr.RelativePrecautionLevel);

            pr.ResetPostGameInfo();
        }

        public List<(GameProgress progress, double weight)>
            BayesianCalculations_GenerateAllConsistentGameProgresses(
                byte pSig, byte dSig, byte? cSig,
                byte pDam, byte dDam, byte? cDam,
                LitigGameProgress baseProg)
        {
            var p = (PrecautionNegligenceProgress)baseProg;
            var list = new List<(GameProgress, double)>();

            // ----------------------------------------------------  posterior over hidden state
            double[] posterior = p.AccidentOccurs switch
            {
                true when p.TrialOccurs && p.PWinsAtTrial =>
                    postAccLiable[pSig - 1][dSig - 1][p.RelativePrecautionLevel],
                true when p.TrialOccurs && !p.PWinsAtTrial =>
                    postAccNoLiable[pSig - 1][dSig - 1][p.RelativePrecautionLevel],
                true =>
                    court.GetHiddenPosteriorFromPath(pSig - 1, dSig - 1, true,
                                                     p.RelativePrecautionLevel, null),
                false when p.EngagesInActivity =>
                    postNoAccident[dSig - 1][p.RelativePrecautionLevel],
                _ =>
                    postDefSignal[dSig - 1]
            };

            // ----------------------------------------------------  plaintiff signal still unknown?
            bool needP = pSig == 0 && !p.AccidentOccurs;
            if (needP)
            {
                double[] pDist = p.EngagesInActivity
                    ? pSignalGivenD_NoAcc[dSig - 1][p.RelativePrecautionLevel]    
                    : pSignalGivenD_NoAct[dSig - 1];                             

                for (int ps = 1; ps <= pDist.Length; ps++)
                {
                    double pWeight = pDist[ps - 1];
                    if (pWeight == 0.0) continue;

                    for (int h = 0; h < posterior.Length; h++)
                    {
                        double weight = posterior[h] * pWeight;
                        if (weight == 0.0) continue;

                        var cp = (PrecautionNegligenceProgress)p.DeepCopy();
                        cp.LiabilityStrengthDiscrete = (byte)(h + 1);
                        cp.PLiabilitySignalDiscrete = (byte)ps;
                        cp.DLiabilitySignalDiscrete = dSig;
                        if (cSig.HasValue)
                            cp.CLiabilitySignalDiscrete = cSig.Value;

                        list.AddRange(DuplicateProgressWithAndWithoutWrongfulAttribution(cp, weight));
                    }
                }
                foreach (var item in list)
                {
                    PrecautionNegligenceProgress precautionNegligenceProgress = (PrecautionNegligenceProgress)item.Item1;
                    precautionNegligenceProgress.BenefitCostRatio = impact.GetBenefitCostRatio(precautionNegligenceProgress.LiabilityStrengthDiscrete - 1, precautionNegligenceProgress.RelativePrecautionLevel);
                    precautionNegligenceProgress.ResetPostGameInfo();
                }
                return list;
            }

            // ----------------------------------------------------  plaintiff signal already known
            for (int h = 0; h < posterior.Length; h++)
            {
                double weight = posterior[h];
                if (weight == 0.0) continue;

                var cp = (PrecautionNegligenceProgress)p.DeepCopy();
                cp.LiabilityStrengthDiscrete = (byte)(h + 1);
                cp.PLiabilitySignalDiscrete = pSig;
                cp.DLiabilitySignalDiscrete = dSig;
                if (cSig.HasValue)
                    cp.CLiabilitySignalDiscrete = cSig.Value;

                list.AddRange(DuplicateProgressWithAndWithoutWrongfulAttribution(cp, weight));
            }

            foreach (var item in list)
            {
                PrecautionNegligenceProgress precautionNegligenceProgress = (PrecautionNegligenceProgress)item.Item1;
                precautionNegligenceProgress.BenefitCostRatio = impact.GetBenefitCostRatio(precautionNegligenceProgress.LiabilityStrengthDiscrete - 1, precautionNegligenceProgress.RelativePrecautionLevel);
                precautionNegligenceProgress.ResetPostGameInfo();
            }

            return list;
        }



        List<(GameProgress progress, double weight)> DuplicateProgressWithAndWithoutWrongfulAttribution(
            PrecautionNegligenceProgress pr, double weight)
        {
            if (!pr.AccidentOccurs)
                return [(pr, weight)];

            int hiddenLiabilityStrengthIndex = pr.LiabilityStrengthDiscrete - 1;
            int precautionLevelIndex = pr.RelativePrecautionLevel;

            double wrongfulAttributionRatioGivenHiddenState =
                impact.GetWrongfulAttributionProbabilityGivenHiddenState(hiddenLiabilityStrengthIndex, precautionLevelIndex);

            var wrong = (PrecautionNegligenceProgress)pr.DeepCopy();
            wrong.AccidentWronglyCausallyAttributedToDefendant = true;
            wrong.ResetPostGameInfo();

            var right = (PrecautionNegligenceProgress)pr.DeepCopy();
            right.AccidentWronglyCausallyAttributedToDefendant = false;
            right.ResetPostGameInfo();

            return
            [
                (wrong, weight * wrongfulAttributionRatioGivenHiddenState),
                (right, weight * (1.0 - wrongfulAttributionRatioGivenHiddenState))
            ];
        }

    }
}

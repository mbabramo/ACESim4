using ACESimBase.Games.LitigGame.PrecautionModel;
using System;
using System.Collections.Generic;

namespace ACESim // Assuming the ACESim base namespace; adjust if needed
{
    /// <summary>
    /// Dispute generator for a negligence case with precaution investment and accident chance.
    /// Simulates the sequence from precaution choice through accident, engagement, settlement, and trial.
    /// Supports full simulation mode (explicit chance nodes for accident/trial) and collapsed chance mode (integrated probabilities).
    /// </summary>
    public class PrecautionNegligenceDisputeGenerator : MultiLitigationQualitySignalDisputeGeneratorBase, IMultiLitigationQualitySignalDisputeGenerator
    {
        public double HarmCost = 1.0; // normalized harm in the event of an accident
        public int PrecautionPowerLevels = 5;
        public int PrecautionLevels = 10;
        public double PrecautionPowerFactor = 0.8;
        public double ProbabilityAccidentNoActivity = 0.0;
        public double ProbabilityAccidentNoPrecaution = 0.0001;
        public double ProbabilityAccidentWrongfulAttribution = 0.000025;
        public double MarginalPrecautionCost = 0.00001; // DEBUG -- try to pick one where the social optimum is roughly half way with a middling precaution power.
        public double LiabilityThreshold = 1.0; // liability if benefit/cost ratio for marginal forsaken precaution > 1


        // Models for domain-specific logic
        private PrecautionImpactModel _impactModel;
        private PrecautionSignalModel _signalModel;
        private PrecautionCourtDecisionModel _courtDecisionModel;

        // Precomputed lookup tables
        private readonly double[] _accidentProbabilities;  // Probability of accident for each precaution level
        private readonly bool[] _breachByPrecaution;       // Whether each precaution level is below the legal standard (true = breach of duty)


        // Random number generator for chance events (if needed for simulation)
        private readonly Random _random;

        /// <summary>
        /// Initializes a new PrecautionNegligenceDisputeGenerator with the given models and settings.
        /// </summary>
        /// <param name="impactModel">Model mapping precaution level to accident probability (and possibly related parameters).</param>
        /// <param name="signalModel">Model for generating private precaution signals for each party.</param>
        /// <param name="courtDecisionModel">Model defining the court's liability threshold and decision logic.</param>
        /// <param name="collapseChanceDecisions">If true, use collapsed chance mode; if false, use full simulation mode.</param>
        /// <param name="enableSettlement">If true, include a settlement negotiation stage before trial.</param>
        /// <param name="random">Optional random number generator for chance events (useful for simulation runs).</param>
        public override void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            var options = LitigGameDefinition.Options;
            options.DamagesMax = options.DamagesMin = HarmCost;
            options.NumDamagesStrengthPoints = 1;
            _impactModel = new PrecautionImpactModel(PrecautionPowerLevels, PrecautionLevels, ProbabilityAccidentNoActivity, ProbabilityAccidentNoPrecaution, MarginalPrecautionCost, HarmCost, null, PrecautionPowerFactor, PrecautionPowerFactor, LiabilityThreshold, ProbabilityAccidentWrongfulAttribution, null);
            _signalModel = new PrecautionSignalModel(PrecautionPowerLevels, options.NumLiabilitySignals, options.NumLiabilitySignals, options.NumCourtLiabilitySignals, options.PLiabilityNoiseStdev, options.DLiabilityNoiseStdev, options.CourtLiabilityNoiseStdev, includeExtremes: false);
            _courtDecisionModel = new PrecautionCourtDecisionModel(_impactModel, _signalModel);
        }

        /// <summary>
        /// Defines the sequence of decisions (including chance events) for this dispute simulation.
        /// This includes the defendant's precaution choice, accident occurrence (if not collapsed), engagement, settlement (if enabled), and trial verdict.
        /// </summary>
        private void DefineDecisionSequence()
        {
            // The base class likely provides a structure (e.g., a list) to hold decision definitions.
            // We construct the decision list according to the simulation flow.
            Decisions = new List<LitigGameDecisionDefinition>();

            // 1. Defendant's precaution level decision
            Decisions.Add(new LitigGameDecisionDefinition
            {
                Name = "ChoosePrecautionLevel",
                DecisionType = DecisionType.Player,    // player decision
                PlayerIndex = LitigGamePlayers.Defendant,
                NumberOfActions = _numPrecautionLevels
            });

            if (!CollapseChanceDecisions)
            {
                // 2. Accident occurrence chance (only in full mode)
                Decisions.Add(new LitigGameDecisionDefinition
                {
                    Name = "AccidentOccurrence",
                    DecisionType = DecisionType.Chance,
                    // For chance decisions, we might specify outcome count or distribution.
                    // Here 2 outcomes: No accident or Accident.
                    NumberOfActions = 2,
                    // Provide probabilities for the two outcomes as a function of state (precaution level).
                    ChanceProbabilityFunction = (LitigGameProgress state, int outcome) =>
                    {
                        // outcome 0 = no accident, outcome 1 = accident
                        var prog = (PrecautionNegligenceProgress)state;
                        double pAccident = _accidentProbabilities[prog.PrecautionLevel];
                        return outcome == 1 ? pAccident : (1.0 - pAccident);
                    }
                });
            }

            // 3. Plaintiff's engagement decision (whether to file suit) – occurs if accident happened (or always considered in collapsed mode).
            Decisions.Add(new LitigGameDecisionDefinition
            {
                Name = "PlaintiffEngage",
                DecisionType = DecisionType.Player,
                PlayerIndex = LitigGamePlayers.Plaintiff,
                NumberOfActions = 2, // 0 = do not sue, 1 = sue
                // This decision is only relevant if an accident is present; otherwise it will be skipped in practice.
                IsActive = (LitigGameProgress state) =>
                {
                    var prog = (PrecautionNegligenceProgress)state;
                    // Active if an accident occurred (full mode) or always true in collapsed mode (since we simulate as if accident occurred).
                    return CollapseChanceDecisions || prog.AccidentOccurred;
                }
            });

            if (SettlementEnabled)
            {
                // 4. Settlement offer by plaintiff (if case is engaged)
                Decisions.Add(new LitigGameDecisionDefinition
                {
                    Name = "SettlementOffer",
                    DecisionType = DecisionType.Player,
                    PlayerIndex = LitigGamePlayers.Plaintiff,
                    NumberOfActions = _numSettlementOfferLevels,
                    IsActive = (LitigGameProgress state) =>
                    {
                        var prog = (PrecautionNegligenceProgress)state;
                        return prog.Engaged; // only active if the lawsuit was filed
                    }
                });
                // 5. Settlement acceptance decision by defendant
                Decisions.Add(new LitigGameDecisionDefinition
                {
                    Name = "SettlementResponse",
                    DecisionType = DecisionType.Player,
                    PlayerIndex = LitigGamePlayers.Defendant,
                    NumberOfActions = 2, // 0 = reject, 1 = accept
                    IsActive = (LitigGameProgress state) =>
                    {
                        var prog = (PrecautionNegligenceProgress)state;
                        return prog.Engaged; // only if case engaged (and offer made)
                    }
                });
            }

            // 6. Trial verdict (court decision) – only if case engaged and (either no settlement or settlement rejected)
            // If using full mode, we may treat this as a chance decision (court's private signal leading to liable or not liable outcome).
            // If using collapsed mode, we integrate probabilities of liability into expected values rather than explicit branching.
            if (!CollapseChanceDecisions)
            {
                Decisions.Add(new LitigGameDecisionDefinition
                {
                    Name = "CourtVerdict",
                    DecisionType = DecisionType.Chance,
                    NumberOfActions = 2, // 0 = not liable, 1 = liable
                    IsActive = (LitigGameProgress state) =>
                    {
                        var prog = (PrecautionNegligenceProgress)state;
                        // Active if the case proceeded to trial: engaged and either no settlement or settlement not accepted.
                        if (!prog.Engaged) return false;
                        if (SettlementEnabled && prog.Settled) return false;
                        return true;
                    },
                    ChanceProbabilityFunction = (LitigGameProgress state, int outcome) =>
                    {
                        // Given the actual precaution and signals, compute probability of this verdict outcome.
                        var prog = (PrecautionNegligenceProgress)state;
                        // Determine true liability condition: did defendant breach duty?
                        bool trulyLiable = _breachByPrecaution[prog.PrecautionLevel];
                        // If trulyLiable, probability court finds liable = some function of evidence (could use signal distribution).
                        // If trulyNotLiable, probability court mistakenly finds liable = some false positive rate.
                        // Here we use the courtDecisionModel to get probabilities of verdict given actual precaution.
                        double pLiable = _courtDecisionModel.GetLiabilityProbability(prog.PrecautionLevel);
                        if (outcome == 1) // liable verdict
                            return pLiable;
                        else
                            return 1.0 - pLiable;
                    }
                });
            }
            // If CollapseChanceDecisions is true, we will not include an explicit CourtVerdict chance node.
            // Instead, the expected liability outcome is incorporated into payoffs (not directly modeled here).
        }

        /// <summary>
        /// Creates a new progress state instance for this game, with additional fields for precaution scenario.
        /// </summary>
        /// <returns>A new PrecautionNegligenceProgress object.</returns>
        public override LitigGameProgress InitializeProgress()
        {
            return new PrecautionNegligenceProgress();
        }

        /// <summary>
        /// Advances the game state by applying the outcome of a decision.
        /// This method is called by the simulation engine for each decision in sequence.
        /// It updates the LitigGameProgress (state) based on the decision index and chosen action.
        /// </summary>
        /// <param name="decisionIndex">The index of the decision in the sequence (as defined in DefineDecisionSequence).</param>
        /// <param name="action">The action taken (for player decisions) or outcome realized (for chance decisions).</param>
        /// <param name="state">The current game progress state to update.</param>
        public override void ApplyDecision(int decisionIndex, int action, LitigGameProgress state)
        {
            var prog = (PrecautionNegligenceProgress)state;
            switch (Decisions[decisionIndex].Name)
            {
                case "ChoosePrecautionLevel":
                    // Defendant chooses precaution level = action
                    prog.PrecautionLevel = action;
                    break;

                case "AccidentOccurrence":
                    // Chance outcome for accident occurrence.
                    // action 0 = no accident, action 1 = accident.
                    prog.AccidentOccurred = (action == 1);
                    if (!prog.AccidentOccurred)
                    {
                        // No accident -> no case, mark game complete immediately.
                        prog.Engaged = false;
                        MarkComplete(prog);
                    }
                    break;

                case "PlaintiffEngage":
                    // Plaintiff decides whether to sue (0 = not engage, 1 = engage).
                    prog.Engaged = (action == 1);
                    if (!prog.Engaged)
                    {
                        // Plaintiff chose not to sue -> end of case (no litigation).
                        MarkComplete(prog);
                    }
                    else
                    {
                        // Lawsuit is filed. If using collapsed mode, we treat accident as having effectively occurred for simulation purposes.
                        if (CollapseChanceDecisions)
                        {
                            prog.AccidentOccurred = true;
                        }
                        // Draw the private signals for each party now, since litigation is proceeding.
                        DrawPrecautionSignals(prog);
                    }
                    break;

                case "SettlementOffer":
                    // Plaintiff makes a settlement demand. 'action' represents the index of the offer level.
                    // We don't immediately resolve anything here; just record the demand if needed.
                    prog.SettlementDemandIndex = action;
                    break;

                case "SettlementResponse":
                    // Defendant responds to settlement offer. action 1 = accept, 0 = reject.
                    bool accepted = (action == 1);
                    if (accepted)
                    {
                        // Settlement reached: mark settlement and conclude case.
                        prog.Settled = true;
                        // (We could record settlement amount from the demand index if needed for payoff calculations.)
                        MarkComplete(prog);
                    }
                    else
                    {
                        // Settlement rejected: proceed to trial. No immediate state change except not settled.
                        prog.Settled = false;
                    }
                    break;

                case "CourtVerdict":
                    // Chance outcome for court's verdict (only in full simulation mode).
                    // action 1 = liable verdict, 0 = not liable.
                    prog.VerdictLiable = (action == 1);
                    // Case ends at trial verdict.
                    MarkComplete(prog);
                    break;
            }
        }

        /// <summary>
        /// Draws private precaution signals for defendant, plaintiff, and court based on the actual precaution level.
        /// This is called once the case is engaged (accident occurred and plaintiff filed suit).
        /// The signals are stored in the progress state for use in strategies (and potentially trial outcome calculation).
        /// </summary>
        /// <param name="prog">The PrecautionNegligenceProgress state to update with signal values.</param>
        private void DrawPrecautionSignals(PrecautionNegligenceProgress prog)
        {
            int actualLevel = prog.PrecautionLevel;
            // Sample noisy signals for each party from the PrecautionSignalModel.
            prog.DefendantSignal = _signalModel.SampleDefendantSignal(actualLevel, _random);
            prog.PlaintiffSignal = _signalModel.SamplePlaintiffSignal(actualLevel, _random);
            prog.CourtSignal = _signalModel.SampleCourtSignal(actualLevel, _random);
            // (DefendantSignal might be nearly equal to actualLevel if defendant has near-perfect knowledge, depending on model settings.)
        }

        /// <summary>
        /// Determines whether the defendant is truly liable under negligence, based on ground truth.
        /// True liability requires that an accident occurred and the defendant’s actual precaution was below the standard of care.
        /// </summary>
        /// <param name="progress">The completed game progress.</param>
        /// <returns>True if the defendant was actually negligent (breached duty causing accident), otherwise false.</returns>
        public bool IsTrulyLiable(LitigGameProgress progress)
        {
            var prog = (PrecautionNegligenceProgress)progress;
            if (!prog.AccidentOccurred)
                return false;
            // Check if actual precaution was insufficient (below threshold).
            bool breach = _breachByPrecaution[prog.PrecautionLevel];
            return breach;
        }

        /// <summary>
        /// Finalizes the game progress at the end of the simulation, recording outcome fields.
        /// This is called whenever the dispute is resolved (no accident, no suit, settlement, or trial verdict).
        /// </summary>
        /// <param name="progress">The game progress to finalize.</param>
        public override void MarkComplete(LitigGameProgress progress)
        {
            var prog = (PrecautionNegligenceProgress)progress;
            // If the case ended before reaching trial, ensure VerdictLiable is false (no trial verdict given).
            if (!prog.Engaged || (SettlementEnabled && prog.Settled))
            {
                prog.VerdictLiable = false;
            }
            // In collapsed chance mode, incorporate accident probability into outcomes as needed.
            // (For example, expected payments or utilities would be scaled by accident probability, but that is handled in payoff calculations outside this method.)
            // We simply mark the progress complete here.
            prog.Completed = true;
            // Base class or engine may use prog.Completed to stop further decisions.
        }
    }

    /// <summary>
    /// Extended litigation progress state for the precaution negligence scenario.
    /// Includes fields for key events and outcomes in the case.
    /// </summary>
    public class PrecautionNegligenceProgress : LitigGameProgress
    {
        // Decision outcomes
        public bool Engaged { get; set; }              // whether the plaintiff engaged (filed the lawsuit)
        public int PrecautionLevel { get; set; }       // defendant's chosen precaution level
        public bool AccidentOccurred { get; set; }     // whether an accident occurred
        public bool VerdictLiable { get; set; }        // trial verdict: true if defendant found liable

        // Settlement details
        public bool Settled { get; set; }              // whether the case was settled (if settlement stage is enabled)
        public int SettlementDemandIndex { get; set; } // index of plaintiff's settlement offer (for reference or payoff calculation)

        // Private signals
        public double DefendantSignal { get; set; }
        public double PlaintiffSignal { get; set; }
        public double CourtSignal { get; set; }
    }
}

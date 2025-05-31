using ACESimBase.GameSolvingSupport;
using System;
using System.Linq;

namespace ACESim
{
    [Serializable]
    public class LitigGameNegligenceDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        // ── Public configurable parameters ────────────────────────────────────────
        public bool CostVariesMode = true;
        public bool RiskVariesMode => !CostVariesMode;
        public byte NumCostLevels = 5;
        public double MinCost = 0.10;
        public double MaxCost = 0.50;

        public byte NumRiskLevels = 5;
        public double MinRisk = 0.10;
        public double MaxRisk = 0.50;

        public double FixedRiskNoPrecaution = 0.30; // used in cost-varies mode
        public double FixedPrecautionCost = 0.30; // used in risk-varies mode

        public double LiabilityStrengthDecay = 0.5; // 0 < value ≤ 1. 

        public double AlternativeCauseProb = 0.05; // background risk
        public double CostOfInjury = 1.00; // damages value

        // ── Backing arrays set in Setup() ─────────────────────────────────────────
        private byte _prePrimaryCount;
        private double[] _costLevels;                 // length = _prePrimaryCount when CostVariesMode
        private double[] _riskLevels;                 // length = _prePrimaryCount when RiskVariesMode

        // LiabilityStrength[p][a][q]  (p = pre-primary index, a = 0/1 for no-prec / prec,
        //                              q = liability-strength level-1)
        private double[][][] _liabilityStrengthProbabilities;

        private bool[][] _shouldBeLiable;             // [p][a] for convenience (a = 0 only)

        // ── ILitigGameDisputeGenerator implementation ────────────────────────────
        public override string GetGeneratorName() => "Negligence";

        public override string OptionsString =>
            $"Mode={(CostVariesMode ? "CostVaries" : "RiskVaries")} " +
            $"AltCause={AlternativeCauseProb} " +
            (CostVariesMode
                ? $"Levels={NumCostLevels} MinCost={MinCost} MaxCost={MaxCost} FixedRisk={FixedRiskNoPrecaution}"
                : $"Levels={NumRiskLevels} MinRisk={MinRisk} MaxRisk={MaxRisk} FixedCost={FixedPrecautionCost}");

        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation =>
            (CostVariesMode ? "Precaution Cost Level" : "Base Risk Level",
             CostVariesMode ? "CostLvl" : "RiskLvl");

        public override (string name, string abbreviation) PrimaryNameAndAbbreviation =>
            ("Precaution Decision", "Prec");

        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation =>
            ("Injury Outcome", "Injury");

        public override string GetActionString(byte action, byte decisionByteCode) =>
            decisionByteCode switch
            {
                (byte)LitigGameDecisions.PrePrimaryActionChance => CostVariesMode
                    ? $"CostLvl{action}"
                    : $"RiskLvl{action}",
                (byte)LitigGameDecisions.PrimaryAction => action == 1
                    ? "NoPrecaution"
                    : "PrecautionTaken",
                (byte)LitigGameDecisions.PostPrimaryActionChance => action == 1
                    ? "Injury"
                    : "NoInjury",
                _ => action.ToString()
            };


        public override void Setup(LitigGameDefinition gameDef)
        {
            LitigGameDefinition = gameDef;
            gameDef.Options.DamagesMin = gameDef.Options.DamagesMax = CostOfInjury;
            gameDef.Options.NumDamagesStrengthPoints = 1;

            _prePrimaryCount = CostVariesMode ? NumCostLevels : NumRiskLevels;
            int numQuality = gameDef.Options.NumLiabilityStrengthPoints;

            // initialise arrays
            _liabilityStrengthProbabilities = new double[_prePrimaryCount][][];
            _shouldBeLiable = new bool[_prePrimaryCount][];
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                _liabilityStrengthProbabilities[p] = new double[2][]; // actions: 0=noPrec,1=prec
                _liabilityStrengthProbabilities[p][0] = new double[numQuality];
                _liabilityStrengthProbabilities[p][1] = new double[numQuality];
                _shouldBeLiable[p] = new bool[2];
            }

            // prepare cost / risk arrays
            if (CostVariesMode)
            {
                _costLevels = Enumerable.Range(0, NumCostLevels)
                                        .Select(i => MinCost + (MaxCost - MinCost) * i / (NumCostLevels - 1))
                                        .ToArray();
            }
            else
            {
                _riskLevels = Enumerable.Range(0, NumRiskLevels)
                                        .Select(i => MinRisk + (MaxRisk - MinRisk) * i / (NumRiskLevels - 1))
                                        .ToArray();
            }

            // populate liability strength & negligence matrices
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                double cost = CostVariesMode ? _costLevels[p] : FixedPrecautionCost;
                double baseRisk = CostVariesMode ? FixedRiskNoPrecaution : _riskLevels[p];

                // mapping for quality index when counts match
                byte directQuality =
                    CostVariesMode
                        ? (byte)(_prePrimaryCount - p)      // cheapest cost → highest quality
                        : (byte)(p + 1);                    // higher risk → higher quality

                for (int a = 0; a < 2; a++) // 0 = no-prec, 1 = prec
                {
                    bool tookPrecaution = (a == 1);

                    // ---------------------- Risk of injury ----------------------
                    double pHarm = tookPrecaution
                        ? AlternativeCauseProb
                        : baseRisk + AlternativeCauseProb - baseRisk * AlternativeCauseProb;

                    // ---------------------- Negligence & causation --------------
                    bool negligentInDuty = !tookPrecaution &&
                                           cost < baseRisk * CostOfInjury;

                    bool causationSatisfied = !tookPrecaution &&
                                              AlternativeCauseProb < baseRisk;

                    bool trulyLiable = negligentInDuty && causationSatisfied;

                    _shouldBeLiable[p][a] = trulyLiable;

                    // ---------------------- Litigation quality ------------------
                    byte qIndex;
                    if (LitigGameDefinition.Options.NumLiabilityStrengthPoints == _prePrimaryCount)
                    {
                        qIndex = tookPrecaution
                            ? (byte)1
                            : directQuality;
                    }
                    else
                    {
                        const double minBCR = 0.25, maxBCR = 4.0;
                        double benefitCostRatio = baseRisk * CostOfInjury / cost;
                        if (benefitCostRatio < minBCR) benefitCostRatio = minBCR;
                        if (benefitCostRatio > maxBCR) benefitCostRatio = maxBCR;
                        double scaled = (benefitCostRatio - minBCR)
                                        * (numQuality - 1.0) / (maxBCR - minBCR) + 1.0;
                        qIndex = (byte)Math.Round(scaled);
                        if (tookPrecaution) qIndex = 1;
                    }

                    for (int q = 0; q < numQuality; q++)
                        _liabilityStrengthProbabilities[p][a] = BuildGeometricDistribution(numQuality, qIndex - 1);
                }
            }
        }

        private double[] BuildGeometricDistribution(int levels, int centerIndex)
        {
            var probs = new double[levels];
            double sum = 0.0;
            for (int i = 0; i < levels; i++)
            {
                int dist = Math.Abs(i - centerIndex);
                probs[i] = Math.Pow(LiabilityStrengthDecay, dist);
                sum += probs[i];
            }
            // normalise
            for (int i = 0; i < levels; i++)
                probs[i] /= sum;
            return probs;
        }

        public override void GetActionsSetup(
            LitigGameDefinition gameDef,
            out byte prePrimaryChanceActions,
            out byte primaryActions,
            out byte postPrimaryChanceActions,
            out byte[] prePrimaryPlayersToInform,
            out byte[] primaryPlayersToInform,
            out byte[] postPrimaryPlayersToInform,
            out bool prePrimaryUnevenChance,
            out bool postPrimaryUnevenChance,
            out bool litigationQualityUnevenChance,
            out bool primaryActionCanTerminate,
            out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = _prePrimaryCount;
            primaryActions = 2; // 1 = no precaution, 2 = precaution
            postPrimaryChanceActions = 2; // 1 = injury, 2 = no injury

            prePrimaryPlayersToInform = new byte[] {
                (byte)LitigGamePlayers.Resolution,
                (byte)LitigGamePlayers.Defendant,
                (byte)LitigGamePlayers.PostPrimaryChance,
                (byte)LitigGamePlayers.LiabilityStrengthChance };

            primaryPlayersToInform = new byte[] {
                (byte)LitigGamePlayers.Resolution,
                (byte)LitigGamePlayers.PostPrimaryChance,
                (byte)LitigGamePlayers.LiabilityStrengthChance };

            postPrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution };

            prePrimaryUnevenChance = false; // uniform
            postPrimaryUnevenChance = true;  // p(injury) varies
            litigationQualityUnevenChance = true;  // mass on one level
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = true;  // no injury ends dispute
        }

        public override bool PotentialDisputeArises(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts, 
            LitigGameProgress gameProgress) =>
            acts.PostPrimaryChanceAction == 1; // injury occurred

        public override bool MarkComplete(
            LitigGameDefinition gameDef,
            byte prePrimaryAction,
            byte primaryAction,
            byte postPrimaryAction) =>
            postPrimaryAction == 2; // no injury

        public override bool MarkComplete(
            LitigGameDefinition gameDef,
            byte prePrimaryAction,
            byte primaryAction) =>
            throw new NotSupportedException();

        public override bool IsTrulyLiable(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts,
            GameProgress progress)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            bool harmOccurred = acts.PostPrimaryChanceAction == 1;
            return harmOccurred && _shouldBeLiable[pIdx][aIdx];
        }

        public override double[] GetPrePrimaryChanceProbabilities(
            LitigGameDefinition gameDef) =>
            Enumerable.Repeat(1.0 / _prePrimaryCount, _prePrimaryCount).ToArray();

        public override double[] GetPostPrimaryChanceProbabilities(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            bool tookPrecaution = acts.PrimaryAction == 2;

            double cost = CostVariesMode ? _costLevels?[pIdx] ?? FixedPrecautionCost : FixedPrecautionCost;
            double baseRisk = CostVariesMode ? FixedRiskNoPrecaution : _riskLevels[pIdx];

            double pHarm = tookPrecaution
                ? AlternativeCauseProb
                : baseRisk + AlternativeCauseProb - baseRisk * AlternativeCauseProb;

            return new[] { pHarm, 1.0 - pHarm };
        }

        public override double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            return _liabilityStrengthProbabilities[pIdx][aIdx];
        }

        public override double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts) =>
            new[] { 1.0 };

        public override double[] GetLitigationIndependentWealthEffects(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts,
            LitigGameProgress gameProgress)
        {
            bool tookPrecaution = acts.PrimaryAction == 2;
            bool injury = acts.PostPrimaryChanceAction == 1;

            double costSpent = tookPrecaution
                ? (CostVariesMode
                    ? _costLevels?[acts.PrePrimaryChanceAction - 1] ?? FixedPrecautionCost
                    : FixedPrecautionCost)
                : 0.0;

            double plaintiffLoss = injury ? -CostOfInjury : 0.0;
            double defendantLoss = -costSpent;

            return new[] { plaintiffLoss, defendantLoss };
        }

        public override double GetLitigationIndependentSocialWelfare(
            LitigGameDefinition gameDef,
            LitigGameStandardDisputeGeneratorActions acts, 
            LitigGameProgress gameProgress)
        {
            double[] w = GetLitigationIndependentWealthEffects(gameDef, acts, gameProgress);
            return w.Sum();
        }

        public override (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            // Precaution outlay only if the defendant actually took care (primaryAction == 2)
            double precaution = 0.0;
            if (acts.PrimaryAction == 2)
            {
                precaution = CostVariesMode
                    ? _costLevels?[acts.PrePrimaryChanceAction - 1] ?? FixedPrecautionCost
                    : FixedPrecautionCost;
            }

            // Injury cost only when an injury event was realised (postPrimaryChanceAction == 1)
            double injury = acts.PostPrimaryChanceAction == 1 ? CostOfInjury : 0.0;

            return (precaution, injury);
        }


        public override bool PostPrimaryDoesNotAffectStrategy() => false;

    }
}





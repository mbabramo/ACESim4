using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    [Serializable]
    public class LitigGameExcuseDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        // ── Public configurable parameters ────────────────────────────────────────
        public int NumCostLevels = 5;
        public double GeometricRatio = 2.00;
        public double MinimumCost = 0.10;
        public double BenefitToPromisee = 0.50;
        public double ImpracticabilityMultiple = 3.0;
        public double LiabilityStrengthDecay = 0.40;     // 0 < value ≤ 1
        public bool UseGeometricProbabilities = true;

        // ── Backing arrays set in Setup() ─────────────────────────────────────────
        private int _prePrimaryCount;
        private double[] _costLevels;
        private double[][][] _liabilityStrengthProbabilities;
        private bool[][] _shouldBeLiable;

        // ── ILitigGameDisputeGenerator implementation ────────────────────────────
        public override string GetGeneratorName() => "Excuse";

        public override string OptionsString =>
            $"Levels={NumCostLevels} Ratio={GeometricRatio} MinCost={MinimumCost} " +
            $"Benefit={BenefitToPromisee} ImpractMult={ImpracticabilityMultiple} " +
            $"Decay={LiabilityStrengthDecay} ProbDist={(UseGeometricProbabilities ? "Geometric" : "Uniform")}";

        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation =>
            ("Cost Level", "CostLvl");

        public override (string name, string abbreviation) PrimaryNameAndAbbreviation =>
            ("Performance Decision", "Perform");

        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation =>
            ("Outcome", "Outcome");

        public override string GetActionString(byte action, byte decisionByteCode) =>
            decisionByteCode switch
            {
                (byte)LitigGameDecisions.PrePrimaryActionChance => $"CostLvl{action}",
                (byte)LitigGameDecisions.PrimaryAction => action == 1 ? "Breach" : "Perform",
                _ => action.ToString()
            };


        public override void Setup(LitigGameDefinition gameDef)
        {
            LitigGameDefinition = gameDef;
            gameDef.Options.DamagesMin = gameDef.Options.DamagesMax = BenefitToPromisee;
            gameDef.Options.NumDamagesStrengthPoints = 1;

            _prePrimaryCount = NumCostLevels;
            int numQuality = gameDef.Options.NumLiabilityStrengthPoints;

            // initialize arrays
            _costLevels = new double[_prePrimaryCount];
            _liabilityStrengthProbabilities = new double[_prePrimaryCount][][];
            _shouldBeLiable = new bool[_prePrimaryCount][];
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                _liabilityStrengthProbabilities[p] = new double[2][];
                _liabilityStrengthProbabilities[p][0] = new double[numQuality];
                _liabilityStrengthProbabilities[p][1] = new double[numQuality];
                _shouldBeLiable[p] = new bool[2];
            }

            // construct cost ladder
            _costLevels[0] = MinimumCost;
            for (int i = 1; i < _prePrimaryCount; i++)
            {
                _costLevels[i] = GeometricRatio * _costLevels[i - 1];
            }

            // populate liability strength distributions and liability matrix
            double threshold = ImpracticabilityMultiple * BenefitToPromisee;
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                double cost = _costLevels[p];
                for (int a = 0; a < 2; a++)
                {
                    bool breach = (a == 0);  // primary action 1 = Breach, 2 = Perform
                    bool trulyLiable = breach && cost <= threshold;
                    _shouldBeLiable[p][a] = trulyLiable;
                    // determine center index for liability strength distribution
                    const double minFactor = 0.25, maxFactor = 4.0;
                    double costBenefitRatio = BenefitToPromisee > 0 ? cost / BenefitToPromisee : maxFactor;
                    if (costBenefitRatio < minFactor) costBenefitRatio = minFactor;
                    if (costBenefitRatio > maxFactor) costBenefitRatio = maxFactor;
                    double scaled = (costBenefitRatio - minFactor) * (numQuality - 1.0) / (maxFactor - minFactor) + 1.0;
                    byte qIndex = (byte)Math.Round(scaled);
                    if (qIndex < 1) qIndex = 1;
                    if (qIndex > Math.Min(numQuality, 5)) qIndex = (byte)Math.Min(numQuality, 5);
                    // build geometric distribution centered at qIndex - 1
                    _liabilityStrengthProbabilities[p][a] = BuildGeometricDistribution(numQuality, qIndex - 1);
                }
            }
        }

        private double[] BuildGeometricDistribution(int levels, int centerIndex)
        {
            // TODO: factor out geometric distribution generation (shared logic in generators)
            var probs = new double[levels];
            double sum = 0.0;
            for (int i = 0; i < levels; i++)
            {
                int dist = Math.Abs(i - centerIndex);
                probs[i] = Math.Pow(LiabilityStrengthDecay, dist);
                sum += probs[i];
            }
            // normalize
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
            prePrimaryChanceActions = (byte)_prePrimaryCount;
            primaryActions = 2;
            postPrimaryChanceActions = 0;

            prePrimaryPlayersToInform = new byte[] {
                (byte)LitigGamePlayers.Defendant,
                (byte)LitigGamePlayers.LiabilityStrengthChance
            };
            primaryPlayersToInform = new byte[] {
                (byte)LitigGamePlayers.Resolution,
                (byte)LitigGamePlayers.LiabilityStrengthChance
            };
            postPrimaryPlayersToInform = null;

            prePrimaryUnevenChance = UseGeometricProbabilities;
            postPrimaryUnevenChance = true;   // irrelevant (no post chance stage)
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = true;
            postPrimaryChanceCanTerminate = false;
        }

        public override bool PotentialDisputeArises(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress) =>
            acts.PrimaryAction == 1;  // dispute arises if Breach

        public override bool MarkComplete(LitigGameDefinition gameDef, byte prePrimaryAction, byte primaryAction) =>
            primaryAction == 2;  // if Perform, no dispute arises (complete)

        public override bool MarkComplete(LitigGameDefinition gameDef, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction) =>
            throw new NotSupportedException();

        public override bool IsTrulyLiable(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, GameProgress progress)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            return _shouldBeLiable[pIdx][aIdx];
        }

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition gameDef)
        {
            if (!UseGeometricProbabilities)
            {
                // uniform distribution over cost levels
                return Enumerable.Repeat(1.0 / _prePrimaryCount, _prePrimaryCount).ToArray();
            }
            // probabilities proportional to (1/GeometricRatio)^(i-1), normalized
            double[] weights = new double[_prePrimaryCount];
            double sum = 0.0;
            for (int i = 0; i < _prePrimaryCount; i++)
            {
                weights[i] = Math.Pow(1.0 / GeometricRatio, i);
                sum += weights[i];
            }
            for (int i = 0; i < _prePrimaryCount; i++)
            {
                weights[i] /= sum;
            }
            return weights;
        }

        public override double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts) =>
            throw new NotImplementedException();  // no post-primary chance stage

        public override double[] GetLiabilityStrengthProbabilities(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            return _liabilityStrengthProbabilities[pIdx][aIdx];
        }

        public override double[] GetDamagesStrengthProbabilities(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts) =>
            new[] { 1.0 };

        public override double[] GetLitigationIndependentWealthEffects(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            if (acts.PrimaryAction == 2)
            {
                // Perform: plaintiff gains benefit, defendant incurs cost
                double cost = _costLevels[acts.PrePrimaryChanceAction - 1];
                return new double[] { BenefitToPromisee, -cost };
            }
            else
            {
                // Breach: plaintiff loses benefit, defendant saves cost
                double cost = _costLevels[acts.PrePrimaryChanceAction - 1];
                return new double[] { -BenefitToPromisee, cost };
            }
        }

        public override double GetLitigationIndependentSocialWelfare(LitigGameDefinition gameDef, LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            if (acts.PrimaryAction == 2)
            {
                // Performance: benefit minus cost
                double cost = _costLevels[acts.PrePrimaryChanceAction - 1];
                return BenefitToPromisee - cost;
            }
            else
            {
                // Breach: 0 if excused, -BenefitToPromisee if wrongful
                int pIdx = acts.PrePrimaryChanceAction - 1;
                bool wrongful = _shouldBeLiable[pIdx][0];
                return wrongful ? -BenefitToPromisee : 0.0;
            }
        }

        public override (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef,
                                LitigGameStandardDisputeGeneratorActions acts, LitigGameProgress gameProgress)
        {
            double costLevel = _costLevels[acts.PrePrimaryChanceAction - 1];

            // Performing (primaryAction == 2) is the “precaution” that costs the promisor.
            double precaution = acts.PrimaryAction == 2 ? costLevel : 0.0;

            // Breach (primaryAction == 1) inflicts lost benefit on the promisee.
            double injury = acts.PrimaryAction == 1 ? BenefitToPromisee : 0.0;

            return (precaution, injury);
        }


        public override bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}





using ACESimBase.GameSolvingSupport;
using System;
using System.Linq;

namespace ACESim
{
    [Serializable]
    public class LitigGameWasteDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        // ── Public configurable parameters ────────────────────────────────────────
        public int NumReturnLevels = 5;                  // must be ≥ 2
        public double GeometricRatio = 0.50;             // 0 < r < 1
        public double FixedCostC = 1.00;
        public double WasteThresholdMultiple = 4.0;
        public double LiabilityStrengthDecay = 0.40;     // 0 < value ≤ 1
        public bool UseGeometricProbabilities = true;

        // ── Backing arrays set in Setup() ─────────────────────────────────────────
        private int _prePrimaryCount;
        private double[] _returnLevels;                  // descending expected returns R₁...Rₙ
        private double[][][] _liabilityStrengthProbabilities;  // [p][a][q] distributions for liability strength
        private bool[][] _shouldBeLiable;                // [p][a] true liability (for a=0 approve, a=1 reject)

        // ── ILitigGameDisputeGenerator implementation ────────────────────────────
        public override string GetGeneratorName() => "Waste";

        public override string OptionsString =>
            $"Levels={NumReturnLevels} Ratio={GeometricRatio} C={FixedCostC} " +
            $"ThresholdMult={WasteThresholdMultiple} Decay={LiabilityStrengthDecay} " +
            $"ProbDist={(UseGeometricProbabilities ? "Geometric" : "Uniform")}";

        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation =>
            ("Return Level", "RetLvl");

        public override (string name, string abbreviation) PrimaryNameAndAbbreviation =>
            ("Approval Decision", "Approval");

        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation =>
            ("Outcome", "Outcome");

        public override string GetActionString(byte action, byte decisionByteCode) =>
            decisionByteCode switch
            {
                (byte)LitigGameDecisions.PrePrimaryActionChance => $"RetLvl{action}",
                (byte)LitigGameDecisions.PrimaryAction => action == 1 ? "Approve" : "Reject",
                _ => action.ToString()
            };


        public override void Setup(LitigGameDefinition gameDef)
        {
            LitigGameDefinition = gameDef;
            gameDef.Options.DamagesMin = gameDef.Options.DamagesMax = FixedCostC;
            gameDef.Options.NumDamagesStrengthPoints = 1;

            _prePrimaryCount = NumReturnLevels;
            int numQuality = gameDef.Options.NumLiabilityStrengthPoints;

            // initialize arrays
            _returnLevels = new double[_prePrimaryCount];
            _liabilityStrengthProbabilities = new double[_prePrimaryCount][][];
            _shouldBeLiable = new bool[_prePrimaryCount][];
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                _liabilityStrengthProbabilities[p] = new double[2][];
                _liabilityStrengthProbabilities[p][0] = new double[numQuality];
                _liabilityStrengthProbabilities[p][1] = new double[numQuality];
                _shouldBeLiable[p] = new bool[2];
            }

            // construct descending ladder of returns
            _returnLevels[0] = 1.0;
            for (int i = 1; i < _prePrimaryCount; i++)
            {
                _returnLevels[i] = GeometricRatio * _returnLevels[i - 1];
            }

            // populate liability strength distributions and liability matrix
            for (int p = 0; p < _prePrimaryCount; p++)
            {
                double R = _returnLevels[p];
                for (int a = 0; a < 2; a++)
                {
                    bool approved = (a == 0);  // primary action 1 (index 0) = Approve, 2 (index 1) = Reject
                    bool trulyLiable = approved && FixedCostC > WasteThresholdMultiple * R;
                    _shouldBeLiable[p][a] = trulyLiable;
                    // determine center index for liability strength distribution
                    byte qIndex;
                    if (trulyLiable)
                        qIndex = 1;
                    else
                        qIndex = (byte)Math.Min(numQuality, 5);
                    // build geometric distribution centered at qIndex-1
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

        public override bool PotentialDisputeArises(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts) =>
            acts.PrimaryAction == 1;  // dispute if Approved (something to litigate)

        public override bool MarkComplete(
            LitigGameDefinition gameDef,
            byte prePrimaryAction,
            byte primaryAction) =>
            primaryAction == 2;  // if Rejected, no dispute arises (complete)

        public override bool MarkComplete(
            LitigGameDefinition gameDef,
            byte prePrimaryAction,
            byte primaryAction,
            byte postPrimaryAction) =>
            throw new NotSupportedException();

        public override bool IsTrulyLiable(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts,
            GameProgress progress)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            return _shouldBeLiable[pIdx][aIdx];
        }

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition gameDef)
        {
            if (!UseGeometricProbabilities)
            {
                // uniform distribution over return levels
                return Enumerable.Repeat(1.0 / _prePrimaryCount, _prePrimaryCount).ToArray();
            }
            // probabilities proportional to r^(i-1), normalized
            double[] weights = new double[_prePrimaryCount];
            double sum = 0.0;
            for (int i = 0; i < _prePrimaryCount; i++)
            {
                weights[i] = Math.Pow(GeometricRatio, i);
                sum += weights[i];
            }
            for (int i = 0; i < _prePrimaryCount; i++)
            {
                weights[i] /= sum;
            }
            return weights;
        }

        public override double[] GetPostPrimaryChanceProbabilities(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts) =>
            throw new NotImplementedException();  // no post-primary chance stage

        public override double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts)
        {
            int pIdx = acts.PrePrimaryChanceAction - 1;
            int aIdx = acts.PrimaryAction - 1;
            return _liabilityStrengthProbabilities[pIdx][aIdx];
        }

        public override double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts) =>
            new[] { 1.0 };

        public override double[] GetLitigationIndependentWealthEffects(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts)
        {
            if (acts.PrimaryAction == 2)
            {
                // Reject: no cost incurred, no return gained
                return new double[] { 0.0, 0.0 };
            }
            else
            {
                // Approve: plaintiff has no direct loss, defendant gains net (R - C)
                double R = _returnLevels[acts.PrePrimaryChanceAction - 1];
                double defendantEffect = R - FixedCostC;
                return new double[] { 0.0, defendantEffect };
            }
        }

        public override double GetLitigationIndependentSocialWelfare(
            LitigGameDefinition gameDef,
            LitigGameDisputeGeneratorActions acts)
        {
            double[] w = GetLitigationIndependentWealthEffects(gameDef, acts);
            return w.Sum();
        }

        public override (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef,
                                LitigGameDisputeGeneratorActions acts)
        {
            double R = _returnLevels[acts.PrePrimaryChanceAction - 1];

            // Rejecting the proposal (primaryAction == 2) sacrifices R → precaution.
            double precaution = acts.PrimaryAction == 2 ? R : 0.0;

            // Approving (primaryAction == 1) imposes the external cost C on the plaintiff.
            double injury = acts.PrimaryAction == 1 ? FixedCostC : 0.0;

            return (precaution, injury);
        }

        public override bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}





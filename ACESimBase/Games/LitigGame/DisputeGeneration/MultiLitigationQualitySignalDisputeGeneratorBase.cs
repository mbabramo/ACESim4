using System;
using System.Collections.Generic;
using ACESimBase.GameSolvingSupport.Symmetry;

namespace ACESim
{
    /// <summary>
    /// Abstract helper that assembles Decision nodes for the early‑signal pattern.
    /// Subclasses provide the signal structure and probability methods plus any
    /// game‑specific calculations required by ILitigGameDisputeGenerator.
    /// </summary>
    public abstract class MultiLitigationQualitySignalDisputeGeneratorBase
        : IMultiLitigationQualitySignalDisputeGenerator
    {
        /* ───────────────────────── generic parts ───────────────────────── */

        public LitigGameDefinition LitigGameDefinition { get; set; }

        public LitigGameProgress CreateGameProgress(bool fullHistoryRequired) => new LitigGameProgress(fullHistoryRequired);
        public virtual void Setup(LitigGameDefinition gameDef)
        {
            LitigGameDefinition = gameDef;
            if (gameDef.Options.CollapseChanceDecisions && ProvidesBayesianCalculations)
                SetupInvertedCalculators(gameDef);
        }

        public virtual List<Decision> GenerateDisputeDecisions(LitigGameDefinition g)
        {
            var list = new List<Decision>();
            bool collapse = g.Options.CollapseChanceDecisions;

            /* 1 — optional pre‑signal injections */
            AddPreSignalDecisions(list);

            /* 2 — build the core signal nodes */
            var s = GetSignalStructure();

            if (s.IncludeHiddenLiability && !collapse)
                list.Add(CreateHiddenLiabilityDecision(s));

            bool firstIsD = s.DefendantSignalFirst;

            if (s.IncludeDefendantSignal && firstIsD)
                list.Add(CreateDefendantSignalDecision(s, first: true));

            if (s.IncludePlaintiffSignal && !firstIsD)
                list.Add(CreatePlaintiffSignalDecision(s, first: true));

            AddInterSignalDecisions(list);

            if (s.IncludeDefendantSignal && !firstIsD)
                list.Add(CreateDefendantSignalDecision(s, first: false));

            if (s.IncludePlaintiffSignal && firstIsD)
                list.Add(CreatePlaintiffSignalDecision(s, first: false));

            /* 3 — optional post‑signal injections */
            AddPostSignalDecisions(list);

            return list;
        }

        public virtual bool SupportsSymmetry() => false;

        /* ─────────────—— IMultiLitigationQualitySignalDisputeGenerator ───────────── */

        public abstract MultiSignalStructure GetSignalStructure();
        public abstract double[] GetLiabilityLevelProbabilities();
        public abstract double[] GetFirstSignalProbabilities();
        public abstract double[] GetSecondSignalProbabilities(byte firstSignalOutcome);
        public virtual double[] GetCourtSignalProbabilities(byte pSignal, byte dSignal) => throw new NotSupportedException();
        public virtual bool ProvidesBayesianCalculations => false;

        /* inversion plumbing hook */
        protected virtual void SetupInvertedCalculators(LitigGameDefinition g) { }

        /* ───────────────────────── helper builders ───────────────────────── */

        protected virtual void AddPreSignalDecisions(List<Decision> list) { }
        protected virtual void AddInterSignalDecisions(List<Decision> list) { }
        protected virtual void AddPostSignalDecisions(List<Decision> list) { }

        private Decision CreateHiddenLiabilityDecision(MultiSignalStructure s) =>
            new("Hidden Liability", "L‑hid", true,
                (byte)LitigGamePlayers.LiabilityStrengthChance,
                s.HiddenLiabilityObservers,
                (byte)s.NumLiabilityLevels,
                (byte)LitigGameDecisions.LiabilityStrength,
                unevenChanceActions: s.HiddenLiabilityUneven)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = !s.HiddenLiabilityUneven,
                SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
            };

        private Decision CreatePlaintiffSignalDecision(MultiSignalStructure s, bool first)
        {
            byte[] informed = first && LitigGameDefinition.Options.CollapseChanceDecisions
                ? new byte[] { (byte)LitigGamePlayers.Plaintiff, (byte)LitigGamePlayers.DLiabilitySignalChance }
                : s.PlaintiffSignalObservers;

            return new("Plaintiff Signal", "P‑sig", true,
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                informed,
                (byte)s.NumSignalLevels,
                (byte)LitigGameDecisions.PLiabilitySignal,
                unevenChanceActions: s.PlaintiffSignalUneven)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = !s.PlaintiffSignalUneven,
                SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
            };
        }

        private Decision CreateDefendantSignalDecision(MultiSignalStructure s, bool first)
        {
            byte[] informed = first && LitigGameDefinition.Options.CollapseChanceDecisions
                ? new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.PLiabilitySignalChance }
                : s.DefendantSignalObservers;

            return new("Defendant Signal", "D‑sig", true,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                informed,
                (byte)s.NumSignalLevels,
                (byte)LitigGameDecisions.DLiabilitySignal,
                unevenChanceActions: s.DefendantSignalUneven)
            {
                IsReversible = true,
                Unroll_Parallelize = true,
                Unroll_Parallelize_Identical = !s.DefendantSignalUneven,
                SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
            };
        }

        /* ─────────────────────—— generic abstract stubs ───────────────────── */

        public abstract bool PotentialDisputeArises(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress gameProgress);
        public abstract bool HandleUpdatingGameProgress(LitigGameProgress gameProgress, byte currentDecisionByteCode, byte action);
        public abstract bool IsTrulyLiable(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, GameProgress p);
        public abstract double[] GetLiabilityStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a);
        public abstract double[] GetDamagesStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a);
        public abstract double GetLitigationIndependentSocialWelfare(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress gameProgress);
        public abstract double[] GetLitigationIndependentWealthEffects(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress gameProgress);
        public virtual (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress gameProgress) => (0.0, 0.0);
        public abstract string GetGeneratorName();
        public virtual string OptionsString => string.Empty;
        public abstract string GetActionString(byte action, byte decisionByteCode);

        /* inverted‑calculation stubs — concrete generators override if used */
        public virtual double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pSig, byte dSig) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pSig, byte dSig) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetLiabilityStrengthProbabilities(byte pL, byte dL, byte? cL) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDamagesStrengthProbabilities(byte pD, byte dD, byte? cD) => throw new NotSupportedException();
        public virtual void
            BayesianCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed) => throw new NotSupportedException();
        public virtual List<(GameProgress progress, double weight)>
            BayesianCalculations_GenerateAllConsistentGameProgresses(byte pL, byte dL, byte? cL, byte pD, byte dD, byte? cD, LitigGameProgress baseProgress) => throw new NotSupportedException();
    }
}

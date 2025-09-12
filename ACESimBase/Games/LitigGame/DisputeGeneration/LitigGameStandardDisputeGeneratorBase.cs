using ACESimBase.GameSolvingSupport.Symmetry;
using System;
using System.Collections.Generic;

namespace ACESim
{
    /// <summary>
    /// Default implementation of the “standard” dispute-generator pattern
    /// (pre-chance → primary decision → post-chance → liability/damages strength
    ///  → P/D signals).  Sub-classes that need a different ordering may override
    /// GenerateDisputeDecisions.
    /// </summary>
    public abstract class LitigGameStandardDisputeGeneratorBase : ILitigGameStandardDisputeGenerator
    {
        public LitigGameDefinition LitigGameDefinition { get; set; }

        public LitigGameProgress CreateGameProgress(bool fullHistoryRequired) => new LitigGameProgress(fullHistoryRequired);

        #region ILitigGameDisputeGenerator – generic parts

        public virtual void Setup(LitigGameDefinition litigGameDefinition)
            => LitigGameDefinition = litigGameDefinition;

        public virtual List<Decision> GenerateDisputeDecisions(LitigGameDefinition gameDefinition)
        {
            var opt = gameDefinition.Options;

            GetActionsSetup(gameDefinition,
                out byte prePrimaryActions,
                out byte primaryActions,
                out byte postPrimaryActions,
                out byte[] preInform,
                out byte[] priInform,
                out byte[] postInform,
                out bool preUneven,
                out bool postUneven,
                out bool strengthUneven,
                out bool primaryTerminates,
                out bool postTerminates);

            List<Decision> decisions = new List<Decision>();

            if (prePrimaryActions > 0)
                decisions.Add(new Decision(PrePrimaryNameAndAbbreviation.name, PrePrimaryNameAndAbbreviation.abbreviation, true,
                    (byte)LitigGamePlayers.PrePrimaryChance, preInform, prePrimaryActions,
                    (byte)LitigGameDecisions.PrePrimaryActionChance, unevenChanceActions: preUneven)
                {
                    StoreActionInGameCacheItem = gameDefinition.GameHistoryCacheIndex_PrePrimaryChance,
                    IsReversible = true,
                    Unroll_Parallelize = GetPrePrimaryUnrollSettings().unrollParallelize,
                    Unroll_Parallelize_Identical = GetPrePrimaryUnrollSettings().unrollIdentical,
                    SymmetryMap = (GetPrePrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision)
                });

            if (primaryActions > 0)
                decisions.Add(new Decision(PrimaryNameAndAbbreviation.name, PrimaryNameAndAbbreviation.abbreviation, false,
                    (byte)LitigGamePlayers.Defendant, priInform, primaryActions,
                    (byte)LitigGameDecisions.PrimaryAction)
                {
                    StoreActionInGameCacheItem = gameDefinition.GameHistoryCacheIndex_PrimaryAction,
                    IsReversible = true,
                    CanTerminateGame = primaryTerminates,
                    Unroll_Parallelize = GetPrimaryUnrollSettings().unrollParallelize,
                    Unroll_Parallelize_Identical = GetPrimaryUnrollSettings().unrollIdentical,
                    SymmetryMap = (GetPrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.CantBeSymmetric)
                });

            if (postPrimaryActions > 0)
                decisions.Add(new Decision(PostPrimaryNameAndAbbreviation.name, PostPrimaryNameAndAbbreviation.abbreviation, true,
                    (byte)LitigGamePlayers.PostPrimaryChance, postInform, postPrimaryActions,
                    (byte)LitigGameDecisions.PostPrimaryActionChance, unevenChanceActions: postUneven)
                {
                    StoreActionInGameCacheItem = gameDefinition.GameHistoryCacheIndex_PostPrimaryChance,
                    IsReversible = true,
                    CanTerminateGame = postTerminates,
                    Unroll_Parallelize = GetPostPrimaryUnrollSettings().unrollParallelize,
                    Unroll_Parallelize_Identical = GetPostPrimaryUnrollSettings().unrollIdentical,
                    SymmetryMap = (GetPostPrimaryUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision)
                });

            var liabPlayers = new List<byte>
            {
                (byte)LitigGamePlayers.PLiabilitySignalChance,
                (byte)LitigGamePlayers.DLiabilitySignalChance,
                (byte)LitigGamePlayers.CourtLiabilityChance,
                (byte)LitigGamePlayers.Resolution
            };
            if (opt.PLiabilityNoiseStdev == 0) liabPlayers.Add((byte)LitigGamePlayers.Plaintiff);
            if (opt.DLiabilityNoiseStdev == 0) liabPlayers.Add((byte)LitigGamePlayers.Defendant);

            decisions.Add(new Decision(opt.NumDamagesStrengthPoints > 1 ? "Liability Strength" : "Case Strength", "LiabStr", true,
                (byte)LitigGamePlayers.LiabilityStrengthChance, liabPlayers.ToArray(), opt.NumLiabilityStrengthPoints,
                (byte)LitigGameDecisions.LiabilityStrength, unevenChanceActions: strengthUneven)
            {
                StoreActionInGameCacheItem = gameDefinition.GameHistoryCacheIndex_LiabilityStrength,
                IsReversible = true,
                Unroll_Parallelize = GetLiabilityStrengthUnrollSettings().unrollParallelize,
                Unroll_Parallelize_Identical = GetLiabilityStrengthUnrollSettings().unrollIdentical,
                SymmetryMap = (GetLiabilityStrengthUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision)
            });

            if (opt.NumDamagesStrengthPoints > 1)
            {
                var dmgPlayers = new List<byte>
                {
                    (byte)LitigGamePlayers.PDamagesSignalChance,
                    (byte)LitigGamePlayers.DDamagesSignalChance,
                    (byte)LitigGamePlayers.CourtDamagesChance,
                    (byte)LitigGamePlayers.Resolution
                };
                if (opt.PDamagesNoiseStdev == 0) dmgPlayers.Add((byte)LitigGamePlayers.Plaintiff);
                if (opt.DDamagesNoiseStdev == 0) dmgPlayers.Add((byte)LitigGamePlayers.Defendant);

                decisions.Add(new Decision("Damages Strength", "DamStr", true,
                    (byte)LitigGamePlayers.DamagesStrengthChance, dmgPlayers.ToArray(), opt.NumDamagesStrengthPoints,
                    (byte)LitigGameDecisions.DamagesStrength, unevenChanceActions: strengthUneven)
                {
                    StoreActionInGameCacheItem = gameDefinition.GameHistoryCacheIndex_DamagesStrength,
                    IsReversible = true,
                    Unroll_Parallelize = GetDamagesStrengthUnrollSettings().unrollParallelize,
                    Unroll_Parallelize_Identical = GetDamagesStrengthUnrollSettings().unrollIdentical,
                    SymmetryMap = (GetDamagesStrengthUnrollSettings().symmetryMapInput, SymmetryMapOutput.ChanceDecision)
                });
            }

            gameDefinition.CreateLiabilitySignalsTables();
            if (opt.NumDamagesStrengthPoints > 1)
                gameDefinition.CreateDamagesSignalsTables();

            AddSignalDecisions(gameDefinition, decisions);

            return decisions;
        }

        private void AddSignalDecisions(LitigGameDefinition gameDef, List<Decision> decisions)
        {
            var o = gameDef.Options;

            // ─── Liability signals ──────────────────────────────────────────────────────
            if (o.PLiabilityNoiseStdev != 0)
            {
                byte[] informed =
                    o.CollapseChanceDecisions
                        ? new byte[] {
                      (byte)LitigGamePlayers.Plaintiff,
                      (byte)LitigGamePlayers.DLiabilitySignalChance,
                      (byte)LitigGamePlayers.CourtLiabilityChance,
                      (byte)LitigGamePlayers.Resolution }
                        : new byte[] { (byte)LitigGamePlayers.Plaintiff };

                decisions.Add(new Decision(
                        "P Liability Signal", "PLS", true,
                        (byte)LitigGamePlayers.PLiabilitySignalChance,
                        informed, o.NumLiabilitySignals,
                        (byte)LitigGameDecisions.PLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                });
            }

            if (o.DLiabilityNoiseStdev != 0)
            {
                byte[] informed =
                    o.CollapseChanceDecisions
                        ? new byte[] {
                      (byte)LitigGamePlayers.Defendant,
                      (byte)LitigGamePlayers.CourtLiabilityChance,
                      (byte)LitigGamePlayers.Resolution }
                        : new byte[] { (byte)LitigGamePlayers.Defendant };

                decisions.Add(new Decision(
                        "D Liability Signal", "DLS", true,
                        (byte)LitigGamePlayers.DLiabilitySignalChance,
                        informed, o.NumLiabilitySignals,
                        (byte)LitigGameDecisions.DLiabilitySignal, unevenChanceActions: true)
                {
                    IsReversible = true,
                    Unroll_Parallelize = true,
                    Unroll_Parallelize_Identical = true,
                    SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                });
            }

            // ─── Damages signals ───────────────────────────────────────────────────────
            if (o.NumDamagesStrengthPoints > 1)
            {
                if (o.PDamagesNoiseStdev != 0)
                {
                    byte[] informed =
                        o.CollapseChanceDecisions
                            ? new byte[] {
                          (byte)LitigGamePlayers.Plaintiff,
                          (byte)LitigGamePlayers.DDamagesSignalChance,
                          (byte)LitigGamePlayers.CourtDamagesChance }
                            : new byte[] { (byte)LitigGamePlayers.Plaintiff };

                    decisions.Add(new Decision(
                            "P Damages Signal", "PDS", true,
                            (byte)LitigGamePlayers.PDamagesSignalChance,
                            informed, o.NumDamagesSignals,
                            (byte)LitigGameDecisions.PDamagesSignal, unevenChanceActions: true)
                    {
                        IsReversible = true,
                        Unroll_Parallelize = true,
                        Unroll_Parallelize_Identical = true,
                        SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                    });
                }

                if (o.DDamagesNoiseStdev != 0)
                {
                    byte[] informed =
                        o.CollapseChanceDecisions
                            ? new byte[] {
                          (byte)LitigGamePlayers.Defendant,
                          (byte)LitigGamePlayers.CourtDamagesChance }
                            : new byte[] { (byte)LitigGamePlayers.Defendant };

                    decisions.Add(new Decision(
                            "D Damages Signal", "DDS", true,
                            (byte)LitigGamePlayers.DDamagesSignalChance,
                            informed, o.NumDamagesSignals,
                            (byte)LitigGameDecisions.DDamagesSignal, unevenChanceActions: true)
                    {
                        IsReversible = true,
                        Unroll_Parallelize = true,
                        Unroll_Parallelize_Identical = true,
                        SymmetryMap = (SymmetryMapInput.ReverseInfo, SymmetryMapOutput.ChanceDecision)
                    });
                }
            }
        }


        public virtual bool SupportsSymmetry() => false;

        #endregion

        #region ILitigGameStandardDisputeGenerator – abstract hooks

        public abstract void GetActionsSetup(
            LitigGameDefinition gameDefinition,
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
            out bool postPrimaryChanceCanTerminate);

        public virtual (string name, string abbreviation) PrePrimaryNameAndAbbreviation
            => ("PrePrimary", "Pre");   // default text – subclasses usually override
        public virtual (string name, string abbreviation) PrimaryNameAndAbbreviation
            => ("Primary", "Prim");
        public virtual (string name, string abbreviation) PostPrimaryNameAndAbbreviation
            => ("PostPrimary", "Post");

        // ---------- model-specific calculations remain abstract ----------
        public abstract bool PotentialDisputeArises(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p);
        public abstract bool MarkComplete(LitigGameDefinition g, byte pre, byte primary);
        public abstract bool MarkComplete(LitigGameDefinition g, byte pre, byte primary, byte post);
        public bool HandleUpdatingGameProgress(LitigGameProgress gameProgress, byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)LitigGameDecisions.PrePrimaryActionChance:
                    gameProgress.DisputeGeneratorActions.PrePrimaryChanceAction = action;
                    break;
                case (byte)LitigGameDecisions.PrimaryAction:
                    gameProgress.DisputeGeneratorActions.PrimaryAction = action;
                    if (LitigGameDefinition.CheckCompleteAfterPrimaryAction && LitigGameDefinition.Options.LitigGameStandardDisputeGenerator.MarkComplete(LitigGameDefinition, gameProgress.DisputeGeneratorActions.PrePrimaryChanceAction, gameProgress.DisputeGeneratorActions.PrimaryAction))
                        gameProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.PostPrimaryActionChance:
                    gameProgress.DisputeGeneratorActions.PostPrimaryChanceAction = action;
                    if (LitigGameDefinition.CheckCompleteAfterPostPrimaryAction && LitigGameDefinition.Options.LitigGameStandardDisputeGenerator.MarkComplete(LitigGameDefinition, gameProgress.DisputeGeneratorActions.PrePrimaryChanceAction, gameProgress.DisputeGeneratorActions.PrimaryAction, gameProgress.DisputeGeneratorActions.PostPrimaryChanceAction))
                        gameProgress.GameComplete = true;
                    break;
                default:
                    return false;
            }
            return true;
        }
        public abstract bool IsTrulyLiable(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, GameProgress p);
        public abstract double[] GetLiabilityStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a);
        public abstract double[] GetDamagesStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a);
        public abstract double GetLitigationIndependentSocialWelfare(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p);
        public abstract double[] GetLitigationIndependentWealthEffects(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p);
        public virtual (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions acts, 
            LitigGameProgress gameProgress)
        => (0.0, 0.0);

        public abstract string GetGeneratorName();
        public virtual string OptionsString => string.Empty;
        public abstract string GetActionString(byte action, byte decisionByteCode);

        // Inverted-calculation methods – must be overridden by those generators that use them
        public virtual double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pSignal, byte dSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pSignal, byte dSignal) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetLiabilityStrengthProbabilities(byte pL, byte dL, byte? cL) => throw new NotSupportedException();
        public virtual double[] BayesianCalculations_GetDamagesStrengthProbabilities(byte pD, byte dD, byte? cD) => throw new NotSupportedException();
        public virtual void
            BayesianCalculations_WorkBackwardsFromSignals(LitigGameProgress gameProgress, byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed) => throw new NotSupportedException();
        public virtual List<(GameProgress progress, double weight)>
            BayesianCalculations_GenerateAllConsistentGameProgresses(byte pL, byte dL, byte? cL, byte pD, byte dD, byte? cD, LitigGameProgress baseProgress) => throw new NotSupportedException();

        // Chance-ordering helper defaults
        public virtual (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPrePrimaryUnrollSettings()
                => (false, false, SymmetryMapInput.SameInfo);

        public virtual (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPrimaryUnrollSettings()
                => (false, false, SymmetryMapInput.SameInfo);

        public virtual (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPostPrimaryUnrollSettings()
                => (false, false, SymmetryMapInput.SameInfo);

        public virtual (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetLiabilityStrengthUnrollSettings()
                => (false, false, SymmetryMapInput.SameInfo);

        public virtual (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetDamagesStrengthUnrollSettings()
                => (false, false, SymmetryMapInput.SameInfo);


        public abstract double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition g);
        public abstract double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a);
        public abstract bool PostPrimaryDoesNotAffectStrategy();

        #endregion
    }
}


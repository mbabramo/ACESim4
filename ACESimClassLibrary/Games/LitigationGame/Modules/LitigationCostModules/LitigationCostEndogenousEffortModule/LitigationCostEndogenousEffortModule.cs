using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "LitigationCostEndogenousEffortModule")]
    [Serializable]
    public class LitigationCostEndogenousEffortModule : LitigationCostStandardModule
    {
        // we can't just use an enum because we may skip decisions
        [Serializable]
        public class LitigationCostEndogenousEffortDecisionNumbers
        { 
            public int PInvestigationBeforeBargaining = 0;
            public int DInvestigationBeforeBargaining = 1;
            public int PTrialPrep = 2;
            public int DTrialPrep = 3;

            public void AdjustDecisionNumbers(LitigationCostEndogenousEffortModuleSettings moduleSettings)
            {
                if (moduleSettings.OptimizeTrialPrep && moduleSettings.OptimizeInvestigationIntensity)
                    ;
                else if (moduleSettings.OptimizeTrialPrep && !moduleSettings.OptimizeInvestigationIntensity)
                {
                    PInvestigationBeforeBargaining = -1;
                    DInvestigationBeforeBargaining = -1;
                    PTrialPrep = 0;
                    DTrialPrep = 1;
                }
                else if (!moduleSettings.OptimizeTrialPrep && moduleSettings.OptimizeInvestigationIntensity)
                {
                    PInvestigationBeforeBargaining = 0;
                    DInvestigationBeforeBargaining = 1;
                    PTrialPrep = -1;
                    DTrialPrep = -1;
                }
                else if (!moduleSettings.OptimizeTrialPrep && !moduleSettings.OptimizeInvestigationIntensity)
                {
                    PInvestigationBeforeBargaining = -1;
                    DInvestigationBeforeBargaining = -1;
                    PTrialPrep = -1;
                    DTrialPrep = -1;
                }
            }
        }

        public LitigationCostEndogenousEffortDecisionNumbers LitigationCostEndogenousEffortDecisions; 

        public LitigationCostEndogenousEffortModuleProgress LCEEProgress { get { return (LitigationCostEndogenousEffortModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public LitigationCostEndogenousEffortInputs LitigationCostEndogenousEffortInputs { get { return (LitigationCostEndogenousEffortInputs)GameModuleInputs; } set { GameModuleInputs = value; } }

        public LitigationCostEndogenousEffortModuleSettings LitigationCostEndogenousEffortModuleSettings { get { return (LitigationCostEndogenousEffortModuleSettings)GameModuleSettings; } }

        public override void ExecuteModule()
        {
            if (LitigationCostEndogenousEffortDecisions == null)
            {
                LitigationCostEndogenousEffortDecisions = new LitigationCostEndogenousEffortDecisionNumbers();
                LitigationCostEndogenousEffortDecisions.AdjustDecisionNumbers(LitigationCostEndogenousEffortModuleSettings);
            }
            if (LitigationGame.DisputeContinues())
            {
                if (Game.CurrentActionPointName == "LitigationCostEndogenousEffortRegisterDispute")
                {
                    RegisterDisputeExists((LitigationCostInputs)GameModuleInputs);
                    LCEEProgress.PInvestigationLevel = LCEEProgress.DInvestigationLevel = LitigationCostEndogenousEffortInputs.AssumedInvestigationIntensityOfOtherParty; // this is per-round investigation costs
                    LCEEProgress.PTrialPrep = LCEEProgress.DTrialPrep = LitigationCostEndogenousEffortInputs.AssumedTrialIntensityOfOtherParty;
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PInvestigationBeforeBargaining && Game.PreparationPhase)
                {
                    if (LitigationCostEndogenousEffortModuleSettings.ExploreInvestigationEquilibria && !LitigationCostEndogenousEffortModuleSettings.UseSimpleEquilibria)
                        SpecifyInputs(new List<double>() { }); // we currently can't explore equilibria with any inputs to decisions
                    else
                        SpecifyInputs(new List<double>() { (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePResult }); 
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PInvestigationBeforeBargaining && !Game.PreparationPhase)
                {
                    if (LitigationGame.CurrentlyEvolvingDecisionIndex == LitigationGame.CurrentDecisionIndex + 1 && !LitigationCostEndogenousEffortModuleSettings.ExploreInvestigationEquilibria) // if currently evolving the defendant's decision and we're not exploring equilibria, set the plaintiff's intensity level to an assumed value, so that defendant is making its decision based on this value (thus producing a static Cournot-Nash equilibrium)
                        LCEEProgress.PInvestigationLevel = LitigationCostEndogenousEffortInputs.AssumedInvestigationIntensityOfOtherParty;
                    else
                        LCEEProgress.PInvestigationLevel = Calculate();
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DInvestigationBeforeBargaining && Game.PreparationPhase)
                {
                    if (LitigationCostEndogenousEffortModuleSettings.ExploreInvestigationEquilibria && !LitigationCostEndogenousEffortModuleSettings.UseSimpleEquilibria)
                        SpecifyInputs(new List<double>() { });
                    else
                        SpecifyInputs(new List<double>() { (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimateDResult });
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DInvestigationBeforeBargaining && !Game.PreparationPhase)
                {
                    if (LitigationGame.CurrentlyEvolvingDecisionIndex == LitigationGame.CurrentDecisionIndex - 1 && !LitigationCostEndogenousEffortModuleSettings.ExploreInvestigationEquilibria) // if currently evolving the plaintiff's decision but not exploring equilibria
                        LCEEProgress.DInvestigationLevel = LitigationCostEndogenousEffortInputs.AssumedInvestigationIntensityOfOtherParty;
                    else
                        LCEEProgress.DInvestigationLevel = Calculate();
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PTrialPrep && Game.PreparationPhase)
                {
                    if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == false)
                    {
                        if (LitigationCostEndogenousEffortModuleSettings.ExploreTrialEquilibria && !LitigationCostEndogenousEffortModuleSettings.UseSimpleEquilibria)
                            SpecifyInputs(new List<double> { });
                        else
                            SpecifyInputs(new List<double> { (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePResult });
                    }
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PTrialPrep && !Game.PreparationPhase)
                {
                    if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == false)
                    {
                        if (LitigationGame.CurrentlyEvolvingModule == this && LitigationGame.CurrentlyEvolvingDecisionIndex == LitigationGame.CurrentDecisionIndex + 1 && !LitigationCostEndogenousEffortModuleSettings.ExploreTrialEquilibria)
                            LCEEProgress.PTrialPrep = LitigationCostEndogenousEffortInputs.AssumedTrialIntensityOfOtherParty;
                        else
                            LCEEProgress.PTrialPrep = Calculate();
                    }
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DTrialPrep && Game.PreparationPhase)
                {
                    if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == false)
                    {
                        if (LitigationCostEndogenousEffortModuleSettings.ExploreTrialEquilibria && !LitigationCostEndogenousEffortModuleSettings.UseSimpleEquilibria)
                            SpecifyInputs(new List<double> { });
                        else
                            SpecifyInputs(new List<double> { (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimateDResult });
                    }
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DTrialPrep && !Game.PreparationPhase)
                {
                    if (LitigationGame.BargainingModule.BargainingProgress.SettlementExists == false)
                    {
                        if (LitigationGame.CurrentlyEvolvingModule == this && LitigationGame.CurrentlyEvolvingDecisionIndex == LitigationGame.CurrentDecisionIndex - 1 && !LitigationCostEndogenousEffortModuleSettings.ExploreTrialEquilibria)
                            LCEEProgress.DTrialPrep = LitigationCostEndogenousEffortInputs.AssumedTrialIntensityOfOtherParty;
                        else
                            LCEEProgress.DTrialPrep = Calculate();
                        DetermineEffectOfIntensityDifferential();
                    }
                }
            }
        }

        private void DetermineEffectOfIntensityDifferential()
        {
            if (LitigationCostEndogenousEffortInputs.RelativeIntensityAffectsTrialOutcomes && LCEEProgress.PInvestigationLevel + LCEEProgress.PTrialPrep != LCEEProgress.DInvestigationLevel + LCEEProgress.DTrialPrep)
            {
                if (LitigationCostEndogenousEffortInputs.IntensityContagion != 0)
                {
                    double pTrialPrepTemp = LCEEProgress.PTrialPrep * (1.0 - LitigationCostEndogenousEffortInputs.IntensityContagion) + LCEEProgress.DTrialPrep * LitigationCostEndogenousEffortInputs.IntensityContagion;
                    double dTrialPrepTemp = LCEEProgress.DTrialPrep * (1.0 - LitigationCostEndogenousEffortInputs.IntensityContagion) + LCEEProgress.PTrialPrep * LitigationCostEndogenousEffortInputs.IntensityContagion;
                    LCEEProgress.PTrialPrep = pTrialPrepTemp;
                    LCEEProgress.DTrialPrep = dTrialPrepTemp;
                }
                double pToDRatio = ((LCEEProgress.PInvestigationExpenses + LCEEProgress.PTrialPrep + LitigationCostEndogenousEffortInputs.CommonTrialExpenses) / (LCEEProgress.DInvestigationExpenses + LCEEProgress.DTrialPrep + LitigationCostEndogenousEffortInputs.CommonTrialExpenses));
                bool pIsMoreIntense = pToDRatio > 1;
                double intensityRatio = pIsMoreIntense ? pToDRatio : (1.0 / pToDRatio);
                double shift = HyperbolicTangentCurve.GetYValue(
                    1.0, // when intensity level is equal
                    0.0, // there is no shift
                    LitigationCostEndogenousEffortInputs.IntensityRatioAsymptoteForProbabilityShift, // size of shift if there were an infinite differential
                    2.0, // if there were double...
                    LitigationCostEndogenousEffortInputs.DoubleIntensityEffectOnProbabilityShift,
                    intensityRatio);
                if (!pIsMoreIntense)
                    shift = 0 - shift;
                LitigationGame.TrialModule.TrialProgress.ShiftBasedOnEffort = shift;
            }
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup = (int)Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            if (decisionNumberWithinActionGroup == LitigationCostEndogenousEffortDecisions.PInvestigationBeforeBargaining)
                return LCEEProgress.PInvestigationLevel;
            if (decisionNumberWithinActionGroup == LitigationCostEndogenousEffortDecisions.DInvestigationBeforeBargaining)
                return LCEEProgress.DInvestigationLevel;
            if (decisionNumberWithinActionGroup == LitigationCostEndogenousEffortDecisions.PTrialPrep)
                return LCEEProgress.PTrialPrep;
            if (decisionNumberWithinActionGroup == LitigationCostEndogenousEffortDecisions.DTrialPrep)
                return LCEEProgress.DTrialPrep; 
            throw new Exception("Unknown decision.");
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PInvestigationBeforeBargaining || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.PTrialPrep)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DInvestigationBeforeBargaining || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)LitigationCostEndogenousEffortDecisions.DTrialPrep)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void RegisterDisputeExists(LitigationCostInputs lcInputs)
        {
            LitigationCostProgress.LitigationCostInputs = lcInputs;
            LitigationCostEndogenousEffortInputs inputs = (LitigationCostEndogenousEffortInputs)lcInputs;
            LCSProgress.PAnticipatedTrialExpenses = inputs.AssumedTrialIntensityOfOtherParty;
            LCSProgress.DAnticipatedTrialExpenses = inputs.AssumedTrialIntensityOfOtherParty;
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void RegisterExtraInvestigationRound(bool isFirstInvestigationRound, bool isLastInvestigationRound, double portionOfRound = 1.0)
        {
            if (LCEEProgress.PAdditionalInvestigativeExpenses == null)
                LCEEProgress.PAdditionalInvestigativeExpenses = new List<double>();
            if (LCEEProgress.DAdditionalInvestigativeExpenses == null)
                LCEEProgress.DAdditionalInvestigativeExpenses = new List<double>();
            double pNewInvestigativeExpenses = LCEEProgress.PInvestigationLevel;
            double dNewInvestigativeExpenses = LCEEProgress.DInvestigationLevel;
            LCEEProgress.PAdditionalInvestigativeExpenses.Add(pNewInvestigativeExpenses);
            LCEEProgress.DAdditionalInvestigativeExpenses.Add(dNewInvestigativeExpenses);
            double pPreviousInvestigativeExpenses = LCSProgress.PInvestigationExpenses;
            double dPreviousInvestigativeExpenses = LCSProgress.DInvestigationExpenses;
            LCSProgress.PInvestigationExpenses += pNewInvestigativeExpenses;
            LCSProgress.DInvestigationExpenses += dNewInvestigativeExpenses;
            
            LCSProgress.NumberInvestigativeRounds++;
            LCSProgress.CalculateTotalExpensesForReporting();
        }

        public override void UpdatePartiesInformation(bool isFirstInvestigationRound)
        {
            double pNewInvestigativeExpenses = LCEEProgress.PInvestigationLevel;
            double dNewInvestigativeExpenses = LCEEProgress.DInvestigationLevel;
            double pPreviousInvestigativeExpenses = LCSProgress.PInvestigationExpenses;
            double dPreviousInvestigativeExpenses = LCSProgress.DInvestigationExpenses;
            double pMarginalUnitsOfInformation = CalculateMarginalUnitsOfInformationForMarginalCosts(pPreviousInvestigativeExpenses, pNewInvestigativeExpenses);
            double dMarginalUnitsOfInformation = CalculateMarginalUnitsOfInformationForMarginalCosts(dPreviousInvestigativeExpenses, dNewInvestigativeExpenses);
            if (LitigationCostStandardInputs.PartiesInformationImprovesOverTime)
            { // improve the parties' information
                double pAdditionalNoiseLevelDamages = LitigationGame.BaseDamagesForecastingModule.GetNoiseLevelOfSinglePieceOfInformationCorrespondingToNPiecesOfInformationAtOtherNoiseLevel(LitigationCostEndogenousEffortInputs.NoiseLevelOfPlaintiffIndependentInformation, pMarginalUnitsOfInformation);
                double dAdditionalNoiseLevelDamages = LitigationGame.BaseDamagesForecastingModule.GetNoiseLevelOfSinglePieceOfInformationCorrespondingToNPiecesOfInformationAtOtherNoiseLevel(LitigationCostEndogenousEffortInputs.NoiseLevelOfDefendantIndependentInformation, dMarginalUnitsOfInformation);
                double pAdditionalNoiseLevelProbability = LitigationGame.BaseProbabilityForecastingModule.GetNoiseLevelOfSinglePieceOfInformationCorrespondingToNPiecesOfInformationAtOtherNoiseLevel(LitigationCostEndogenousEffortInputs.NoiseLevelOfPlaintiffIndependentInformation, pMarginalUnitsOfInformation);
                double dAdditionalNoiseLevelProbability = LitigationGame.BaseProbabilityForecastingModule.GetNoiseLevelOfSinglePieceOfInformationCorrespondingToNPiecesOfInformationAtOtherNoiseLevel(LitigationCostEndogenousEffortInputs.NoiseLevelOfDefendantIndependentInformation, dMarginalUnitsOfInformation);
                const double effectivelyInfiniteNoise = 9999.0;
                if (double.IsInfinity(pAdditionalNoiseLevelDamages))
                    pAdditionalNoiseLevelDamages = effectivelyInfiniteNoise;
                if (double.IsInfinity(dAdditionalNoiseLevelDamages))
                    dAdditionalNoiseLevelDamages = effectivelyInfiniteNoise;
                if (double.IsInfinity(pAdditionalNoiseLevelProbability))
                    pAdditionalNoiseLevelProbability = effectivelyInfiniteNoise;
                if (double.IsInfinity(dAdditionalNoiseLevelProbability))
                    dAdditionalNoiseLevelProbability = effectivelyInfiniteNoise;
                if (double.IsInfinity(pAdditionalNoiseLevelDamages))
                    pAdditionalNoiseLevelDamages = effectivelyInfiniteNoise;
                if (pAdditionalNoiseLevelDamages < effectivelyInfiniteNoise || dAdditionalNoiseLevelDamages < effectivelyInfiniteNoise)
                    LitigationGame.BaseDamagesForecastingModule.UpdateCombinedForecasts(pAdditionalNoiseLevelDamages, dAdditionalNoiseLevelDamages);
                if (pAdditionalNoiseLevelProbability < effectivelyInfiniteNoise || dAdditionalNoiseLevelProbability < effectivelyInfiniteNoise)
                    LitigationGame.BaseProbabilityForecastingModule.UpdateCombinedForecasts(pAdditionalNoiseLevelProbability, dAdditionalNoiseLevelProbability);
            }
        }

        public override void RegisterTrial()
        {
            // now, the only expenses are the endogenous ones
            
            LCEEProgress.PTrialExpenses += LCEEProgress.PTrialPrep + LitigationCostEndogenousEffortInputs.CommonTrialExpenses;
            LCEEProgress.DTrialExpenses += LCEEProgress.DTrialPrep + LitigationCostEndogenousEffortInputs.CommonTrialExpenses;
            LCEEProgress.CalculateTotalExpensesForReporting();
        }

        private double CalculateTotalCostsForUnitOfInformation(double totalUnitsOfInformation)
        {
            // if f(i) is the marginal cost of information function, then we want the total cost, i.e. integral f(i) di.
            // here, f(i) = first * multiplier^(i - 1). the integral is first * (multiplier ^ (i - 1)) / ln multiplier + C.
            // but we want to evaluate this from i = 0 to i = t. That evaluates to 
            // first * (multiplier^t - 1) / (multiplier ln multiplier)
            double mult = LitigationCostEndogenousEffortInputs.MarginalCostMultiplierForSubsequentPiecesOfIndependentInformation;
            return LitigationCostEndogenousEffortInputs.MarginalCostForFirstPieceOfIndependentInformation * (Math.Pow(mult, totalUnitsOfInformation) - 1.0) / (mult * Math.Log(mult));
        }

        private double CalculateUnitsOfInformationForTotalCosts(double totalCost)
        {
            double mult = LitigationCostEndogenousEffortInputs.MarginalCostMultiplierForSubsequentPiecesOfIndependentInformation;
            // Now, we solve for totalUnitsOfInformation in the function above
            return
                Math.Log((totalCost * mult * Math.Log(mult) / LitigationCostEndogenousEffortInputs.MarginalCostForFirstPieceOfIndependentInformation) + 1)
                /
                Math.Log(mult);
        }

        private double CalculateMarginalUnitsOfInformationForMarginalCosts(double investigativeCostSoFar, double additionalCost)
        {
            double unitsOfInformationSoFar = CalculateUnitsOfInformationForTotalCosts(investigativeCostSoFar);
            double newTotalUnitsOfInformation = CalculateUnitsOfInformationForTotalCosts(investigativeCostSoFar + additionalCost);
            return newTotalUnitsOfInformation - unitsOfInformationSoFar;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            LitigationCostEndogenousEffortModule copy = new LitigationCostEndogenousEffortModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = LitigationCostEndogenousEffortModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public override object GenerateSetting(string options)
        {
            bool optimizeInvestigationIntensity = GetBoolCodeGeneratorOption(options, "OptimizeInvestigationIntensity");
            bool optimizeTrialPrep = GetBoolCodeGeneratorOption(options, "OptimizeTrialPrep");
            bool exploreInvestigationEquilibria = GetBoolCodeGeneratorOption(options, "ExploreEndogenousEffortInvestigationEquilibria");
            bool exploreTrialEquilibria = GetBoolCodeGeneratorOption(options, "ExploreEndogenousEffortTrialEquilibria");
            bool endogenousEffortEquilibriumUsesSimpleMethod = GetBoolCodeGeneratorOption(options, "EndogenousEffortEquilibriumUsesSimpleMethod");
            int endogenousEffortEquilibriumSimpleMethodRepetitions = GetIntCodeGeneratorOption(options, "EndogenousEffortEquilibriumSimpleMethodRepetitions");
            object gameModuleSettings = new LitigationCostEndogenousEffortModuleSettings() { OptimizeInvestigationIntensity = optimizeInvestigationIntensity, OptimizeTrialPrep = optimizeTrialPrep, UseSimpleEquilibria = endogenousEffortEquilibriumUsesSimpleMethod, ExploreInvestigationEquilibria = exploreInvestigationEquilibria, ExploreTrialEquilibria = exploreTrialEquilibria };

            List<Decision> decisions = new List<Decision>();
            if (optimizeInvestigationIntensity)
            {
                decisions.Add(GetInvestigationDecision("PInvestigationIntensity", "pint", true, exploreInvestigationEquilibria, endogenousEffortEquilibriumUsesSimpleMethod, endogenousEffortEquilibriumSimpleMethodRepetitions));
                decisions.Add(GetInvestigationDecision("DInvestigationIntensity", "dint", false, exploreInvestigationEquilibria, endogenousEffortEquilibriumUsesSimpleMethod, endogenousEffortEquilibriumSimpleMethodRepetitions));
            }
            if (optimizeTrialPrep)
            {
                decisions.Add(GetTrialDecision("PTrialPrep", "ptp", true, exploreTrialEquilibria, endogenousEffortEquilibriumUsesSimpleMethod, endogenousEffortEquilibriumSimpleMethodRepetitions));
                decisions.Add(GetTrialDecision("DTrialPrep", "dtp", false, exploreTrialEquilibria, endogenousEffortEquilibriumUsesSimpleMethod, endogenousEffortEquilibriumSimpleMethodRepetitions));
            }
            return new LitigationCostEndogenousEffortModule()
            {
                DecisionsCore = decisions,
                // shouldn't be needed now that we have GetActionGroupsForModule: ActionsAtBeginningOfModule = new List<string>() { "LitigationCostEndogenousEffortRegisterDispute" },
                GameModuleName = "LitigationCostModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = gameModuleSettings
            };
        }

        private static Decision GetInvestigationDecision(string name, string abbreviation, bool isPDecision, bool exploreEndogenousEffortEquilibria, bool endogenousEffortEquilibriumUsesSimpleMethod, int endogenousEffortEquilibriumSimpleMethodRepetitions)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InformationSetAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = 0.0,
                    UpperBound = 3000.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = false,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = false,
                MustBeInEquilibriumWithNextDecision = exploreEndogenousEffortEquilibria && isPDecision,
                MustBeInEquilibriumWithPreviousDecision = exploreEndogenousEffortEquilibria && !isPDecision,
                UseSimpleMethodForDeterminingEquilibrium = endogenousEffortEquilibriumUsesSimpleMethod,
                RepetitionsForSimpleMethodForDeterminingEquilibrium = endogenousEffortEquilibriumSimpleMethodRepetitions,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = true,
                TestInputs = null, // new List<double>() { 0.5, 0.07, 0.07 }, 
                TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5 }
            };
        }

        const double maxExtraTrialPrep = 2000.0;
        private static Decision GetTrialDecision(string name, string abbreviation, bool isPDecision, bool exploreEndogenousEffortEquilibria, bool endogenousEffortEquilibriumUsesSimpleMethod, int endogenousEffortEquilibriumSimpleMethodRepetitions)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InformationSetAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = 0.0,
                    UpperBound = maxExtraTrialPrep
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = false,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = false,
                MustBeInEquilibriumWithNextDecision = exploreEndogenousEffortEquilibria && isPDecision,
                MustBeInEquilibriumWithPreviousDecision = exploreEndogenousEffortEquilibria && !isPDecision,
                UseSimpleMethodForDeterminingEquilibrium = endogenousEffortEquilibriumUsesSimpleMethod,
                RepetitionsForSimpleMethodForDeterminingEquilibrium = endogenousEffortEquilibriumSimpleMethodRepetitions,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = true,
                AverageInPreviousVersion = true,
                TestInputs = null, // new List<double>() { 0.5 },
                TestOutputs = null, // new List<double>() { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150 }
            };
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndogenousEffortInvestigationIntensity") && secondActionGroup.Name.Contains("BeginningDropOrDefault"))
                return OrderingConstraint.CloseBefore;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndogenousEffortInvestigationIntensity") && secondActionGroup.Name.Contains("BeginningDropOrDefault"))
                return OrderingConstraint.CloseAfter;
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndogenousEffortTrialPrep") && secondActionGroup.Name.Contains("TrialModule"))
                return OrderingConstraint.CloseBefore;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndogenousEffortTrialPrep") && secondActionGroup.Name.Contains("ProbabilityPWins"))
                return OrderingConstraint.CloseAfter;
            return null;
        }

        public override List<ActionGroup> GetActionGroupsForModule()
        {
            LitigationCostEndogenousEffortDecisions = new LitigationCostEndogenousEffortDecisionNumbers();
            LitigationCostEndogenousEffortDecisions.AdjustDecisionNumbers(LitigationCostEndogenousEffortModuleSettings);

            ActionGroup actionGroup1 =
                new ActionGroup()
                {
                    Name = "EndogenousEffortInvestigationIntensity",
                    ModuleNumber = ModuleNumber
                };
            actionGroup1.ActionPoints = new List<ActionPoint>() { new ActionPoint() { ActionGroup = actionGroup1, Name = "LitigationCostEndogenousEffortRegisterDispute" } };
            if (LitigationCostEndogenousEffortModuleSettings.OptimizeInvestigationIntensity)
            {
                actionGroup1.ActionPoints.Add(new DecisionPoint() { DecisionNumberWithinActionGroup = 0, Name = "PInvestigationIntensity", Decision = DecisionsCore[LitigationCostEndogenousEffortDecisions.PInvestigationBeforeBargaining], ActionGroup = actionGroup1 });
                actionGroup1.ActionPoints.Add(new DecisionPoint() { DecisionNumberWithinActionGroup = 0, Name = "DInvestigationIntensity", Decision = DecisionsCore[LitigationCostEndogenousEffortDecisions.DInvestigationBeforeBargaining], ActionGroup = actionGroup1 });
            }

            if (LitigationCostEndogenousEffortModuleSettings.OptimizeTrialPrep)
            {
                ActionGroup actionGroup2 =
                    new ActionGroup()
                    {
                        Name = "EndogenousEffortTrialPrep",
                        ModuleNumber = ModuleNumber
                    };
                actionGroup2.ActionPoints = new List<ActionPoint>() { };
                actionGroup2.ActionPoints.Add(new DecisionPoint() { DecisionNumberWithinActionGroup = 0, Name = "PTrialPrep", Decision = DecisionsCore[LitigationCostEndogenousEffortDecisions.PTrialPrep], ActionGroup = actionGroup1 });
                actionGroup2.ActionPoints.Add(new DecisionPoint() { DecisionNumberWithinActionGroup = 0, Name = "DTrialPrep", Decision = DecisionsCore[LitigationCostEndogenousEffortDecisions.DTrialPrep], ActionGroup = actionGroup1 });
                return new List<ActionGroup> { actionGroup1, actionGroup2 };
            }
            else
                return new List<ActionGroup> { actionGroup1 };

        }
    }
}

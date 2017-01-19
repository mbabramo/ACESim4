using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SeparateAggressivenessOverrideModule")]
    [Serializable]
    public class SeparateAggressivenessOverrideModule : BargainingAggressivenessOverrideModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }

        public SeparateAggressivenessOverrideModuleInputs SeparateAggressivenessOverrideModuleInputs { get { return (SeparateAggressivenessOverrideModuleInputs)GameModuleInputs; } }

        public enum YesDecisions
        { // Note: We can switch order of plaintiffdecision and defendantdecision here. must also reverse in generatesetting and in MustBeInEquilibrium...
           PlaintiffDecision, DefendantDecision
        }

        public override void ExecuteModule()
        {
            if (LitigationGame.DisputeContinues())
            {
                if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.PlaintiffDecision && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(true));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.PlaintiffDecision && !Game.PreparationPhase)
                {
                    LitigationGame.LGP.PAggressivenessOverride = Calculate();
                    if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup != (int)YesDecisions.PlaintiffDecision && SeparateAggressivenessOverrideModuleInputs.MisestimateOpponentsAggressivenessWhenOptimizing) // when optimizing defendant decision, make plaintiff act more as defendant expects plaintiff to act
                        LitigationGame.LGP.PAggressivenessOverride = SeparateAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption * SeparateAggressivenessOverrideModuleInputs.AssumedAggressivenessOfOpponent + (1.0 - SeparateAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption) * LitigationGame.LGP.PAggressivenessOverride;
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.DefendantDecision && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(false));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.DefendantDecision && !Game.PreparationPhase)
                {
                    LitigationGame.LGP.DAggressivenessOverride = Calculate();
                    if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup != (int)YesDecisions.DefendantDecision && SeparateAggressivenessOverrideModuleInputs.MisestimateOpponentsAggressivenessWhenOptimizing)
                        LitigationGame.LGP.DAggressivenessOverride = SeparateAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption * SeparateAggressivenessOverrideModuleInputs.AssumedAggressivenessOfOpponent + (1.0 - SeparateAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption) * LitigationGame.LGP.DAggressivenessOverride;
                    AddLatestAggressivenessOverridesToList();
                }
            }
        }

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            bool plaintiff = decisionNumberWithinActionGroup == (int) YesDecisions.PlaintiffDecision;
            List<Tuple<string, string>> pNamesAndAbbreviations; 
            List<Tuple<string, string>> dNamesAndAbbreviations;
            LitigationGame.BargainingModule.GetInputNamesAndAbbreviationsForBargaining(out pNamesAndAbbreviations, out dNamesAndAbbreviations);
            List<Tuple<string, string>> namesAndAbbreviations = plaintiff ? pNamesAndAbbreviations : dNamesAndAbbreviations;
            if (LitigationGame.BargainingModule is UtilityRangeBargainingModule)
                namesAndAbbreviations.Add(new Tuple<string, string>("Aggressiveness contagion", "contag"));
            if (LitigationGame.BargainingInputs.ConsiderTasteForFairness)
            {
                if (plaintiff)
                {
                    namesAndAbbreviations.Add(new Tuple<string, string>("Plaintiff taste for fairness", "PTasteForFairness"));
                }
                else
                {
                    namesAndAbbreviations.Add(new Tuple<string, string>("Defendant taste for fairness", "DTasteForFairness"));
                }
            }
            if (plaintiff)
            {
                namesAndAbbreviations.Add(new Tuple<string, string>("Plaintiff taste for settlement", "PTasteForSettlement"));
                namesAndAbbreviations.Add(new Tuple<string, string>("Plaintiff regret aversion", "PRegretAversion"));
            }
            else
            {
                namesAndAbbreviations.Add(new Tuple<string, string>("Defendant taste for settlement", "DTasteForSettlement"));
                namesAndAbbreviations.Add(new Tuple<string, string>("Defendant regret aversion", "DRegretAversion"));
            }
            return namesAndAbbreviations;
        }

        public List<double> GetInputs(bool plaintiff)
        {
            List<double> pBargainingInputs, dBargainingInputs;
            LitigationGame.BargainingModule.GetInputsForBargaining(out pBargainingInputs, out dBargainingInputs);
            List<double> inputs = plaintiff ? pBargainingInputs : dBargainingInputs;
            
            if (LitigationGame.BargainingModule is UtilityRangeBargainingModule)
                inputs.Add(((UtilityRangeBargainingInputs)(LitigationGame.BargainingInputs)).AggressivenessContagion);

            if (LitigationGame.BargainingInputs.ConsiderTasteForFairness)
            {
                if (plaintiff)
                {
                    inputs.Add(LitigationGame.BargainingInputs.PTasteForFairness);
                }
                else
                {
                    inputs.Add(LitigationGame.BargainingInputs.DTasteForFairness);
                }
            }
            if (plaintiff)
            {
                inputs.Add(LitigationGame.BargainingInputs.PTasteForSettlement);
                inputs.Add(LitigationGame.BargainingInputs.PRegretAversion);
            }
            else
            {
                inputs.Add(LitigationGame.BargainingInputs.DTasteForSettlement);
                inputs.Add(LitigationGame.BargainingInputs.DRegretAversion);
            }
            var inputNames = AllStrategies[(int)Game.CurrentDecisionIndexWithinActionGroup].Decision.InputNames;
            return inputs;
        }

        private double? GetEarlierAggressivenessModuleResult()
        {
            if (LitigationGame.LGP.PAggressivenessOverride != null)
            { // The earlier aggressiveness module is used to set the default values for this aggressiveness module.
                if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.PlaintiffDecision)
                    return (double)LitigationGame.LGP.PAggressivenessOverride;
                else
                    return LitigationGame.LGP.DAggressivenessOverride ?? 0;
            }
            return null;
            //BargainingAggressivenessOverrideModule earlierAggressivenessModule = GetGameModuleThisModuleReliesOn(0) as BargainingAggressivenessOverrideModule;
            //if (earlierAggressivenessModule.BargainingAggressivenessOverrideProgress.PAggressivenessOverride != null)
            //{ // The earlier aggressiveness module is used to set the default values for this aggressiveness module.
            //    if (Game.CurrentDecisionIndexWithinActionGroup == (int)YesDecisions.PlaintiffDecision)
            //        return (double)earlierAggressivenessModule.BargainingAggressivenessOverrideProgress.PAggressivenessOverride;
            //    else
            //        return (double)earlierAggressivenessModule.BargainingAggressivenessOverrideProgress.DAggressivenessOverride;
            //}
            //return null;
        }

        public override double Calculate()
        {
            var currentlyEvolvingModule = Game.CurrentlyEvolvingModule;
            if ((int)GameModuleSettings == 2 /* i.e., this is second of two modules, with symmetric before */
                && currentlyEvolvingModule != null
                && currentlyEvolvingModule != this
                && currentlyEvolvingModule is BargainingAggressivenessOverrideModule)
            {
                var bp = LitigationGame.BargainingModule.BargainingProgress;
                if (bp.CurrentBargainingRoundNumber == bp.CurrentlyEvolvingBargainingRoundNumber && bp.CurrentBargainingSubclaimNumber == bp.CurrentlyEvolvingBargainingSubclaimNumber)
                { // this may have already evolved in a prior repetition, but an earlier aggressiveness override module within this round and subclaim is evolving, we must pass on its values.
                    double? result = GetEarlierAggressivenessModuleResult();
                    if (result != null)
                        return (double)result;
                }
            }
            return base.Calculate();
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            if ((int)GameModuleSettings == 2)
            { // this has not evolved yet, but if the earlier aggressiveness module has a result, we want to pass it on
                double? result = GetEarlierAggressivenessModuleResult();
                if (result != null)
                    return (double)result;
            }
            if (LitigationGame.LitigationGameInputs.PlayersActToMaximizeSocialWelfare)
                return 0.5; // otherwise, players will immediately focus just on goal of reducing litigation
            return 0.0;
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)YesDecisions.PlaintiffDecision)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)YesDecisions.DefendantDecision)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SeparateAggressivenessOverrideModule copy = new SeparateAggressivenessOverrideModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = BargainingAggressivenessOverrideModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            int aggressivenessModuleNumber = GetIntCodeGeneratorOption(options, "AggressModNumber");

            bool findEquilibriumComplexMethod = GetBoolCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumUseComplexMethod");
            bool findEquilibriumSimpleMethod = GetBoolCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumUseSimpleMethod");
            int equilibriumSimpleRepetitions = GetIntCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodRepetitions");
            bool equilibriumUseFastConvergence = findEquilibriumSimpleMethod && GetBoolCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodUseFastConvergence");
            bool equilibriumAbortFastConvergenceIfPreciseEnough = findEquilibriumSimpleMethod && GetBoolCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodAbortFastConvergenceIfPreciseEnough");
            double equilibriumFastConvergencePrecision = GetDoubleCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodFastConvergencePrecision");
            int equilibriumFastConvergenceIterationsOverride = GetIntCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodIterationsOverride");
            if (findEquilibriumComplexMethod && findEquilibriumSimpleMethod)
                throw new NotImplementedException("Cannot specify complex method and simple method for bargaining aggressiveness module.");

            bool exportFramesOfMovie = GetBoolCodeGeneratorOption(options, "BargainingAggressivenessFindEquilibriumSimpleMethodCreateMovie");

            List<Decision> decisions = new List<Decision>();


            // swap here if reversing order of decisions
            decisions.Add(GetDecision("PAggressivenessOverride", "po", true, findEquilibriumComplexMethod, findEquilibriumSimpleMethod, equilibriumSimpleRepetitions, equilibriumUseFastConvergence, equilibriumAbortFastConvergenceIfPreciseEnough, equilibriumFastConvergencePrecision, equilibriumUseFastConvergence ? (int?) equilibriumFastConvergenceIterationsOverride : null, exportFramesOfMovie));
            decisions.Add(GetDecision("DAggressivenessOverride", "do", false, findEquilibriumComplexMethod, findEquilibriumSimpleMethod, equilibriumSimpleRepetitions, equilibriumUseFastConvergence, equilibriumAbortFastConvergenceIfPreciseEnough, equilibriumFastConvergencePrecision, equilibriumUseFastConvergence ? (int?) equilibriumFastConvergenceIterationsOverride : null, exportFramesOfMovie));

            List<string> moduleNamesReliedOn = new List<string>();
            if (aggressivenessModuleNumber == 2)
                moduleNamesReliedOn.Add("BargainingAggressivenessModule1");

            return new SeparateAggressivenessOverrideModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "BargainingAggressivenessModule" + aggressivenessModuleNumber.ToString(),
                GameModuleNamesThisModuleReliesOn = moduleNamesReliedOn,
                GameModuleSettings = aggressivenessModuleNumber,
                WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter = true,
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

        private static Decision GetDecision(string name, string abbreviation, bool isPDecision, bool findEquilibriumComplexMethod, bool findEquilibriumSimpleMethod, int equilibriumSimpleRepetitions, bool equilibriumUseFastConvergence, bool equilibriumAbortFastConvergenceIfPreciseEnough, double equilibriumFastConvergencePrecision, long? iterationsOverride, bool exportFramesOfMovie)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = false,
                InputAbbreviations = null, // set dynamically
                InputNames = null, // set dynamically
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = -1.5, 
                    UpperBound = 2.5,
                    AllowBoundsToExpandIfNecessary = false
                },
                ConvertOneDimensionalDataToLookupTable = true,
                NumberPointsInLookupTable = 1000,
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="AggressiveReport.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = -1.5, yMax = 2.5, xAxisLabel="Party's estimate of probability it would win", yAxisLabel="Proportion of bargaining surplus to insist on", graphName="Parties' bargaining aggressiveness", seriesName= isPDecision ? "Plaintiff" : "Defendant", addRepetitionInfoToSeriesName = true, replaceSeriesOfSameName=exportFramesOfMovie ? true : false, fadeSeriesOfSameName=exportFramesOfMovie ? false : true, maxVisiblePerSeries=15, exportFramesOfMovies = exportFramesOfMovie },
                        ReportAfterEachEvolutionStep = true
                    }
                },
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = true, // isPDecision,
                UsePreviousVersionWhenOptimizingOtherDecisionInModule = isPDecision, // (NOTE: currently not relevant, because we are always optimizing using decision from previous round) when optimizing the defendant's decision, we want to use the PREVIOUS plaintiff decision, so there is no effect from being first
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                // switch the exclamation marks on isPDecision if reversing the order of decisions
                MustBeInEquilibriumWithNextDecision = (findEquilibriumComplexMethod || findEquilibriumSimpleMethod) && isPDecision, 
                MustBeInEquilibriumWithPreviousDecision = (findEquilibriumComplexMethod || findEquilibriumSimpleMethod) && !isPDecision, 
                UseSimpleMethodForDeterminingEquilibrium = findEquilibriumSimpleMethod,
                RepetitionsForSimpleMethodForDeterminingEquilibrium = equilibriumSimpleRepetitions,
                UseFastConvergenceWithSimpleEquilibrium = equilibriumUseFastConvergence,
                AbortFastConvergenceIfPreciseEnough = equilibriumAbortFastConvergenceIfPreciseEnough,
                PrecisionForFastConvergence = equilibriumFastConvergencePrecision,
                IterationsOverride = iterationsOverride,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = false,
                TestInputs = null, // new List<double>() { 0.5 },
                TestInputsList = null, // new List<List<double>>() { new List<double>() { 0.4 }, new List<double>() { 0.6 } },
                TestOutputs = new List<double> { -0.1, 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5 }
            };
        }
    }
}

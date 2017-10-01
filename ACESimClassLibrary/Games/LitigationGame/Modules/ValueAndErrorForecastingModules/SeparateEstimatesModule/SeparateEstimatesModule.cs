using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SeparateEstimatesModule")]
    [Serializable]
    public class SeparateEstimatesModule : ValueAndErrorForecastingModule, ICodeBasedSettingGenerator
    {
        public SeparateEstimatesModuleProgress SeparateEstimatesModuleProgress { get { return (SeparateEstimatesModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public SeparateEstimatesInputs SeparateEstimatesInputs { get { return (SeparateEstimatesInputs)GameModuleInputs; } } 

        public enum SeparateEstimatesDecisions
        {
            GenericForecastResult,
            GenericForecastError,
            GenericForecastOtherError,
            GenericCombinationResult,
            GenericCombinationError
        }

        public override void SetForecasts(double actualPResult)
        {
            SeparateEstimatesModuleProgress.GenericNoiseLevel = SeparateEstimatesInputs.GenericNoiseLevel;
            SeparateEstimatesModuleProgress.GenericProxy = (double)actualPResult + SeparateEstimatesInputs.GenericNoiseRealized;

            SeparateEstimatesModuleProgress.GenericNoiseLevel2 = SeparateEstimatesInputs.GenericNoiseLevel2;
            SeparateEstimatesModuleProgress.GenericProxy2 = (double)actualPResult + SeparateEstimatesInputs.GenericNoiseRealized2;

            if (double.IsNaN(actualPResult))
                throw new Exception("Overflow error.");
            SeparateEstimatesModuleProgress.ActualPIssueStrength = actualPResult;
            SeparateEstimatesModuleProgress.ActualPResultTransformed = LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(actualPResult);
            bool currentlyEvolvingCurrentlyExecutingModule = Game.CurrentlyEvolvingCurrentlyExecutingModule;
            if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastResult && Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule) // we want to skip the generic evolution when we're not evolving these decisions, since the generic calculations are only used internally
                    SpecifyInputs(new List<double>() { (double)SeparateEstimatesModuleProgress.GenericProxy, SeparateEstimatesInputs.GenericNoiseLevel });
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastResult && !Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SeparateEstimatesModuleProgress.GenericEstimateResult = Calculate();
                SeparateEstimatesModuleProgress.CalcResultDecisionNumber = (int)Game.CurrentDecisionIndex;
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastError && Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SpecifyInputs(new List<double>() { (double)SeparateEstimatesModuleProgress.GenericProxy, SeparateEstimatesInputs.GenericNoiseLevel });
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastError && !Game.PreparationPhase)
            {
                SeparateEstimatesModuleProgress.CalcErrorDecisionNumber = (int)Game.CurrentDecisionIndex;
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SeparateEstimatesModuleProgress.GenericEstimateError = Calculate();
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastOtherError && Game.PreparationPhase)
            {
                // given an estimate and an error, and knowledge of someone else's noise level (but not that person's estimate), what is an estimate of that person's error?
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SpecifyInputs(new List<double>() { (double)SeparateEstimatesModuleProgress.GenericEstimateResult, (double)SeparateEstimatesModuleProgress.GenericEstimateError, SeparateEstimatesInputs.GenericNoiseLevel2 });
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericForecastOtherError && !Game.PreparationPhase)
            {
                SeparateEstimatesModuleProgress.CalcOtherErrorDecisionNumber = (int)Game.CurrentDecisionIndex;
                if (currentlyEvolvingCurrentlyExecutingModule)
                {
                    // When scoring (and also possibly for a later decision), we will need to get an estimate of the second result based on the proxy and noise level for that second result
                    SeparateEstimatesModuleProgress.GenericEstimateResult2 = CalculateWithoutAffectingEvolution(new List<double> { (double)SeparateEstimatesModuleProgress.GenericProxy2, SeparateEstimatesInputs.GenericNoiseLevel2 }, SeparateEstimatesModuleProgress.CalcResultDecisionNumber);
                    // Now, we calculate our estimate of the error in calculating this second result based on the first result/error and the second noise level
                    SeparateEstimatesModuleProgress.GenericEstimateOtherError = Calculate();
                }
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericCombinationResult && Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                {
                    // to train the combination of two forecasts, we first need to also have an estimate of the error of the second estimate based on the proxy for the second estimate, using the same functions that we evolved above
                    SeparateEstimatesModuleProgress.GenericEstimateError2 = CalculateWithoutAffectingEvolution(new List<double> { (double)SeparateEstimatesModuleProgress.GenericProxy2, SeparateEstimatesInputs.GenericNoiseLevel2 }, SeparateEstimatesModuleProgress.CalcErrorDecisionNumber);
                    // now, we can use our original forecast and error and new forecast and error to generate another forecast -- but for now, let's just use the errors, since adding too many dimensions may decrease the accuracy of the forecast
                    SpecifyInputs(new List<double>() { (double)SeparateEstimatesModuleProgress.GenericEstimateError, (double)SeparateEstimatesModuleProgress.GenericEstimateError2 });
                }
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericCombinationResult && !Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                {
                    double weightOnOriginal = Calculate();
                    SeparateEstimatesModuleProgress.GenericCombinedEstimateResult = weightOnOriginal * (double)SeparateEstimatesModuleProgress.GenericEstimateResult + (1.0 - weightOnOriginal) * (double)SeparateEstimatesModuleProgress.GenericEstimateResult2;
                }
                SeparateEstimatesModuleProgress.CalcCombinedResultDecisionNumber = (int)Game.CurrentDecisionIndex;
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericCombinationError && Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SpecifyInputs(new List<double> { (double)SeparateEstimatesModuleProgress.GenericEstimateError, (double)SeparateEstimatesModuleProgress.GenericEstimateError2 });
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SeparateEstimatesDecisions.GenericCombinationError && !Game.PreparationPhase)
            {
                if (currentlyEvolvingCurrentlyExecutingModule)
                    SeparateEstimatesModuleProgress.GenericCombinedEstimateError = Calculate();
                SeparateEstimatesModuleProgress.CalcCombinedErrorDecisionNumber = (int)Game.CurrentDecisionIndex;
            }
        }

        public override void PostForecasting()
        {
            SeparateEstimatesModuleProgress.PNoiseLevel = SeparateEstimatesModuleProgress.CurrentEquivalentPNoiseLevel = SeparateEstimatesInputs.PNoiseLevel;
            SeparateEstimatesModuleProgress.DNoiseLevel = SeparateEstimatesModuleProgress.CurrentEquivalentDNoiseLevel = SeparateEstimatesInputs.DNoiseLevel;
            SeparateEstimatesModuleProgress.PlaintiffProxy = SeparateEstimatesModuleProgress.ActualPIssueStrength + SeparateEstimatesInputs.PNoiseRealized;
            SeparateEstimatesModuleProgress.DefendantProxy = SeparateEstimatesModuleProgress.ActualPIssueStrength + SeparateEstimatesInputs.DNoiseRealized;
            GetForecastFromProxy((double)SeparateEstimatesModuleProgress.PlaintiffProxy, (double)SeparateEstimatesModuleProgress.PNoiseLevel, PartyOfForecast.Plaintiff, out SeparateEstimatesModuleProgress.PEstimatePResult, out SeparateEstimatesModuleProgress.PEstimatePError);
            GetForecastFromProxy((double)SeparateEstimatesModuleProgress.DefendantProxy, (double)SeparateEstimatesModuleProgress.DNoiseLevel, PartyOfForecast.Defendant, out SeparateEstimatesModuleProgress.DEstimateDResult, out SeparateEstimatesModuleProgress.DEstimateDError);
            SeparateEstimatesModuleProgress.DEstimateDResult = SeparateEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - SeparateEstimatesModuleProgress.DEstimateDResult : 0.0 - SeparateEstimatesModuleProgress.DEstimateDResult;

            SeparateEstimatesModuleProgress.PEstimateDResult = SeparateEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - SeparateEstimatesModuleProgress.PEstimatePResult : 0.0 - SeparateEstimatesModuleProgress.PEstimatePResult;
            SeparateEstimatesModuleProgress.DEstimatePResult = SeparateEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - SeparateEstimatesModuleProgress.DEstimateDResult : 0.0 - SeparateEstimatesModuleProgress.DEstimateDResult;

            //SeparateEstimatesModuleProgress.PEstimateDError = GetEstimateOfOtherError((double)SeparateEstimatesModuleProgress.PEstimatePResult, (double)SeparateEstimatesModuleProgress.PEstimatePError, (double)SeparateEstimatesModuleProgress.CurrentEquivalentDNoiseLevel);
            //SeparateEstimatesModuleProgress.DEstimatePError = GetEstimateOfOtherError((double)SeparateEstimatesModuleProgress.DEstimateDResult, (double)SeparateEstimatesModuleProgress.DEstimateDError, (double)SeparateEstimatesModuleProgress.CurrentEquivalentPNoiseLevel);
        }

        public override double GetEstimateOfOtherError(double ownEstimate, double ownAbsoluteError, double otherNoiseLevel)
        {
            double estimate = CalculateWithoutAffectingEvolution(new List<double> { ownEstimate, ownAbsoluteError, otherNoiseLevel }, SeparateEstimatesModuleProgress.CalcOtherErrorDecisionNumber);
            return estimate;
        }

        public override void GetForecastFromProxy(double proxy, double noiseLevel, PartyOfForecast partyOfForecast, out double valueEstimate, out double errorEstimate)
        {
            valueEstimate = CalculateWithoutAffectingEvolution(new List<double> { proxy, noiseLevel }, SeparateEstimatesModuleProgress.CalcResultDecisionNumber);
            errorEstimate = CalculateWithoutAffectingEvolution(new List<double> { proxy, noiseLevel }, SeparateEstimatesModuleProgress.CalcErrorDecisionNumber);
        }


        public override double GetInitialProxyForWhetherPartyExpectsToWin(PartyOfForecast partyOfForecast)
        {
            if (partyOfForecast == PartyOfForecast.Plaintiff)
                return (double)SeparateEstimatesModuleProgress.PlaintiffProxy;
            else
                return (double)SeparateEstimatesModuleProgress.DefendantProxy;
        }

        public override void GetCombinedForecast(double originalForecast, double originalAbsoluteError, double originalNoiseLevel, double independentProxy, double independentNoiseLevel, PartyOfForecast partyOfForecast, out double newEstimate, out double newError, out double newNoiseLevel)
        {
            if (independentNoiseLevel > 1.0)
            { // right now, we're not building our model to incorporate very small bits of information, so effectively we ignore such information for now. if we want to add support for that, we'll need to add new decisions, because adding very high noise into the distributions for the decisions that we have causes too much error in those decisions. So, we would need a forecastResultHighNoise, forecastErrorHighNoise, combinationResultHighNoise, and combinationErrorHighNoise.
                newEstimate = originalForecast;
                newError = originalAbsoluteError;
                newNoiseLevel = originalNoiseLevel;
                return;
            }
            double independentForecast;
            double independentAbsoluteError;
            GetForecastFromProxy(independentProxy, independentNoiseLevel, partyOfForecast, out independentForecast, out independentAbsoluteError);
            if (originalAbsoluteError == 0)
            {
                newEstimate = originalForecast;
                newError = 0;
                newNoiseLevel = 0;
                return;
            }
            else if (independentNoiseLevel == 0)
            {
                newEstimate = independentForecast;
                newError = 0;
                newNoiseLevel = 0;
                return;
            }
            List<double> combinationInputs = new List<double> { originalAbsoluteError, independentAbsoluteError };
            double weightOnOriginal = CalculateWithoutAffectingEvolution(combinationInputs, SeparateEstimatesModuleProgress.CalcCombinedResultDecisionNumber);
            newEstimate = weightOnOriginal * originalForecast + (1.0 - weightOnOriginal) * independentForecast;
            newError = CalculateWithoutAffectingEvolution(combinationInputs, SeparateEstimatesModuleProgress.CalcCombinedErrorDecisionNumber);
            newNoiseLevel = Math.Sqrt(1 / (1 / (originalNoiseLevel * originalNoiseLevel) + 1 / (independentNoiseLevel * independentNoiseLevel)));
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup = (int) Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            switch (decisionNumberWithinActionGroup)
            {
                case (int) SeparateEstimatesDecisions.GenericForecastResult:
                    return ConstrainToRange.Constrain((double)SeparateEstimatesModuleProgress.GenericProxy, 0.0, 1.0);
                case (int)SeparateEstimatesDecisions.GenericForecastError:
                    return (double)SeparateEstimatesModuleProgress.GenericNoiseLevel / 3.0;
                case (int)SeparateEstimatesDecisions.GenericForecastOtherError:
                    return (double)SeparateEstimatesModuleProgress.GenericNoiseLevel2 / 3.0;
                case (int)SeparateEstimatesDecisions.GenericCombinationResult:
                case (int)SeparateEstimatesDecisions.GenericCombinationError:
                    const double ratioOfStandardDeviationToAverageAbsoluteError = 1.2533; // approximate conversion, assumes normal distribution
                    double original_sd = inputs[0] * ratioOfStandardDeviationToAverageAbsoluteError;
                    double independent_sd = inputs[1] * ratioOfStandardDeviationToAverageAbsoluteError;
                    double original_var = original_sd * original_sd;
                    double independent_var = independent_sd * independent_sd;
                    double weightOnOriginal = (1 / original_var) / (1 / original_var + 1 / independent_var);
                    double new_sd = Math.Sqrt(1 / (1 / original_var + 1 / independent_var));
                    double newError = new_sd / ratioOfStandardDeviationToAverageAbsoluteError;
                    if (decisionNumberWithinActionGroup == (int)SeparateEstimatesDecisions.GenericCombinationResult)
                        return weightOnOriginal;
                    else
                        return newError;
                default:
                    throw new Exception("Unknown decision.");
            }
        }

        public override void Score()
        {
            double valueBeingForecast = (double) SeparateEstimatesModuleProgress.ActualPIssueStrength;
            double actualValue;
            switch (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup)
            {
                case (int)SeparateEstimatesDecisions.GenericForecastResult:
                    actualValue = valueBeingForecast;
                    break;
                case (int)SeparateEstimatesDecisions.GenericCombinationResult:
                    Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, (valueBeingForecast - (double)SeparateEstimatesModuleProgress.GenericCombinedEstimateResult) * (valueBeingForecast - (double)SeparateEstimatesModuleProgress.GenericCombinedEstimateResult)); // for this decision only, we are optimizing a weight variable from 0 to 1, and the weight variable is not the actual correct value, so we need to score based on the squared error.
                    return;
                case (int)SeparateEstimatesDecisions.GenericForecastError:
                case (int)SeparateEstimatesDecisions.GenericCombinationError:
                    actualValue = Math.Abs(valueBeingForecast - (double)SeparateEstimatesModuleProgress.GenericEstimateResult);
                    break;
                case (int)SeparateEstimatesDecisions.GenericForecastOtherError:
                    actualValue = Math.Abs(valueBeingForecast - (double)SeparateEstimatesModuleProgress.GenericEstimateResult2);
                    break;
                default:
                    throw new Exception("Unknown decision.");

            }
            Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, actualValue);
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SeparateEstimatesModule copy = new SeparateEstimatesModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = SeparateEstimatesModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public override object GenerateSetting(string options)
        {
            bool includeCombinationDecisions = options.Contains("IncludeCombinationDecisions: true") || options.Contains("IncludeCombinationDecisions: TRUE");
            bool onlyOneRepetitionNeeded = options.Contains("OnlyOneRepetitionNeeded: true") || options.Contains("OnlyOneRepetitionNeeded: TRUE");

            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("ForecastResult", "fr", true, onlyOneRepetitionNeeded));
            decisions.Add(GetDecision("ForecastError", "fe", true, onlyOneRepetitionNeeded));
            decisions.Add(GetDecision("ForecastOtherError", "foe", true, onlyOneRepetitionNeeded));
            if (includeCombinationDecisions)
            {
                decisions.Add(GetDecision("ForecastCombinedResult", "fcr", false, onlyOneRepetitionNeeded));
                decisions.Add(GetDecision("ForecastCombinedError", "fce", true, onlyOneRepetitionNeeded));
            }

            return new SeparateEstimatesModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeForecasting" },
                ActionsAtEndOfModule = new List<string>() { "AfterForecasting" },
                GameModuleName = options.Contains("Probability") ? "ProbabilityForecastingModule" : "DamagesForecastingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }

        private static Decision GetDecision(string name, string abbreviation, bool scoreRepresentsCorrectAnswer, bool onlyOneRepetitionNeeded)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InformationSetAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = false,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = onlyOneRepetitionNeeded ? 1 : 99999,
                PreservePreviousVersionWhenOptimizing = false,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = scoreRepresentsCorrectAnswer,
                TestInputs = null, // new List<double>() { 0.5, 0.07, 0.07 }, 
                TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5 }
            };
        }
    }
}

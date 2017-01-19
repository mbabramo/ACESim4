using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ForecastingModule")]
    [Serializable]
    public class ForecastingModule : GameModule, ICodeBasedSettingGenerator
    {
        public ForecastingModuleProgress ForecastingProgress { get { return (ForecastingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }


        public override void ExecuteModule()
        {
            throw new NotImplementedException();
        }

        public virtual void Process(ForecastingInputs forecastingInputs, List<double> inputsToForecast)
        {
            if (Game.CurrentActionGroup.ActionPoints.Count == 0)
            { // this is a development shortcut that eliminates the decisions associated with this forecasting module. The ReturnFirstInputAsForecast option must be set to true, and then no evolution will be performed.
                ForecastingProgress.Forecast = inputsToForecast[0]; // Math.Min(Math.Max(0, inputsToForecast[0]), 1);
            }
            if (Game.CurrentDecisionIndexWithinActionGroup == 0 && Game.PreparationPhase)
            {
                SpecifyInputs(inputsToForecast);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == 0 && !Game.PreparationPhase)
            {
                ForecastingProgress.Forecast = Calculate();
            }
        }

        public virtual void SpecifyFinalOutcome(double eventualOutcome)
        {
            SpecifyFinalOutcome(eventualOutcome, true);
        }

        public virtual void SpecifyFinalOutcome(double? eventualOutcome, bool forecastConditionIsMet)
        {
            ForecastingProgress.ForecastConditionIsMet = forecastConditionIsMet;
            if (forecastConditionIsMet)
                ForecastingProgress.EventualOutcome = (double)eventualOutcome;
        }

        public override void Score()
        {
            if (ForecastingProgress != null && ForecastingProgress.ForecastConditionIsMet)
            {
                // Because ScoreRepresentsCurrentAnswer is true, we just pass the correct answer as the score.
                Game.Score((int)Game.CurrentlyEvolvingDecisionIndex, ForecastingProgress.EventualOutcome);
                // If this were like other decisions, we would pass in the squared error as follows. This would
                // work if we set ScoreRepresentsCurrentAnswer to false, but it would be less efficient.
                //double error = ForecastingProgress.Forecast - ForecastingProgress.EventualOutcome;
                //double squareError = error * error;
                //Game.Score((int)FirstDecisionNumber + 0, squareError);
            }
        }

        // subclasses must copy this and change "ForecastingModule" to the subclass name in the first line of the code.
        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ForecastingModule copy = new ForecastingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = ForecastingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        internal virtual bool SetForecastingModuleSettings(string options)
        {
            bool doNotEvolveForecast = GetBoolCodeGeneratorOption(options, "DoNotEvolveForecast");
            return doNotEvolveForecast;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            bool doNotEvolveForecast = SetForecastingModuleSettings(options);

            if (!doNotEvolveForecast)
                decisions.Add(new Decision()
                {
                    UseOversampling = true,
                    SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                    Name = GetForecastName(),
                    Abbreviation = GetForecastAbbreviation(),
                    DynamicNumberOfInputs = true,
                    ConvertOneDimensionalDataToLookupTable = GetInputAbbreviations().Count == 1,
                    NumberPointsInLookupTable = 1000,
                    InputAbbreviations = GetInputAbbreviations(),
                    InputNames = GetInputNames(),
                    StrategyBounds = GetStrategyBounds(),
                    Bipolar = false,
                    ScoreRepresentsCorrectAnswer = true,
                    StrategyGraphInfos = GetStrategyGraphInfos(),
                    HighestIsBest = false,
                    MaxEvolveRepetitions = 1,
                    PreservePreviousVersionWhenOptimizing = true,
                    AverageInPreviousVersion = true,
                    SkipThisDecisionWhenEvolvingIfAlreadyEvolved = false
                });

            return GenerateNewForecastingModule(decisions);
        }

        internal virtual List<StrategyGraphInfo> GetStrategyGraphInfos()
        {
            return new List<StrategyGraphInfo>();
        }

        internal virtual ForecastingModule GenerateNewForecastingModule(List<Decision> decisions)
        {
            return new ForecastingModule() // subclasses should override this
            {
                DecisionsCore = decisions,
                GameModuleName = "ForecastingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = GameModuleSettings
            };
        }

        internal virtual string GetForecastAbbreviation()
        {
            return "FC";
        }

        internal virtual string GetForecastName()
        {
            return "Forecast";
        }

        internal virtual StrategyBounds GetStrategyBounds()
        {
            return new StrategyBounds()
            {
                LowerBound = 0.0,
                UpperBound = 1.0
            };
        }

        internal virtual List<string> GetInputNames()
        {
            return new List<string>() {
                    "Input1"
                };
        }

        internal virtual List<string> GetInputAbbreviations()
        {
            return new List<string>() {
                    "I1"
                };
        }

    }
}

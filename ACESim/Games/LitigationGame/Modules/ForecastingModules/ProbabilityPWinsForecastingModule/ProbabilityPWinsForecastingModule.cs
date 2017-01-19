using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ProbabilityPWinsForecastingModule")]
    [Serializable]
    public class ProbabilityPWinsForecastingModule : ForecastingModule
    {
        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }

        public ProbabilityPWinsForecastingModuleSettings ProbabilityPWinsForecastingModuleSettings { get { return (ProbabilityPWinsForecastingModuleSettings)GameModuleSettings; } set { GameModuleSettings = value; } }

        public override void ExecuteModule()
        {
            if (Game.CurrentlyEvolvingModule == this)
            {
                Process(null, new List<double>() { LitigationGame.DisputeGeneratorModule.DGProgress.EvidentiaryStrengthLiability });
            }
        }

        public override void Score()
        {
            if (LitigationGame.LGP.PWins == null)
                throw new Exception("This module must evolve in a way such that all cases go to trial.");
            SpecifyFinalOutcome(LitigationGame.LGP.PWins == true ? 1.0 : 0.0);
            // Because ScoreRepresentsCurrentAnswer is true, we just pass the correct answer as the score.
            if (ForecastingProgress != null && ForecastingProgress.ForecastConditionIsMet)
                Game.Score((int)Game.CurrentlyEvolvingDecisionIndex, ForecastingProgress.EventualOutcome);
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ProbabilityPWinsForecastingModule copy = new ProbabilityPWinsForecastingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = ForecastingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }


        internal override ForecastingModule GenerateNewForecastingModule(List<Decision> decisions)
        {
            return new ProbabilityPWinsForecastingModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "ProbabilityPWinsForecastingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = GameModuleSettings
            };
        }

        public double CalculateProbabilityPWins(double evidentiaryStrengthLiability)
        {
            if (ProbabilityPWinsForecastingModuleSettings.UseBruteForceCalculationOfProbabilityPWins) // indicates we're using brute force calculation instead of evolving this
                return CalculateProbabilityPWinsBasedOnBruteForceCalculation(evidentiaryStrengthLiability);
            if (AllStrategies[(int) FirstDecisionNumberInGameModule].CurrentlyDevelopingStrategy || !AllStrategies[(int) FirstDecisionNumberInGameModule].StrategyDevelopmentInitiated)
                return ConstrainToRange.Constrain(evidentiaryStrengthLiability, 0.0, 1.0); // we haven't evolved yet, so this will be a default value
            return CalculateWithoutAffectingEvolution(new List<double>() { evidentiaryStrengthLiability }, (int)FirstDecisionNumberInGameModule);
        }

        private double CalculateProbabilityPWinsBasedOnBruteForceCalculation(double evidentiaryStrengthLiability)
        {
            return CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(evidentiaryStrengthLiability, ProbabilityPWinsForecastingModuleSettings.JudgeNoiseLevelLiability);
        }

        internal override bool SetForecastingModuleSettings(string options)
        {
            bool useBruteForceCalculationOfProbabilityPWins = GetBoolCodeGeneratorOption(options, "UseBruteForceCalculationOfProbabilityPWins");
            double judicialNoiseLevelLiability = GetDoubleCodeGeneratorOption(options, "JudgeNoiseLevelLiability");
            ProbabilityPWinsForecastingModuleSettings = new ProbabilityPWinsForecastingModuleSettings() { UseBruteForceCalculationOfProbabilityPWins = useBruteForceCalculationOfProbabilityPWins, JudgeNoiseLevelLiability = judicialNoiseLevelLiability };
            return useBruteForceCalculationOfProbabilityPWins;
        }

        public double CalculateProbabilityPWinsGivenDistributionOfEvidentiaryStrengthLiability(double[] possibleEvidentiaryStrengthLiabilityValues, double[] weights)
        {
            StatCollectorFasterButNotThreadSafe sc = new StatCollectorFasterButNotThreadSafe();
            bool traceCalculation = false;
            for (int v = 0; v < possibleEvidentiaryStrengthLiabilityValues.Length; v++)
            {
                double prob = CalculateProbabilityPWins(possibleEvidentiaryStrengthLiabilityValues[v]);
                sc.Add(prob, weights == null ? 1.0 : weights[v]);
                if (traceCalculation)
                {
                    Debug.WriteLine(v + ": " + possibleEvidentiaryStrengthLiabilityValues[v] + ", " + prob + ", " + weights[v]);
                }
            }
            if (traceCalculation)
                Debug.WriteLine(sc.Average());
            return sc.Average();
        }


        public override object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            bool doNotEvolveForecast = SetForecastingModuleSettings(options);

            if (!doNotEvolveForecast)
                decisions.Add(new Decision()
                {
                    UseOversampling = true,
                    // IterationsMultiplier = 5, 
                    SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                    Name = GetForecastName(),
                    Abbreviation = GetForecastAbbreviation(),
                    DynamicNumberOfInputs = true,
                    ConvertOneDimensionalDataToLookupTable = GetInputAbbreviations().Count == 1,
                    NumberPointsInLookupTable = 1000, /* make sure this is even -- even though this leads to strange step sizes, it ensures that we get symmetrical results */
                    InputAbbreviations = GetInputAbbreviations(),
                    InputNames = GetInputNames(),
                    ProduceClusteringPointsEvenlyFrom0To1 = GetInputAbbreviations().Count == 1,
                    StrategyBounds = GetStrategyBounds(),
                    Bipolar = false,
                    ScoreRepresentsCorrectAnswer = true,
                    StrategyGraphInfos = GetStrategyGraphInfos(),
                    HighestIsBest = false,
                    MaxEvolveRepetitions = 1,
                    PreservePreviousVersionWhenOptimizing = true,
                    AlwaysUseLatestVersion = true, /* This is why we have overriden this method */
                    AverageInPreviousVersion = true,
                    SkipThisDecisionWhenEvolvingIfAlreadyEvolved = false
                });

            return GenerateNewForecastingModule(decisions);
        }

        internal override List<StrategyGraphInfo> GetStrategyGraphInfos()
        {
            return new List<StrategyGraphInfo>()
            {
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "ESL", MinValue=0.0, MaxValue=1.0, NumValues = 150 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="ProbPWinsReport.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = 0, yMax = 1.0, xAxisLabel="Actual strength of plaintiff's case on liability", yAxisLabel="Probability plaintiff wins", graphName="Evidentiary strength and probability of victory", seriesName= "OnlySeries" },
                        ReportAfterEachEvolutionStep = true
                    }
                }
            };
        }

        internal override string GetForecastAbbreviation()
        {
            return "PPWins";
        }

        internal override string GetForecastName()
        {
            return "PWinsProbability";
        }

        internal override List<string> GetInputNames()
        {
            return new List<string>() {
                    "EvidentiaryStrengthLiability"
                };
        }

        internal override List<string> GetInputAbbreviations()
        {
            return new List<string>() {
                    "ESL"
                };
        }


        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && secondActionGroup.Name.Contains("TrialModule"))
                return OrderingConstraint.After;
            if (forEvolution && secondActionGroup.Name.Contains("TrialModule"))
                return OrderingConstraint.Before;
            return null;
        }

    }
}

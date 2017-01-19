using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SymmetricAggressivenessOverrideModule")]
    [Serializable]
    public class SymmetricAggressivenessOverrideModule : BargainingAggressivenessOverrideModule, ICodeBasedSettingGenerator
    {
        const double initialRangeOfRandomStrategiesToTest = 2.0;

        public SymmetricAggressivenessOverrideModuleSettings SymmetricAggressivenessOverrideModuleSettings { get { return (SymmetricAggressivenessOverrideModuleSettings) GameModuleSettings; } }
        public SymmetricAggressivenessOverrideModuleInputs SymmetricAggressivenessOverrideModuleInputs { get { return (SymmetricAggressivenessOverrideModuleInputs)GameModuleInputs; } }
        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }

        public override void ExecuteModule()
        {
            if (LitigationGame.DisputeContinues())
            {
                bool calculatingFinalMiniroundOverride = Game.CurrentDecisionIndexWithinActionGroup > 0; // we are calculating the override for the final miniround.

                double scaledRandomStrategy = SymmetricAggressivenessOverrideModuleInputs.RandomlyDeterminedStrategy; // currently no scaling; just assume opponent has some level of aggressiveness within range and see what our optimal strategy would be including out-of-bargaining-range values //  (1.0 - 0.5 * rangeOfRandomStrategiesToTest) + rangeOfRandomStrategiesToTest * SymmetricAggressivenessOverrideModuleInputs.RandomlyDeterminedStrategy;
                if (Game.PreparationPhase)
                {
                    SpecifyInputs(new List<double>() { scaledRandomStrategy });
                }
                else
                {
                    double pAggressivenessOverride = 0, dAggressivenessOverride = 0;
                    if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup) 
                    {
                        bool randomStrategyIsForPlaintiff = SymmetricAggressivenessOverrideModuleInputs.RandSeedToDetermineWhoStrategyIsFor > 0.5;
                        double thisPartysStrategyInResponseToRandomStrategy = Calculate(); // we're evolving this party's strategy, so it will be set to something random
                        if (randomStrategyIsForPlaintiff)
                        {
                            if (SymmetricAggressivenessOverrideModuleInputs.MisestimateOpponentsAggressivenessWhenOptimizing) // if so, then we will during evolution make the plaintiff act more as we assume the plaintiff will act
                                pAggressivenessOverride = (1.0 - SymmetricAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption) * scaledRandomStrategy + SymmetricAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption * SymmetricAggressivenessOverrideModuleInputs.AssumedAggressivenessOfOpponent;
                            else
                                pAggressivenessOverride = scaledRandomStrategy;
                            dAggressivenessOverride = thisPartysStrategyInResponseToRandomStrategy;
                        }
                        else
                        {
                            if (SymmetricAggressivenessOverrideModuleInputs.MisestimateOpponentsAggressivenessWhenOptimizing) // if so, then we will during evolution make the plaintiff act more as we assume the plaintiff will act
                                dAggressivenessOverride = (1.0 - SymmetricAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption) * scaledRandomStrategy + SymmetricAggressivenessOverrideModuleInputs.WeightOnAggressivenessAssumption * SymmetricAggressivenessOverrideModuleInputs.AssumedAggressivenessOfOpponent;
                            else
                                dAggressivenessOverride = scaledRandomStrategy;
                            pAggressivenessOverride = thisPartysStrategyInResponseToRandomStrategy;
                        }
                    }
                    else
                    {
                        double optimizedStrategy = Calculate(); // if evolution is incomplete, this will be the default behavior; if complete, this will depend on the equilibrium found below (because a GeneralOverrideValue will be set)
                        dAggressivenessOverride = optimizedStrategy;
                        pAggressivenessOverride = optimizedStrategy;
                    }
                    if (calculatingFinalMiniroundOverride)
                    {
                        LitigationGame.LGP.PAggressivenessOverrideFinal = pAggressivenessOverride;
                        LitigationGame.LGP.DAggressivenessOverrideFinal = dAggressivenessOverride;
                    }
                    else
                    {
                        LitigationGame.LGP.PAggressivenessOverride = pAggressivenessOverride;
                        LitigationGame.LGP.DAggressivenessOverride = dAggressivenessOverride;
                        // Set tentative values for final aggressiveness override
                        LitigationGame.LGP.PAggressivenessOverrideFinal = pAggressivenessOverride;
                        LitigationGame.LGP.DAggressivenessOverrideFinal = dAggressivenessOverride;
                    }
                    AddLatestAggressivenessOverridesToList();
                }
            }
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            return 0.4;
        }

        public override void Score()
        {
            bool randomStrategyIsForPlaintiff = SymmetricAggressivenessOverrideModuleInputs.RandSeedToDetermineWhoStrategyIsFor > 0.5;
            if (!randomStrategyIsForPlaintiff) // i.e., optimized strategy is for plaintiff
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else 
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SymmetricAggressivenessOverrideModule copy = new SymmetricAggressivenessOverrideModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = BargainingAggressivenessOverrideModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        internal static void FindSymmetricEquilibrium(Strategy theStrategy)
        {
            theStrategy.GeneralOverrideValue = null;
            double result;
            result = StrategyPointOn45DegreeLine.FindStrategyPointOn45DegreeLine(theStrategy);
            theStrategy.GeneralOverrideValue = result;
        }

        public virtual object GenerateSetting(string options)
        {

            int aggressivenessModuleNumber = GetIntCodeGeneratorOption(options, "AggressModNumber");
            bool divideBargainingIntoMinirounds = GetBoolCodeGeneratorOption(options, "DivideBargainingIntoMinirounds");
            int numBargainingMinirounds = GetIntCodeGeneratorOption(options, "NumBargainingMinirounds");

            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("SymAggressivenessOverride", "sao", divideBargainingIntoMinirounds ? "Initial" : "Unnamed"));
            if (divideBargainingIntoMinirounds)
                decisions.Add(GetDecision("SymAggressivenessOverrideFinal", "saof", "Final"));

            if (divideBargainingIntoMinirounds && numBargainingMinirounds == 1)
                throw new Exception("Cannot divide a bargaining round into a single miniround.");

            return new SymmetricAggressivenessOverrideModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "BargainingAggressivenessModule" + aggressivenessModuleNumber.ToString(),
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = new SymmetricAggressivenessOverrideModuleSettings() { AggressivenessModuleNumber = aggressivenessModuleNumber, DivideBargainingIntoMinirounds = divideBargainingIntoMinirounds, NumBargainingMinirounds = numBargainingMinirounds },
                WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter = true,
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

        private static Decision GetDecision(string name, string abbreviation, string chartSeriesName)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = "Sym",
                DynamicNumberOfInputs = true,
                SmoothingPointsOverride = 100, // must use small number of points so that we have a large sample for each point
                ActionToTakeFollowingStrategyDevelopment = FindSymmetricEquilibrium,
                UseOversampling = false,
                InputAbbreviations = new List<String>() { "OppAgg" },
                InputNames = new List<string>() { "Opponent's aggressiveness" },
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = (0.5 - 0.5 * initialRangeOfRandomStrategiesToTest),
                    UpperBound = (0.5 + 0.5 * initialRangeOfRandomStrategiesToTest),
                    AllowBoundsToExpandIfNecessary = false
                },
                //XAxisMinOverrideForPlot = 0.0,
                //XAxisMaxOverrideForPlot = 1.0,
                //YAxisMinOverrideForPlot = 0.0,
                //YAxisMaxOverrideForPlot = 1.0,
                XAxisLabelForPlot = "Opponent's share of perceived surplus insisted on",
                YAxisLabelForPlot = "Optimal share of perceived surplus to insist on",
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "OppAgg", MinValue=0.0, MaxValue=1.0, NumValues = 150 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="SymAggressiveReport.txt",
                        SettingsFor2DGraph = new Graph2DSettings() 
                        { 
                            xMin = 0.0, 
                            xMax = 1.0, 
                            yMin = -1.5, 
                            yMax = 2.5, 
                            xAxisLabel="Opponent's share of perceived surplus insisted on", 
                            yAxisLabel="Optimal share of perceived surplus to insist on", 
                            graphName="Symmetric bargaining aggressiveness", 
                            seriesName= chartSeriesName, // This means that we should simply use the repetition in the legend, if there is more than one
                            addRepetitionInfoToSeriesName = true, // If we have more than one repetition of this decision, we still have only one module, but we can add repetition tag string to series name to differentiate
                            replaceSeriesOfSameName=false, 
                            fadeSeriesOfSameName=true, 
                            superimposedLines = 
                            new List<Graph2DSuperimposedLine>() 
                            { 
                                new Graph2DSuperimposedLine()
                                {
                                    superimposedLineStartX = 0.0,
                                    superimposedLineEndX = 1.0,
                                    superimposedLineStartY = 0.0,
                                    superimposedLineEndY = 1.0,
                                    color = Color.Gray
                                }
                            } 
                        },
                        ReportAfterEachEvolutionStep = true
                    }
                },
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999, 
                PreservePreviousVersionWhenOptimizing = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = false,
                AverageInPreviousVersion = true,
                TestInputs = null, // new List<double>() { 0.5 }, 
                TestInputsList = null, // new List<List<double>>() { new List<double>() { 0.4 }, new List<double>() { 0.6 } },
                TestOutputs = null // new List<double> { -0.1, -0.02, 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5 }
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Drawing;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "RangeUncertaintyAggressivenessOverrideModule")]
    [Serializable]
    public class RangeUncertaintyAggressivenessOverrideModule : BargainingAggressivenessOverrideModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }

        public RangeUncertaintyAggressivenessOverrideModuleInputs RangeUncertaintyAggressivenessOverrideModuleInputs { get { return (RangeUncertaintyAggressivenessOverrideModuleInputs)GameModuleInputs; } }

        public RangeUncertaintyAggressivenessOverrideModuleSettings RangeUncertaintyAggressivenessOverrideModuleSettings { get { return (RangeUncertaintyAggressivenessOverrideModuleSettings)GameModuleSettings;  } }

        public enum AggressivenessDecisions
        { // Note: We can switch order of plaintiffdecision and defendantdecision here. must also reverse in generatesetting and in MustBeInEquilibrium...
           StructuredDecision, MultiplierDecision
        }

        public override void ExecuteModule()
        {
            if (LitigationGame.DisputeContinues())
            {
                if (!(LitigationGame.BargainingModule is UtilityRangeBargainingModule))
                    throw new Exception("This module can work only in conjunction with the UtilityRangeBargainingModule.");

                bool plaintiffsStrategyFirst = RangeUncertaintyAggressivenessOverrideModuleInputs.RandSeedToDetermineWhoStrategyIsFor > 0.5; // if evolving, then we would be evolving the plaintiff's strategy

                if (Game.CurrentDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.StructuredDecision && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(plaintiffsStrategyFirst));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.StructuredDecision && !Game.PreparationPhase)
                {
                    double firstStrategyCalculationResult = Calculate();
                    double secondStrategyCalculationResult = CalculateWithoutAffectingEvolution(GetInputs(!plaintiffsStrategyFirst), (int)Game.CurrentDecisionIndex, usePreviousVersionOfStrategy: Game.CurrentlyEvolvingCurrentlyExecutingDecision);

                    LitigationGame.LGP.PAggressivenessOverride = plaintiffsStrategyFirst ? firstStrategyCalculationResult : secondStrategyCalculationResult;
                    LitigationGame.LGP.DAggressivenessOverride = plaintiffsStrategyFirst ? secondStrategyCalculationResult : firstStrategyCalculationResult;
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.MultiplierDecision && Game.PreparationPhase)
                {
                    SpecifyInputs(new List<double>() { RangeUncertaintyAggressivenessOverrideModuleInputs.RandomlyDeterminedStrategy * 4.0 - 2.0 });
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.MultiplierDecision && !Game.PreparationPhase)
                {
                    double calcResult = Calculate();
                    if (Game.CurrentlyEvolvingCurrentlyExecutingDecision)
                    {
                        if (plaintiffsStrategyFirst)
                        {
                            LitigationGame.LGP.PAggressivenessOverride += calcResult;
                            LitigationGame.LGP.DAggressivenessOverride += RangeUncertaintyAggressivenessOverrideModuleInputs.RandomlyDeterminedStrategy * 4.0 - 2.0;
                        }
                        else
                        {
                            LitigationGame.LGP.PAggressivenessOverride += RangeUncertaintyAggressivenessOverrideModuleInputs.RandomlyDeterminedStrategy * 4.0 - 2.0;
                            LitigationGame.LGP.DAggressivenessOverride += calcResult;
                        }
                    }
                }
            }
        }

        internal static void FindSymmetricEquilibrium(Strategy theStrategy)
        {
            double result;
            result = StrategyPointOn45DegreeLine.FindStrategyPointOn45DegreeLine(theStrategy, 0.0, 4.0);
            // right now, we'll use this just for feedback now that we're averaging theStrategy.AllStrategies[theStrategy.DecisionNumber - 1].PreviousVersionOfThisStrategy.ResultIncrement = result;
        }

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            if (decisionNumberWithinActionGroup == 0)
            {
                List<Tuple<string, string>> namesAndAbbreviations = new List<Tuple<string, string>> { new Tuple<string, string>("BRange", "brange"), new Tuple<string, string>("OwnUncertainty", "ownunc"), new Tuple<string, string>("RelativeUncertainty", "relunc") };
                return namesAndAbbreviations;
            }
            else
            {
                List<Tuple<string, string>> namesAndAbbreviations = new List<Tuple<string, string>> { new Tuple<string, string>("OpponentsMultiplier", "oppmult") };
                return namesAndAbbreviations;
            }
        }

        public List<double> GetInputs(bool plaintiff)
        {
            UtilityRangeBargainingModuleProgress prog = (UtilityRangeBargainingModuleProgress) LitigationGame.BargainingModule.BargainingProgress;
            List<double> pBargainingInputs, dBargainingInputs;
            double pPerceivedBargainingRange = 0 - (double)prog.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked - (double)prog.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked;
            double dPerceivedBargainingRange = 0 - (double)prog.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked - (double)prog.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
            bool useActualAnticipatedTrialCostsInstead = Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup; // use the actual bargaining range when evolving
            if (useActualAnticipatedTrialCostsInstead)
                pPerceivedBargainingRange = dPerceivedBargainingRange = (LitigationGame.LitigationCostModule.LitigationCostProgress.PAnticipatedTrialExpenses + LitigationGame.LitigationCostModule.LitigationCostProgress.DAnticipatedTrialExpenses) / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;

            // Inputs:  bargaining range relative to damages, own uncertainty relative to damages, and own uncertainty relative to opponent's perceived uncertainty
            pBargainingInputs = new List<double>() { pPerceivedBargainingRange, (double)prog.MostRecentPProjectionPAbsErrorInSettingThreatPoint, (double)prog.MostRecentPProjectionPAbsErrorInSettingThreatPoint / (double)prog.MostRecentPProjectionDAbsErrorInSettingThreatPoint };
            dBargainingInputs = new List<double>() { dPerceivedBargainingRange, (double)prog.MostRecentDProjectionDAbsErrorInSettingThreatPoint, (double)prog.MostRecentDProjectionDAbsErrorInSettingThreatPoint / (double)prog.MostRecentDProjectionPAbsErrorInSettingThreatPoint };

            List<double> inputs = plaintiff ? pBargainingInputs : dBargainingInputs;
            
            return inputs;
        }

        private double? GetEarlierAggressivenessModuleResult()
        {
            bool calculatingPlaintiffsStrategy = RangeUncertaintyAggressivenessOverrideModuleInputs.RandSeedToDetermineWhoStrategyIsFor > 0.5;
            if (LitigationGame.LGP.PAggressivenessOverride != null)
            { // The earlier aggressiveness module is used to set the default values for this aggressiveness module.
                if (calculatingPlaintiffsStrategy)
                    return (double)LitigationGame.LGP.PAggressivenessOverride;
                else
                    return LitigationGame.LGP.DAggressivenessOverride ?? 0;
            }
            return null;
        }

        public override double Calculate()
        {
            var currentlyEvolvingModule = Game.CurrentlyEvolvingModule;
            if (RangeUncertaintyAggressivenessOverrideModuleSettings.AggressivenessModuleNumber == 2 /* i.e., this is second of two modules */
                && currentlyEvolvingModule != null
                && currentlyEvolvingModule != this
                && currentlyEvolvingModule is BargainingAggressivenessOverrideModule)
            {
                var bp = LitigationGame.BargainingModule.BargainingProgress;
                if (bp.CurrentBargainingRoundNumber == bp.CurrentlyEvolvingBargainingRoundNumber && bp.CurrentBargainingSubclaimNumber == bp.CurrentlyEvolvingBargainingSubclaimNumber)
                { // this may have already evolved in a prior repetition, but an earlier aggressiveness override module within this round and subclaim is evolving, so we must pass on its values.
                    double? result = GetEarlierAggressivenessModuleResult();
                    if (result != null)
                        return (double)result;
                }
            }
            return base.Calculate();
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            if (RangeUncertaintyAggressivenessOverrideModuleSettings.AggressivenessModuleNumber == 2)
            { // this has not evolved yet, but if the earlier aggressiveness module has a result, we want to pass it on
                double? result = GetEarlierAggressivenessModuleResult();
                if (result != null)
                    return (double)result;
            }
            if (LitigationGame.LitigationGameInputs.PlayersActToMaximizeSocialWelfare)
                return 0.0 ; // otherwise, players will immediately focus just on goal of reducing litigation
            return 0;
        }

        public override void Score()
        {
            // we'll calculate the score relative to a baseline where the plaintiff has a 50% chance of winning. This makes it easier to
            // compare plaintiff and defendant scores, so there will be less noise from randomness associated with whether we have randomly picked the plaintiff
            // or the defendant. 
            UtilityRangeBargainingModule bm = (UtilityRangeBargainingModule)LitigationGame.BargainingModule;
            Tuple<double, double> pAndDBaselineWealthChangesAsProportionOfDamages = bm.GetEquivalentWealthDeltaProjectionIfPartiesHadCorrectInformation(getFiftyPercentBaselineInstead: true);
            double damages = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
            Tuple<double, double> pAndDBaselineFinalWealth = new Tuple<double, double>(
                LitigationGame.Plaintiff.InitialWealth + pAndDBaselineWealthChangesAsProportionOfDamages.Item1 * damages, 
                LitigationGame.Defendant.InitialWealth + pAndDBaselineWealthChangesAsProportionOfDamages.Item2 * damages
                );
            
            bool evolvingPlaintiffsStrategy = RangeUncertaintyAggressivenessOverrideModuleInputs.RandSeedToDetermineWhoStrategyIsFor > 0.5;
            double performance = LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(evolvingPlaintiffsStrategy);
            double score = performance - (evolvingPlaintiffsStrategy ? pAndDBaselineFinalWealth.Item1 : pAndDBaselineFinalWealth.Item2);

            Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, score);

            //if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.StructuredDecision)
            //    Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(evolvingPlaintiffsStrategy));
            //else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)AggressivenessDecisions.MultiplierDecision)
            //    Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(evolvingPlaintiffsStrategy));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            RangeUncertaintyAggressivenessOverrideModule copy = new RangeUncertaintyAggressivenessOverrideModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = BargainingAggressivenessOverrideModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            bool evolvingRangeUncertaintySettings = GetBoolCodeGeneratorOption(options, "EvolvingRangeUncertaintySettings");
            int aggressivenessModuleNumber = GetIntCodeGeneratorOption(options, "AggressModNumber");

            int? iterationsOverrideMin = null, iterationsOverrideMax = null, smoothingPointsOverrideMin = null, smoothingPointsOverrideMax = null;
            double iterationsOverrideCurvature = 1.0, smoothingPointsOverrideCurvature = 1.0;
            if (evolvingRangeUncertaintySettings)
            { 
                iterationsOverrideMin = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessIterationsOverrideMin");
                iterationsOverrideMax = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessIterationsOverrideMax"); 
                iterationsOverrideCurvature = GetDoubleCodeGeneratorOption(options, "RangeUncertaintyAggressivenessIterationsOverrideCurvature");
                smoothingPointsOverrideMin = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessSmoothingPointsOverrideMin");
                smoothingPointsOverrideMax = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessSmoothingPointsOverrideMax");
                smoothingPointsOverrideCurvature = GetDoubleCodeGeneratorOption(options, "RangeUncertaintyAggressivenessSmoothingPointsOverrideCurvature");
            }

            List<Decision> decisions = new List<Decision>();

            // swap here if reversing order of decisions
            Decision mainDecision = GetDecision("StructuredAggressivenessOverride", "sao", iterationsOverrideMax, smoothingPointsOverrideMax); // we will update based on the actual number we want for this repetition in UpdateBasedOnTagInfo
            if (!evolvingRangeUncertaintySettings)
            {
                mainDecision.PreevolvedStrategyFilename = "RangeUncertaintyDecision";
                mainDecision.SkipThisDecisionWhenEvolvingIfAlreadyEvolved = true;
            }
            decisions.Add(mainDecision);
            bool includeMultiplierStep = false;
            if (!evolvingRangeUncertaintySettings)
                includeMultiplierStep = false;
            if (includeMultiplierStep)
                decisions.Add(GetDecision("MultiplierAggressivenessOverride", "mao", 0, 0));

            List<string> moduleNamesReliedOn = new List<string>();
            if (aggressivenessModuleNumber == 2)
                moduleNamesReliedOn.Add("BargainingAggressivenessModule1");


            return new RangeUncertaintyAggressivenessOverrideModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "BargainingAggressivenessModule" + aggressivenessModuleNumber.ToString(),
                GameModuleNamesThisModuleReliesOn = moduleNamesReliedOn,
                GameModuleSettings = new RangeUncertaintyAggressivenessOverrideModuleSettings() { 
                    AggressivenessModuleNumber = aggressivenessModuleNumber,
                    EvolvingRangeUncertaintyModule = evolvingRangeUncertaintySettings,
                    IterationsOverrideMinimum = iterationsOverrideMin,
                    IterationsOverrideMaximum = iterationsOverrideMax,
                    IterationsOverrideCurvature = iterationsOverrideCurvature,
                    SmoothingPointsOverrideMinimum = smoothingPointsOverrideMin,
                    SmoothingPointsOverrideMaximum = smoothingPointsOverrideMax,
                    SmoothingPointsOverrideCurvature = smoothingPointsOverrideCurvature
                },
                WhenEvolvingMoveCumulativeDistributionsBeforeThisGroupToAfter = true,
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round", "Range uncertainty aggressiveness iteration" }
            };
        }

        public override void UpdateBasedOnTagInfo(string tag, int matchNumber, int totalMatches, ref Decision d)
        {
            if (RangeUncertaintyAggressivenessOverrideModuleSettings.EvolvingRangeUncertaintyModule)
            { 
                string tagName = "Range uncertainty aggressiveness iteration";
                if (tagName == tag)
                {
                    double proportion = (double)matchNumber / (double)totalMatches; // ModuleRepetitionNumber is 1-based
                    d.IterationsOverride = (long?)MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues((double)RangeUncertaintyAggressivenessOverrideModuleSettings.IterationsOverrideMinimum, (double)RangeUncertaintyAggressivenessOverrideModuleSettings.IterationsOverrideMaximum, (double)RangeUncertaintyAggressivenessOverrideModuleSettings.IterationsOverrideCurvature, proportion);
                    d.SmoothingPointsOverride = (int?)MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues((double)RangeUncertaintyAggressivenessOverrideModuleSettings.SmoothingPointsOverrideMinimum, (double)RangeUncertaintyAggressivenessOverrideModuleSettings.SmoothingPointsOverrideMaximum, (double)RangeUncertaintyAggressivenessOverrideModuleSettings.SmoothingPointsOverrideCurvature, proportion);
                }
            }
        }

        private static Decision GetDecision(string name, string abbreviation, int? iterationsOverride, int? smoothingPointsOverride)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                AlwaysUseLatestVersion = true,
                ActionToTakeFollowingStrategyDevelopment = abbreviation == "mao" ? FindSymmetricEquilibrium : (Action<Strategy>)null,
                DynamicNumberOfInputs = true,
                UseOversampling = false,
                InputAbbreviations = null, // set dynamically
                InputNames = null, // set dynamically
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = abbreviation == "mao" ? -2 : -5,
                    UpperBound = abbreviation == "mao" ? 2 : 5,
                    AllowBoundsToExpandIfNecessary = false
                },
                ConvertOneDimensionalDataToLookupTable = true,
                NumberPointsInLookupTable = 1000,
                SmoothingPointsOverride = smoothingPointsOverride,
                IterationsOverride = iterationsOverride,
                Bipolar = false,
                StrategyGraphInfos = abbreviation == "sao" ? new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation = "ownunc", NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="AggressiveReport2.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { yMin = -5, yMax = 5, xAxisLabel="Own uncertainty relative to damages", yAxisLabel="Uncertainty units from bargaining range midpoint", graphName="Aggressiveness given own uncertainty", addRepetitionInfoToSeriesName = true, replaceSeriesOfSameName= false, fadeSeriesOfSameName= true, maxVisiblePerSeries=15, exportFramesOfMovies = false },
                        ReportAfterEachEvolutionStep = true
                    },
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "relunc", NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="AggressiveReport2.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { yMin = -5, yMax = 5, xAxisLabel="Own uncertainty relative to expected opp.'s", yAxisLabel="Uncertainty units from bargaining range midpoint", graphName="Aggressiveness given opponent's uncertainty",  addRepetitionInfoToSeriesName = true, replaceSeriesOfSameName= false, fadeSeriesOfSameName= true, maxVisiblePerSeries=15, exportFramesOfMovies = false },
                        ReportAfterEachEvolutionStep = true
                    },
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "brange", NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="AggressiveReport3.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { yMin = -5.0, yMax = 5.0, xAxisLabel="Bargaining range / damages claim", yAxisLabel="Uncertainty units from bargaining range midpoint", graphName="Aggressiveness given bargaining range size",  addRepetitionInfoToSeriesName = true, replaceSeriesOfSameName= false, fadeSeriesOfSameName= true, maxVisiblePerSeries=15, exportFramesOfMovies = false },
                        ReportAfterEachEvolutionStep = true
                    }
                } : new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "oppmult", MinValue=-2.0, MaxValue=2.0, NumValues = 150 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="SymAggressiveReport.txt",   
                        SettingsFor2DGraph = new Graph2DSettings() 
                        { 
                            xMin = -2.0, 
                            xMax = 2.0, 
                            yMin = -2, 
                            yMax = 2, 
                            xAxisLabel="Opponent's multiple of optimized strategy", 
                            yAxisLabel="Party's optimal multiple", 
                            graphName="Symmetric bargaining aggressiveness", 
                            addRepetitionInfoToSeriesName = true, // If we have more than one repetition of this decision, we still have only one module, but we can add repetition tag string to series name to differentiate
                            replaceSeriesOfSameName=false, 
                            fadeSeriesOfSameName=true, 
                            superimposedLines = 
                            new List<Graph2DSuperimposedLine>() 
                            { 
                                new Graph2DSuperimposedLine()
                                {
                                    superimposedLineStartX = -2.0,
                                    superimposedLineEndX = 2.0,
                                    superimposedLineStartY = -2.0,
                                    superimposedLineEndY = 2.0,
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
                PreservePreviousVersionWhenOptimizing = true, // isPDecision,
                UsePreviousVersionWhenOptimizingOtherDecisionInModule = false,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                // switch the exclamation marks on isPDecision if reversing the order of decisions
                MustBeInEquilibriumWithNextDecision = false,
                MustBeInEquilibriumWithPreviousDecision = false,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = false,
                TestInputs = null, // new List<double>() { 0.03, 1.0 },
                TestInputsList = null, // new List<List<double>>() { new List<double>() { 0.4 }, new List<double>() { 0.6 } },
                TestOutputs = null, //new List<double> { -1.0, -0.9, -0.8, -0.7, -0.6, -0.5, -0.4, -0.3, -0.2, -0.1, 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 }
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "DropOrDefaultChoiceModule")]
    [Serializable]
    public class DropOrDefaultChoiceModule : DropOrDefaultModule, ICodeBasedSettingGenerator
    {
        public DropOrDefaultChoiceModuleSettings DropOrDefaultChoiceModuleSettings { get { return (DropOrDefaultChoiceModuleSettings)GameModuleSettings; } }
        public DropOrDefaultChoiceModuleProgress DropOrDefaultChoiceModuleProgress { get { return (DropOrDefaultChoiceModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public DropOrDefaultChoiceInputs DropOrDefaultChoiceInputs { get { return (DropOrDefaultChoiceInputs)GameModuleInputs; } } 

        public enum DropOrDefaultChoiceDecisions
        {
            PDropsCase, 
            DDefaultsCase
        }

        public override void MakeDecisions()
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.PDropsCase && Game.PreparationPhase)
                DropOrDefaultChoiceModuleProgress.StartedModuleThisBargainingRound = false;
            bool blockDropping = Game.CurrentlyEvolving &&
                Game.CurrentlyEvolvingActionGroup.Name == "ProbabilityPWinsForecastingModule";
                
            if (LitigationGame.DisputeContinues() || DropOrDefaultChoiceModuleProgress.StartedModuleThisBargainingRound)
            {
                if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.PDropsCase && Game.PreparationPhase)
                {
                    DropOrDefaultChoiceModuleProgress.StartedModuleThisBargainingRound = true; // it must be that the dispute is continuing (because we set StartedModuleThisBargainingRound to false above), so now we signal that we have started the module, so that we will finish it even if the dispute does not continue.
                    if (DropOrDefaultChoiceModuleSettings.DropOrDefaultBasedOnProbabilityCutoffOnly)
                    {
                        if (Game.CurrentlyEvolvingCurrentlyExecutingDecision)
                            Game.Progress.CutoffVariable = GetInputs(true)[0];
                        SpecifyInputs(new List<double> { });
                    }
                    else
                        SpecifyInputs(GetInputs(true));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.PDropsCase && !Game.PreparationPhase)
                {
                    HeisenbugTracker.EnableCheckInInfoStorage = true;
                    double calcResult = Calculate(); 
                    if (DropOrDefaultChoiceModuleSettings.DropOrDefaultBasedOnProbabilityCutoffOnly)
                    {
                        double probEst = GetInputs(true)[0];
                        DropOrDefaultChoiceModuleProgress.PDropsCase = !blockDropping && calcResult > probEst; // i.e., when cutoff point is greater than probability estimate
                    }
                    else
                        DropOrDefaultChoiceModuleProgress.PDropsCase = !blockDropping && calcResult > 0; // outcome is bipolar
                    HeisenbugTracker.EnableCheckInInfoStorage = false;
                    if (DropOrDefaultChoiceInputs.MakeDecisionsAssumingOtherWillNotDrop && Game.CurrentlyEvolvingCurrentlyExecutingActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.DDefaultsCase)
                        DropOrDefaultChoiceModuleProgress.PDropsCase = !blockDropping && false;
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.DDefaultsCase && Game.PreparationPhase)
                {
                    if (!DropOrDefaultChoiceInputs.MakeDecisionsAssumingOtherWillNotDrop)
                    {
                        throw new Exception("Internal exception: having defendant take into account whether plaintiff has dropped is not supported.");
                        // If we wanted to support it, we would need to separate dropping or default into two separate action groups and signal for an UpdateCumulativeDistributionsModule
                        // to be updated between them (as well as after the second). 
                    }

                    // Now, specify inputs.
                    if (DropOrDefaultChoiceModuleSettings.DropOrDefaultBasedOnProbabilityCutoffOnly)
                    {
                        if (Game.CurrentlyEvolvingCurrentlyExecutingDecision)
                            Game.Progress.CutoffVariable = GetInputs(false)[0];
                        SpecifyInputs(new List<double>() { });
                    }
                    else
                        SpecifyInputs(GetInputs(false));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.DDefaultsCase && !Game.PreparationPhase)
                {
                    if (DropOrDefaultChoiceInputs.MakeDecisionsAssumingOtherWillNotDrop || !DropOrDefaultChoiceModuleProgress.PDropsCase) // only continue if P hasn't dropped the case
                    {
                        if (DropOrDefaultChoiceModuleSettings.DropOrDefaultBasedOnProbabilityCutoffOnly)
                            DropOrDefaultChoiceModuleProgress.DDefaultsCase = !blockDropping && Calculate() > GetInputs(false)[0]; // i.e., when cutoff point is greater than probability estimate
                        else
                            DropOrDefaultChoiceModuleProgress.DDefaultsCase = !blockDropping && Calculate() > 0; // outcome is bipolar
                    }
                    if (Game.CurrentlyEvolvingCurrentlyExecutingActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.PDropsCase)
                        DropOrDefaultChoiceModuleProgress.DDefaultsCase = !blockDropping && false;

                    if (DropOrDefaultChoiceModuleProgress.DropOrDefaultPeriod == DropOrDefaultPeriod.Mid)
                        DropOrDefaultChoiceModuleProgress.MidDropRoundsCompleted++;
                }
                //DropOrDefaultChoiceModuleProgress.PDropsCase = false; // uncomment to disable dropping when generating reports
                //DropOrDefaultChoiceModuleProgress.DDefaultsCase = false; // uncomment to disable dropping when generating reports
                if (DropOrDefaultChoiceModuleProgress.PDropsCase && DropOrDefaultChoiceModuleProgress.DDefaultsCase)
                {
                    DropOrDefaultChoiceModuleProgress.DualDrop = true;
                    if (DropOrDefaultChoiceInputs.RandomSeedIfBothGiveUpAtOnce < 0.5)
                        DropOrDefaultChoiceModuleProgress.PDropsCase = !blockDropping && false;
                    else
                        DropOrDefaultChoiceModuleProgress.DDefaultsCase = !blockDropping && false;
                }
                if (DropOrDefaultChoiceModuleProgress.PDropsCase)
                {
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("P drops case");
                    LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = true, DroppedAfterBargainingRound = LitigationGame.LGP.BargainingModuleProgress.BargainingRound, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
                }
                else if (DropOrDefaultChoiceModuleProgress.DDefaultsCase)
                {
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("D drops case");
                    LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = false, DroppedAfterBargainingRound = LitigationGame.LGP.BargainingModuleProgress.BargainingRound, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
                }
                else
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("Neither party drops");
                // Uncomment the following if we want all other modules to evolve without consideration for whether a case is dropped or defaulted (not generally recommended)
                //if (Game.CurrentlyEvolvingModule != null && Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingModuleNumberGroupingAllModuleRepetitions != ModuleNumberGroupingAllRepetitions && ((Game.CurrentlyEvolvingModule is DropOrDefaultModule)))
                //{
                //    DropOrDefaultChoiceModuleProgress.PDropsCase = false;
                //    DropOrDefaultChoiceModuleProgress.DDefaultsCase = false;
                //}
            }
        }

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            bool plaintiff = decisionNumberWithinActionGroup == 0;
            List<Tuple<string, string>> pNamesAndAbbreviations = new List<Tuple<string,string>>();
            List<Tuple<string, string>> dNamesAndAbbreviations = new List<Tuple<string, string>>();

            List<Tuple<string, string>> pNamesAndAbbreviationsBargaining;
            List<Tuple<string, string>> dNamesAndAbbreviationsBargaining;
            LitigationGame.BargainingModule.GetInputNamesAndAbbreviationsForBargaining(out pNamesAndAbbreviationsBargaining, out dNamesAndAbbreviationsBargaining);
            pNamesAndAbbreviations.AddRange(pNamesAndAbbreviationsBargaining);
            dNamesAndAbbreviations.AddRange(dNamesAndAbbreviationsBargaining);

            if (plaintiff)
                return pNamesAndAbbreviations;
            else
                return dNamesAndAbbreviations;

        }

        public List<double> GetInputs(bool plaintiff)
        {
            List<double> pInputs, dInputs;
            LitigationGame.BargainingModule.GetInputsForBargaining(out pInputs, out dInputs);
            //if (adjustmentsModuleInputs1 == null && adjustmentsModuleInputs2 == null)
                return plaintiff ? pInputs : dInputs;

            // Not needed for now, because this is being moved to GetInputsForBargaining.
            //List<double> adjustmentsModuleInputs1 = LitigationGame.AdjustmentsModule1.GetStatusValues();
            //List<double> adjustmentsModuleInputs2 = LitigationGame.AdjustmentsModule2.GetStatusValues();
            //if (adjustmentsModuleInputs1 != null)
            //    theInputs.AddRange(adjustmentsModuleInputs1);
            //if (adjustmentsModuleInputs2 != null)
            //    theInputs.AddRange(adjustmentsModuleInputs2);
            // return theInputs;
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            bool dropOrDefaultBasedOnProbabilityCutoffOnly = DropOrDefaultChoiceModuleSettings.DropOrDefaultBasedOnProbabilityCutoffOnly;
            int decisionNumberWithinActionGroup = (int) Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            switch (decisionNumberWithinActionGroup)
            {
                case (int) DropOrDefaultChoiceDecisions.PDropsCase:
                    return dropOrDefaultBasedOnProbabilityCutoffOnly ? 0.0 : - 1.0; // don't drop
                case (int)DropOrDefaultChoiceDecisions.DDefaultsCase:
                    return dropOrDefaultBasedOnProbabilityCutoffOnly ? 0.0 : -1.0; // don't default
                default:
                    throw new Exception("Unknown decision.");
            }
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.PDropsCase)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropOrDefaultChoiceDecisions.DDefaultsCase)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            DropOrDefaultChoiceModule copy = new DropOrDefaultChoiceModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = DropOrDefaultChoiceModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public override object GenerateSetting(string options)
        {
            DropOrDefaultPeriod period = GetDropOrDefaultPeriodFromCodeGeneratorOptions(options);
            string pointString = period == DropOrDefaultPeriod.Beginning ? "Beginning" : (period == DropOrDefaultPeriod.Mid ? "Mid" : "End");

            bool dropOrDefaultBasedOnProbabilityCutoffOnly = GetBoolCodeGeneratorOption(options, "DropOrDefaultBasedOnProbabilityCutoffOnly");
            DropOrDefaultChoiceModuleSettings gameModuleSettings = new DropOrDefaultChoiceModuleSettings() { Period = period, DropOrDefaultBasedOnProbabilityCutoffOnly = dropOrDefaultBasedOnProbabilityCutoffOnly}; // we set this here (even though it will also be set in the Progress variable) because we need to know when ConductEvolutionThisEvolveStep is called

            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("PDrop" + pointString, "pdrop", true, period, dropOrDefaultBasedOnProbabilityCutoffOnly));
            decisions.Add(GetDecision("DDefault" + pointString, "ddef", false, period, dropOrDefaultBasedOnProbabilityCutoffOnly));

            return new DropOrDefaultChoiceModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "ActionBeforeDropOrDefault" },
                GameModuleName = pointString + "DropOrDefaultModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = gameModuleSettings,
                UpdateCumulativeDistributionsAfterSingleActionGroup = false, /* currently updating only after dispute generation */
                Tags = period == DropOrDefaultPeriod.Mid ? new List<string>() { "Bargaining round", "Drop middle bargaining round" } : null
            };
        }

        private static Decision GetDecision(string name, string abbreviation, bool isPDecision, DropOrDefaultPeriod period, bool dropOrDefaultBasedOnProbabilityCutoffOnly)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.01,
                InformationSetAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = dropOrDefaultBasedOnProbabilityCutoffOnly ? -0.2 /* the strategy in this case represents the probability proxy */ : -1.0 /* representing a bipolar decision */,
                    UpperBound = dropOrDefaultBasedOnProbabilityCutoffOnly ? 1.2 /* the strategy in this case represents the probability proxy */ : 1.0 /* representing a bipolar decision */
                },
                Bipolar = !dropOrDefaultBasedOnProbabilityCutoffOnly,
                Cutoff = dropOrDefaultBasedOnProbabilityCutoffOnly,
                CutoffPositiveOneIsPlayedToLeft = true,
                IterationsMultiplier = 1.0,
                ImproveOptimizationOfCloseCasesForBipolarDecision = false,
                ImproveOptimizationOfCloseCasesForBipolarDecisionMultiplier = 5,
                ImproveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize = 0.05,
                StrategyGraphInfos = dropOrDefaultBasedOnProbabilityCutoffOnly ? new List<StrategyGraphInfo>() { } : new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 200 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                            new InputValueFixInGraph() { InputAbbreviation= isPDecision ? "DCP" : "PCD", Value = 1.0 }
                        },
                        OutputReportFilename="DropOrDefault.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = -1.0, yMax = 1.0, xAxisLabel="Party's estimate of probability it would win", yAxisLabel="Decision whether to give up when challenged", graphName="Parties' drop-default decision if challenged", seriesName= (isPDecision ? "Plaintiff" : "Defendant") + (period == DropOrDefaultPeriod.Beginning ? " start" : (period == DropOrDefaultPeriod.Mid ? " mid" : " end")), replaceSeriesOfSameName=false, fadeSeriesOfSameName=true },
                        ReportAfterEachEvolutionStep = true
                    },
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 200 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="DropOrDefault.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = -1.0, yMax = 1.0, xAxisLabel="Party's estimate of probability it would win", yAxisLabel="Decision whether to give up", graphName="Parties' drop-default decision", seriesName= (isPDecision ? "Plaintiff" : "Defendant") + (period == DropOrDefaultPeriod.Beginning ? " start" : (period == DropOrDefaultPeriod.Mid ? " mid" : " end")), replaceSeriesOfSameName=false, fadeSeriesOfSameName=true },
                        ReportAfterEachEvolutionStep = true
                    }
                },
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999, 
                PreservePreviousVersionWhenOptimizing = false,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = false,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = false,
                TestInputs = null, // new List<double>() { 0.10 }, // include only inputs that are not eliminated 
                TestInputsList = null, // new List<List<double>>() { new List<double>() { 0.05 }, new List<double>() { 0.10 }, new List<double>() { 0.15 }, new List<double>() { 0.20 }, new List<double>() { 0.25 }, new List<double>() { 0.29 }, new List<double>() { 0.30 }, new List<double>() { 0.31 }, new List<double>() { 0.32 }, new List<double>() { 0.33 }, new List<double>() { 0.34 }, new List<double>() { 0.35 }, new List<double>() { 0.36 }, new List<double>() { 0.40 }, new List<double>() { 0.45}, new List<double>() { 0.50 }, new List<double>() { 0.55 }, new List<double>() { 0.60 }, new List<double>() { 0.65 }, new List<double>() { 0.70 }, new List<double>() { 0.75 }, new List<double>() { 0.80 }, new List<double>() { 0.85 }, new List<double>() { 0.90 } },
                TestOutputs = new List<double>() { -1.0, 1.0 }
            };
        }
    }
}

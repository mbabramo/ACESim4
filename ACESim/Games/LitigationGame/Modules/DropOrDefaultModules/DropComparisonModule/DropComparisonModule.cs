using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "DropComparisonModule")]
    [Serializable]
    public class DropComparisonModule : DropOrDefaultModule, ICodeBasedSettingGenerator
    {
        public DropComparisonModuleSettings DropComparisonModuleSettings { get { return (DropComparisonModuleSettings)GameModuleSettings; } }
        public DropComparisonModuleProgress DropComparisonModuleProgress { get { return (DropComparisonModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public DropComparisonInputs DropComparisonInputs { get { return (DropComparisonInputs)GameModuleInputs; } } 

        public enum DropComparisonDecisions
        {
            PDoesntDrop,
            PDropsCase,
            DDoesntDefault,
            DDefaultsCase
        }

        public override void MakeDecisions()
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDoesntDrop && Game.PreparationPhase)
                DropComparisonModuleProgress.StartedModuleThisBargainingRound = false; 
            if (LitigationGame.DisputeContinues() || DropComparisonModuleProgress.StartedModuleThisBargainingRound)
            {
                if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDoesntDrop && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(true));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDoesntDrop && !Game.PreparationPhase)
                {
                    DropComparisonModuleProgress.PUtilityNotDropping = Calculate();
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDropsCase && Game.PreparationPhase)
                {
                    DropComparisonModuleProgress.StartedModuleThisBargainingRound = true; // it must be that the dispute is continuing (because we set StartedModuleThisBargainingRound to false above), so now we signal that we have started the module, so that we will finish it even if the dispute does not continue.
                    SpecifyInputs(GetInputs(true));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDropsCase && !Game.PreparationPhase)
                {
                    DropComparisonModuleProgress.PUtilityDropping = Calculate();
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDoesntDefault && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(false));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDoesntDefault && !Game.PreparationPhase)
                {
                    DropComparisonModuleProgress.DUtilityNotDefaulting = Calculate();
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDefaultsCase && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(false));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDefaultsCase && !Game.PreparationPhase)
                {
                    DropComparisonModuleProgress.DUtilityDefaulting = Calculate();
                    DetermineWhetherToDrop();
                }
                else 
                    throw new Exception("Internal error. Unknown decision.");

            }
        }

        public void DetermineWhetherToDrop()
        {

            bool blockDropping = Game.CurrentlyEvolving &&
                Game.CurrentlyEvolvingActionGroup.Name == "ProbabilityPWinsForecastingModule";
                
            bool pWouldDrop = false, dWouldDefault = false;
            // first determine whether to drop when we are evolving, based on which decision we are evolving
            bool autosettingP = false, autosettingD = false;
            bool assumeOtherPartyWontDropInThisRound = false;
            if (Game.CurrentlyEvolvingCurrentlyExecutingActionGroup)
            {
                int? currentlyEvolvingDecisionIndex = Game.CurrentlyEvolvingDecisionIndexWithinActionGroup;
                if (currentlyEvolvingDecisionIndex == (int)DropComparisonDecisions.PDropsCase || currentlyEvolvingDecisionIndex == (int)DropComparisonDecisions.PDoesntDrop)
                {
                    autosettingP = true;
                    pWouldDrop = !blockDropping && currentlyEvolvingDecisionIndex == (int)DropComparisonDecisions.PDropsCase;
                    if (assumeOtherPartyWontDropInThisRound)
                    {
                        dWouldDefault = false; 
                        autosettingD = true;
                    }
                }
                else
                {
                    autosettingD = true;
                    dWouldDefault = !blockDropping && currentlyEvolvingDecisionIndex == (int)DropComparisonDecisions.DDefaultsCase;
                    if (assumeOtherPartyWontDropInThisRound)
                    {
                        pWouldDrop = false; 
                        autosettingP = true;
                    }
                }
            }
                // now determine by comparison whether p would drop and/or d would default, if we are not evolving those decisions
            if (!autosettingP)
                pWouldDrop = !blockDropping && DropComparisonModuleProgress.PUtilityDropping > DropComparisonModuleProgress.PUtilityNotDropping;
            if (!autosettingD)
                dWouldDefault = !blockDropping && DropComparisonModuleProgress.DUtilityDefaulting > DropComparisonModuleProgress.DUtilityNotDefaulting;
            // handle the rare case in which both would give up simultaneously
            if (pWouldDrop && dWouldDefault)
            {
                if (DropComparisonInputs.RandomSeedIfBothGiveUpAtOnce < 0.5)
                    pWouldDrop = false;
                else
                    dWouldDefault= false;
            }

            // update rounds
            if (DropComparisonModuleProgress.DropOrDefaultPeriod == DropOrDefaultPeriod.Mid)
                DropComparisonModuleProgress.MidDropRoundsCompleted++;

            // Record our decision
            DropComparisonModuleProgress.PDropsCase = pWouldDrop;
            DropComparisonModuleProgress.DDefaultsCase = dWouldDefault;
            if (DropComparisonModuleProgress.PDropsCase)
                LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = true, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
            else if (DropComparisonModuleProgress.DDefaultsCase)
                LitigationGame.LGP.DropInfo = new DropInfo() { DroppedByPlaintiff = false, DropOrDefaultPeriod = DropOrDefaultProgress.DropOrDefaultPeriod };
        }

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            bool plaintiff = decisionNumberWithinActionGroup == (int)DropComparisonDecisions.PDropsCase || decisionNumberWithinActionGroup == (int)DropComparisonDecisions.PDoesntDrop;
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
            return plaintiff ? pInputs : dInputs;
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup = (int) Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            switch (decisionNumberWithinActionGroup)
            {
                    // just use dummy values so that nothing initially gets dropped
                case (int) DropComparisonDecisions.PDropsCase:
                    return -1.0; // don't drop
                case (int) DropComparisonDecisions.PDoesntDrop:
                    return 1.0; // > -1.0
                case (int)DropComparisonDecisions.DDefaultsCase:
                    return -1.0;
                case (int)DropComparisonDecisions.DDoesntDefault:
                    return 1.0;
                default:
                    throw new Exception("Unknown decision.");
            }
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDropsCase || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.PDoesntDrop)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDefaultsCase || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)DropComparisonDecisions.DDoesntDefault)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            DropComparisonModule copy = new DropComparisonModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = DropComparisonModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public override object GenerateSetting(string options)
        {
            DropOrDefaultPeriod period = GetDropOrDefaultPeriodFromCodeGeneratorOptions(options);
            string pointString = period == DropOrDefaultPeriod.Beginning ? "Beginning" : (period == DropOrDefaultPeriod.Mid ? "Mid" : "End");

            DropComparisonModuleSettings gameModuleSettings = new DropComparisonModuleSettings() { Period = period }; // we set this here (even though it will also be set in the Progress variable) because we need to know when ConductEvolutionThisEvolveStep is called

            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("PDoesntDrop" + pointString, "pnodrop", true, false, period));
            decisions.Add(GetDecision("PDrops" + pointString, "pdrop", true, true, period));
            decisions.Add(GetDecision("DDoesntDefault" + pointString, "dnodef", false, false, period));
            decisions.Add(GetDecision("DDefaults" + pointString, "ddef", false, true, period));

            return new DropComparisonModule()
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

        private static Decision GetDecision(string name, string abbreviation, bool isPDecision, bool isGiveUpCondition, DropOrDefaultPeriod period)
        {
            string seriesName = "";
            if (isPDecision && isGiveUpCondition)
                seriesName += "P drops";
            else if (isPDecision && !isGiveUpCondition)
                seriesName += "P doesn't drop";
            else if (!isPDecision && isGiveUpCondition)
                seriesName += "D defaults";
            else if (!isPDecision && !isGiveUpCondition)
                seriesName += "D doesn't default";
            string periodString = period == DropOrDefaultPeriod.Beginning ? " start" : (period == DropOrDefaultPeriod.Mid ? " mid" : " end");
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.3,
                InputAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                { // not relevant because score represents correct answer
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                IterationsMultiplier = 1.0,
                ImproveOptimizationOfCloseCasesForBipolarDecision = false, 
                ImproveOptimizationOfCloseCasesForBipolarDecisionMultiplier = 10,
                ImproveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize = 0.10,
                StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
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
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = 98500, yMax = 102500, xAxisLabel="Party's estimate of probability it would win", yAxisLabel="Expected utility", graphName="Expected effect of dropping at " + periodString, seriesName= seriesName, replaceSeriesOfSameName=false, fadeSeriesOfSameName=true },
                        ReportAfterEachEvolutionStep = true
                    }
                },
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = false,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = true,
                AutomaticallyGeneratePlotRegardlessOfGeneralSetting = false,
                TestInputs = null, // new List<double>() { 0.10 }, // include only inputs that are not eliminated 
                TestInputsList = null, // new List<List<double>>() { new List<double>() { 0.05 }, new List<double>() { 0.10 }, new List<double>() { 0.15 }, new List<double>() { 0.20 }, new List<double>() { 0.25 }, new List<double>() { 0.29 }, new List<double>() { 0.30 }, new List<double>() { 0.31 }, new List<double>() { 0.32 }, new List<double>() { 0.33 }, new List<double>() { 0.34 }, new List<double>() { 0.35 }, new List<double>() { 0.36 }, new List<double>() { 0.40 }, new List<double>() { 0.45}, new List<double>() { 0.50 }, new List<double>() { 0.55 }, new List<double>() { 0.60 }, new List<double>() { 0.65 }, new List<double>() { 0.70 }, new List<double>() { 0.75 }, new List<double>() { 0.80 }, new List<double>() { 0.85 }, new List<double>() { 0.90 } },
                TestOutputs = null
            };
        }
    }
}

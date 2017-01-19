using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SideBetAdjustmentsModule")]
    [Serializable]
    public class SideBetAdjustmentsModule : AdjustmentsModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }
        public SideBetAdjustmentsModuleInputs SideBetAdjustmentsModuleInputs { get { return (SideBetAdjustmentsModuleInputs)GameModuleInputs; } }
        public SideBetAdjustmentsModuleProgress SideBetProgress { get { return (SideBetAdjustmentsModuleProgress)GameModuleProgress; } }

        public enum SideBetDecisions
        {
            PChallengesD,
            DChallengesP
        }


        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "SideBetPostDecision")
            {
                PostDecisionProcessing();
            }
            else
                MakeDecisions();
        }

        public void MakeDecisions()
        {
            if (LitigationGame.DisputeContinues() || SideBetProgress.StartedModule)
            {
                if (Game.CurrentDecisionIndexWithinActionGroup == (int)SideBetDecisions.PChallengesD && Game.PreparationPhase)
                {
                    SideBetProgress.StartedModule = true;
                    SpecifyInputs(GetInputs(true));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SideBetDecisions.PChallengesD && !Game.PreparationPhase)
                {
                    SideBetProgress.PChallengesD = Calculate() > 0; // outcome is bipolar
                    if (Game.CurrentlyEvolvingModule == this && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup != (int)SideBetDecisions.PChallengesD)
                        SideBetProgress.PChallengesD = false;
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SideBetDecisions.DChallengesP && Game.PreparationPhase)
                {
                    SpecifyInputs(GetInputs(false));
                }
                else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SideBetDecisions.DChallengesP && !Game.PreparationPhase)
                {
                    SideBetProgress.DChallengesP = Calculate() > 0; // outcome is bipolar
                    if (Game.CurrentlyEvolvingModule == this && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup != (int)SideBetDecisions.DChallengesP)
                    {
                        SideBetProgress.DChallengesP = false;
                    }
                }
                
            }
        }

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            List<Tuple<string, string>> pNAndA, dNAndA;
            LitigationGame.BargainingModule.GetInputNamesAndAbbreviationsForBargaining(out pNAndA, out dNAndA);
            if (decisionNumberWithinActionGroup == 0)
                return pNAndA;
            else
                return dNAndA;
        }

        public List<double> GetInputs(bool plaintiff)
        {
            List<double> pInputs, dInputs;
            LitigationGame.BargainingModule.GetInputsForBargaining(out pInputs, out dInputs);
            if (plaintiff)
                return pInputs;
            else
                return dInputs;
        }

        private void PostDecisionProcessing()
        {
            if (SideBetProgress.SideBets == null)
                SideBetProgress.SideBets = new List<LitigationSideBet>();
            AddSideBetsToProgress(SideBetProgress.PChallengesD, SideBetProgress.DChallengesP);
        }

        private void AddSideBetsToProgress(bool pChallengedD, bool dChallengedP)
        {
            if (SideBetAdjustmentsModuleInputs.DoubleChallengeCountsAsSingleChallenge && pChallengedD && dChallengedP)
                SideBetProgress.SideBets.Add(new LitigationSideBet() { DPctOfDamagesClaimIfDWins = SideBetAdjustmentsModuleInputs.PctOfDamagesClaimChallengedPartyWouldReceive, PPctOfDamagesClaimIfPWins = SideBetAdjustmentsModuleInputs.PctOfDamagesClaimChallengedPartyWouldReceive });
            else
            {
                if (pChallengedD)
                    SideBetProgress.SideBets.Add(new LitigationSideBet() { DPctOfDamagesClaimIfDWins = SideBetAdjustmentsModuleInputs.PctOfDamagesClaimChallengedPartyWouldReceive, PPctOfDamagesClaimIfPWins = SideBetAdjustmentsModuleInputs.PctOfDamagesChallengerWouldReceive });
                if (dChallengedP)
                    SideBetProgress.SideBets.Add(new LitigationSideBet() { DPctOfDamagesClaimIfDWins = SideBetAdjustmentsModuleInputs.PctOfDamagesChallengerWouldReceive, PPctOfDamagesClaimIfPWins = SideBetAdjustmentsModuleInputs.PctOfDamagesClaimChallengedPartyWouldReceive });
            }
        }

        public override void AdjustDamagesAmounts(ref double ultimateDamagesIfPWins, ref double paymentFromPToDIfDWins)
        {
            double netPaymentFromDWhenPWins = 0, netPaymentFromDWhenDWins = 0;
            netPaymentFromDWhenPWins = SideBetProgress.SideBets.Sum(x => x.NetPaymentFromDToP(true, LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim));
            netPaymentFromDWhenDWins = SideBetProgress.SideBets.Sum(x => x.NetPaymentFromDToP(false, LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim));
            ultimateDamagesIfPWins += netPaymentFromDWhenPWins;
            paymentFromPToDIfDWins -= netPaymentFromDWhenDWins;
        }

        public override List<double> GetStatusValues()
        {
            if (SideBetProgress.StartedModule)
                return new List<double>() { SideBetProgress.PChallengesD ? 1.0 : -1.0, SideBetProgress.DChallengesP ? 1.0 : -1.0 };
            return new List<double>() { 0, 0 };
        }

        public override List<Tuple<string,string>> GetStatusValuesNamesAndAbbreviations()
        {
            return new List<Tuple<string, string>>() { new Tuple<string, string>("P challenges D", "PCD"), new Tuple<string, string>("D challenges P", "DCP") };
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            return -1.0;
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SideBetDecisions.PChallengesD)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SideBetDecisions.DChallengesP)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SideBetAdjustmentsModule copy = new SideBetAdjustmentsModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = SideBetAdjustmentsModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }


        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            bool isBeforeBargaining = true;

            decisions.Add(GetDecision("PChallengesD", "pcd", true));
            decisions.Add(GetDecision("DChallengesP","dcp",false));

            int adjustmentsModuleNumber = GetIntCodeGeneratorOption(options, "AdjustModNumber");

            return new SideBetAdjustmentsModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "AdjustmentsModule" + adjustmentsModuleNumber,
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                ActionsAtEndOfModule = new List<string> { "SideBetPostDecision" },
                Tags = new List<string> { "Adjustment" }
            };
        }


        private static Decision GetDecision(string name, string abbreviation, bool isPDecision)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InputAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = -1.0,
                    UpperBound = 1.0
                },
                Bipolar = true,
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
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="SideBetStrategy.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0.0, xMax = 1.0, yMin = -1.0, yMax = 1.0, xAxisLabel="Party's estimate of probability it would win", yAxisLabel="Decision whether to challenge opponent to bet", graphName="Parties' challenge decision", seriesName= (isPDecision ? "Plaintiff challenges defendant" : "Defendant challenges plaintiff"), replaceSeriesOfSameName=false, fadeSeriesOfSameName=true },
                        ReportAfterEachEvolutionStep = true
                    }
                },
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = isPDecision,
                UsePreviousVersionWhenOptimizingOtherDecisionInModule = isPDecision,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = false,
                TestInputs = null,
                TestInputsList = null, // new List<List<double>> { new List<double> { 0.45 }, new List<double> { 0.46 }, new List<double> { 0.47 }, new List<double> { 0.48 }, new List<double> { 0.49 }, new List<double> { 0.50 }, new List<double> { 0.51 } },
                TestOutputs = null, // new List<double> { -1, 1 } 
            };
        }
    }
}

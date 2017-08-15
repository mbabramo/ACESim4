using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "IndependentEstimatesModule")]
    [Serializable]
    public class IndependentEstimatesModule : ValueAndErrorForecastingModule, ICodeBasedSettingGenerator
    {
        public IndependentEstimatesModuleProgress IndependentEstimatesModuleProgress { get { return (IndependentEstimatesModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public IndependentEstimatesInputs IndependentEstimatesInputs { get { return (IndependentEstimatesInputs)GameModuleInputs; } }

        public bool IsProbabilityEstimate { get { return (bool)GameModuleSettings; } } 

        public enum IndependentEstimatesDecisions
        {
            CumulativeDistribution
        }

        public override void PostForecasting()
        {
            double valueToWhichNoiseIsAdded;
            if (IndependentEstimatesModuleProgress.IsProbabilityNotDamages)
            {
                int cumulativeDistributionNumber = 0;
                if (Game.MostRecentCumulativeDistributions == null)
                    throw new Exception("Trying to access cumulative distributions that have not been set. Possible causes: (1) A dummy decision needs to be added. (2) You may be running a report without having evolved the relevant needed decisions.");
                CumulativeDistribution cd = Game.MostRecentCumulativeDistributions[cumulativeDistributionNumber];
                // calculate what the correct probability estimate would be if the noise coincidentally were zero
                double outValue;
                IndependentEstimatesModuleProgress.ActualPIssueStrength = valueToWhichNoiseIsAdded = LitigationGame.DisputeGeneratorModule.DGProgress.EvidentiaryStrengthLiability;
                IndependentEstimatesModuleProgress.ActualPResultTransformed = LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(valueToWhichNoiseIsAdded);
            }
            else
            {
                IndependentEstimatesModuleProgress.ActualPIssueStrength = IndependentEstimatesModuleProgress.ActualPResultTransformed = valueToWhichNoiseIsAdded = LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWinsAsPctOfClaim;
            }
            
            IndependentEstimatesModuleProgress.PNoiseLevel = IndependentEstimatesModuleProgress.CurrentEquivalentPNoiseLevel = IndependentEstimatesInputs.PNoiseLevel;
            IndependentEstimatesModuleProgress.DNoiseLevel = IndependentEstimatesModuleProgress.CurrentEquivalentDNoiseLevel = IndependentEstimatesInputs.DNoiseLevel;
            IndependentEstimatesModuleProgress.PlaintiffProxy = valueToWhichNoiseIsAdded + IndependentEstimatesInputs.PNoiseRealized;
            IndependentEstimatesModuleProgress.DefendantProxy = valueToWhichNoiseIsAdded + IndependentEstimatesInputs.DNoiseRealized;
            // the following seems to be extremely rare; not sure why it happens at all
            if (IndependentEstimatesModuleProgress.PlaintiffProxy > 1E100 || IndependentEstimatesModuleProgress.PlaintiffProxy < -1E100)
                IndependentEstimatesModuleProgress.PlaintiffProxy = valueToWhichNoiseIsAdded;
            if (IndependentEstimatesModuleProgress.DefendantProxy > 1E100 || IndependentEstimatesModuleProgress.DefendantProxy < -1E100)
                IndependentEstimatesModuleProgress.DefendantProxy = valueToWhichNoiseIsAdded;
            GetForecastFromProxy((double)IndependentEstimatesModuleProgress.PlaintiffProxy, (double)IndependentEstimatesModuleProgress.PNoiseLevel, PartyOfForecast.Plaintiff, out IndependentEstimatesModuleProgress.PEstimatePResult, out IndependentEstimatesModuleProgress.PEstimatePError);

            if (IndependentEstimatesModuleProgress.PEstimatePResult == null)
                throw new Exception();
            GetForecastFromProxy((double)IndependentEstimatesModuleProgress.DefendantProxy, (double)IndependentEstimatesModuleProgress.DNoiseLevel, PartyOfForecast.Defendant, out IndependentEstimatesModuleProgress.DEstimateDResult, out IndependentEstimatesModuleProgress.DEstimateDError);
            IndependentEstimatesModuleProgress.DEstimateDResult = IndependentEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - IndependentEstimatesModuleProgress.DEstimateDResult : 0.0 - IndependentEstimatesModuleProgress.DEstimateDResult;

            IndependentEstimatesModuleProgress.PEstimateDResult = IndependentEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - IndependentEstimatesModuleProgress.PEstimatePResult : 0.0 - IndependentEstimatesModuleProgress.PEstimatePResult;
            IndependentEstimatesModuleProgress.DEstimatePResult = IndependentEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - IndependentEstimatesModuleProgress.DEstimateDResult : 0.0 - IndependentEstimatesModuleProgress.DEstimateDResult;

            if (GameProgressLogger.LoggingOn)
            {
                GameProgressLogger.Log(String.Format("PNoiseLevel {0}; PProxy {1} = ActualVal {4} + PNoiseRealized {5} ==> PEstimatePResult {2} PEstimatePError {3}", IndependentEstimatesModuleProgress.PNoiseLevel, IndependentEstimatesModuleProgress.PlaintiffProxy, IndependentEstimatesModuleProgress.PEstimatePResult, IndependentEstimatesModuleProgress.PEstimatePError, valueToWhichNoiseIsAdded, IndependentEstimatesInputs.PNoiseRealized));
                GameProgressLogger.Log(String.Format("DNoiseLevel {0}; DProxy {1} = ActualVal {4} + DNoiseRealized {5} ==> DEstimateDResult {2} DEstimateDError {3}", IndependentEstimatesModuleProgress.DNoiseLevel, IndependentEstimatesModuleProgress.DefendantProxy, IndependentEstimatesModuleProgress.DEstimateDResult, IndependentEstimatesModuleProgress.DEstimateDError, valueToWhichNoiseIsAdded, IndependentEstimatesInputs.DNoiseRealized));
            }
        }

        public override double GetEstimateOfOtherError(double ownEstimate, double ownAbsoluteError, double otherNoiseLevel)
        {
            throw new NotImplementedException();
        }


        public override void GetForecastFromProxy(double proxy, double noiseLevel, PartyOfForecast partyOfForecast, out double valueEstimate, out double errorEstimate)
        {
            int cumulativeDistributionNumber = IndependentEstimatesModuleProgress.IsProbabilityNotDamages ? 0 : 1;
            if (Game.MostRecentCumulativeDistributions == null)
                throw new Exception("Trying to access cumulative distributions that have not been set. Possible causes: (1) A dummy decision needs to be added. (2) You may be running a report without having evolved the relevant needed decisions.");
            CumulativeDistribution cd = Game.MostRecentCumulativeDistributions[cumulativeDistributionNumber];
            ValueFromSignalEstimator estimator;
            if (IsProbabilityEstimate)
                estimator = new ValueFromSignalEstimator(cd, LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWinsGivenDistributionOfEvidentiaryStrengthLiability);
            else
                estimator = new ValueFromSignalEstimator(cd);
            bool onlyAddBiasWhenNotEvolving = true;
            double addToProxy = 0;
            if (!onlyAddBiasWhenNotEvolving || Game.CurrentlyEvolving == false)
               addToProxy = IndependentEstimatesInputs.BiasAffectingEntireLegalSystem + ((partyOfForecast == PartyOfForecast.Plaintiff) ? IndependentEstimatesInputs.BiasAffectingPlaintiff : IndependentEstimatesInputs.BiasAffectingDefendant);
            estimator.AddSignal(new SignalOfValue() { Signal = proxy + addToProxy, StandardDeviationOfErrorTerm = noiseLevel });

            estimator.UpdateSummaryStatistics();
            valueEstimate = estimator.ExpectedValueOrProbability(IsProbabilityEstimate);
            errorEstimate = estimator.StandardDeviationOfExpectedValue;
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Initial forecast for " + (partyOfForecast == PartyOfForecast.Plaintiff ? "P" : "D") + " based on signal " + proxy + " with noise level " + noiseLevel + " ==> estimate " + valueEstimate + " stdev " + errorEstimate);
            if (partyOfForecast == PartyOfForecast.Plaintiff)
                IndependentEstimatesModuleProgress.PEstimator = estimator;
            else if (partyOfForecast == PartyOfForecast.Defendant)
                IndependentEstimatesModuleProgress.DEstimator = estimator;
        }

        public override double GetInitialProxyForWhetherPartyExpectsToWin(PartyOfForecast partyOfForecast)
        {
            if (partyOfForecast == PartyOfForecast.Plaintiff)
                return (double) IndependentEstimatesModuleProgress.PlaintiffProxy;
            else
                return (ForecastingProgress.IsProbabilityNotDamages ? 1.0 : 0.0) - (double) IndependentEstimatesModuleProgress.DefendantProxy;
        }

        public override void UpdateCombinedForecasts(double independentPNoiseLevel, double independentDNoiseLevel)
        {
            double originalCurrentEquivalentDNoiseLevel = (double)ForecastingProgress.CurrentEquivalentDNoiseLevel;
            double originalCurrentEquivalentPNoiseLevel = (double)ForecastingProgress.CurrentEquivalentPNoiseLevel;
            double pProxyForPResult = GetAdditionalProxy(independentPNoiseLevel, true);
            double dProxyForPResult = GetAdditionalProxy(independentDNoiseLevel, false);
            GetCombinedForecastUsingUnderlyingDistribution(originalCurrentEquivalentPNoiseLevel, pProxyForPResult, independentPNoiseLevel, PartyOfForecast.Plaintiff, out ForecastingProgress.PEstimatePResult, out ForecastingProgress.PEstimatePError, out ForecastingProgress.CurrentEquivalentPNoiseLevel);
            GetCombinedForecastUsingUnderlyingDistribution(originalCurrentEquivalentDNoiseLevel, dProxyForPResult, independentDNoiseLevel, PartyOfForecast.Defendant, out ForecastingProgress.DEstimatePResult, out ForecastingProgress.DEstimateDError, out ForecastingProgress.CurrentEquivalentDNoiseLevel); 
            SetEstimatesOfDResult();
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Updated P and D forecasts of P result: " + ForecastingProgress.PEstimatePResult + ", " + ForecastingProgress.DEstimatePResult);
            //ForecastingProgress.PEstimateDError = GetEstimateOfOtherError((double)ForecastingProgress.PEstimatePResult, (double)ForecastingProgress.PEstimatePError, (double)ForecastingProgress.CurrentEquivalentDNoiseLevel);
            //ForecastingProgress.DEstimatePError = GetEstimateOfOtherError((double)ForecastingProgress.DEstimateDResult, (double)ForecastingProgress.DEstimateDError, (double)ForecastingProgress.CurrentEquivalentPNoiseLevel);
        }

        private void SetEstimatesOfDResult()
        {
            ForecastingProgress.PEstimateDResult = ForecastingProgress.IsProbabilityNotDamages ? 1.0 - (double)ForecastingProgress.PEstimatePResult : 0.0 - (double)ForecastingProgress.PEstimatePResult;
            ForecastingProgress.DEstimateDResult = ForecastingProgress.IsProbabilityNotDamages ? 1.0 - (double)ForecastingProgress.DEstimatePResult : 0.0 - (double)ForecastingProgress.DEstimatePResult;
        }

        public void UpdateResultsAndErrors()
        {
            ValueFromSignalEstimator p = GetValueFromSignalEstimator(PartyOfForecast.Plaintiff);
            ForecastingProgress.PEstimatePResult = p.ExpectedValueOrProbability(IsProbabilityEstimate);
            ForecastingProgress.PEstimatePError = p.StandardDeviationOfExpectedValue;
            ValueFromSignalEstimator d = GetValueFromSignalEstimator(PartyOfForecast.Defendant);
            ForecastingProgress.DEstimatePResult = d.ExpectedValueOrProbability(IsProbabilityEstimate);
            ForecastingProgress.DEstimateDError = d.StandardDeviationOfExpectedValue;
            SetEstimatesOfDResult();
        }

        public void GetLatestEstimates(out double? pEstimatePResult, out double? dEstimatePResult)
        {
            ValueFromSignalEstimator p = GetValueFromSignalEstimator(PartyOfForecast.Plaintiff);
            pEstimatePResult = p.ExpectedValueOrProbability(IsProbabilityEstimate);
            ValueFromSignalEstimator d = GetValueFromSignalEstimator(PartyOfForecast.Defendant);
            dEstimatePResult = d.ExpectedValueOrProbability(IsProbabilityEstimate);
        }

        // (Uncomment if uncommenting section below)
        //static string lastCurrentlyEvolvingDecisionOfParty = ""; 
        public void GetCombinedForecastUsingUnderlyingDistribution( double originalNoiseLevel, double independentProxy, double independentNoiseLevel, PartyOfForecast partyOfForecast, out double? newEstimate, out double? newError, out double? newNoiseLevel)
        {
            ValueFromSignalEstimator estimator = GetValueFromSignalEstimator(partyOfForecast);
            double addToProxy = 0;
            bool onlyAddBiasWhenNotEvolving = true; // if set to true, the signals will be interpreted based on the assumption that NEITHER party is biased. Thus, we're dealing with bias in a world in which neither party expects there to be any bias but some bias actually exists.
            if (!onlyAddBiasWhenNotEvolving || Game.CurrentlyEvolvingDecision == null)
            {
                // Initially planned strategy: 
                // We want to add the plaintiff's or defendant's bias term --- but only if NOT currently evolving a decision of that party.
                // That is, when we have a bias that affects one player, that player's behavior is based on the assumption that he or she is not biased,
                // though the player might still recognize the distribution of bias in the other player (or in the judge).
                // PROBLEM: Since the defendant's decisions are affected by the general existence of plaintiff bias, plaintiff's decisions are indirectly affected, to the extent that they are affected by defendant's decisions (i.e., in settlement). But maybe this is actually reasonable theoretically. Even if plaintiffs are generally biased, they will have a realistic sense of how defendants act, and to some extent that may affect how they act.
                // POSSIBLE FUTURE SOLUTION: When evolving plaintiff's decisions, we need the defendant to act as if plaintiff is NOT biased (and vice versa). But otherwise, we need the defendant to recognize the distribution of plaintiff bias. 
                Decision currentlyEvolvingDecision = Game.CurrentlyEvolvingDecision;
                bool currentlyEvolvingDecisionOfParty = currentlyEvolvingDecision != null && ((currentlyEvolvingDecision.DecisionTypeCode == "P" && partyOfForecast == PartyOfForecast.Plaintiff) || (currentlyEvolvingDecision.DecisionTypeCode == "D" && partyOfForecast == PartyOfForecast.Defendant));
                //uncomment below to make sure that each decision is correctly labeled as it is evolving.
                //string currentlyEvolvingDecisionOfParty = currentlyEvolvingDecision == null ? "NONE" : currentlyEvolvingDecision.DecisionTypeCode;
                //if (currentlyEvolvingDecisionOfParty != lastCurrentlyEvolvingDecisionOfParty)
                //{
                //    lastCurrentlyEvolvingDecisionOfParty = currentlyEvolvingDecisionOfParty;
                //    Debug.WriteLine("CED: " + lastCurrentlyEvolvingDecisionOfParty);
                //}
                addToProxy = IndependentEstimatesInputs.BiasAffectingEntireLegalSystem; // Everybody's signal is affected. Note that this essentially just adds noise for everyone in the entire legal system. So, if evidentiary quality is actually 0.05, there might be some chance that everyone estimates the signal as 0.4. Everyone will treat the signal as they would in a system without systematic bias. Is this the right way to do that? An alternative approach would be to add no bias in the interpretation of the distributions, but to allow for some error in the assessment of whether P should win. (We could do that without doing a special evolution.)
                if (!currentlyEvolvingDecisionOfParty)
                    addToProxy += ((partyOfForecast == PartyOfForecast.Plaintiff) ? IndependentEstimatesInputs.BiasAffectingPlaintiff : IndependentEstimatesInputs.BiasAffectingDefendant); // that is, the plaintiff's decisions will be evolved with no bias by the plaintiff, but the defendant's decisions will be affected by the general existence of plaintiff bias, though of course the defendant will not know the plaintiff's bias in any individual case. The reverse is also true. 
            }
            estimator.AddSignal(new SignalOfValue() { Signal = independentProxy + addToProxy, StandardDeviationOfErrorTerm = independentNoiseLevel });
            estimator.UpdateSummaryStatistics();
            newEstimate = estimator.ExpectedValueOrProbability(IsProbabilityEstimate);
            newError = estimator.StandardDeviationOfExpectedValue;
            newNoiseLevel = Math.Sqrt(1 / (1 / (originalNoiseLevel * originalNoiseLevel) + 1 / (independentNoiseLevel * independentNoiseLevel)));
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Combined forecast incorporating proxy " + independentProxy + " with noise level " + independentNoiseLevel + " for " + (partyOfForecast == PartyOfForecast.Plaintiff ? "P" : "D") + ": " + newEstimate);
        }

        public ValueFromSignalEstimator GetValueFromSignalEstimator(PartyOfForecast partyOfForecast)
        {
            ValueFromSignalEstimator estimator = null;
            if (partyOfForecast == PartyOfForecast.Plaintiff)
                estimator = IndependentEstimatesModuleProgress.PEstimator;
            else if (partyOfForecast == PartyOfForecast.Defendant)
                estimator = IndependentEstimatesModuleProgress.DEstimator;
            return estimator;
        }

        public override void UpdateUnderlyingDistribution()
        {
            if (Game.MostRecentCumulativeDistributions == null)
                return;
            CumulativeDistribution cd = Game.MostRecentCumulativeDistributions[IndependentEstimatesModuleProgress.IsProbabilityNotDamages ? 0 : 1];
            if (IndependentEstimatesModuleProgress.PEstimator != null)
            {
                IndependentEstimatesModuleProgress.PEstimator.UpdateUnderlyingDistribution(cd);
                IndependentEstimatesModuleProgress.DEstimator.UpdateUnderlyingDistribution(cd);
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("Updated cumulative distribution ==> p estimate: " + IndependentEstimatesModuleProgress.PEstimator.ExpectedValueOrProbability(IsProbabilityEstimate) + " d: " + IndependentEstimatesModuleProgress.DEstimator.ExpectedValueOrProbability(IsProbabilityEstimate));
                UpdateResultsAndErrors();
            }
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            throw new Exception("Internal error. There is no calculation to be called.");
        }

        public override void Score()
        {
            throw new Exception("Internal error. There is no calculation to be called.");
            // When calculating a comulative distribution, the score is just the value of the variable for which we are calculating the distribution
            //double valueBeingForecast = (double) IndependentEstimatesModuleProgress.ActualPResult;
            //Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, valueBeingForecast);
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            IndependentEstimatesModule copy = new IndependentEstimatesModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = IndependentEstimatesModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public override object GenerateSetting(string options)
        {
            bool includeCombinationDecisions = options.Contains("IncludeCombinationDecisions: true") || options.Contains("IncludeCombinationDecisions: TRUE");
            bool onlyOneRepetitionNeeded = options.Contains("OnlyOneRepetitionNeeded: true") || options.Contains("OnlyOneRepetitionNeeded: TRUE");

            bool isProbabilityEstimate = options.Contains("Probability");

            List<Decision> decisions = new List<Decision>();

            // decisions.Add(GetDecision(options.Contains("Probability") ? "ProbabilityCumulativeDistribution" : "DamagesCumulativeDistribution", "cd"));

            return new IndependentEstimatesModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeForecasting" },
                ActionsAtEndOfModule = new List<string>() { "AfterForecasting" },
                GameModuleName = isProbabilityEstimate ?  "ProbabilityForecastingModule" : "DamagesForecastingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                GameModuleSettings = isProbabilityEstimate
            };
        }

        //private static Decision GetDecision(string name, string abbreviation)
        //{
        //    return new Decision()
        //    {
        //        Name = name,
        //        Abbreviation = abbreviation,
        //        DummyDecisionRequiringNoOptimization = true, // IMPORTANT: This is a dummy decision designed just to trigger the cumulative distributions update
        //        DynamicNumberOfInputs = true,
        //        UseOversampling = true,
        //        SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
        //        InputAbbreviations = null,
        //        InputNames = null,
        //        StrategyBounds = new StrategyBounds()
        //        {
        //            LowerBound = 0.0,
        //            UpperBound = 1.0
        //        },
        //        Bipolar = false,
        //        StrategyGraphInfos = new List<StrategyGraphInfo>(),
        //        HighestIsBest = false,
        //        PhaseOutDefaultBehaviorOverRepetitions = 0,
        //        MaxEvolveRepetitions = 99999,
        //        PreservePreviousVersionWhenOptimizing = false,
        //        EvolveThisDecisionEvenWhenSkippingByDefault = false,
        //        ScoreRepresentsCorrectAnswer = false,
        //        TestInputs = null, // new List<double>() { 0.5, 0.07, 0.07 }, 
        //        TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5 }
        //    };
        //}



    }
}

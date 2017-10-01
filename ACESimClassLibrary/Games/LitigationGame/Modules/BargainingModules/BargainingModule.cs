using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingModule : GameModule
    {

        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }
        public BargainingModuleProgress BargainingProgress { get { return (BargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public BargainingInputs BargainingInputs { get { return (BargainingInputs)GameModuleInputs; } }

        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "BeforeBargaining")
                BargainingProgress.DisputeContinues = LitigationGame.DisputeContinues();
            if (BargainingProgress.DisputeContinues) // continue only if there is a dispute and a settlement either does not exist or the settlement variable has not yet been set
            {
                // In the bargaining phase, each party takes into account its estimate of the base probability, its uncertainty, its opponent's noise level, and both sides' litigation costs. Note that this can be repeated, so before executing it, we see if we have already reached a settlement.
                if (Game.CurrentActionPointName == "BeforeBargaining")
                {
                    //int decisionNumberOfLastDecision;
                    //CumulativeDistribution cdProbability = Game.Strategies[(int)IndependentEstimatesModuleProgress.CumulativeDistributionDecisionNumber].CumulativeDistributions[cumulativeDistributionNumber];
                    //LitigationGame.BaseProbabilityForecastingModule.UpdateUnderlyingDistribution(cdProbability);

                    //CumulativeDistribution cdDamages = Game.Strategies[(int)IndependentEstimatesModuleProgress.CumulativeDistributionDecisionNumber].CumulativeDistributions[cumulativeDistributionNumber];
                    // LitigationGame.BaseDamagesForecastingModule.UpdateUnderlyingDistribution(cdDamages); 
                    SharedPrebargainingSetup();
                    OtherPrebargainingSetup();
                }
                else if (Game.CurrentActionPointName == "AfterBargaining")
                {
                    DetermineSettlement();
                    if (BargainingProgress.SettlementExists == false)
                        LitigationGame.LitigationCostModule.RegisterSettlementFailure();
                }
                else
                {
                    MakeDecisionBasedOnBargainingInputs();
                }
                    
            }
        }

        public void SharedPrebargainingSetup()
        {
            GetBargainingRoundAndSubclaimNumbers(); 
        }

        public virtual void RegisterExtraInvestigationRound(double portionOfRound = 1.0, bool updatePartiesInformation = true)
        {
            LitigationGame.LitigationCostModule.RegisterExtraInvestigationRound(BargainingProgress.CurrentBargainingRoundIsFirstRepetition, BargainingProgress.CurrentBargainingRoundIsFirstRepetition, portionOfRound);
            if (updatePartiesInformation)
                LitigationGame.LitigationCostModule.UpdatePartiesInformation(BargainingProgress.CurrentBargainingRoundIsFirstRepetition);
        }

        public void GetBargainingRoundAndSubclaimNumbers()
        {

            var ceag = Game.CurrentlyEvolvingActionGroup;
            Game.CurrentActionGroup.GetRepetitionInfoForTag("Bargaining round", out BargainingProgress.CurrentBargainingRoundNumber, out BargainingProgress.CurrentBargainingRoundIsFirstRepetition, out BargainingProgress.CurrentBargainingRoundIsLastRepetition);


            if (ceag == Game.CurrentActionGroup)
            { // once we get to the action group we are evolving, then we set FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent; note that we don't need it until later for scoring, and we only need to do it once
                if (BargainingProgress.CurrentBargainingRoundIsFirstRepetition)
                    BargainingProgress.FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent = null;
                else
                {
                    int tagIndex = Game.CurrentActionGroup.Tags.IndexOf("Bargaining round");
                    BargainingProgress.FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent = ceag.FirstRepetitionCorrespondingToTag[tagIndex];
                }
            }
            if (BargainingProgress.CurrentBargainingRoundIsFirstRepetition)
                BargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent = null;
            else
            {
                int tagIndex = Game.CurrentActionGroup.Tags.IndexOf("Bargaining round");
                BargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent = Game.CurrentActionGroup.FirstRepetitionCorrespondingToTag[tagIndex];
            }
            bool isFirstRepetition, isLastRepetition;
            Game.CurrentActionGroup.GetRepetitionInfoForTag("Bargaining subclaim", out BargainingProgress.CurrentBargainingSubclaimNumber, out isFirstRepetition, out isLastRepetition);
            if (ceag == null)
            {
                BargainingProgress.CurrentlyEvolvingBargainingRoundNumber = null;
                BargainingProgress.CurrentlyEvolvingBargainingSubclaimNumber = null;
            }
            else
            {
                ceag.GetRepetitionInfoForTag("Bargaining round", out BargainingProgress.CurrentlyEvolvingBargainingRoundNumber, out isFirstRepetition, out isLastRepetition);
                ceag.GetRepetitionInfoForTag("Bargaining subclaim", out BargainingProgress.CurrentlyEvolvingBargainingSubclaimNumber, out isFirstRepetition, out isLastRepetition);
            }
        }

        public virtual void OtherPrebargainingSetup()
        {
            GetInputsForBargaining(out BargainingProgress.PDecisionInputs, out BargainingProgress.DDecisionInputs);
        }

        public virtual void DetermineSettlement()
        {
        }

        public void AddInputNamesAndAbbreviationsForNoiseLevel(List<Tuple<string, string>> namesAndAbbrevs, bool isPInputs, bool includeOwnNoiseLevel, bool includeOpponentNoiseLevel)
        {
            IndependentEstimatesInputs ieprob = null;
            if (LitigationGame.BaseProbabilityForecastingInputs is IndependentEstimatesInputs)
                ieprob = (IndependentEstimatesInputs)LitigationGame.BaseProbabilityForecastingInputs;
            IndependentEstimatesInputs iedamag = null;
            if (LitigationGame.BaseDamagesForecastingInputs is IndependentEstimatesInputs)
                iedamag = (IndependentEstimatesInputs)LitigationGame.BaseDamagesForecastingInputs;
            if (includeOwnNoiseLevel)
            {
                if (ieprob != null && isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Plaintiff noise level probability", "PNoiseProb")
                    );
                if (iedamag != null && isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Plaintiff noise level damages", "PNoiseDamag")
                    );
                if (ieprob != null && !isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Defendant noise level probability", "DNoiseProb")
                    );
                if (iedamag != null && !isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Defendant noise level damages", "DNoiseDamag")
                    );
            }
            if (includeOpponentNoiseLevel)
            {
                if (ieprob != null && isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Plaintiff estimate defendant noise level probability", "PEstDNoiseProb")
                    );
                if (iedamag != null && isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Plaintiff estimate defendant noise level damages", "PEstDNoiseDamag")
                    );
                if (ieprob != null && !isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Defendant estimate plaintiff noise level probability", "DEstPNoiseProb")
                    );
                if (iedamag != null && !isPInputs)
                    namesAndAbbrevs.Add(
                        new Tuple<string, string>("Defendant estimate plaintiff noise level damages", "DEstPNoiseDamag")
                    );
            }
        }

        public void AddInputsForNoiseLevel(List<double> inputs, bool isPInputs, bool includeOwnNoiseLevel, bool includeOpponentNoiseLevel)
        {
            IndependentEstimatesInputs ieprob = null;
            if (LitigationGame.BaseProbabilityForecastingInputs is IndependentEstimatesInputs)
                ieprob = (IndependentEstimatesInputs)LitigationGame.BaseProbabilityForecastingInputs;
            IndependentEstimatesInputs iedamag = null;
            if (LitigationGame.BaseDamagesForecastingInputs is IndependentEstimatesInputs)
                iedamag = (IndependentEstimatesInputs)LitigationGame.BaseDamagesForecastingInputs;
            if (includeOwnNoiseLevel)
            {
                if (ieprob != null && isPInputs)
                    inputs.Add(
                        ieprob.PNoiseLevel
                    );
                if (iedamag != null && isPInputs)
                    inputs.Add(
                        iedamag.PNoiseLevel
                    );
                if (ieprob != null && !isPInputs)
                    inputs.Add(
                        ieprob.DNoiseLevel
                    );
                if (iedamag != null && !isPInputs)
                    inputs.Add(
                        iedamag.DNoiseLevel
                    );
            }
            if (includeOpponentNoiseLevel)
            {
                if (ieprob != null && isPInputs)
                    inputs.Add(
                        ieprob.PEstimateDNoiseLevel
                    );
                if (iedamag != null && isPInputs)
                    inputs.Add(
                        iedamag.PEstimateDNoiseLevel
                    );
                if (ieprob != null && !isPInputs)
                    inputs.Add(
                        ieprob.DEstimatePNoiseLevel
                    );
                if (iedamag != null && !isPInputs)
                    inputs.Add(
                        iedamag.DEstimatePNoiseLevel
                    );
            }
        }

        public void GetInputNamesAndAbbreviationsForBargaining(out List<Tuple<string, string>> pNamesAndAbbrevs, out List<Tuple<string, string>> dNamesAndAbbrevs)
        {
            pNamesAndAbbrevs = new List<Tuple<string, string>>() 
                { 
                    new Tuple<string, string>("Plaintiff estimate probability of plaintiff win", "PEstProb"),
                    new Tuple<string, string>("Plaintiff estimate error in probability estimate", "PEstimateProbErr"),
                    new Tuple<string, string>("Plaintiff estimate of damages proportion if plaintiff wins", "PEstDamag"),
                    new Tuple<string, string>("Plaintiff estimate error in damages estimate", "PEstDamagErr"),
                    new Tuple<string, string>("Plaintiff anticipated litigation expenses if trial", "PAntLitigExp"),
                    new Tuple<string, string>("Defendant anticipated litigation expenses if trial", "DAntLitigExp"),
                    new Tuple<string, string>("Defendant offer in previous round", "DOfferPrevRound")
                };
            AddInputNamesAndAbbreviationsForNoiseLevel(pNamesAndAbbrevs, true, true, true);
            pNamesAndAbbrevs.AddRange(LitigationGame.Plaintiff.GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization());
            
            dNamesAndAbbrevs = new List<Tuple<string, string>>()
                { 
                    new Tuple<string, string>("Defendant estimate probability of defendant win", "DEstProbDWin"),
                    new Tuple<string, string>("Defendant estimate error in probability estimate", "DEstimateProbErr"),
                    new Tuple<string, string>("Defendant estimate of damages effect if plaintiff wins", "DEstDamag"),
                    new Tuple<string, string>("Defendant estimate error in damages estimate", "DEstDamagErr"),
                    new Tuple<string, string>("Defendant anticipated litigation expenses if trial", "DAntLitigExp"),
                    new Tuple<string, string>("Plaintiff anticipated litigation expenses if trial", "PAntLitigExp"),
                    new Tuple<string, string>("Plaintiff offer in previous round", "POfferPrevRound")
                };
            AddInputNamesAndAbbreviationsForNoiseLevel(dNamesAndAbbrevs, false, true, true);
            dNamesAndAbbrevs.AddRange(LitigationGame.Defendant.GetParametersNamesAndAbbreviationsForThisTypeOfUtilityMaxization());
            
            if (LitigationGame.LitigationCostModule is LitigationCostEndogenousEffortModule)
            {
                pNamesAndAbbrevs.Add(new Tuple<string, string>("PToDRatio", "PDRat"));
                dNamesAndAbbrevs.Add(new Tuple<string, string>("DToPRatio", "DPRat"));
            }
            pNamesAndAbbrevs.AddRange(LitigationGame.AdjustmentsModule1.GetStatusValuesNamesAndAbbreviations());
            pNamesAndAbbrevs.AddRange(LitigationGame.AdjustmentsModule2.GetStatusValuesNamesAndAbbreviations());
            dNamesAndAbbrevs.AddRange(LitigationGame.AdjustmentsModule1.GetStatusValuesNamesAndAbbreviations());
            dNamesAndAbbrevs.AddRange(LitigationGame.AdjustmentsModule2.GetStatusValuesNamesAndAbbreviations());
        }

        // Change these if changing order
        public const int BargainingInputIndexProbForecast = 0;
        public const int BargainingInputIndexProbForecastError = 1;
        public const int BargainingInputIndexDamagesForecast = 2;
        public const int BargainingInputIndexDamagesForecastError = 3;
        public const int BargainingInputNumIndexBeforeNoiseLevel = 7;
        public static int? LastNumberOfInputNamesAndAbbreviationsForBargaining = null;
        public static List<int> IndicesForPOwnNoiseLevel, IndicesForDOwnNoiseLevel;
        public static object LockObj = new object();
        public void GetInputsForBargainingWithCorrectInformationSubstituted(out List<double> pInputs, out List<double> dInputs, bool getFiftyPercentBaselineInstead = false)
        {
            GetInputsForBargaining(out pInputs, out dInputs);
            if (getFiftyPercentBaselineInstead)
            {
                pInputs[BargainingInputIndexProbForecast] = 0.5;
                dInputs[BargainingInputIndexProbForecast] = 0.5;
            }
            else
            {
                pInputs[BargainingInputIndexProbForecast] = LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(LitigationGame.DisputeGeneratorModule.DGProgress.EvidentiaryStrengthLiability);
                dInputs[BargainingInputIndexProbForecast] = 1.0 - pInputs[BargainingInputIndexProbForecast];
            }
            pInputs[BargainingInputIndexProbForecastError] = dInputs[BargainingInputIndexProbForecastError] = 0;
            pInputs[BargainingInputIndexDamagesForecast] = LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWinsAsPctOfClaim;
            dInputs[BargainingInputIndexDamagesForecast] = 0.0 - pInputs[BargainingInputIndexDamagesForecast];
            pInputs[BargainingInputIndexDamagesForecastError] = dInputs[BargainingInputIndexDamagesForecastError] = 0;
            if (IndicesForDOwnNoiseLevel == null)
            {
                lock (LockObj)
                {
                    if (IndicesForDOwnNoiseLevel == null)
                    {
                        List<Tuple<string,string>> pNAndA, dNAndA;
                        GetInputNamesAndAbbreviationsForBargaining(out pNAndA, out dNAndA);
                        IndicesForPOwnNoiseLevel = pNAndA.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Item.Item1.Contains("Plaintiff noise level")).Select(x => x.Index).ToList();
                        IndicesForDOwnNoiseLevel = dNAndA.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Item.Item1.Contains("Defendant noise level")).Select(x => x.Index).ToList();
                    }
                }
            }
            foreach (int i in IndicesForPOwnNoiseLevel)
                pInputs[i] = 0.0;
            foreach (int i in IndicesForDOwnNoiseLevel)
                dInputs[i] = 0.0;
        }

        public void GetInputsForBargaining(out List<double> pInputs, out List<double> dInputs)
        {
            double pOfferForDToConsiderInNextRound = 0, dOfferForPToConsiderInNextRound = 0;
            if (BargainingInputs.PartiesTakeIntoAccountPreviousOffer && BargainingProgress.POfferForDToConsiderInNextRound != null)
            {
                pOfferForDToConsiderInNextRound = (double)BargainingProgress.POfferForDToConsiderInNextRound;
                dOfferForPToConsiderInNextRound = (double)BargainingProgress.DOfferForPToConsiderInNextRound;
            }
            double pEstimateOwnProbabilityError = 0, dEstimateOwnProbabilityError = 0, pEstimateOwnDamagesError = 0, dEstimateOwnDamagesError = 0;
            if (BargainingInputs.PartiesConsiderAccuracyOfOwnEstimates)
            {
                pEstimateOwnProbabilityError = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePError;
                dEstimateOwnProbabilityError = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimateDError;
                pEstimateOwnDamagesError = (double)LitigationGame.BaseDamagesForecastingModule.ForecastingProgress.PEstimatePError;
                dEstimateOwnDamagesError = (double)LitigationGame.BaseDamagesForecastingModule.ForecastingProgress.DEstimateDError;
            }
            double pProbForecast, pDamagesForecast, dProbForecast, dDamagesForecast;
            bool useInitialProxiesOnly = false; 
            if (useInitialProxiesOnly)
            {
                pProbForecast = (double)LitigationGame.BaseProbabilityForecastingModule.GetInitialProxyForWhetherPartyExpectsToWin(partyOfForecast: ValueAndErrorForecastingModule.PartyOfForecast.Plaintiff);
                pDamagesForecast = (double)LitigationGame.BaseDamagesForecastingModule.GetInitialProxyForWhetherPartyExpectsToWin(partyOfForecast: ValueAndErrorForecastingModule.PartyOfForecast.Plaintiff);
                dProbForecast = (double)LitigationGame.BaseProbabilityForecastingModule.GetInitialProxyForWhetherPartyExpectsToWin(partyOfForecast: ValueAndErrorForecastingModule.PartyOfForecast.Defendant);
                dDamagesForecast = (double)LitigationGame.BaseDamagesForecastingModule.GetInitialProxyForWhetherPartyExpectsToWin(partyOfForecast: ValueAndErrorForecastingModule.PartyOfForecast.Defendant);
            }
            else
            {
                pProbForecast = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePResult;
                pDamagesForecast = (double)LitigationGame.BaseDamagesForecastingModule.ForecastingProgress.PEstimatePResult;
                dProbForecast = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimateDResult;
                dDamagesForecast = (double)LitigationGame.BaseDamagesForecastingModule.ForecastingProgress.DEstimateDResult;
            }
            List<double> pInputsList, dInputsList;
            pInputsList = new List<double> { pProbForecast, pEstimateOwnProbabilityError, pDamagesForecast, pEstimateOwnDamagesError, LitigationGame.LitigationCostModule.LitigationCostProgress.PAnticipatedTrialExpenses, LitigationGame.LitigationCostModule.LitigationCostProgress.DAnticipatedTrialExpenses, dOfferForPToConsiderInNextRound };
            List<double> pUtilityCalcParams = LitigationGame.Plaintiff.GetParametersForThisTypeOfUtilityMaximization();
            pInputsList.AddRange(pUtilityCalcParams);
            AddInputsForNoiseLevel(pInputsList, true, true, true);
            dInputsList = new List<double> { dProbForecast, dEstimateOwnProbabilityError,  dDamagesForecast, dEstimateOwnDamagesError, LitigationGame.LitigationCostModule.LitigationCostProgress.DAnticipatedTrialExpenses, LitigationGame.LitigationCostModule.LitigationCostProgress.PAnticipatedTrialExpenses, pOfferForDToConsiderInNextRound };
            List<double> dUtilityCalcParams = LitigationGame.Defendant.GetParametersForThisTypeOfUtilityMaximization();
            dInputsList.AddRange(dUtilityCalcParams);
            AddInputsForNoiseLevel(dInputsList, false, true, true);
            if (LitigationGame.LitigationCostModule is LitigationCostEndogenousEffortModule)
            {
                LitigationCostEndogenousEffortModule m = (LitigationCostEndogenousEffortModule) LitigationGame.LitigationCostModule;
                double pToDRatioExcludingInvestigationCosts = (m.LCEEProgress.PTrialPrep + m.LitigationCostEndogenousEffortInputs.CommonTrialExpenses) / (m.LCEEProgress.DTrialPrep + m.LitigationCostEndogenousEffortInputs.CommonTrialExpenses);
                pInputsList.Add(pToDRatioExcludingInvestigationCosts);
                dInputsList.Add(1.0 / pToDRatioExcludingInvestigationCosts);
            }
            List<double> adjustmentsModuleInputs1 = LitigationGame.AdjustmentsModule1.GetStatusValues();
            List<double> adjustmentsModuleInputs2 = LitigationGame.AdjustmentsModule2.GetStatusValues();
            pInputsList.AddRange(adjustmentsModuleInputs1);
            pInputsList.AddRange(adjustmentsModuleInputs2);
            dInputsList.AddRange(adjustmentsModuleInputs1);
            dInputsList.AddRange(adjustmentsModuleInputs2);

            pInputs = pInputsList;
            dInputs = dInputsList;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public virtual void MakeDecisionBasedOnBargainingInputs()
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class LitigationCostStandardInputs : LitigationCostInputs
    {
        [SwapInputSeeds("InvestigationExpensesIfDispute")]
        public double PInvestigationExpensesIfDispute;
        [SwapInputSeeds("InvestigationExpensesIfDispute")]
        public double DInvestigationExpensesIfDispute;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterEachMiddleBargainingRound")]
        public double PMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterEachMiddleBargainingRound")]
        public double DMarginalInvestigationExpensesAfterEachMiddleBargainingRound;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterFirstBargainingRound")]
        public double PMarginalInvestigationExpensesAfterFirstBargainingRound;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterFirstBargainingRound")]
        public double DMarginalInvestigationExpensesAfterFirstBargainingRound;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterLastBargainingRound")]
        public double PMarginalInvestigationExpensesAfterLastBargainingRound;
        [SwapInputSeeds("MarginalInvestigationExpensesAfterLastBargainingRound")]
        public double DMarginalInvestigationExpensesAfterLastBargainingRound;
        [SwapInputSeeds("SettlementFailureCostPerBargainingRound")]
        public double PSettlementFailureCostPerBargainingRound;
        [SwapInputSeeds("SettlementFailureCostPerBargainingRound")]
        public double DSettlementFailureCostPerBargainingRound;
        public double CommonTrialExpenses;
        [SwapInputSeeds("AdditionalTrialExpenses")]
        public double PAdditionalTrialExpenses;
        [SwapInputSeeds("AdditionalTrialExpenses")]
        public double DAdditionalTrialExpenses;
        public double TrialTaxEachParty;
        public double TrialTaxLoser;
        public double TrialTaxDLoser;
        public bool PartiesInformationImprovesOverTime;
        [SwapInputSeeds("NoiseLevelOfFirstIndependentInformation")]
        public double NoiseLevelOfPlaintiffFirstIndependentInformation;
        [SwapInputSeeds("NoiseLevelOfFirstIndependentInformation")]
        public double NoiseLevelOfDefendantFirstIndependentInformation;
        [SwapInputSeeds("NoiseLevelOfIndependentInformation")]
        public double NoiseLevelOfPlaintiffIndependentInformation;
        [SwapInputSeeds("NoiseLevelOfIndependentInformation")]
        public double NoiseLevelOfDefendantIndependentInformation;
        public bool LoserPaysRule;
        public bool LimitLoserPaysToOwnExpenses;
        public double MinimumMarginOfVictoryForLoserPays;
        public bool ApplyLoserPaysWhenCasesAbandoned;
        public double ApplyLoserPaysToSettlementsForLessThanThisProportionOfDamages;
        public double LoserPaysMultiple;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingGame2ProgressInfo : GameProgress
    {
        public double Player1NoiseLevel;
        public double Player1AbsNoiseRealized;
        public double Player2NoiseLevel;
        public double Player2AbsNoiseRealized;

        public double Player1FailCost;
        public double Player2FailCost;

        public double PlayersCombinedCosts;

        public double Player1ResultIfNegotiationFails;
        public double Player2ResultIfNegotiationFails;

        public double Player1ProxyForPlayer1;
        public double Player2ProxyForPlayer2;

        public double Player1EstimateOfPlayer1WithCosts;
        public double Player2EstimateOfPlayer2WithCosts;

        public double Player1EstimateOfOwnErrorForEachPlayer;
        public double Player2EstimateOfOwnErrorForEachPlayer; 
        public double Player1EstimateOfOpponentErrorForEachPlayer;
        public double Player2EstimateOfOpponentErrorForEachPlayer;

        public double AmountOfPiePlayer1InsistsOn;
        public double AmountOfPiePlayer2InsistsOn;
        public double HowFarApartOffersAre;
        public double? HowFarApartOffersAreWhenNegotiationFails;
        public double Player1OfferAboveEstimate;

        public double Player1EstimateOfPlayer1WithoutCosts;
        public double Player1EstimateOfPlayer2WithoutCosts;
        public double Player1EstimateOfPlayer2WithCosts;
        public double Player2EstimateOfPlayer2WithoutCosts;
        public double Player2EstimateOfPlayer1WithoutCosts;
        public double Player2EstimateOfPlayer1WithCosts;

        public double ProportionOfCostSavingsPlayer1InsistsOn;
        public double ProportionOfCostSavingsPlayer2InsistsOn;

        public double NegotiationSucceeds; // 1.0 or 0.0 (so that we can calculate statistics)
        public double Player1Result;
        public double Player2Result;

        public override GameProgress DeepCopy()
        {
            BargainingGame2ProgressInfo copy = new BargainingGame2ProgressInfo();

            copy.Player1NoiseLevel = this.Player1NoiseLevel;
            copy.Player1AbsNoiseRealized = this.Player1AbsNoiseRealized; 
            copy.Player2NoiseLevel = this.Player2NoiseLevel;
            copy.Player2AbsNoiseRealized = this.Player2AbsNoiseRealized;

            copy.Player1FailCost = this.Player1FailCost;
            copy.Player2FailCost = this.Player2FailCost;
            copy.PlayersCombinedCosts = this.PlayersCombinedCosts;

            copy.Player1ResultIfNegotiationFails = this.Player1ResultIfNegotiationFails;
            copy.Player2ResultIfNegotiationFails = this.Player2ResultIfNegotiationFails;

            copy.Player1ProxyForPlayer1 = this.Player1ProxyForPlayer1;
            copy.Player2ProxyForPlayer2 = this.Player2ProxyForPlayer2;

            copy.Player1EstimateOfPlayer1WithCosts = this.Player1EstimateOfPlayer1WithCosts;
            copy.Player2EstimateOfPlayer2WithCosts = this.Player2EstimateOfPlayer2WithCosts;

            copy.Player1EstimateOfOwnErrorForEachPlayer = this.Player1EstimateOfOwnErrorForEachPlayer;
            copy.Player2EstimateOfOwnErrorForEachPlayer = this.Player2EstimateOfOwnErrorForEachPlayer;
            copy.Player1EstimateOfOpponentErrorForEachPlayer = this.Player1EstimateOfOpponentErrorForEachPlayer;
            copy.Player2EstimateOfOpponentErrorForEachPlayer = this.Player2EstimateOfOpponentErrorForEachPlayer;

            copy.AmountOfPiePlayer1InsistsOn = this.AmountOfPiePlayer1InsistsOn;
            copy.AmountOfPiePlayer2InsistsOn = this.AmountOfPiePlayer2InsistsOn;
            copy.HowFarApartOffersAre = this.HowFarApartOffersAre;
            copy.Player1OfferAboveEstimate = this.Player1OfferAboveEstimate;

            copy.Player1EstimateOfPlayer1WithoutCosts = this.Player1EstimateOfPlayer1WithoutCosts;
            copy.Player1EstimateOfPlayer2WithoutCosts = this.Player1EstimateOfPlayer2WithoutCosts;
            copy.Player1EstimateOfPlayer2WithCosts = this.Player1EstimateOfPlayer2WithCosts;
            copy.Player2EstimateOfPlayer2WithoutCosts = this.Player2EstimateOfPlayer2WithoutCosts;
            copy.Player2EstimateOfPlayer1WithoutCosts = this.Player2EstimateOfPlayer1WithoutCosts;
            copy.Player2EstimateOfPlayer1WithCosts = this.Player2EstimateOfPlayer1WithCosts;

            copy.ProportionOfCostSavingsPlayer1InsistsOn = this.ProportionOfCostSavingsPlayer1InsistsOn;
            copy.ProportionOfCostSavingsPlayer2InsistsOn = this.ProportionOfCostSavingsPlayer2InsistsOn;

            copy.NegotiationSucceeds = this.NegotiationSucceeds;
            copy.Player1Result = this.Player1Result;
            copy.Player2Result = this.Player2Result;

            base.CopyFieldInfo(copy);

            return copy;
        }
    }
}

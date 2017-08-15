using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingGame1ProgressInfo : GameProgressInfo
    {
        public double Player1ResultIfNegotiationFails;
        public double Player2ResultIfNegotiationFails;

        public double Player1ProxyForPlayer1;
        public double Player1ProxyForPlayer2;
        public double Player2ProxyForPlayer1;
        public double Player2ProxyForPlayer2;

        public double ObfuscationForSelf;
        public double ObfuscationForOpponent;

        public double OfuscationRealizedOfPlayer1ResultForPlayer1;
        public double OfuscationRealizedOfPlayer1ResultForPlayer2;
        public double OfuscationRealizedOfPlayer2ResultForPlayer1;
        public double OfuscationRealizedOfPlayer2ResultForPlayer2;

        public double Player1EstimateOfPlayer1;
        public double Player1EstimateOfPlayer2;
        public double Player2EstimateOfPlayer1;
        public double Player2EstimateOfPlayer2;

        public double Player1EstimateOfPlayer1Error;
        public double Player1EstimateOfPlayer2Error;
        public double Player2EstimateOfPlayer1Error;
        public double Player2EstimateOfPlayer2Error;

        public double AmountOfPiePlayer1InsistsOn;
        public double AmountOfPiePlayer2InsistsOn;
        public double HowFarApartOffersAre;
        public double Player1OfferAboveEstimate;

        public double Player1PerceivedBargainingRoom;
        public double Player2PerceivedBargainingRoom;
        public double Player1PctOfPerceivedBargainingRoomInsistedOn;

        public double NegotiationSucceeds; // 1.0 or 0.0 (so that we can calculate statistics)
        public double Player1Result;
        public double Player2Result;

        public override GameProgressInfo DeepCopy()
        {
            BargainingGame1ProgressInfo copy = new BargainingGame1ProgressInfo();

            copy.GameComplete = this.GameComplete;
            copy.CurrentDecisionNumber = this.CurrentDecisionNumber;
            copy.PreparedThroughDecisionNumber = this.PreparedThroughDecisionNumber;

            copy.Player1ResultIfNegotiationFails = this.Player1ResultIfNegotiationFails;
            copy.Player2ResultIfNegotiationFails = this.Player2ResultIfNegotiationFails;
            copy.Player1ProxyForPlayer1 = this.Player1ProxyForPlayer1;
            copy.Player1ProxyForPlayer2 = this.Player1ProxyForPlayer2;
            copy.Player2ProxyForPlayer1 = this.Player2ProxyForPlayer1;
            copy.Player2ProxyForPlayer2 = this.Player2ProxyForPlayer2;
            copy.Player1EstimateOfPlayer1 = this.Player1EstimateOfPlayer1;
            copy.Player1EstimateOfPlayer2 = this.Player1EstimateOfPlayer2;
            copy.Player2EstimateOfPlayer1 = this.Player2EstimateOfPlayer1;
            copy.Player2EstimateOfPlayer2 = this.Player2EstimateOfPlayer2;
            copy.Player1EstimateOfPlayer1Error = this.Player1EstimateOfPlayer1Error;
            copy.Player1EstimateOfPlayer2Error = this.Player1EstimateOfPlayer2Error;
            copy.Player2EstimateOfPlayer1Error = this.Player2EstimateOfPlayer1Error;
            copy.Player2EstimateOfPlayer2Error = this.Player2EstimateOfPlayer2Error;
            copy.AmountOfPiePlayer1InsistsOn = this.AmountOfPiePlayer1InsistsOn;
            copy.AmountOfPiePlayer2InsistsOn = this.AmountOfPiePlayer2InsistsOn;
            copy.HowFarApartOffersAre = this.HowFarApartOffersAre;
            copy.Player1OfferAboveEstimate = this.Player1OfferAboveEstimate;
            copy.Player1PerceivedBargainingRoom = this.Player1PerceivedBargainingRoom;
            copy.Player2PerceivedBargainingRoom = this.Player2PerceivedBargainingRoom;
            copy.Player1PctOfPerceivedBargainingRoomInsistedOn = this.Player1PctOfPerceivedBargainingRoomInsistedOn;
            copy.NegotiationSucceeds = this.NegotiationSucceeds;
            copy.Player1Result = this.Player1Result;
            copy.Player2Result = this.Player2Result;

            return copy;
        }
    }
}

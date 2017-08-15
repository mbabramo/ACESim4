using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class BargainingGame1 : Game
    {
        Strategy PreviousStrategy;

        public BargainingGame1ProgressInfo BG1P { get { return (BargainingGame1ProgressInfo)Progress; } }
        public BargainingGame1Inputs BG1I { get { return (BargainingGame1Inputs)GameInputs; } }

        public BargainingGame1(Strategy previousVersionOfStrategyIfAny)
        {
            PreviousStrategy = previousVersionOfStrategyIfAny;

        }
        

        public override void PrepareForCurrentDecision()
        {
            if (Progress.CurrentDecisionNumber == 0)
            { // we're just getting started; calculate some variables we'll need elsewhere
                // actual result
               BG1P.Player1ResultIfNegotiationFails = (1.0 - BG1I.wastePct) * BG1I.player1PctOfNotWasted;
               BG1P.Player2ResultIfNegotiationFails = (1.0 - BG1I.wastePct) * (1.0 - BG1I.player1PctOfNotWasted);

               BG1P.ObfuscationForSelf = BG1I.obfuscationLevelForSelf;
               BG1P.ObfuscationForOpponent = BG1I.obfuscationLevelForOpponent;

               BG1P.OfuscationRealizedOfPlayer1ResultForPlayer1 = BG1I.obfuscationRealizedOfPlayer1ResultForPlayer1;
               BG1P.OfuscationRealizedOfPlayer1ResultForPlayer2 = BG1I.obfuscationRealizedOfPlayer1ResultForPlayer2;
               BG1P.OfuscationRealizedOfPlayer2ResultForPlayer1 = BG1I.obfuscationRealizedOfPlayer2ResultForPlayer1;
               BG1P.OfuscationRealizedOfPlayer2ResultForPlayer2 = BG1I.obfuscationRealizedOfPlayer2ResultForPlayer2;


                // proxies for player results
               BG1P.Player1ProxyForPlayer1 =BG1P.Player1ResultIfNegotiationFails + BG1I.obfuscationRealizedOfPlayer1ResultForPlayer1;
               BG1P.Player1ProxyForPlayer2 =BG1P.Player2ResultIfNegotiationFails + BG1I.obfuscationRealizedOfPlayer1ResultForPlayer2;
               BG1P.Player2ProxyForPlayer1 =BG1P.Player1ResultIfNegotiationFails + BG1I.obfuscationRealizedOfPlayer2ResultForPlayer1;
               BG1P.Player2ProxyForPlayer2 =BG1P.Player2ResultIfNegotiationFails + BG1I.obfuscationRealizedOfPlayer2ResultForPlayer2;
            }
            else if (Progress.CurrentDecisionNumber == 4)
            { // we've already finished evolving player 1's estimate of its own results (including accuracy of that estimate) and its opponent's, and now we're going to get ready to play the rest of the game by using the strategies evolved to figure out player 2's corresponding estimates. (We can do this because the approach is symmetrical; the two players may have different levels of information, but each player knows the quality of its information.) We do this now so that we don't need to do it repetitively in evolution of player 2.
                double[] player2Inputs = new double[] {BG1P.Player2ProxyForPlayer2, BG1I.obfuscationLevelForSelf,BG1P.Player2ProxyForPlayer1, BG1I.obfuscationLevelForOpponent };
               BG1P.Player2EstimateOfPlayer2 = Strategies[0].Calculate(player2Inputs);
               BG1P.Player2EstimateOfPlayer2Error = Strategies[1].Calculate(player2Inputs);
               BG1P.Player2EstimateOfPlayer1 = Strategies[2].Calculate(player2Inputs);
               BG1P.Player2EstimateOfPlayer1Error = Strategies[3].Calculate(player2Inputs);

               BG1P.Player1PerceivedBargainingRoom = 1.0 -BG1P.Player1EstimateOfPlayer1 -BG1P.Player1EstimateOfPlayer2;
               BG1P.Player2PerceivedBargainingRoom = 1.0 -BG1P.Player2EstimateOfPlayer1 -BG1P.Player2EstimateOfPlayer2;

                // Let's use the previous strategy evolved for the next decision (i.e., the decision of how much of the pie to insist on) to determine player 2's move.
                if (PreviousStrategy == null || !PreviousStrategy.StrategyDevelopmentInitiated) // this is the default strategy before we've done any evolution -- player 2 insists roughly on 25% of the surplus from bargaining. 
                   BG1P.AmountOfPiePlayer2InsistsOn = BG1P.Player2EstimateOfPlayer2 + 0.25 *BG1P.Player2PerceivedBargainingRoom;
                else
                   BG1P.AmountOfPiePlayer2InsistsOn = PreviousStrategy.Calculate(new double[] {BG1P.Player2EstimateOfPlayer2,BG1P.Player2EstimateOfPlayer2Error,BG1P.Player2EstimateOfPlayer1,BG1P.Player2EstimateOfPlayer1Error });
            }
        }

        /// <summary>
        /// If game play is completed, then gameSettings.gameComplete should be set to true. 
        /// </summary>
        public override void MakeCurrentDecision()
        {
            if (Progress.GameComplete)
                return;

            double score = 0;
            switch (Progress.CurrentDecisionNumber)
            {
                case 0:
                    score = Forecast.Score(Strategies[0], GetDecisionInputs(),BG1P.Player1ResultIfNegotiationFails, out BG1P.Player1EstimateOfPlayer1);
                    break;
                case 1:
                    score = Forecast.Score(Strategies[1], GetDecisionInputs(), Math.Abs(((BargainingGame1ProgressInfo)Progress).Player1EstimateOfPlayer1 -BG1P.Player1ResultIfNegotiationFails), out BG1P.Player1EstimateOfPlayer1Error);
                    break;
                case 2:
                    score = Forecast.Score(Strategies[2], GetDecisionInputs(),BG1P.Player2ResultIfNegotiationFails, out BG1P.Player1EstimateOfPlayer2);
                    break;
                case 3:
                    score = Forecast.Score(Strategies[3], GetDecisionInputs(), Math.Abs(((BargainingGame1ProgressInfo)Progress).Player1EstimateOfPlayer2 -BG1P.Player2ResultIfNegotiationFails), out BG1P.Player1EstimateOfPlayer2Error);
                    break;

                case 4:
                    double amountOfPiePlayer2CouldLiveWithout = 1.0 -BG1P.AmountOfPiePlayer2InsistsOn;
                   BG1P.AmountOfPiePlayer1InsistsOn = MakeDecision(); // this is the amount of the pie that player 1 insists on giving
                   BG1P.Player1PctOfPerceivedBargainingRoomInsistedOn = (((BargainingGame1ProgressInfo)Progress).AmountOfPiePlayer1InsistsOn -BG1P.Player1EstimateOfPlayer1) /BG1P.Player1PerceivedBargainingRoom;
                
                    // If the bargaining offers cross, we settle in middle.
                   BG1P.HowFarApartOffersAre =BG1P.AmountOfPiePlayer1InsistsOn - amountOfPiePlayer2CouldLiveWithout;
                   BG1P.NegotiationSucceeds =BG1P.HowFarApartOffersAre < 0 ? 1.0 : 0.0; // offers are negatively far apart if we have a range of agreement
                    if (((BargainingGame1ProgressInfo)Progress).NegotiationSucceeds == 1.0)
                    {
                       BG1P.Player1Result = (((BargainingGame1ProgressInfo)Progress).AmountOfPiePlayer1InsistsOn + amountOfPiePlayer2CouldLiveWithout) / 2.0;
                       BG1P.Player2Result = 1.0 -BG1P.Player1Result;
                    }
                    else
                    {
                       BG1P.Player1Result =BG1P.Player1ResultIfNegotiationFails;
                       BG1P.Player2Result =BG1P.Player2ResultIfNegotiationFails;
                    }

                    Score(Progress.CurrentDecisionNumber.Value,BG1P.Player1Result);
                    Progress.GameComplete = true;
                    break;
            }

            if (CurrentlyEvolvingDecisionNum != null && CurrentlyEvolvingDecisionNum < 4 && CurrentlyEvolvingDecisionNum == Progress.CurrentDecisionNumber)
            { // abort the evolution process early
                Score((int)Progress.CurrentDecisionNumber, score);
                Progress.GameComplete = true;
            }
        }

        protected override double[] GetDecisionInputs()
        {
            double[] inputs = null;
            int decisionNumber = (int)Progress.CurrentDecisionNumber;
            if (Progress.CurrentDecisionNumber < 4)
                inputs = new double[] {BG1P.Player1ProxyForPlayer1, BG1I.obfuscationLevelForSelf,BG1P.Player1ProxyForPlayer2, BG1I.obfuscationLevelForOpponent };
            // Uncomment to make it only two inputs  inputs = new double[] {BG1P.Player1ProxyForPlayer1, BG1I.obfuscationLevelForSelf };
            else
                inputs = new double[] {BG1P.Player1EstimateOfPlayer1,BG1P.Player1EstimateOfPlayer1Error,BG1P.Player1EstimateOfPlayer2,BG1P.Player1EstimateOfPlayer2Error };

            if (Progress.CurrentDecisionNumber == RecordInputsForDecisionNum && !PreparationPhase)
                RecordedInputs.Add(inputs);
            return inputs;
        }
    }
}

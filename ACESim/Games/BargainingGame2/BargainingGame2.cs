using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class BargainingGame2 : Game
    {
        public BargainingGame2ProgressInfo BG2P { get { return (BargainingGame2ProgressInfo)Progress; } }
        public BargainingGame2Inputs BG2I { get { return (BargainingGame2Inputs)GameInputs; } }

        public enum BargainingGame2Decisions
        {
            ownResultIncludingCosts = 0,
            ownErrorEstimatingResultIncludingCosts = 1,
            opponentErrorEstimatingResultIncludingCosts = 2,
            decisionOnThresholdForAgreement = 3,
            adjustmentToAvoidOverfitting = 4
        }

        public BargainingGame2()
        {
        }

        static bool symmetryTestingOn = false; // can be helpful during development (may wish to delete)
        static bool symmetryTestingFirstOfPair = false;
        static BargainingGame2Inputs game2InputsFirstOfPair;
        static BargainingGame2ProgressInfo progressFirstOfPair;
        public override void PrepareForCurrentDecision()
        {
            if (CurrentDecisionIndex == (int) BargainingGame2Decisions.ownResultIncludingCosts)
            { // we're just getting started; calculate some variables we'll need elsewhere

                if (symmetryTestingOn)
                {
                    symmetryTestingFirstOfPair = !symmetryTestingFirstOfPair;
                    if (symmetryTestingFirstOfPair)
                        game2InputsFirstOfPair = BG2I;
                    else
                    {
                        BG2I.selfPctOfPie = 1.0 - game2InputsFirstOfPair.selfPctOfPie;
                        BG2I.oppFailCost = game2InputsFirstOfPair.selfFailCost;
                        BG2I.selfFailCost = game2InputsFirstOfPair.oppFailCost;
                        BG2I.oppNoiseLevel = game2InputsFirstOfPair.selfNoiseLevel;
                        BG2I.selfNoiseLevel = game2InputsFirstOfPair.oppNoiseLevel;
                        BG2I.oppNoiseRealized = game2InputsFirstOfPair.selfNoiseRealized;
                        BG2I.selfNoiseRealized = game2InputsFirstOfPair.oppNoiseRealized;
                    }
                }

                BG2P.Player1NoiseLevel = BG2I.selfNoiseLevel;
                BG2P.Player1AbsNoiseRealized = Math.Abs(BG2I.selfNoiseRealized);
                BG2P.Player2NoiseLevel = BG2I.oppNoiseLevel;
                BG2P.Player2AbsNoiseRealized = Math.Abs(BG2I.oppNoiseRealized);

                //System.Diagnostics.Debug.WriteLine(BG2I.selfNoiseLevel + " " + BG2I.selfNoiseRealized + " " + BG2I.oppNoiseLevel + " " + BG2I.oppNoiseRealized + " ");

                // actual result
                BG2P.Player1ResultIfNegotiationFails = BG2I.selfPctOfPie - BG2I.selfFailCost;
                BG2P.Player2ResultIfNegotiationFails = (1.0 - BG2I.selfPctOfPie) - BG2I.oppFailCost;
                BG2P.PlayersCombinedCosts = BG2I.selfFailCost + BG2I.oppFailCost;
                BG2P.Player1FailCost = BG2I.selfFailCost;
                BG2P.Player2FailCost = BG2I.oppFailCost;

                // proxies for player results
                BG2P.Player1ProxyForPlayer1 = BG2P.Player1ResultIfNegotiationFails + BG2I.selfNoiseRealized;
                BG2P.Player2ProxyForPlayer2 = BG2P.Player2ResultIfNegotiationFails + BG2I.oppNoiseRealized;
            }
            else if (CurrentDecisionIndex == (int)BargainingGame2Decisions.opponentErrorEstimatingResultIncludingCosts)
            {
                // we've already finished evolving player 1's estimate of its own results (including accuracy of that estimate) and its opponent's, and we can use this information to calculate comparable information for player 2
                List<double> player2Inputs = new List<double> { BG2P.Player2ProxyForPlayer2, BG2I.oppNoiseLevel };
                BG2P.Player2EstimateOfPlayer2WithCosts = Strategies[(int)BargainingGame2Decisions.ownResultIncludingCosts].Calculate(player2Inputs);
                BG2P.Player2EstimateOfOwnErrorForEachPlayer = Strategies[(int)BargainingGame2Decisions.ownErrorEstimatingResultIncludingCosts].Calculate(player2Inputs);
                
            }
            else if (CurrentDecisionIndex == (int)BargainingGame2Decisions.decisionOnThresholdForAgreement)
            {
                // now we're going to get ready to play the rest of the game by using the strategies evolved to figure out player 2's corresponding estimates. (We can do this because the approach is symmetrical; the two players may have different levels of information, but each player knows the quality of its information.) We do this now so that we don't need to do it repetitively in evolution of player 2.
                List<double> player2Inputs = new List<double> { BG2P.Player2ProxyForPlayer2, BG2I.selfNoiseLevel }; // now, player 2 uses player 1's noise level to estimate player 1's error
                BG2P.Player2EstimateOfOpponentErrorForEachPlayer = Strategies[(int)BargainingGame2Decisions.opponentErrorEstimatingResultIncludingCosts].Calculate(player2Inputs);

                BG2P.Player2EstimateOfPlayer2WithoutCosts = BG2P.Player2EstimateOfPlayer2WithCosts + BG2I.oppFailCost; // our estimate of player 2 subtracts the costs, so now we add it back
                BG2P.Player2EstimateOfPlayer1WithoutCosts = 1.0 - BG2P.Player2EstimateOfPlayer2WithoutCosts;
                BG2P.Player2EstimateOfPlayer1WithCosts = BG2P.Player2EstimateOfPlayer1WithoutCosts - BG2I.selfFailCost;

                Strategy PreviousStrategy = null, StrategyBeforeThat = null;

                if (CurrentlyEvolving)
                {
                    PreviousStrategy = Strategies[(int)BargainingGame2Decisions.decisionOnThresholdForAgreement].PreviousVersionOfThisStrategy;
                    StrategyBeforeThat = Strategies[(int)BargainingGame2Decisions.decisionOnThresholdForAgreement].VersionOfStrategyBeforePrevious;
                }

                // Let's use the previous strategy evolved for the next decision (i.e., the decision of how much of the pie to insist on) to determine player 2's move.
                if (CurrentlyEvolving && (PreviousStrategy == null || !PreviousStrategy.StrategyDevelopmentInitiated)) // this is the default strategy before we've done any evolution -- player 2 insists roughly on the specified percentage of the surplus from bargaining. Ideally, the initial specification shouldn't matter in the long term
                    BG2P.ProportionOfCostSavingsPlayer2InsistsOn = 0.65;
                else
                {
                    if (CurrentlyEvolving)
                    {
                        BG2P.ProportionOfCostSavingsPlayer2InsistsOn =
                            PreviousStrategy.Calculate(new List<double> { BG2P.Player2EstimateOfPlayer2WithCosts, BG2P.Player2EstimateOfPlayer1WithCosts, BG2P.Player2EstimateOfOwnErrorForEachPlayer, BG2P.Player2EstimateOfOpponentErrorForEachPlayer })
                            - Strategies[(int)BargainingGame2Decisions.adjustmentToAvoidOverfitting].Calculate(new List<double> { });
                        if (StrategyBeforeThat != null)
                        { // we want to use the average of the previous two strategies -- this prevents us from having an alternate set of even/odd strategies.
                            double sameCalculationWithStrategyBeforeThat = StrategyBeforeThat.Calculate(new List<double> { BG2P.Player2EstimateOfPlayer2WithCosts, BG2P.Player2EstimateOfPlayer1WithCosts, BG2P.Player2EstimateOfOwnErrorForEachPlayer, BG2P.Player2EstimateOfOpponentErrorForEachPlayer })
                                - Strategies[(int)BargainingGame2Decisions.adjustmentToAvoidOverfitting].Calculate(new List<double> { });
                            BG2P.ProportionOfCostSavingsPlayer2InsistsOn = (BG2P.ProportionOfCostSavingsPlayer2InsistsOn + sameCalculationWithStrategyBeforeThat) / 2.0;
                        }
                    }
                    else
                        BG2P.ProportionOfCostSavingsPlayer2InsistsOn =
                            Strategies[(int)BargainingGame2Decisions.decisionOnThresholdForAgreement].Calculate(new List<double> { BG2P.Player2EstimateOfPlayer2WithCosts, BG2P.Player2EstimateOfPlayer1WithCosts, BG2P.Player2EstimateOfOwnErrorForEachPlayer, BG2P.Player2EstimateOfOpponentErrorForEachPlayer })
                            - Strategies[(int)BargainingGame2Decisions.adjustmentToAvoidOverfitting].Calculate(new List<double> { });
                }
                BG2P.AmountOfPiePlayer2InsistsOn = BG2P.Player2EstimateOfPlayer2WithCosts + BG2P.ProportionOfCostSavingsPlayer2InsistsOn * BG2P.PlayersCombinedCosts;
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
            switch (CurrentDecisionIndex)
            {
                case (int)BargainingGame2Decisions.ownResultIncludingCosts:
                    score = Forecast.Score(Strategies[(int) CurrentDecisionIndex], GetDecisionInputs(), BG2P.Player1ResultIfNegotiationFails, out BG2P.Player1EstimateOfPlayer1WithCosts);
                    break;
                case (int)BargainingGame2Decisions.ownErrorEstimatingResultIncludingCosts:
                    score = Forecast.Score(Strategies[(int) CurrentDecisionIndex], GetDecisionInputs(), Math.Abs(((BargainingGame2ProgressInfo)Progress).Player1EstimateOfPlayer1WithCosts - BG2P.Player1ResultIfNegotiationFails), out BG2P.Player1EstimateOfOwnErrorForEachPlayer);
                    break;
                case (int) BargainingGame2Decisions.opponentErrorEstimatingResultIncludingCosts:
                    score = Forecast.Score(Strategies[(int) CurrentDecisionIndex], GetDecisionInputs(), Math.Abs(((BargainingGame2ProgressInfo)Progress).Player2EstimateOfPlayer2WithCosts - BG2P.Player2ResultIfNegotiationFails), out BG2P.Player1EstimateOfOpponentErrorForEachPlayer);
                    break;
                case (int) BargainingGame2Decisions.decisionOnThresholdForAgreement:
                    BG2P.ProportionOfCostSavingsPlayer1InsistsOn = MakeDecision(); // this is the amount of the pie that player 1 insists on giving
                    if (CurrentlyEvolvingDecisionIndex == (int)BargainingGame2Decisions.decisionOnThresholdForAgreement)
                    {
                        ResolveBargaining();
                        Score(CurrentDecisionIndex.Value, BG2P.Player1Result);
                        Progress.GameComplete = true;
                    }
                    break;
                case (int)BargainingGame2Decisions.adjustmentToAvoidOverfitting:
                    BG2P.ProportionOfCostSavingsPlayer1InsistsOn -= MakeDecision(); // we are going to lower our previous aggressiveness by a small amount to account for overfitting -- this decision is made with a different set of game inputs
                    ResolveBargaining();
                    Score(CurrentDecisionIndex.Value, BG2P.Player1Result);
                    Progress.GameComplete = true;
                    break;
            }

            if (CurrentlyEvolvingDecisionIndex != null && CurrentlyEvolvingDecisionIndex < (int) BargainingGame2Decisions.decisionOnThresholdForAgreement && CurrentlyEvolvingDecisionIndex == CurrentDecisionIndex)
            { // abort the evolution process early
                Score((int)CurrentDecisionIndex, score);
                Progress.GameComplete = true;
            }
        }

        private void ResolveBargaining()
        {
            // If the bargaining offers cross, we settle in middle.                    
            BG2P.AmountOfPiePlayer1InsistsOn = BG2P.Player1EstimateOfPlayer1WithCosts + BG2P.ProportionOfCostSavingsPlayer1InsistsOn * BG2P.PlayersCombinedCosts;
            double amountOfPiePlayer2CouldLiveWithout = 1.0 - BG2P.AmountOfPiePlayer2InsistsOn;
            BG2P.HowFarApartOffersAre = BG2P.AmountOfPiePlayer1InsistsOn - amountOfPiePlayer2CouldLiveWithout;
            BG2P.NegotiationSucceeds = BG2P.HowFarApartOffersAre < 0 ? 1.0 : 0.0; // offers are negatively far apart if we have a range of agreement
            BG2P.HowFarApartOffersAreWhenNegotiationFails = BG2P.NegotiationSucceeds == 1.0 ? (double?) null : (double?) BG2P.HowFarApartOffersAre;
            if (BG2P.NegotiationSucceeds == 1.0)
            {
                BG2P.Player1Result = (BG2P.AmountOfPiePlayer1InsistsOn + amountOfPiePlayer2CouldLiveWithout) / 2.0;
                BG2P.Player2Result = 1.0 - BG2P.Player1Result;
            }
            else
            {
                BG2P.Player1Result = BG2P.Player1ResultIfNegotiationFails;
                BG2P.Player2Result = BG2P.Player2ResultIfNegotiationFails;
            }
            if (symmetryTestingOn)
            {
                if (symmetryTestingFirstOfPair)
                    progressFirstOfPair = BG2P;
                else
                {
                    if (Math.Abs(progressFirstOfPair.Player1Result - BG2P.Player2Result) > 0.01)
                        System.Diagnostics.Debug.WriteLine("Here.");
                }
            }
        }

        protected override List<double> GetDecisionInputs()
        {
            double[] inputs = null;
            int decisionNumber = (int)CurrentDecisionIndex;
            if (CurrentDecisionIndex < (int)BargainingGame2Decisions.opponentErrorEstimatingResultIncludingCosts)
                inputs = new double[] { BG2P.Player1ProxyForPlayer1, BG2I.selfNoiseLevel };
            else if (CurrentDecisionIndex == (int)BargainingGame2Decisions.opponentErrorEstimatingResultIncludingCosts)
                inputs = new double[] { BG2P.Player1ProxyForPlayer1, BG2I.oppNoiseLevel }; // player 1 knows its own proxy, and (more importantly) knows its opponent's noise level
            else if (CurrentDecisionIndex == (int)BargainingGame2Decisions.decisionOnThresholdForAgreement)
            {
                BG2P.Player1EstimateOfPlayer1WithoutCosts = BG2P.Player1EstimateOfPlayer1WithCosts + BG2I.selfFailCost; // our estimate of player 1 subtracts the costs, so now we add it back
                BG2P.Player1EstimateOfPlayer2WithoutCosts = 1.0 - BG2P.Player1EstimateOfPlayer1WithoutCosts;
                BG2P.Player1EstimateOfPlayer2WithCosts = BG2P.Player1EstimateOfPlayer2WithoutCosts - BG2I.oppFailCost;

                // The estimates of the players are with costs. That implicitly gives a sense of what would happen without costs, since without costs, the players' returns always add up to 1.
                inputs = new double[] { BG2P.Player1EstimateOfPlayer1WithCosts, BG2P.Player1EstimateOfPlayer2WithCosts, BG2P.Player1EstimateOfOwnErrorForEachPlayer, BG2P.Player1EstimateOfOpponentErrorForEachPlayer };
            }
            else if (CurrentDecisionIndex == (int)BargainingGame2Decisions.adjustmentToAvoidOverfitting)
            {
                inputs = new double[] { }; // no inputs -- we're trying to get a constant here.
            }
            else
                throw new Exception("Internal error: Unknown decision.");

            if (CurrentDecisionIndex == RecordInputsForDecisionNumber && !PreparationPhase)
                RecordedInputs.Add(inputs);
            return inputs.ToList();
        }
    }
}

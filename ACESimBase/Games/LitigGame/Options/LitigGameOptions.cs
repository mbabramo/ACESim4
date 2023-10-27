using System;
using ACESim.Util;
using System.Collections.Generic;
using System.Linq;
using ACESim.Util.DiscreteProbabilities;

namespace ACESim
{
    [Serializable]
    public class LitigGameOptions : GameOptions
    {
        /// <summary>
        /// The generator of disputes (e.g., contract, tort, etc.), determining the litigation quality. If null, then there is an equal probability of each litigation quality outcome.
        /// </summary>
        public ILitigGameDisputeGenerator LitigGameDisputeGenerator
        { 
            get;
            set;
        }
        /// <summary>
        /// This can generate some additional decisions before trial. If null, none are generated.
        /// </summary>
        public ILitigGamePretrialDecisionGenerator LitigGamePretrialDecisionGeneratorGenerator;
        /// <summary>
        /// This allows for making side bets in each bargaining round, if not null.
        /// </summary>
        public LitigGameRunningSideBets LitigGameRunningSideBets;

        /// <summary>
        /// The number of different liability quality levels that a case can have, with the lowest levels reflecting the lowest probability of liability. Parties obtain estimates of the liability level.
        /// </summary>
        public byte NumLiabilityStrengthPoints;
        /// <summary>
        /// The number of discrete signals that a party can receive. For example, 10 signals would allow each party to differentiate 10 different levels of case strength. If there is only 1 liability signal, liability is certain. (TODO: Allow for number of court liability signals to differ from number of party liability signals. In that case, we could have 1 liability signal for parties and 2 or more for courts, so liability occurs only some of the time. Also, having multiple liability signals for courts often isn't needed -- the question is just whether court thinks party is liable or not, except with some options where strength of case matters.)
        /// </summary>
        public byte NumLiabilitySignals;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the plaintiff's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double PLiabilityNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the defendant's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double DLiabilityNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the court's estimate of the case strength. This applies only when the action is the noise, rather than the signal itself.
        /// </summary>
        public double CourtLiabilityNoiseStdev;

        /// <summary>
        /// The number of different damages quality levels that a case can have, with the lowest levels reflecting the lowest expected level of damages. Parties obtain estimates of the damages strength.
        /// </summary>
        public byte NumDamagesStrengthPoints;
        /// <summary>
        /// The number of discrete signals about damages that a party can receive. For example, 10 signals would allow each party to differentiate 10 different levels of damages strength.
        /// </summary>
        public byte NumDamagesSignals;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the plaintiff's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double PDamagesNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the defendant's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double DDamagesNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the court's estimate of the case strength. This applies only when the action is the noise, rather than the signal itself.
        /// </summary>
        public double CourtDamagesNoiseStdev;
        /// <summary>
        /// If true, then plaintiff has no choice but to file and defendant has no choice but to answer
        /// </summary>
        public bool SkipFileAndAnswerDecisions;
        /// <summary>
        /// If true, then the bargaining round starts with P and D deciding whether to bargain at all this round. The per bargaining round costs will be borne either way.
        /// </summary>
        public bool IncludeAgreementToBargainDecisions;
        /// <summary>
        /// This can be used to set the strategy to be used when optimizing the opponent for a certain number of iterations. Note that this applies to specific decisions.
        /// </summary>
        public LitigGameWarmStartOptions WarmStartOptions;
        /// <summary>
        /// The number of iterations to warm start for
        /// </summary>
        public int? WarmStartThroughIteration;
        /// <summary>
        /// The number of discrete offers a party can make at any given time. For example, 10 signals might allow offers of 0.05, 0.15, ..., 0.95, but delta offers may allow offers to get gradually more precise.
        /// </summary>
        public byte NumOffers;
        /// <summary>
        /// If true, then parties may offer damages of 0% or 100% of the alleged amount. This may not be necessary when parties can abandon or default. If false, offer levels are all between these extremes, so there is a greater density of offers.
        /// </summary>
        public bool IncludeEndpointsForOffers;
        /// <summary>
        /// True if each barganiing round consists of simultaneous offers.
        /// </summary>
        public bool BargainingRoundsSimultaneous;
        /// <summary>
        /// True if simultaneous offers are eventually revealed to the opposing party.
        /// </summary>
        public bool SimultaneousOffersUltimatelyRevealed;
        /// <summary>
        /// Indicates whether p goes first in each bargaining round for non-simultaneous bargaining (later bools are ignored).
        /// </summary>
        public List<bool> PGoesFirstIfNotSimultaneous; // if not simultaneous
        /// <summary>
        /// Options for making offers that are relative to previous offers.
        /// </summary>
        public DeltaOffersOptions DeltaOffersOptions;
        /// <summary>
        /// If true, then a plaintiff can abandon the litigation or a defendant can decide to default immediately after a bargaining round. If both do so, then a coin is flipped to decide who has done so.
        /// </summary>
        public bool AllowAbandonAndDefaults;
        /// <summary>
        /// If true, and AllowAbandonAndDefaults is true, then the decision whether to abandon/default is made at the beginning of the bargaining round rather than at the end of the bargaining round. This can greatly reduce the number of information sets in a one bargaining round game, at the expense of reducing the incentive to try to bluff the other player into giving up.
        /// </summary>
        public bool PredeterminedAbandonAndDefaults;
        /// <summary>
        /// If true, then during optimization, chance decisions at the end of the game are not fully played out to a single conclusion. Instead, a list of all possible alternative endings is created, and the outcome is the average of these. This can lead to a more compact game tree.
        /// </summary>
        public bool CollapseAlternativeEndings;

        /// <summary>
        /// A multiplier applied to all costs figures below. This makes it straightforward to change the overall level of costs without changing every option individually.
        /// </summary>
        public double CostsMultiplier
        {
            get;
            set;
        }
        /// <summary>
        /// Costs that the plaintiff must pay if the case goes to trial.
        /// </summary>
        public double PTrialCosts;
        /// <summary>
        /// Costs that the defendant must pay if the case goes to trial.
        /// </summary>
        public double DTrialCosts;
        // The following show the original parameter values before changes during warmups / scenarios:
        public double? CostsMultiplier_Original;
        public double? PTrialCosts_Original;
        public double? DTrialCosts_Original;
        /// <summary>
        /// Cost that the plaintiff must pay for filing
        /// </summary>
        public double PFilingCost;
        /// <summary>
        /// The portion of the filing cost that is saved if the defendant doesn't answer. If non-zero, this changes the meaning of "filing cost" into "litigation cost up to first bargaining round," with some of that borne in filing and some of it borne only if the defendant doesn't answer.
        /// </summary>
        public double PFilingCost_PortionSavedIfDDoesntAnswer = 0;
        /// <summary>
        /// Cost that the defendant must pay for answering
        /// </summary>
        public double DAnswerCost;
        /// <summary>
        /// Costs that each party must pay per round of bargaining, where RoundSpecificBargainingCosts are not specified. Note that an immediate successful resolution will still produce costs.
        /// </summary>
        public double PerPartyCostsLeadingUpToBargainingRound;
        /// <summary>
        /// If non-null, then per-party costs leading up to the bargaining round vary by round and 
        /// </summary>
        public (double pCosts, double dCosts)[] RoundSpecificBargainingCosts;
        /// <summary>
        /// The number of bargaining rounds
        /// </summary>
        public int NumPotentialBargainingRounds;
        /// <summary>
        /// If true, and there is a loser at trial, then the loser pays the winner's costs.
        /// </summary>
        public bool LoserPays;
        /// <summary>
        /// This can be used to increase or decrease the amount of the winner's costs paid by the winner.
        /// </summary>
        public double LoserPaysMultiple;
        /// <summary>
        /// If true, then the loser pays rule applies also when a party gives up (except when that occurs by not answering in the first place).
        /// </summary>
        public bool LoserPaysAfterAbandonment;
        /// <summary>
        /// If true, then if LoserPays is true, plaintiff wins, and plaintiff receives less than defendant's offer. 
        /// </summary>
        public bool Rule68;
        /// <summary>
        /// If loser pays, that will be only if there is a large margin of victory.
        /// </summary>
        public bool LoserPaysOnlyLargeMarginOfVictory;
        /// <summary>
        /// The number of liability signals for the court (generally 2, but as many as there are liability signals if using the margin of victory)
        /// </summary>
        public int NumCourtLiabilitySignals
        {
            get
            {
                if (NumLiabilitySignals == 1)
                    return 1;
                if (LoserPays && LoserPaysOnlyLargeMarginOfVictory)
                    return (NumLiabilitySignals % 2 == 0 ? NumLiabilitySignals : NumLiabilitySignals - 1);
                return 2;
            }
        }
        /// <summary>
        /// The threshold for triggering fee shifting in favor of the plaintiff, with fee shifting in favor of the defendant mirroring on the other side of the probability spectrum.
        /// This will apply to the court's estimate of liability strength.
        /// </summary>
        public double LoserPaysMarginOfVictoryThreshold;
        /// <summary>
        /// If true, "shootout settlements" apply. That is, where settlement fails, plaintiff buys from defendant at midpoint of offer price right to double damages (if ShootoutStrength == 1.0).
        /// </summary>
        public bool ShootoutSettlements;
        /// <summary>
        /// 
        /// If true, then additional damages from shootout settlements apply even if a party drops out after the shootout has occurred.
        /// </summary>
        public bool ShootoutsApplyAfterAbandonment;
        /// <summary>
        /// If true, then the shootout (or other offer-of-judgment rule dependent on an offer) depends on an average of all rounds, not just the last round
        /// </summary>
        public bool ShootoutOfferValueIsAveraged;
        /// <summary>
        /// The proportion of extra damages that the plaintiff buys after a failed shootout round. This strength is also multiplied by the midpoint of the settlement offers to determine the price that the plaintiff must pay for the damages.
        /// </summary>
        public double ShootoutStrength; 

        /// <summary>
        /// Plaintiff's initial wealth.
        /// </summary>
        public double PInitialWealth;
        /// <summary>
        /// Defendant's initial wealth.
        /// </summary>
        public double DInitialWealth;

        /// <summary>
        /// The minimum amount of damages that a plaintiff might be awarded
        /// </summary>
        public double DamagesMin;
        /// <summary>
        /// The maximum amount of damages that a plaintiff might be awarded
        /// </summary>
        public double DamagesMax;

        /// <summary>
        /// The degree of regret aversion. If a party finishes with wealth w0 but could have finished with wealth w1, where w1 > w0, then the party experiences effective wealth of w0 - RegretAversion*(w1 - w0)
        /// </summary>
        public double RegretAversion;
        /// <summary>
        /// Plaintiff's utility calculator (risk-neutral or specific type of risk averse)
        /// </summary>
        public UtilityCalculator PUtilityCalculator;
        /// <summary>
        /// Defendant's utility calculator (risk-neutral or specific type of risk averse)
        /// </summary>
        public UtilityCalculator DUtilityCalculator;


        public bool IncludeSignalsReport;
        public bool IncludeCourtSuccessReport;
        public bool FirstRowOnly;
        public List<(Func<Decision, GameProgress, byte>, string)> AdditionalTableOverrides;

        // the following are derived and should not be set directly

        public DeltaOffersCalculation DeltaOffersCalculation;
        public DiscreteValueSignalParameters PLiabilitySignalParameters, DLiabilitySignalParameters;
        public DiscreteValueSignalParameters PDamagesSignalParameters, DDamagesSignalParameters;

        public override void Simplify()
        {
            // This is for debugging
            byte limit = 3;
            if (NumLiabilityStrengthPoints > limit)
                NumLiabilityStrengthPoints = limit;
            if (NumLiabilitySignals > limit)
                NumLiabilitySignals = 3;
            if (NumDamagesStrengthPoints > limit)
                NumDamagesStrengthPoints = limit;
            if (NumDamagesSignals > limit)
                NumDamagesSignals = 3;
        }

        public bool IsSymmetric() => LitigGameDisputeGenerator.SupportsSymmetry() && BargainingRoundsSimultaneous && SkipFileAndAnswerDecisions /* Note: If changing this, must ensure PFilingCost == DAnswerCost */ && PLiabilityNoiseStdev == DLiabilityNoiseStdev && PDamagesNoiseStdev == DDamagesNoiseStdev && PTrialCosts == DTrialCosts && PTrialCosts_Original == DTrialCosts_Original && PInitialWealth + DamagesMax == DInitialWealth && (NumLiabilityStrengthPoints == 1 || NumDamagesStrengthPoints == 1) /* If BOTH liability and damages are uncertain, then suppose there is a 50% chance of liability and a 50% chance of high or zero damages; in this case, there is a 25% expectation of damages, so the game is not symmetric, as the defendant in effect has two ways to win */;


        public override string ToString()
        {
            return
$@"LitigationGame: {Name}
CollapseChanceDecisions {CollapseChanceDecisions}
NumOffers {NumOffers} {(IncludeEndpointsForOffers ? "(Includes endpoints)" : "")}  
NumPotentialBargainingRounds {NumPotentialBargainingRounds}  BargainingRoundsSimultaneous {BargainingRoundsSimultaneous} SimultaneousOffersUltimatelyRevealed {SimultaneousOffersUltimatelyRevealed} 
NumLiabilityStrengthPoints {NumLiabilityStrengthPoints} NumLiabilitySignals {NumLiabilitySignals} PLiabilityNoiseStdev {PLiabilityNoiseStdev} DLiabilityNoiseStdev {DLiabilityNoiseStdev} CourtLiabilityNoiseStdev {CourtLiabilityNoiseStdev} 
NumDamagesStrengthPoints {NumDamagesStrengthPoints} NumDamagesSignals {NumDamagesSignals} PDamagesNoiseStdev {PDamagesNoiseStdev} DDamagesNoiseStdev {DDamagesNoiseStdev} CourtDamagesNoiseStdev {CourtDamagesNoiseStdev} 
SkipFileAndAnswerDecisions {SkipFileAndAnswerDecisions} AllowAbandonAndDefaults {AllowAbandonAndDefaults} PredeterminedAbandonAndDefaults {PredeterminedAbandonAndDefaults} IncludeAgreementToBargainDecisions {IncludeAgreementToBargainDecisions} DeltaOffersOptions {DeltaOffersOptions} 
CostsMultiplier {CostsMultiplier} PTrialCosts {PTrialCosts} DTrialCosts {DTrialCosts} PFilingCost {PFilingCost} DAnswerCost {DAnswerCost} {(RoundSpecificBargainingCosts == null ? $"PerPartyCostsLeadingUpToBargainingRound {PerPartyCostsLeadingUpToBargainingRound}" : $"RoundSpecificBargainingCosts {String.Join(",", RoundSpecificBargainingCosts)}")} 
LoserPays {LoserPays} Multiple {LoserPaysMultiple} AfterAbandonment {LoserPaysAfterAbandonment} Rule68 {Rule68} Margin {LoserPaysOnlyLargeMarginOfVictory} ({LoserPaysMarginOfVictoryThreshold})
{(ShootoutSettlements ? $@"ShootoutSettlements ShootoutStrength {ShootoutStrength} ShootoutOfferValueIsAveraged {ShootoutOfferValueIsAveraged} ShootoutsApplyAfterAbandonment {ShootoutsApplyAfterAbandonment}
" : "")}PInitialWealth {PInitialWealth} DInitialWealth {DInitialWealth} 
DamagesMin {DamagesMin} DamagesMax {DamagesMax} 
RegretAversion {RegretAversion} 
PUtilityCalculator {PUtilityCalculator} DUtilityCalculator {DUtilityCalculator} 
WarmStartOptions {WarmStartOptions} for {WarmStartThroughIteration} iterations";

        }
    }
}
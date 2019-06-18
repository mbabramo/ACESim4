using System;
using ACESim.Util;
using System.Collections.Generic;

namespace ACESim
{
    [Serializable]
    public class MyGameOptions : GameOptions
    {
        /// <summary>
        /// The generator of disputes (e.g., contract, tort, etc.), determining the litigation quality. If null, then there is an equal probability of each litigation quality outcome.
        /// </summary>
        public IMyGameDisputeGenerator MyGameDisputeGenerator;
        /// <summary>
        /// This can generate some additional decisions before trial. If null, none are generated.
        /// </summary>
        public IMyGamePretrialDecisionGenerator MyGamePretrialDecisionGeneratorGenerator;
        /// <summary>
        /// This allows for making side bets in each bargaining round, if not null.
        /// </summary>
        public MyGameRunningSideBets MyGameRunningSideBets;
        /// <summary>
        /// The number of different quality points that a litigation can have. 
        /// </summary>
        public byte NumLitigationQualityPoints;
        /// <summary>
        /// The number of discrete signals that a party can receive. For example, 10 signals would allow each party to differentiate 10 different levels of case strength.
        /// </summary>
        public byte NumSignals;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the plaintiff's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double PNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the defendant's estimate of the case strength. When the action is the signal, this determines the probabilities of each signal; when the action is the noise, this affects the conversion of the noise to a signal, considering the underlying true value.
        /// </summary>
        public double DNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the court's estimate of the case strength. This applies only when the action is the noise, rather than the signal itself.
        /// </summary>
        public double CourtNoiseStdev;
        /// <summary>
        /// If true, then the bargaining round starts with P and D deciding whether to bargain at all this round. The per bargaining round costs will be borne either way.
        /// </summary>
        public bool IncludeAgreementToBargainDecisions;
        /// <summary>
        /// If true, this is a partial recall game, in which players do not remember earlier bargaining rounds.
        /// </summary>
        public MyGameBargainingRoundRecall BargainingRoundRecall;
        /// <summary>
        /// The number of discrete offers a party can make at any given time. For example, 10 signals might allow offers of 0.05, 0.15, ..., 0.95, but delta offers may allow offers to get gradually more precise.
        /// </summary>
        public byte NumOffers;
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
        /// A multiplier applied to all costs figures below. This makes it straightforward to change the overall level of costs without changing every option individually.
        /// </summary>
        public double CostsMultiplier;
        /// <summary>
        /// Costs that the plaintiff must pay if the case goes to trial.
        /// </summary>
        public double PTrialCosts;
        /// <summary>
        /// Costs that the defendant must pay if the case goes to trial.
        /// </summary>
        public double DTrialCosts;
        /// <summary>
        /// Cost that the plaintiff must pay for filing
        /// </summary>
        public double PFilingCost;
        /// <summary>
        /// Cost that the defendant must pay for answering
        /// </summary>
        public double DAnswerCost;
        /// <summary>
        /// Costs that each party must pay per round of bargaining. Note that an immediate successful resolution will still produce costs.
        /// </summary>
        public double PerPartyCostsLeadingUpToBargainingRound;
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
        /// Plaintiff's initial wealth.
        /// </summary>
        public double PInitialWealth;
        /// <summary>
        /// Defendant's initial wealth.
        /// </summary>
        public double DInitialWealth;
        /// <summary>
        /// Damages alleged
        /// </summary>
        public double DamagesToAllege;
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
        internal bool IncludeCourtSuccessReport;
        public List<(Func<Decision, GameProgress, byte>, string)> AdditionalTableOverrides;

        // the following are derived and should not be set directly

        public DeltaOffersCalculation DeltaOffersCalculation;
        public DiscreteValueSignalParameters PSignalParameters, DSignalParameters;
    }
}
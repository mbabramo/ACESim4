using System;
using ACESim.Util;
using System.Collections.Generic;

namespace ACESim
{
    public struct MyGameOptions
    {
        /// <summary>
        /// The number of different quality points that a litigation can have. 
        /// </summary>
        public byte NumLitigationQualityPoints;
        /// <summary>
        /// The number of discrete signals that a party can receive. For example, 10 signals would allow each party to differentiate 10 different levels of case strength.
        /// </summary>
        public byte NumSignals;
        /// <summary>
        /// The number of noise values that a party can receive to determine the discrete signal that the party receives. This is ignored if ActionIsNoiseNotSignal = false (effectively, NumNoiseValues is automatically equal to NumSignals in that case).
        /// </summary>
        public byte NumNoiseValues;
        /// <summary>
        /// The number of noise values that may distort the court's signal when it makes a decision. 
        /// </summary>
        public byte NumCourtNoiseValues;
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
        /// If true, then the chance action represents noise, and a litigant's signal is determined by combining the litigation quality with a noise value, drawn from the inverse cumulative normal distribution. If false, then a litigant's signal represents an unbiased probability estimate of the probability of winning (so, for example, if there are 10 signals, then a signal of 3 would represent a 35% chance of winning). The court decision will then be an uneven chance decision consistent with this estimate. This depends, however, on the assumption that litigation quality is uniformly distributed between 0 and 1. If it is not, then this should be set to true. 
        /// </summary>
        public bool ActionIsNoiseNotSignal;

        /// <summary>
        /// If true, then the bargaining round starts with P and D deciding whether to bargain at all this round. The per bargaining round costs will be borne either way.
        /// </summary>
        public bool IncludeAgreementToBargainDecisions;
        /// <summary>
        /// If true, this is a partial recall game, in which players do not remember earlier bargaining rounds.
        /// </summary>
        public bool ForgetEarlierBargainingRounds;
        /// <summary>
        /// The number of discrete offers a party can make at any given time. For example, 10 signals might allow offers of 0.05, 0.15, ..., 0.95, but delta offers may allow offers to get gradually more precise.
        /// </summary>
        public byte NumOffers;
        /// <summary>
        /// Subdivide a single offer decision into a series of binary decisions.
        /// </summary>
        public bool SubdivideOffers;
        /// <summary>
        /// True if each barganiing round consists of simultaneous offers.
        /// </summary>
        public bool BargainingRoundsSimultaneous;
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
        /// If true, and there is a loser at trial, then the loser pays all of the winner's costs.
        /// </summary>
        public bool LoserPays;
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
        public double DamagesAlleged;
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
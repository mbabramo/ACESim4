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
        /// The number of noise values that a party can receive to determine the discrete signal that the party receives. This is ignored if UseRawSignals = false (effectively, NumNoiseValues is automatically equal to NumSignals in that case).
        /// </summary>
        public byte NumNoiseValues;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the plaintiff's estimate of the case strength.
        /// </summary>
        public double PNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the defendant's estimate of the case strength.
        /// </summary>
        public double DNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the court's estimate of the case strength. This applies only when using raw signals.
        /// </summary>
        public double CourtNoiseStdev;
        /// <summary>
        /// If true, then a litigant's signal is determined solely by adding the litigation quality and a noise parameter, drawn from the inverse cumulative normal distribution. If false, then a litigant's signal represents an unbiased probability estimate of the probability of winning (so, for example, if there are 10 signals, then a signal of 3 would represent a 35% chance of winning). The court decision will then be an uneven chance decision consistent with this estimate. This depends, however, on the assumption that litigation quality is uniformly distributed between 0 and 1. If it is not, then raw signals should be used. 
        /// </summary>
        public bool UseRawSignals;


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
        /// Costs that each party must pay per round of bargaining. Note that an immediate successful resolution will still produce costs.
        /// </summary>
        public double PerPartyBargainingRoundCosts;
        /// <summary>
        /// The number of bargaining rounds
        /// </summary>
        public int NumBargainingRounds;

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
        /// Plaintiff's utility calculator (risk-neutral or specific type of risk averse)
        /// </summary>
        public UtilityCalculator PUtilityCalculator;
        /// <summary>
        /// Defendant's utility calculator (risk-neutral or specific type of risk averse)
        /// </summary>
        public UtilityCalculator DUtilityCalculator;


        public bool IncludeSignalsReport;

        // the following are derived and should not be set directly

        public DeltaOffersCalculation DeltaOffersCalculation;
        public DiscreteValueSignalParameters PSignalParameters, DSignalParameters;
    }
}
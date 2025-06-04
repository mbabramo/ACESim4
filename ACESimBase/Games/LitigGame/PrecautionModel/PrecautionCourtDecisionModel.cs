using System;
using ACESimBase.Util.DiscreteProbabilities;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Determines court liability outcomes using expected benefit / cost of the first untaken precaution.
    /// Requires a PrecautionImpactModel (true accident dynamics) and a PrecautionSignalModel (court's information).
    /// All indices are zero‑based.
    /// </summary>
    public sealed class PrecautionCourtDecisionModel
    {
        // References
        readonly PrecautionImpactModel impact;
        readonly PrecautionSignalModel signal;

        // Dimensions
        readonly int numCourtSamplesForCalculatingLiability;
        readonly int numPrecautionLevels;

        // Precomputed tables
        readonly double[][] expRiskReduction;  // [signal][precaution]
        readonly double[][] expBenefit;        // [signal][precaution]
        readonly double[][] benefitCostRatio;  // [signal][precaution]
        readonly bool[][] liable;            // [signal][precaution]
        double[][] liableProbGivenHidden;   // [hidden][precaution]

        /// <summary>
        /// Build a court‑decision model.
        /// </summary>
        public PrecautionCourtDecisionModel(
            PrecautionImpactModel impactModel,
            PrecautionSignalModel signalModel)
        {
            impact = impactModel ?? throw new ArgumentNullException(nameof(impactModel));
            signal = signalModel ?? throw new ArgumentNullException(nameof(signalModel));

            numPrecautionLevels = impactModel.PrecautionLevels;
            numCourtSamplesForCalculatingLiability = signalModel.GetHiddenPosteriorFromCourtSignal(0).Length == 0
                ? throw new InvalidOperationException("Signal model appears unconfigured for court signals.")
                : signalModel.GetCourtSignalDistributionGivenDefendantSignal(0).Length; // quick way to fetch count

            // Precompute decision metrics
            expRiskReduction = new double[numCourtSamplesForCalculatingLiability][];
            expBenefit = new double[numCourtSamplesForCalculatingLiability][];
            benefitCostRatio = new double[numCourtSamplesForCalculatingLiability][];
            liable = new bool[numCourtSamplesForCalculatingLiability][];

            for (int s = 0; s < numCourtSamplesForCalculatingLiability; s++)
            {
                var posterior = signal.GetHiddenPosteriorFromCourtSignal(s); // length = hiddenCount, sums to 1
                expRiskReduction[s] = new double[numPrecautionLevels];
                expBenefit[s] = new double[numPrecautionLevels];
                benefitCostRatio[s] = new double[numPrecautionLevels];
                liable[s] = new bool[numPrecautionLevels];

                for (int k = 0; k < numPrecautionLevels; k++)
                {
                    // Expected risk reduction from k -> k+1 (if k is max‑1, still allowed; if k==max, delta=0)
                    double expectedDelta = 0;
                    for (int h = 0; h < posterior.Length; h++)
                    {
                        double delta = impact.GetRiskReduction(h, k);
                        expectedDelta += delta * posterior[h];
                    }
                    expRiskReduction[s][k] = expectedDelta; // already normalized via posterior
                    double benefit = expectedDelta * impactModel.HarmCost;
                    expBenefit[s][k] = benefit;
                    double ratio = impactModel.MarginalPrecautionCost == 0 ? double.PositiveInfinity : benefit / impactModel.MarginalPrecautionCost;
                    benefitCostRatio[s][k] = ratio;
                    liable[s][k] = ratio >= impactModel.LiabilityThreshold && k < numPrecautionLevels - 1; // cannot be liable if no further precaution exists
                }
            }
            BuildLiableProbTable();
        }
        void BuildLiableProbTable()
        {
            int hiddenCount = signal.HiddenStatesCount;
            liableProbGivenHidden = new double[hiddenCount][];
            for (int h = 0; h < hiddenCount; h++)
            {
                liableProbGivenHidden[h] = new double[numPrecautionLevels];
                double[] courtDist = signal.GetCourtSignalDistributionGivenHidden(h);
                for (int k = 0; k < numPrecautionLevels; k++)
                    for (int s = 0; s < numCourtSamplesForCalculatingLiability; s++)
                        if (liable[s][k])
                            liableProbGivenHidden[h][k] += courtDist[s];
            }
        }

        // ---------------- Public API ------------------------

        /// <summary>
        /// Expected benefit of first untaken precaution given court signal and chosen precaution.
        /// </summary>
        public double GetExpectedBenefit(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return expBenefit[courtSignal][precautionLevel];
        }

        /// <summary>
        /// Benefit/cost ratio for first untaken precaution.
        /// </summary>
        public double GetBenefitCostRatio(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return benefitCostRatio[courtSignal][precautionLevel];
        }

        /// <summary>
        /// Court's liability decision (true = liable) for given court signal and precaution level.
        /// </summary>
        public bool IsLiable(int courtSignal, int precautionLevel)
        {
            ValidateIndices(courtSignal, precautionLevel);
            return liable[courtSignal][precautionLevel];
        }
        public double GetLiabilityProbability(double[] courtSignalDistribution, int precautionLevel)
        {
            if (courtSignalDistribution == null || courtSignalDistribution.Length != numCourtSamplesForCalculatingLiability)
                throw new ArgumentException(nameof(courtSignalDistribution));
            ValidateIndices(0, precautionLevel); // ensure precautionLevel is in range

            double liabilityProb = 0.0;
            for (int s = 0; s < numCourtSamplesForCalculatingLiability; s++)
            {
                if (IsLiable(s, precautionLevel))
                {
                    liabilityProb += courtSignalDistribution[s];
                }
                // (If not liable for signal s, contribute 0)
            }
            return liabilityProb;
        }

        public double[] GetCourtSignalDistributionGivenSignalsAndLiability(
            int plaintiffSignal, int defendantSignal, int precautionLevel)
        {
            double[] baseDist = signal.GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(
                                    plaintiffSignal, defendantSignal);
            double[] filtered = new double[baseDist.Length];
            double total = 0.0;

            for (int s = 0; s < baseDist.Length; s++)
            {
                if (IsLiable(s, precautionLevel))
                {
                    filtered[s] = baseDist[s];
                    total += baseDist[s];
                }
            }
            if (total == 0.0) return filtered;           // all zeros ⇒ liability impossible
            for (int s = 0; s < filtered.Length; s++)    // renormalize
                filtered[s] /= total;
            return filtered;
        }

        public double[] GetCourtSignalDistributionGivenSignalsAndNoLiability(
            int plaintiffSignal, int defendantSignal, int precautionLevel)
        {
            double[] baseDist = signal.GetCourtSignalDistributionGivenPlaintiffAndDefendantSignals(
                                    plaintiffSignal, defendantSignal);
            double[] filtered = new double[baseDist.Length];
            double total = 0.0;

            for (int s = 0; s < baseDist.Length; s++)
            {
                if (!IsLiable(s, precautionLevel))
                {
                    filtered[s] = baseDist[s];
                    total += baseDist[s];
                }
            }
            if (total == 0.0) return filtered;           // all zeros ⇒ no-liability impossible
            for (int s = 0; s < filtered.Length; s++)    // renormalize
                filtered[s] /= total;
            return filtered;
        }

        public double[] GetHiddenPosteriorFromSignalsAndCourtDistribution(
            int plaintiffSignal, int defendantSignal, double[] courtSignalDistribution)
        {
            if (courtSignalDistribution == null || courtSignalDistribution.Length == 0)
                throw new ArgumentException(nameof(courtSignalDistribution));

            int H = signal.HiddenStatesCount;
            double[] postP = signal.GetHiddenPosteriorFromPlaintiffSignal(plaintiffSignal);
            double[] postD = signal.GetHiddenPosteriorFromDefendantSignal(defendantSignal);
            double[] unnorm = new double[H];

            // combine plaintiff & defendant evidence (uniform prior assumed)
            for (int h = 0; h < H; h++)
                unnorm[h] = postP[h] * postD[h];

            // incorporate court-signal distribution as soft evidence
            for (int h = 0; h < H; h++)
            {
                if (unnorm[h] == 0.0) continue;
                double[] courtGivenH = signal.GetCourtSignalDistributionGivenHidden(h);
                double likelihood = 0.0;
                for (int s = 0; s < courtGivenH.Length; s++)
                    likelihood += courtGivenH[s] * courtSignalDistribution[s];
                unnorm[h] *= likelihood;
            }

            double total = 0.0;
            for (int h = 0; h < H; h++) total += unnorm[h];
            if (total == 0.0) return new double[H];      // inconsistent evidence ⇒ all zeros

            double[] posterior = new double[H];
            for (int h = 0; h < H; h++) posterior[h] = unnorm[h] / total;
            return posterior;
        }

        public double[] GetLiabilityOutcomeProbabilities(
            int hiddenState,
            int precautionLevel) => [ 1.0 - liableProbGivenHidden[hiddenState][precautionLevel],
               liableProbGivenHidden[hiddenState][precautionLevel]];

        /// <summary>
        /// Returns { P(no-liability) , P(liability) } given the two private signals,
        /// the accident outcome, and the defendant’s precaution level.
        /// Works in both expanded and collapsed-chance game trees.
        /// All indices are zero-based.
        /// </summary>
        public double[] GetLiabilityOutcomeProbabilities(
            int plaintiffSignal,
            int defendantSignal,
            bool accidentOccurred,
            int precautionLevel)
        {
            ValidateIndices(0, precautionLevel); // check precaution range
            if ((uint)plaintiffSignal >= signal.NumPSignals)
                throw new ArgumentOutOfRangeException(nameof(plaintiffSignal));
            if ((uint)defendantSignal >= signal.NumDSignals)
                throw new ArgumentOutOfRangeException(nameof(defendantSignal));

            // -------- posterior over hidden states ----------
            int H = signal.HiddenStatesCount;
            double uniformPrior = 1.0 / H;
            double[] posterior = new double[H];
            double total = 0.0;

            for (int h = 0; h < H; h++)
            {
                double w =
                    uniformPrior *
                    signal.GetPlaintiffSignalProbability(h, plaintiffSignal) *
                    signal.GetDefendantSignalProbability(h, defendantSignal);

                double pAcc = impact.GetAccidentProbability(h, precautionLevel);
                w *= accidentOccurred ? pAcc : (1.0 - pAcc);

                posterior[h] = w;
                total += w;
            }

            if (total == 0.0)           // unreachable evidence ⇒ uninformed prior
                return new[] { 0.5, 0.5 };

            for (int h = 0; h < H; h++) posterior[h] /= total;

            // -------- integrate court decision ----------
            double liableProb = 0.0;
            for (int h = 0; h < H; h++)
                liableProb += posterior[h] * liableProbGivenHidden[h][precautionLevel];

            return new[] { 1.0 - liableProb, liableProb };
        }

        /// Posterior P(hidden | pSig, dSig, accident, precaution, decision)
        public double[] GetHiddenPosteriorFromPath(
                int plaintiffSignal,
                int defendantSignal,
                bool accidentOccurred,
                int precautionLevel,
                bool? wasLiable)          // null = path ended in settlement
        {
            int H = signal.HiddenStatesCount;
            var posterior = new double[H];
            double total = 0.0;
            double uniformPrior = 1.0 / H;

            for (int h = 0; h < H; h++)
            {
                double w =
                    uniformPrior *
                    signal.GetPlaintiffSignalProbability(h, plaintiffSignal) *
                    signal.GetDefendantSignalProbability(h, defendantSignal);

                double pAcc = impact.GetAccidentProbability(h, precautionLevel);
                w *= accidentOccurred ? pAcc : (1.0 - pAcc);

                if (wasLiable.HasValue)        // incorporate the court outcome only if known
                {
                    double liableProb = liableProbGivenHidden[h][precautionLevel];
                    w *= wasLiable.Value ? liableProb : (1.0 - liableProb);
                }

                posterior[h] = w;
                total += w;
            }

            if (total == 0.0) return posterior;    // inconsistent evidence → all zeros
            for (int h = 0; h < H; h++) posterior[h] /= total;
            return posterior;
        }

        /// <summary>
        /// Posterior distribution over hidden precaution-power states when
        ///  • the defendant observed <paramref name="defendantSignal"/> and decided
        ///    not to engage in the activity,
        ///  • therefore the plaintiff received no signal,
        ///  • no accident occurred, and
        ///  • the court never became involved.
        /// </summary>
        /// <param name="defendantSignal">The realised defendant signal index.</param>
        /// <returns>
        /// A length-H array whose entries sum to one (or to zero if the signal
        /// index is impossible under every hidden state).
        /// </returns>
        public double[] GetHiddenPosteriorFromDefendantSignal(int defendantSignal)
        {
            int hiddenStates = signal.HiddenStatesCount;
            var posterior = new double[hiddenStates];

            double uniformPrior = 1.0 / hiddenStates;
            double normalisingConstant = 0.0;

            for (int h = 0; h < hiddenStates; h++)
            {
                double weight = uniformPrior *
                                signal.GetDefendantSignalProbability(h, defendantSignal);

                posterior[h] = weight;
                normalisingConstant += weight;
            }

            if (normalisingConstant == 0.0)
                return posterior;  // impossible signal – caller sees all zeros

            for (int h = 0; h < hiddenStates; h++)
                posterior[h] /= normalisingConstant;

            return posterior;
        }



        // ---------------- Helpers ---------------------------

        void ValidateIndices(int signal, int precautionLevel)
        {
            if ((uint)signal >= numCourtSamplesForCalculatingLiability) throw new ArgumentOutOfRangeException(nameof(signal));
            if ((uint)precautionLevel >= numPrecautionLevels) throw new ArgumentOutOfRangeException(nameof(precautionLevel));
        }
    }
}

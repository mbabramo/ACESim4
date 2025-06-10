using System;
using System.Linq;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Combines a <see cref="PrecautionImpactModel"/> and a <see cref="PrecautionSignalModel"/>.
    /// Provides all probability tables that require both models (signals *and* accident physics).
    /// The heavy tables are built once in the constructor; public methods are cheap look‑ups.
    /// </summary>
    public sealed class PrecautionRiskModel
    {
        readonly PrecautionImpactModel impact;
        readonly PrecautionSignalModel signal;

        // Dimensions
        readonly int H; // hidden count
        readonly int P; // plaintiff signals
        readonly int D; // defendant signals
        readonly int K; // precaution levels

        // Accident probability tables
        readonly double[][] accidentProbGivenD;   // [d][k]
        readonly double[][][] accidentProbGivenPD; // [p][d][k]

        // Wrongful‑attribution probability
        readonly double[][][] wrongfulAttribProbGivenPD; // [p][d][k]

        // Plaintiff‑signal distributions conditional on defendant signal & accident status
        readonly double[][][] plaintiffGivenD_AfterAcc; // [d][k][p]
        readonly double[][][] plaintiffGivenD_NoAcc;    // [d][k][p]

        //-------------------------------------------------------------------
        // Constructor
        //-------------------------------------------------------------------
        public PrecautionRiskModel(PrecautionImpactModel impactModel, PrecautionSignalModel signalModel)
        {
            impact = impactModel ?? throw new ArgumentNullException(nameof(impactModel));
            signal = signalModel ?? throw new ArgumentNullException(nameof(signalModel));
            if (impact.HiddenCount != signal.HiddenStatesCount)
                throw new ArgumentException("HiddenCount mismatch between models.");

            H = impact.HiddenCount;
            K = impact.PrecautionLevels;
            P = signal.NumPSignals;
            D = signal.NumDSignals;

            // ---- Build tables ----
            accidentProbGivenD = BuildAccidentProbGivenDTable();
            accidentProbGivenPD = BuildAccidentProbGivenPDTable();
            wrongfulAttribProbGivenPD = BuildWrongfulAttribProbGivenPDTable();

            // Plaintiff‑signal tables (needed by dispute generator)
            plaintiffGivenD_AfterAcc = BuildPlaintiffSignalGivenDefendantAfterAccidentTable();
            plaintiffGivenD_NoAcc = BuildPlaintiffSignalGivenDefendantNoAccidentTable();
        }

        //-------------------------------------------------------------------
        // Public access – accident probabilities
        //-------------------------------------------------------------------
        public double GetAccidentProbabilityGivenDefendantSignal(int defendantSignal, int precautionLevel)
        {
            ValidateD(defendantSignal);
            ValidateK(precautionLevel);
            return accidentProbGivenD[defendantSignal][precautionLevel];
        }

        public double GetAccidentProbabilityGivenBothSignals(int defendantSignal, int plaintiffSignal, int precautionLevel)
        {
            ValidateD(defendantSignal);
            ValidateP(plaintiffSignal);
            ValidateK(precautionLevel);
            return accidentProbGivenPD[plaintiffSignal][defendantSignal][precautionLevel];
        }

        //-------------------------------------------------------------------
        // Public access – wrongful attribution probability
        //-------------------------------------------------------------------
        public double GetWrongfulAttributionProbabilityGivenSignals(int defendantSignal, int plaintiffSignal, int precautionLevel)
        {
            ValidateD(defendantSignal);
            ValidateP(plaintiffSignal);
            ValidateK(precautionLevel);
            return wrongfulAttribProbGivenPD[plaintiffSignal][defendantSignal][precautionLevel];
        }

        //-------------------------------------------------------------------
        // Public access – plaintiff‑signal distributions
        //-------------------------------------------------------------------
        public double[] GetPlaintiffSignalDistGivenDefendantAfterAccident(int defendantSignal, int precautionLevel)
        {
            ValidateD(defendantSignal);
            ValidateK(precautionLevel);
            return plaintiffGivenD_AfterAcc[defendantSignal][precautionLevel];
        }

        public double[] GetPlaintiffSignalDistGivenDefendantNoAccident(int defendantSignal, int precautionLevel)
        {
            ValidateD(defendantSignal);
            ValidateK(precautionLevel);
            return plaintiffGivenD_NoAcc[defendantSignal][precautionLevel];
        }

        //-------------------------------------------------------------------
        // ----------------  Internal builders ------------------------------
        //-------------------------------------------------------------------

        double[][] BuildAccidentProbGivenDTable()
        {
            var table = new double[D][];
            double uniformPrior = 1.0 / H;

            for (int d = 0; d < D; d++)
            {
                table[d] = new double[K];
                for (int k = 0; k < K; k++)
                {
                    double numer = 0.0, denom = 0.0;
                    for (int h = 0; h < H; h++)
                    {
                        double weight = uniformPrior * signal.GetDefendantSignalProbability(h, d);
                        denom += weight;
                        numer += weight * impact.GetAccidentProbability(h, k);
                    }
                    table[d][k] = denom == 0.0 ? 0.0 : numer / denom;
                }
            }
            return table;
        }

        double[][][] BuildAccidentProbGivenPDTable()
        {
            var table = new double[P][][];
            double uniformPrior = 1.0 / H;
            for (int p = 0; p < P; p++)
            {
                table[p] = new double[D][];
                for (int d = 0; d < D; d++)
                {
                    table[p][d] = new double[K];
                    for (int k = 0; k < K; k++)
                    {
                        double numer = 0.0, denom = 0.0;
                        for (int h = 0; h < H; h++)
                        {
                            double weight = uniformPrior *
                                            signal.GetPlaintiffSignalProbability(h, p) *
                                            signal.GetDefendantSignalProbability(h, d);
                            denom += weight;
                            numer += weight * impact.GetAccidentProbability(h, k);
                        }
                        table[p][d][k] = denom == 0.0 ? 0.0 : numer / denom;
                    }
                }
            }
            return table;
        }

        double[][][] BuildWrongfulAttribProbGivenPDTable()
        {
            var table = new double[P][][];
            double uniformPrior = 1.0 / H;
            for (int p = 0; p < P; p++)
            {
                table[p] = new double[D][];
                for (int d = 0; d < D; d++)
                {
                    table[p][d] = new double[K];
                    for (int k = 0; k < K; k++)
                    {
                        double numer = 0.0, denom = 0.0;
                        for (int h = 0; h < H; h++)
                        {
                            double pP = signal.GetPlaintiffSignalProbability(h, p);
                            double pD = signal.GetDefendantSignalProbability(h, d);
                            double joint = uniformPrior * pP * pD;

                            double pCaused = impact.GetAccidentProbability(h, k) -
                                             impact.PAccidentWrongfulAttribution * (1.0 - impact.GetAccidentProbability(h, k)); // reverse engineering pCaused from total; safe because formula matches BuildAccidentProb

                            pCaused = Math.Clamp(pCaused, 0.0, 1.0);
                            double pWrongful = (1.0 - pCaused) * impact.PAccidentWrongfulAttribution;
                            double pAccident = pCaused + pWrongful;

                            numer += joint * pWrongful;
                            denom += joint * pAccident;
                        }
                        table[p][d][k] = denom == 0.0 ? 0.0 : numer / denom;
                    }
                }
            }
            return table;
        }

        double[][][] BuildPlaintiffSignalGivenDefendantAfterAccidentTable()
        {
            var table = new double[D][][];
            double uniformPrior = 1.0 / H;

            for (int d = 0; d < D; d++)
            {
                table[d] = new double[K][];
                for (int k = 0; k < K; k++)
                {
                    double[] numer = new double[P];
                    double denom = 0.0;
                    for (int h = 0; h < H; h++)
                    {
                        double w = uniformPrior *
                                   signal.GetDefendantSignalProbability(h, d) *
                                   impact.GetAccidentProbability(h, k);
                        if (w == 0.0) continue;
                        double[] pDist = signal.GetPlaintiffSignalDistributionGivenHidden(h);
                        for (int p = 0; p < P; p++)
                            numer[p] += w * pDist[p];
                        denom += w;
                    }
                    table[d][k] = denom == 0.0 ? numer : numer.Select(x => x / denom).ToArray();
                }
            }
            return table;
        }

        double[][][] BuildPlaintiffSignalGivenDefendantNoAccidentTable()
        {
            var table = new double[D][][];
            double uniformPrior = 1.0 / H;

            for (int d = 0; d < D; d++)
            {
                table[d] = new double[K][];
                for (int k = 0; k < K; k++)
                {
                    double[] numer = new double[P];
                    double denom = 0.0;
                    for (int h = 0; h < H; h++)
                    {
                        double pAcc = impact.GetAccidentProbability(h, k);
                        double w = uniformPrior *
                                   signal.GetDefendantSignalProbability(h, d) *
                                   (1.0 - pAcc);
                        if (w == 0.0) continue;
                        double[] pDist = signal.GetPlaintiffSignalDistributionGivenHidden(h);
                        for (int p = 0; p < P; p++)
                            numer[p] += w * pDist[p];
                        denom += w;
                    }
                    table[d][k] = denom == 0.0 ? numer : numer.Select(x => x / denom).ToArray();
                }
            }
            return table;
        }

        //-------------------------------------------------------------------
        // Validation helpers
        //-------------------------------------------------------------------
        void ValidateP(int p) { if ((uint)p >= P) throw new ArgumentOutOfRangeException(nameof(p)); }
        void ValidateD(int d) { if ((uint)d >= D) throw new ArgumentOutOfRangeException(nameof(d)); }
        void ValidateK(int k) { if ((uint)k >= K) throw new ArgumentOutOfRangeException(nameof(k)); }
    }
}

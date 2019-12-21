namespace SimpleAdditiveEvidence
{
    public readonly struct Outcome
    {
        public readonly double PUtility, DUtility, TrialRate, AccuracySq, AccuracyHypoSq, AccuracyForP, AccuracyForD, MinPOffer, MaxPOffer, MinDOffer, MaxDOffer;

        public Outcome(double PUtility, double DUtility, double TrialRate, double AccuracySq, double AccuracyHypoSq, double AccuracyForP, double AccuracyForD, (double, double) POffer, (double, double) DOffer)
        {
            this.PUtility = PUtility;
            this.DUtility = DUtility;
            this.TrialRate = TrialRate;
            this.AccuracySq = AccuracySq;
            this.AccuracyHypoSq = AccuracyHypoSq;
            this.AccuracyForP = AccuracyForP;
            this.AccuracyForD = AccuracyForD;
            this.MinPOffer = POffer.Item1;
            this.MaxPOffer = POffer.Item2;
            this.MinDOffer = DOffer.Item1;
            this.MaxDOffer = DOffer.Item2;
        }

        public override string ToString()
        {
            return $"{PUtility},{DUtility},{TrialRate},{AccuracySq},{AccuracyHypoSq},{AccuracyForP},{AccuracyForD},{MinPOffer},{MaxPOffer},{MinDOffer},{MaxDOffer}";
        }

        public static string GetHeaderString()
        {
            return "PUtility,DUtility,TrialRate,AccuracySq,AccuracyHypoSq,AccuracyForP,AccuracyForD,MinPOffer,MaxPOffer,MinDOffer,MaxDOffer";
        }
    }
}

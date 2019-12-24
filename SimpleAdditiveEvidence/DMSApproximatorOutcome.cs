namespace SimpleAdditiveEvidence
{
    public readonly struct DMSApproximatorOutcome
    {
        public readonly double PUtility, DUtility, TrialRate, AccuracySq, AccuracyHypoSq, AccuracyForP, AccuracyForD, MinPOffer, MaxPOffer, MinDOffer, MaxDOffer;

        public DMSApproximatorOutcome(double PUtility, double DUtility, double TrialRate, double AccuracySq, double AccuracyHypoSq, double AccuracyForP, double AccuracyForD, (double, double) POfferLine, (double, double) DOfferLine)
        {
            this.PUtility = PUtility;
            this.DUtility = DUtility;
            this.TrialRate = TrialRate;
            this.AccuracySq = AccuracySq;
            this.AccuracyHypoSq = AccuracyHypoSq;
            this.AccuracyForP = AccuracyForP;
            this.AccuracyForD = AccuracyForD;
            this.MinPOffer = POfferLine.Item1;
            this.MaxPOffer = POfferLine.Item2;
            this.MinDOffer = DOfferLine.Item1;
            this.MaxDOffer = DOfferLine.Item2;
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

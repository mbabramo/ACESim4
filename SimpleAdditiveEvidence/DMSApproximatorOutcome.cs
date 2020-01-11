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
            if (POfferLine.Item1 >= DOfferLine.Item2) // if P's most generous offer is less than D's most generous offer, then we never settle -- so denote this outside option of always trial in a common way
            {
                this.MinPOffer = this.MaxPOffer = 1.0;
                this.MinDOffer = this.MaxDOffer = 1.0;
            }
            this.MinPOffer = POfferLine.Item1;
            this.MaxPOffer = POfferLine.Item2;
            this.MinDOffer = DOfferLine.Item1;
            this.MaxDOffer = DOfferLine.Item2;
        }

        public override string ToString()
        {
            return $"{PUtility},{DUtility},{TrialRate},{AccuracySq},{AccuracyHypoSq},{AccuracyForP},{AccuracyForD},{MinPOffer},{MaxPOffer},{MinDOffer},{MaxDOffer}";
        }

        public string ToStringForSpecificSignal(double signal, string pOffer, string dOffer, string pOfferStringCorrect, string dOfferStringCorrect)
        {
            return $"{signal},{PUtility},{DUtility},{TrialRate},{AccuracySq},{AccuracyHypoSq},{AccuracyForP},{AccuracyForD},{pOffer},{dOffer},{pOfferStringCorrect},{dOfferStringCorrect}";
        }

        public static string GetHeaderString()
        {
            return "PUtility,DUtility,TrialRate,AccuracySq,AccuracyHypoSq,AccuracyForP,AccuracyForD,MinPOffer,MaxPOffer,MinDOffer,MaxDOffer";
        }

        public static string GetHeaderStringForSpecificSignal()
        {
            return "Signal,PUtility,DUtility,TrialRate,AccuracySq,AccuracyHypoSq,AccuracyForP,AccuracyForD,POffer,DOffer,POfferCorrect,DOfferCorrect";
        }
    }
}

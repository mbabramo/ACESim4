namespace ACESim // Assuming the ACESim base namespace; adjust if needed
{
    /// <summary>
    /// Extended litigation progress state for the precaution negligence scenario.
    /// Includes fields for key events and outcomes in the case.
    /// </summary>
    public class PrecautionNegligenceProgress : LitigGameProgress
    {
        public PrecautionNegligenceProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }

        // Decision outcomes
        public bool EngagesInActivity { get; set; }              // whether the plaintiff engaged (filed the lawsuit)
        public int RelativePrecautionLevel { get; set; }       // defendant's chosen precaution level
        public bool AccidentOccurs { get; set; }     // whether an accident occurred
        public double BenefitCostRatio { get; set; } // of the forsaken precaution


        public override LitigGameProgress DeepCopy()
        {
            PrecautionNegligenceProgress copy = new PrecautionNegligenceProgress(FullHistoryRequired);

            // copy.GameComplete = this.GameComplete;
            CopyFieldInfo(copy);

            // We don't need to copy the PostGameInfo, because that's automatically created

            return copy;
        }

        internal override void CopyFieldInfo(GameProgress theCopy)
        {
            var copy = (PrecautionNegligenceProgress)theCopy;

            base.CopyFieldInfo(copy);

            copy.EngagesInActivity = EngagesInActivity;
            copy.RelativePrecautionLevel = RelativePrecautionLevel;
            copy.AccidentOccurs = AccidentOccurs;
        }
}

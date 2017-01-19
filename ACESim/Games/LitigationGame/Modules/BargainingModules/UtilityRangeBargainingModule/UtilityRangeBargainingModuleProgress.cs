using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class UtilityRangeBargainingModuleProgress : BargainingModuleProgress
    {
        /* Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public double? PDecisionmakerWealthAtTimeOfDecisionBeingEvolved;
        public double? DWealthAtTimeOfDecisionBeingEvolved;
        public double? PDecisionmakerWealthBeforeCurrentBargainingRound;
        public double? DWealthBeforeCurrentBargainingRound;
        public double? PAggressivenessOverrideModified;
        public double? DAggressivenessOverrideModified;
        public double? PAggressivenessOverrideModifiedFinal;
        public double? DAggressivenessOverrideModifiedFinal;
        public double? MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked;
        public double? MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked;
        public double? MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked;
        public double? MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked;
        public double? MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
        public double? MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked;
        public double? MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
        public double? MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked;
        public double? MostRecentPProjectionPAbsErrorInSettingThreatPoint;
        public double? MostRecentDProjectionPAbsErrorInSettingThreatPoint;
        public double? MostRecentPProjectionDAbsErrorInSettingThreatPoint;
        public double? MostRecentDProjectionDAbsErrorInSettingThreatPoint;
        public double? MostRecentPAbsErrorInSettingThreatPoint;
        public double? MostRecentDAbsErrorInSettingThreatPoint;
        public bool? NEVForP;
        public bool? NEVForD;
        public double? MostRecentPlaintiffOffer;
        public double? MostRecentDefendantOffer;
        public double? MostRecentBargainingDistance;
        public double? MostRecentPPerceivedBargainingRange;
        public double? MostRecentDPerceivedBargainingRange;
        public List<double?> BargainingDistanceList;
        public List<double?> PPerceivedBargainingRangeList;
        public List<double?> DPerceivedBargainingRangeList;
        public double? ChangeInPlaintiffOffer;
        public double? ChangeInDefendantOffer;
        public double? FakePProjectionOfPUtilityDelta;
        public double? FakePProjectionOfDUtilityDelta;
        public double? FakeDProjectionOfPUtilityDelta;
        public double? FakeDProjectionOfDUtilityDelta;
        public int? PUtilityProjectionDecisionNumber;
        public int? DUtilityProjectionDecisionNumber;
        public int? PWealthTranslationDecisionNumber;
        public int? DWealthTranslationDecisionNumber;


        static ConcurrentQueue<UtilityRangeBargainingModuleProgress> RecycledUtilityRangeBargainingModuleProgressQueue = new ConcurrentQueue<UtilityRangeBargainingModuleProgress>();

        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledUtilityRangeBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new UtilityRangeBargainingModuleProgress GetRecycledOrAllocate()
        {
            UtilityRangeBargainingModuleProgress recycled = null;
            RecycledUtilityRangeBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
                recycled.CleanAfterRecycling();
                return recycled;
            }
            return new UtilityRangeBargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            PDecisionmakerWealthAtTimeOfDecisionBeingEvolved = null;
            DWealthAtTimeOfDecisionBeingEvolved = null;
            PDecisionmakerWealthBeforeCurrentBargainingRound = null;
            DWealthBeforeCurrentBargainingRound = null;
            PAggressivenessOverrideModified = null;
            DAggressivenessOverrideModified = null;
            PAggressivenessOverrideModifiedFinal = null;
            DAggressivenessOverrideModifiedFinal = null;
            MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked = null;
            MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked = null;
            MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked = null;
            MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked = null;
            MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = null;
            MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = null;
            MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = null;
            MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = null;
            MostRecentPProjectionPAbsErrorInSettingThreatPoint = null;
            MostRecentDProjectionPAbsErrorInSettingThreatPoint = null;
            MostRecentPProjectionDAbsErrorInSettingThreatPoint = null;
            MostRecentDProjectionDAbsErrorInSettingThreatPoint = null;
            MostRecentPAbsErrorInSettingThreatPoint = null;
            MostRecentDAbsErrorInSettingThreatPoint = null;
            NEVForP = null;
            NEVForD = null;
            MostRecentPlaintiffOffer = null;
            MostRecentDefendantOffer = null;
            MostRecentBargainingDistance = null;
            MostRecentPPerceivedBargainingRange = null;
            MostRecentDPerceivedBargainingRange = null;
            BargainingDistanceList = null; // torecycle
            PPerceivedBargainingRangeList = null; // torecycle
            DPerceivedBargainingRangeList = null; // torecycle
            ChangeInPlaintiffOffer = null;
            ChangeInDefendantOffer = null;
            FakePProjectionOfPUtilityDelta = null;
            FakePProjectionOfDUtilityDelta = null;
            FakeDProjectionOfPUtilityDelta = null;
            FakeDProjectionOfDUtilityDelta = null;
            PUtilityProjectionDecisionNumber = null;
            DUtilityProjectionDecisionNumber = null;
            PWealthTranslationDecisionNumber = null;
            DWealthAtTimeOfDecisionBeingEvolved = null;
            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            UtilityRangeBargainingModuleProgress copy = new UtilityRangeBargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(UtilityRangeBargainingModuleProgress copy)
        {
            copy.PDecisionmakerWealthAtTimeOfDecisionBeingEvolved = PDecisionmakerWealthAtTimeOfDecisionBeingEvolved;
            copy.DWealthAtTimeOfDecisionBeingEvolved = DWealthAtTimeOfDecisionBeingEvolved;
            copy.PDecisionmakerWealthBeforeCurrentBargainingRound = PDecisionmakerWealthBeforeCurrentBargainingRound;
            copy.DWealthBeforeCurrentBargainingRound = DWealthBeforeCurrentBargainingRound;
            copy.PAggressivenessOverrideModified = PAggressivenessOverrideModified;
            copy.DAggressivenessOverrideModified = DAggressivenessOverrideModified;
            copy.PAggressivenessOverrideModifiedFinal = PAggressivenessOverrideModifiedFinal;
            copy.DAggressivenessOverrideModifiedFinal = DAggressivenessOverrideModifiedFinal;
            copy.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked = MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked;
            copy.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked = MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked;
            copy.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked = MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked;
            copy.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked = MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked;
            copy.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
            copy.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked;
            copy.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
            copy.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked;
            copy.MostRecentPProjectionPAbsErrorInSettingThreatPoint = MostRecentPProjectionPAbsErrorInSettingThreatPoint;
            copy.MostRecentPProjectionDAbsErrorInSettingThreatPoint = MostRecentPProjectionDAbsErrorInSettingThreatPoint;
            copy.MostRecentDProjectionPAbsErrorInSettingThreatPoint = MostRecentDProjectionPAbsErrorInSettingThreatPoint;
            copy.MostRecentDProjectionDAbsErrorInSettingThreatPoint = MostRecentDProjectionDAbsErrorInSettingThreatPoint;
            copy.MostRecentPAbsErrorInSettingThreatPoint = MostRecentPAbsErrorInSettingThreatPoint;
            copy.MostRecentDAbsErrorInSettingThreatPoint = MostRecentDAbsErrorInSettingThreatPoint;
            copy.NEVForP = NEVForP;
            copy.NEVForD = NEVForD;
            copy.MostRecentPlaintiffOffer = MostRecentPlaintiffOffer;
            copy.MostRecentDefendantOffer = MostRecentDefendantOffer;
            copy.MostRecentBargainingDistance = MostRecentBargainingDistance;
            copy.BargainingDistanceList = BargainingDistanceList == null ? null : BargainingDistanceList.ToList();
            copy.PPerceivedBargainingRangeList = PPerceivedBargainingRangeList == null ? null : PPerceivedBargainingRangeList.ToList();
            copy.MostRecentPPerceivedBargainingRange = MostRecentPPerceivedBargainingRange;
            copy.MostRecentDPerceivedBargainingRange = MostRecentDPerceivedBargainingRange;
            copy.DPerceivedBargainingRangeList = DPerceivedBargainingRangeList == null ? null : DPerceivedBargainingRangeList.ToList();
            copy.ChangeInPlaintiffOffer = ChangeInPlaintiffOffer;
            copy.ChangeInDefendantOffer = ChangeInDefendantOffer;
            copy.FakePProjectionOfPUtilityDelta = FakePProjectionOfPUtilityDelta;
            copy.FakePProjectionOfDUtilityDelta = FakePProjectionOfDUtilityDelta;
            copy.FakeDProjectionOfPUtilityDelta = FakeDProjectionOfPUtilityDelta;
            copy.FakeDProjectionOfDUtilityDelta = FakeDProjectionOfDUtilityDelta;
            copy.PUtilityProjectionDecisionNumber = PUtilityProjectionDecisionNumber;
            copy.DUtilityProjectionDecisionNumber = DUtilityProjectionDecisionNumber;
            copy.PWealthTranslationDecisionNumber = PWealthTranslationDecisionNumber;
            copy.DWealthAtTimeOfDecisionBeingEvolved = DWealthAtTimeOfDecisionBeingEvolved;
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            switch (variableNameForReport)
            {
                default:
                    break;
            }
            found = false;
            return base.GetNonFieldValueForReport(variableNameForReport, out found);
        }
    }
}


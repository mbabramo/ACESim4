using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingModuleProgress : GameModuleProgress
    {
        /* NOTE: Be sure to update CopyFieldInfo and CleanAfterRecycling when changing local variables */
        public int? BargainingRound;
        public bool IsFirstBargainingRound;
        public bool IsLastBargainingRound;
        public bool SettlementExists { get { if (SettlementProgress == null) return false; return SettlementProgress.CompleteSettlementReached(); } }
        public SettlementProgress SettlementProgress;
        public bool? SettlementExistsAfterFirstBargainingRound;
        public int SettlementFailedAfterBargainingRoundNum;
        public int? BargainingRoundInWhichSettlementReached;
        public double? POfferForDToConsiderInNextRound;
        public double? DOfferForPToConsiderInNextRound;
        public double? PEstimatePResultMostRecentBargainingRound;
        public double? DEstimatePResultMostRecentBargainingRound;
        public List<double> PDecisionInputs;
        public List<double> DDecisionInputs;
        public List<bool> SettlementBlockedThisBargainingRoundBecauseOfEvolution;
        public List<double?> POfferList;
        public List<double?> DOfferList;
        public int? CurrentBargainingRoundNumber;
        public bool CurrentBargainingRoundIsFirstRepetition;
        public bool CurrentBargainingRoundIsLastRepetition;
        public int? CurrentlyEvolvingBargainingRoundNumber;
        public int? CurrentBargainingSubclaimNumber;
        public int? CurrentlyEvolvingBargainingSubclaimNumber;
        public bool DisputeContinues;
        public ActionGroup FirstActionGroupForThisBargainingRoundIfDifferent = null;
        public ActionGroup FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent = null;


        static ConcurrentQueue<BargainingModuleProgress> RecycledBargainingModuleProgressQueue = new ConcurrentQueue<BargainingModuleProgress>();
        
        private static int NumRecycled;
        private const int MaxToRecycle = 500;
        public override void Recycle()
        {
            if (NumRecycled <= MaxToRecycle)
            {
                System.Threading.Interlocked.Increment(ref NumRecycled);
                RecycledBargainingModuleProgressQueue.Enqueue(this);
            }
        }

        public static new BargainingModuleProgress GetRecycledOrAllocate()
        {
            BargainingModuleProgress recycled = null;
            RecycledBargainingModuleProgressQueue.TryDequeue(out recycled);
            if (recycled != null)
            {
                System.Threading.Interlocked.Decrement(ref NumRecycled);
				recycled.CleanAfterRecycling();
                return recycled;
            }
            return new BargainingModuleProgress();
        }

        public override void CleanAfterRecycling()
        {
            BargainingRound = null;
            IsFirstBargainingRound = false;
            IsLastBargainingRound = false;
            SettlementProgress = null;
            SettlementExistsAfterFirstBargainingRound = null;
            BargainingRoundInWhichSettlementReached = null;
            POfferForDToConsiderInNextRound = null;
            DOfferForPToConsiderInNextRound = null;
            PEstimatePResultMostRecentBargainingRound = null;
            DEstimatePResultMostRecentBargainingRound = null;
            PDecisionInputs = null; // torecycle
            DDecisionInputs = null; // torecycle
            SettlementBlockedThisBargainingRoundBecauseOfEvolution = null; // torecycle
            POfferList = null; // torecycle
            DOfferList = null; // torecycle
            CurrentBargainingRoundNumber = null; // torecycle
            CurrentBargainingRoundIsFirstRepetition = false;
            CurrentBargainingRoundIsLastRepetition = false;
            CurrentlyEvolvingBargainingRoundNumber = null;
            CurrentBargainingSubclaimNumber = null;
            CurrentlyEvolvingBargainingSubclaimNumber = null;
            DisputeContinues = false;
            FirstActionGroupForThisBargainingRoundIfDifferent = null;
            FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent = null;

            base.CleanAfterRecycling();
        }

        public override GameModuleProgress DeepCopy()
        {
            BargainingModuleProgress copy = new BargainingModuleProgress();

            CopyFieldInfo(copy);
            return copy;
        }

        internal void CopyFieldInfo(BargainingModuleProgress copy)
        {
            copy.BargainingRound = BargainingRound;
            copy.IsFirstBargainingRound = IsFirstBargainingRound;
            copy.IsLastBargainingRound = IsLastBargainingRound;
            copy.SettlementProgress = SettlementProgress == null ? null : SettlementProgress.DeepCopy();
            copy.SettlementExistsAfterFirstBargainingRound = SettlementExistsAfterFirstBargainingRound;
            copy.SettlementFailedAfterBargainingRoundNum = SettlementFailedAfterBargainingRoundNum;
            copy.BargainingRoundInWhichSettlementReached = BargainingRoundInWhichSettlementReached;
            copy.POfferForDToConsiderInNextRound = POfferForDToConsiderInNextRound;
            copy.DOfferForPToConsiderInNextRound = DOfferForPToConsiderInNextRound;
            copy.PEstimatePResultMostRecentBargainingRound = PEstimatePResultMostRecentBargainingRound;
            copy.DEstimatePResultMostRecentBargainingRound = DEstimatePResultMostRecentBargainingRound;
            copy.PDecisionInputs = PDecisionInputs == null ? null : PDecisionInputs.ToList();
            copy.DDecisionInputs = DDecisionInputs == null ? null : DDecisionInputs.ToList();
            copy.SettlementBlockedThisBargainingRoundBecauseOfEvolution = SettlementBlockedThisBargainingRoundBecauseOfEvolution;
            copy.POfferList = POfferList == null ? null : POfferList.ToList();
            copy.DOfferList = DOfferList == null ? null : DOfferList.ToList();
            copy.CurrentBargainingRoundNumber = CurrentBargainingRoundNumber;
            copy.CurrentBargainingRoundIsFirstRepetition = CurrentBargainingRoundIsFirstRepetition;
            copy.CurrentBargainingRoundIsLastRepetition = CurrentBargainingRoundIsLastRepetition;
            copy.CurrentBargainingSubclaimNumber = CurrentBargainingSubclaimNumber;
            copy.CurrentlyEvolvingBargainingRoundNumber = CurrentlyEvolvingBargainingRoundNumber;
            copy.CurrentlyEvolvingBargainingSubclaimNumber = CurrentlyEvolvingBargainingSubclaimNumber;
            copy.DisputeContinues = DisputeContinues;
            copy.FirstActionGroupForThisBargainingRoundIfDifferent = FirstActionGroupForThisBargainingRoundIfDifferent;
            copy.FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent = FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent;
            base.CopyFieldInfo(copy);
        }

        public override object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            found = true; // assume for now
            switch (variableNameForReport)
            {

                case "SettlementExists":
                    found = true;
                    return SettlementExists;

                case "AgreedUponPaymentByDAsProportion":
                    found = true;
                    return SettlementExists ? (double?) SettlementProgress.OverallSuccessOfPlaintiff() : (double?) null;

                default:
                    found = false;
                    return null;
            }
        }

        public bool SettlementBlockedForSomeBargainingRoundBecauseOfEvolution()
        {
            return (SettlementBlockedThisBargainingRoundBecauseOfEvolution != null && SettlementBlockedThisBargainingRoundBecauseOfEvolution.Any(x => x == true));
        }
    }
}

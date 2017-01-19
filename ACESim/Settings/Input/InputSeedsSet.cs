using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class InputSeedsStorage
    {
        public bool StoreUsedInputSeedsSets()
        {
            return false;
        }

        const int iterationsThresholdBelowWhichToAlwaysStore = 1000;

        public InputSeedsRandomization randomizationApproach = InputSeedsRandomization.alwaysRandom;
        public bool enableInputMirroring = true;

        public InputSeeds GetInputSeeds(int numSeeds, long numIterations, bool alwaysStoreTheseInputSeeds = false, bool neverGenerateAllImmediately = false,
            bool[] flipSeed = null,
            int?[] substituteSeed = null)
        {
            InputSeeds inputSeedsToReturn = null;

            if (!neverGenerateAllImmediately && (alwaysStoreTheseInputSeeds || StoreUsedInputSeedsSets() || numIterations <= iterationsThresholdBelowWhichToAlwaysStore))
                inputSeedsToReturn = GetInputSeedsPotentiallyStored(numSeeds, numIterations, flipSeed, substituteSeed);
            else
                inputSeedsToReturn = new InputSeeds(numSeeds, numIterations, neverGenerateAllImmediately, randomizationApproach, flipSeed, substituteSeed, enableInputMirroring);

            return inputSeedsToReturn;
        }

        const int maxSizeOfSet = 20;
        internal List<InputSeeds> theInputSeeds = new List<InputSeeds>();
        object inputSeedsSetLock = new object();
        internal InputSeeds GetInputSeedsPotentiallyStored(int totalNumSeeds, long totalNumIterations,
            bool[] flipSeed,
            int?[] substituteSeed)
        {
            lock (inputSeedsSetLock)
            {
                InputSeeds theSeeds = null;
                theSeeds = theInputSeeds.SingleOrDefault(x => x.numSeeds == totalNumSeeds && x.numIterations == totalNumIterations && x.randomizationApproach == randomizationApproach);
                if (theSeeds == null)
                {
                    theSeeds = new InputSeeds(totalNumSeeds, totalNumIterations, false, randomizationApproach, flipSeed, substituteSeed, enableInputMirroring);
                    theInputSeeds.Add(theSeeds);
                    if (theInputSeeds.Count > maxSizeOfSet)
                        theInputSeeds.RemoveAt(0);
                }
                else
                {
                    theInputSeeds.Remove(theSeeds);
                    theInputSeeds.Add(theSeeds); // move to end
                }
                return theSeeds;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public enum InputSeedsRandomization
    {
        useOrdinary,
        useAlternate,
        alwaysRandom
    }

    [Serializable]
    public class InputSeeds
    {
        public int numSeeds { get; internal set; }
        public long numIterations { get; internal set; }
        public double[,] theGeneratedSeeds;
        public long[] offsets;
        internal InputSeedsColumnGenerator theGenerator;
        public bool generateAllImmediately = true;
        public InputSeedsRandomization randomizationApproach;
        bool[] flipSeed;
        int?[] substituteSeed;
        public bool inputMirroringEnabled = true;

        public double this[int seedNum, IterationID iterationID]
        {
            get
            {
                return GetSpecifiedSeed(seedNum, iterationID);
            }
        }

        private double GetSpecifiedSeed(int seedNum, IterationID iterationID)
        {
            long iterationNum = iterationID.GetIterationNumber(seedNum);
            bool doOddIterationSeedChanging = false;
            if (inputMirroringEnabled)
            { // we use the same input seeds twice in a row, BUT with specified changes
                doOddIterationSeedChanging = iterationNum % 2 == 1; // i.e., this is odd observation
                iterationNum /= 2;
            }
            int? substituteSeedNum = null;
            if (doOddIterationSeedChanging && seedNum < substituteSeed.Length) // note that we may have extra seeds -- i.e., one for oversampling coefficient
                substituteSeedNum = substituteSeed[seedNum];
            int seedNumToUse = substituteSeedNum ?? seedNum;
            long virtualIteration = iterationNum + offsets[seedNumToUse];
            double returnVal;
            if (generateAllImmediately)
            {
                while (virtualIteration >= numIterations)
                    virtualIteration -= numIterations;
                returnVal = theGeneratedSeeds[seedNumToUse, virtualIteration];
            }
            else
                returnVal = CalculateSeed(seedNumToUse, virtualIteration);
            if (doOddIterationSeedChanging && seedNum < flipSeed.Length && flipSeed[seedNum])
                returnVal = 1.0 - returnVal;
            return returnVal;
        }

        public InputSeeds(
            int theNumSeeds, 
            long theNumIterations, 
            bool neverGenerateAllImmediately, 
            InputSeedsRandomization randomizationApproachToUse,
            bool[] flipSeed,
            int?[] substituteSeed,
            bool enableInputMirroring)
        {
            this.randomizationApproach = randomizationApproachToUse;
            numSeeds = theNumSeeds;
            numIterations = theNumIterations;
            this.flipSeed = flipSeed;
            this.substituteSeed = substituteSeed;
            inputMirroringEnabled = 
                enableInputMirroring &&
                (
                    (flipSeed != null && flipSeed.Where(x => x == true).Any())
                    ||
                    (substituteSeed != null && substituteSeed.Where(x => x != null).Any())
                );
            ResetOffsetsIfNecessary();

            if (neverGenerateAllImmediately || numIterations * numSeeds > 100000) 
                generateAllImmediately = false; // too memory intensive -- we'll calculate these on the fly

            if (generateAllImmediately)
            {
                theGeneratedSeeds = new double[numSeeds, numIterations];
                EvenlySpaceSeeds();
            }
        }

        private void ResetOffsetsIfNecessary()
        {
            if (offsets == null || offsets.Length != numSeeds)
            {
                if (randomizationApproach == InputSeedsRandomization.useOrdinary)
                    InitializeOffsets();
                else if (randomizationApproach == InputSeedsRandomization.useAlternate)
                    AlternativeOffsets();
                else
                    RandomizeOffsets();
            }
        }

        public void EvenlySpaceSeeds()
        {
            for (int d = 0; d < numSeeds; d++)
            {
                //int randOffset = RandomGenerator.NextIntegerExclusiveOfSecondValue(0, numIterations);
                for (int i = 0; i < numIterations; i++)
                {
                    double theValue = CalculateSeed(d, i);
                    theGeneratedSeeds[d, i] = theValue;

                }
            }

            //for (int i = 0; i < numIterations; i++)
            //{
            //    for (int d = 0; d < numSeeds; d++)
            //    {
            //        Debug.Write(theGeneratedSeeds[d, i]);
            //        if (d < numSeeds - 1)
            //            Debug.Write(",");
            //        else
            //            Debug.WriteLine("");
            //    }
            //}

        }

        public double CalculateSeed(int d, long i)
        {
            return CalculateSeed_FasterAndViableForUnknownNumberOfIterations(d, i); // we have switched to this exclusively because of the advent of success replication. We need to make sure that the pseudo-numbers being generated are the same regardless of the baseline number of iterations. Even without success replication, we can't make an assumption about the number of iterations baseline when there is a decision that may or may not ultimately be reached.

            //if (i >= numIterations) // this can occur when we think there will be a set number of iterations, but some of the iterations then fail to reach a particular decision of the game, so we need to go beyond the specified number
            //    return CalculateSeed_FasterAndViableForUnknownNumberOfIterations(d, i);
            //else
            //    return CalculateSeed_SlowerButMoreEvenlyDistributed(d, i);
        }

        public double CalculateSeed_FasterAndViableForUnknownNumberOfIterations(int d, long i)
        { // faster but doesn't produce as good results based on some experimentation
            return FastPseudoRandom.GetRandom(i, d);
        }

        public double CalculateSeed_SlowerButMoreEvenlyDistributed(int d, long i)
        {
            int keyNum;
            switch (d)
            {
                case 0:
                    keyNum = 2;
                    break;
                case 1:
                    keyNum = 3;
                    break;
                case 2:
                    keyNum = 5;
                    break;
                case 3:
                    keyNum = 7;
                    break;
                case 4:
                    keyNum = 11;
                    break;
                case 5:
                    keyNum = 13;
                    break;
                case 6:
                    keyNum = 17;
                    break;
                case 7:
                    keyNum = 19;
                    break;
                case 8:
                    keyNum = 23;
                    break;
                case 9:
                    keyNum = 29;
                    break;
                case 10:
                    keyNum = 31;
                    break;
                default:
                    keyNum = Primes.Nth(d + 1);
                    break;
            }
            double availableSpaceWidth = 1.0;
            double availableSpaceStart = 0.0;
            double iterationsToProcess = (double)numIterations;
            long virtualIteration = i + 1; // +randOffset;
            //if (virtualIteration > numIterations)
            //    virtualIteration -= numIterations;

            while (iterationsToProcess >= 1)
            {
                availableSpaceWidth = availableSpaceWidth / keyNum;
                availableSpaceStart = availableSpaceStart + availableSpaceWidth * (virtualIteration % keyNum);
                virtualIteration /= keyNum;
                iterationsToProcess /= keyNum;
            }

            double theValue = availableSpaceStart + availableSpaceWidth / 2;
            return theValue;
        }

        public void InitializeOffsets()
        {
            offsets = new long[numSeeds];
            for (int s = 0; s < numSeeds; s++)
            {
                offsets[s] = 0;
            }
        }

        public void AlternativeOffsets()
        {
            offsets = new long[numSeeds];
            for (int s = 0; s < numSeeds; s++)
            {
                offsets[s] = s * numIterations / numSeeds;
            }
        }

        // After we evenly space seeds, we should mix things up on subsequent uses by changing the offsets for each dimension.
        public void RandomizeOffsets()
        {
            offsets = new long[numSeeds];
            for (int s = 0; s < numSeeds; s++)
            {
                offsets[s] = RandomGenerator.NextIntegerExclusiveOfSecondValue(0, (int) Math.Min((long) Int32.MaxValue, numIterations));
            }
        }

       
    }

}

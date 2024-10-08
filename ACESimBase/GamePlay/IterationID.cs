﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class IterationID
    {
        public long IterationNumber;

        public int IterationNumIntUnchecked => unchecked ( (int) IterationNumber );

        public IterationID()
        {
        }

        public IterationID(long iterationNumber)
        {
            IterationNumber = iterationNumber;
        }

        public double GetRandomNumberBasedOnIterationID(byte randomIndex)
        {
            //return RandomGenerator.NextDouble();
            return new ConsistentRandomSequenceProducer(IterationNumber).GetDoubleAtIndex((int) randomIndex);
            //return FastPseudoRandom.GetRandom(IterationNumber, (int)randomIndex);
            // Note: We found that the following didn't produce truly randomly distributed numbers. Using the same Random instance per thread works, but that doesn't produce predictable, consistent results.
            //int seed = (IterationNumber * 1000 + randomIndex).GetHashCode();
            //return GetRandomDoubleFromRandomSeed(seed);
        }

        public virtual long GetIterationNumber()
        {
            return IterationNumber;
        }

        public virtual IterationID DeepCopy()
        {
            return new IterationID(IterationNumber);
        }

        public virtual long MaxIterationNum()
        {
            return IterationNumber;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (int)IterationNumber;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;
            return ((IterationID)obj).IterationNumber == IterationNumber;
        }

        public override string ToString()
        {
            return IterationNumber.ToString();
        }
    }

    [Serializable]
    public class IterationIDComposite : IterationID
    {
        public IterationID Source;
        public bool[] KeepSourceInputSeed; // for each input seed, we designate whether to keep the source input seed or this one

        public IterationIDComposite(long iterationNumberToSuperimpose, IterationID source, bool[] keepSourceInputSeed) : base(iterationNumberToSuperimpose)
        {
            Source = source;
            KeepSourceInputSeed = keepSourceInputSeed;
        }

        public override string ToString()
        {
            return Source.ToString() + " >>> " + IterationNumber.ToString();
        }


        public override IterationID DeepCopy()
        {
            return new IterationIDComposite(IterationNumber, Source.DeepCopy(), KeepSourceInputSeed.ToArray());
        }

        public override long MaxIterationNum()
        {
            return Math.Max(IterationNumber, Source.MaxIterationNum());
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int result = (int)IterationNumber;
                result = (result * 397) ^ Source.GetHashCode();
                return result;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;
            return ((IterationIDComposite)obj).IterationNumber == IterationNumber && Equals(((IterationIDComposite)obj).Source, Source);
        }
    }

}
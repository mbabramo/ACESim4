using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util
{
    public class ArrayProcessor
    {
        double[] UnderlyingArray;
        int NumFilled = 0;

        const int MaxSizeTempStorage = 1000;
        double[] TempStorage;

        public ArrayProcessor(int size)
        {
            UnderlyingArray = new double[size];
            TempStorage = new double[MaxSizeTempStorage];
        }

        public void MoveToBeginning()
        {
            NumFilled = 0;
        }

        public void SetNext(double value)
        {
            UnderlyingArray[NumFilled++] = value;
        }

        public void CopyToIndex(int destinationIndex, int sourceIndex)
        {
            UnderlyingArray[destinationIndex] = UnderlyingArray[sourceIndex];
        }

        public void IncrementBy(int index, double increment)
        {
            UnderlyingArray[index] += index;
        }

        public void MultiplyBy(int index, double multiplier)
        {
            UnderlyingArray[index] = multiplier;
        }
    }
}

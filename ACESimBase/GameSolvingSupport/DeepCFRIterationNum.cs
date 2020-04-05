namespace ACESim
{
    public readonly struct DeepCFRIterationNum
    {
        public readonly int IterationNum;
        public readonly int VariationNum;

        public DeepCFRIterationNum(int iterationNum, int variationNum)
        {
            IterationNum = iterationNum;
            VariationNum = variationNum;
        }

        public DeepCFRIterationNum NextIteration()
        {
            return new DeepCFRIterationNum(IterationNum + 1, 0);
        }

        public DeepCFRIterationNum NextVariation()
        {
            return new DeepCFRIterationNum(IterationNum, VariationNum + 1);
        }

        public double GetRandomDouble(byte decisionNum)
        {
            return new ConsistentRandomSequenceProducer(AsLong()).GetDoubleAtIndex(decisionNum);
        }

        public int GetRandomInt(byte decisionNum, int minValue, int exclusiveMaxValue)
        {
            return new ConsistentRandomSequenceProducer(AsLong()).GetIntAtIndex(decisionNum, minValue, exclusiveMaxValue);
        }

        long AsLong()
        {
            return doubleInt2long(IterationNum, VariationNum);
        }
        static long doubleInt2long(int a1, int a2)
        {
            long b = a2;
            b = b << 32;
            b = b | (uint)a1;
            return b;
        }
    }
}
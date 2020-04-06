namespace ACESim
{
    public readonly struct DeepCFRObservationNum
    {
        public readonly int ObservationNum;
        public readonly int VariationNum;

        public DeepCFRObservationNum(int iterationNum, int variationNum)
        {
            ObservationNum = iterationNum;
            VariationNum = variationNum;
        }

        public DeepCFRObservationNum NextObservation()
        {
            return new DeepCFRObservationNum(ObservationNum + 1, 0);
        }

        public DeepCFRObservationNum NextVariation()
        {
            return new DeepCFRObservationNum(ObservationNum, VariationNum + 1);
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
            return doubleInt2long(ObservationNum, VariationNum);
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
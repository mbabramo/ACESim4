using ACESimBase.Util.Randomization;

namespace ACESimBase.GameSolvingSupport.DeepCFR
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
            return new ConsistentRandomSequenceProducer(ObservationNum, VariationNum).GetDoubleAtIndex(decisionNum);
        }

        public int GetRandomInt(byte decisionNum, int minValue, int exclusiveMaxValue)
        {
            return new ConsistentRandomSequenceProducer(ObservationNum, VariationNum).GetIntAtIndex(decisionNum, minValue, exclusiveMaxValue);
        }
    }
}
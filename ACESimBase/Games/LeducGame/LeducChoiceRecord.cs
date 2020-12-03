namespace ACESim
{
    public class LeducChoiceRecord
    {
        public LeducChoiceStage Stage;
        public LeducPlayerChoice Choice;

        public override string ToString()
        {
            return $"{Stage}: {Choice}";
        }

        public LeducChoiceRecord DeepCopy()
        {
            return new LeducChoiceRecord() { Stage = Stage, Choice = Choice };
        }
    }
}

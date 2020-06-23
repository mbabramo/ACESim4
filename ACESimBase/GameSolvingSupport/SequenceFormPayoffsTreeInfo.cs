namespace ACESim
{
    public class SequenceFormPayoffsTreeInfo
    {
        public double ChanceProbability = 1.0;
        public int RowPlayerCumulativeChoice;
        public int ColPlayerCumulativeChoice;

        public SequenceFormPayoffsTreeInfo(double chanceProbability, int rowPlayerCumulativeChoice, int colPlayerCumulativeChoice)
        {
            if (chanceProbability == 0)
            {
                var DEBUG = 0;
            }
            ChanceProbability = chanceProbability;
            RowPlayerCumulativeChoice = rowPlayerCumulativeChoice;
            ColPlayerCumulativeChoice = colPlayerCumulativeChoice;
        }

        public SequenceFormPayoffsTreeInfo()
        {
            ChanceProbability = 1.0;
            RowPlayerCumulativeChoice = 0;
            ColPlayerCumulativeChoice = 0;
        }

        public SequenceFormPayoffsTreeInfo WithChanceProbabilityMultiplied(double multiplyBy)
        {
            return new SequenceFormPayoffsTreeInfo(ChanceProbability * multiplyBy, RowPlayerCumulativeChoice, ColPlayerCumulativeChoice);
        }

        public SequenceFormPayoffsTreeInfo WithCumulativeChoice(bool rowPlayer, int cumulativeChoice) => rowPlayer ? WithRowPlayerCumulativeChoice(cumulativeChoice) : WithColPlayerCumulativeChoice(cumulativeChoice);

        public SequenceFormPayoffsTreeInfo WithRowPlayerCumulativeChoice(int rowPlayerCumulativeChoice) => new SequenceFormPayoffsTreeInfo(ChanceProbability, rowPlayerCumulativeChoice, ColPlayerCumulativeChoice);

        public SequenceFormPayoffsTreeInfo WithColPlayerCumulativeChoice(int colPlayerCumulativeChoice) => new SequenceFormPayoffsTreeInfo(ChanceProbability, RowPlayerCumulativeChoice, colPlayerCumulativeChoice);
    }

}

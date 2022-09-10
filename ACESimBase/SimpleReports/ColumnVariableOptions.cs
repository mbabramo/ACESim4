namespace ACESim
{
    public enum ColumnVariableOptions
    {
        Mean,
        Stdev,
        Min,
        Max,
        AbsOfMean, // note: the absolute value of the mean, not the mean of the absolute values
        SquareOfMean // note: the square of the mean, not the mean of the squares
    }
}

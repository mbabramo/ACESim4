using ACESim;

namespace ACESimBase.Util.Tikz
{
    public record TikzPoint(double x, double y)
    {
        public override string ToString()
        {
            return $"({x.ToSignificantFigures(3)},{y.ToSignificantFigures(3)})";
        }
    }
}

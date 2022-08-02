using ACESim;
using System;

namespace ACESimBase.Util.Tikz
{
    public record TikzPoint(double x, double y)
    {
        public override string ToString()
        {
            return $"({x.ToSignificantFigures(5)},{y.ToSignificantFigures(5)})";
        }

        public string DrawCircleCommand(string fillColor = "black", int points = 1) => $@"\draw[fill={fillColor}]({x},{y}) circle ({points} pt)";

        public TikzPoint WithXTranslation(double xShift) => new TikzPoint(x + xShift, y);
        public TikzPoint WithYTranslation(double yShift) => new TikzPoint(x, y + yShift);
        public TikzPoint WithTranslation(double xShift, double yShift) => new TikzPoint(x + xShift, y + yShift);
    }
}

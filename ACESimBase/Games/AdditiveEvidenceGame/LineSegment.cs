using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public record struct LineSegment(double xStart, double xEnd, double yStart, double slope)
    {
        public override string ToString()
        {
            return $"({Math.Round(xStart, 2)}, {Math.Round(yStart, 2)}) - ({Math.Round(xEnd, 2)}, {Math.Round(yEnd, 2)}) slope {Math.Round(slope, 2)}";
        }

        public double yEnd => yStart + (xEnd - xStart) * slope;
        public double yAvg => 0.5 * yStart + 0.5 * yEnd;
        public double xAvg => 0.5 * xStart + 0.5 * xEnd;
        public double yVal(double xVal) => yStart + slope * (xVal - xStart);
        public double xVal(double yVal) => xStart + (yVal - yStart) / slope;
        public bool IntersectsHorizontalLine(double yVal) => yVal > yStart && yVal < yEnd;
        public double XIntersection(LineSegment other)
        {
            if (this == other)
                throw new System.Exception();
            return (xStart - other.xStart) / (other.slope - slope);
        }

        public bool YApproximatelyEqual(LineSegment other) => Math.Abs(yStart - other.yStart) < 1E-6 && Math.Abs(yEnd - other.yEnd) < 1E-6;

        public (LineSegment l1, LineSegment l2) DivideAtX(double xVal)
        {
            return (this with { xEnd = xVal }, this with { xStart = xVal, yStart = yVal(xVal) });
        }

        private static (double low, double high)? GetOverlap((double low, double high) first, (double low, double high) second)
        {
            if (first.low <= second.high && first.high >= second.low)
            {
                var result = (Math.Max(first.low, second.low), Math.Min(first.high, second.high));
                return result;
            }
            return null;
        }

        private IEnumerable<(double startY, double endY)> DivideAroundOverlapYRegion((double startY, double endY) overlapRegion)
        {
            // we assume that yRange is encompassed here.
            if (yStart < overlapRegion.startY)
                yield return (yStart, overlapRegion.startY);
            yield return overlapRegion;
            if (yEnd > overlapRegion.endY)
                yield return (overlapRegion.endY, yEnd);
        }

        private LineSegment GetSubsegmentFromYRegion((double startY, double endY) yRegion) => this with { xStart = xVal(yRegion.startY), xEnd = xVal(yRegion.endY), yStart = yRegion.startY };

        /// <summary>
        /// Given this and another line segment, we want to divide each line segment and then pair the two sets up so that each pair consists of one subsequent from this and from the other line segment, and the two elements of each pair either have the same y range or entirely nonoverlapping y ranges. 
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public IEnumerable<(LineSegment l1, LineSegment l2)> GetPairsOfNonoverlappingAndEntirelyOverlappingYRanges(LineSegment other)
        {
            var thisYRange = (yStart, yEnd);
            var otherYRange = (other.yStart, other.yEnd);
            var yOverlap = GetOverlap(thisYRange, otherYRange);
            if (yOverlap == null || (yOverlap.Value == thisYRange && yOverlap.Value == otherYRange))
                yield return (this, other);
            else
            {
                foreach (var thisYRegion in DivideAroundOverlapYRegion(yOverlap.Value))
                {
                    foreach (var otherYRegion in other.DivideAroundOverlapYRegion(yOverlap.Value))
                    {
                        yield return (GetSubsegmentFromYRegion(thisYRegion), other.GetSubsegmentFromYRegion(otherYRegion));
                    }
                }
            }
        }

        public List<LineSegment> Truncate(double yVal, bool yValIsMin)
        {
            if ((yValIsMin && yEnd <= yVal) /* segment must be at least yVal, but entire segment is below this */ || (!yValIsMin && yStart >= yVal) /* segment must be no more than yVal, but entire segment is above this */)
                return new List<LineSegment> { new LineSegment(xStart, xEnd, yVal, 0) };
            if (!IntersectsHorizontalLine(yVal))
                return new List<LineSegment>() { this };
            double xIntersect = xVal(yVal);
            if (yValIsMin)
            {
                return new List<LineSegment>()
                {
                    new LineSegment(xStart, xIntersect, yVal, 0),
                    new LineSegment(xIntersect, xEnd, yVal, slope)
                };
            }
            else
            {
                return new List<LineSegment>()
                {
                    new LineSegment(xStart, xIntersect, yStart, slope),
                    new LineSegment(xIntersect, xEnd, yVal, 0)
                };
            }
        }

        public static List<LineSegment> GetTruncatedLineSegments(List<(double low, double high)> xVals, List<double> yVals, double slope, double truncationValue, bool truncationIsMin) => Consolidate(Truncate(GetFromXValsYStartsAndSlope(xVals, yVals, slope), truncationValue, truncationIsMin)).ToList();

        private static List<LineSegment> GetFromXValsYStartsAndSlope(List<(double low, double high)> xVals, List<double> yVals, double slope) => xVals.Zip(yVals, (x, y) => new LineSegment(x.low, x.high, y, slope)).ToList();

        private static List<LineSegment> Truncate(List<LineSegment> segments, double yVal, bool yValIsMin)
        {
            List<LineSegment> l = new List<LineSegment>();
            foreach (var source in segments)
                l.AddRange(source.Truncate(yVal, yValIsMin));
            return Consolidate(l).ToList();
        }
        private bool Continues(LineSegment previous) => previous.xEnd == xStart && previous.yEnd == yStart && slope == previous.slope;
        private static IEnumerable<LineSegment> Consolidate(List<LineSegment> segments)
        {
            LineSegment? pending = null;
            for (int i = 0; i < segments.Count; i++)
            {
                LineSegment s = segments[i];
                if (pending == null)
                    pending = s;
                else if (!s.Continues(pending.Value))
                {
                    yield return pending.Value;
                    pending = s;
                }
                else // this is a continuation
                    pending = pending.Value with { xEnd = s.xEnd };
            }
            if (pending is LineSegment p)
                yield return p;
        }

        public IEnumerable<LineSegment> EnumeratePotentialReplacements(double[] slopes, double[] potentialYPoints)
        {
            foreach (var slope in slopes)
                foreach (double potentialYPoint in potentialYPoints)
                    yield return new LineSegment(xStart, xEnd, potentialYPoint, slope);
        }


    }
}

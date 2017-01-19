using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;

namespace ACESim
{
    [Serializable]
    public class KDTree : ISerializationPrep
    {
        public bool ReadOnly = false;
        int Level;
        int NumDimensions;
        public double[] LowerBounds;
        public double[] UpperBounds;
        public bool NeighborsCalculated = false;
        public List<KDTree> Neighbors;
        public List<KDTree> NeighborsAndNearNeighbors;
        public KDTree TopOfKDTree;
        public List<Point> PointsWithin;
        int DimensionSplitFromParent; // e.g., 0 means that this is like the parent in every way except that dimension 0 was split
        int NewDimensionSplit; // split between this and its children, if any
        double? Splitpoint; // location of split in dimension NewDimensionSplit
        int SplitThreshold; // split after we have this number of points
        public bool SuspendSplit = false; // set this to true to stop splitting. After setting it back to false, can then call split if necessary. Doing this when adding a large number of points can produce a more balanced tree.

        KDTree Parent;
        KDTree ChildLowerValue;
        KDTree ChildHigherValue;

        object changeLock = new object();

        public KDTree DeepCopy(KDTree parent)
        {
            KDTree newTree = new KDTree();
            newTree.Parent = parent;
            if (ChildHigherValue != null)
            {
                newTree.ChildLowerValue = ChildLowerValue.DeepCopy(newTree);
                newTree.ChildHigherValue = ChildHigherValue.DeepCopy(newTree);
            }
            newTree.Level = Level;
            newTree.NumDimensions = NumDimensions;
            newTree.LowerBounds = LowerBounds.ToArray();
            newTree.UpperBounds = UpperBounds.ToArray();
            newTree.NeighborsCalculated = false; // we'll calculate neighbors later
            newTree.PointsWithin = PointsWithin.ToList();
            newTree.DimensionSplitFromParent = DimensionSplitFromParent;
            newTree.NewDimensionSplit = NewDimensionSplit;
            newTree.Splitpoint = Splitpoint;
            newTree.SplitThreshold = SplitThreshold;
            newTree.changeLock = new object();
            if (parent == null)
            {
                newTree.CalculateNeighborsRecursively();
                newTree.RecursiveAction(x => x.AssignNeighborsAndNearNeighbors());
            }
            return newTree;
        }

        public KDTree()
        {
        }

        public KDTree(int theNumDimensions, List<Point> thePointsWithin, KDTree theParent, double[] lowerBounds, double[] upperBounds, int dimensionSplitFromParent, int splitThreshold, bool considerSplitting = true)
        {
            NumDimensions = theNumDimensions;
            PointsWithin = thePointsWithin;
            Parent = theParent;
            if (Parent == null)
                Level = 1;
            else
                Level = Parent.Level + 1;
            LowerBounds = lowerBounds;
            UpperBounds = upperBounds;
            DimensionSplitFromParent = dimensionSplitFromParent;
            NewDimensionSplit = DimensionSplitFromParent + 1;
            if (NewDimensionSplit == NumDimensions)
                NewDimensionSplit = 0;
            SplitThreshold = splitThreshold;
            if (considerSplitting)
                SplitKDTreeIfNecessary(); 
        }

        public virtual void PreSerialize()
        {
            Neighbors = null;
            NeighborsAndNearNeighbors = null;
            ChildHigherValue = null;
            ChildLowerValue = null;
        }

        public virtual void UndoPreSerialize()
        {
            bool readOnly = ReadOnly;
            ReadOnly = false;
            CompleteInitializationAfterAddingAllPoints();
            ReadOnly = readOnly;
        }

        public override string ToString()
        {
            return PointsWithin.Count.ToString() + " points in " + String.Concat(LowerBounds.Zip(UpperBounds, (lower, upper) => lower.ToString() + "<-->" + upper.ToString() + " "));
        }

        public string ToIndentedString()
        {
            string theString = "";
            for (int i = 0; i < Level - 1; i++)
                theString += "   ";
            return theString + ToString() + "\n";
        }

        public string ToTreeString()
        {
            if (ChildHigherValue == null)
                return ToIndentedString();
            return ToIndentedString() + ChildHigherValue.ToTreeString() + ChildLowerValue.ToTreeString();
        }

        public IEnumerable<KDTree> EnumerateLeaves(bool higherValuesFirst = true)
        {
            if (ChildHigherValue != null)
            {
                IEnumerable<KDTree> higherValues = ChildHigherValue.EnumerateLeaves(higherValuesFirst);
                IEnumerable<KDTree> lowerValues = ChildLowerValue.EnumerateLeaves(higherValuesFirst);
                IEnumerable<KDTree> first, second;
                if (higherValuesFirst)
                {
                    first = higherValues;
                    second = lowerValues;
                }
                else
                {
                    first = lowerValues;
                    second = higherValues;
                }
                foreach (var tree in first)
                    yield return tree;
                foreach (var tree in second)
                    yield return tree;
                yield break;
            }
            else
            {
                yield return this;
                yield break;
            }
        }

        public int RecursivePointCount()
        {
            if (ChildHigherValue == null)
                return PointsWithin.Count;
            else
                return ChildHigherValue.RecursivePointCount() + ChildLowerValue.RecursivePointCount();
        }

        public void AddPoint(Point thePoint)
        {
            KDTree topKDTree = GetTopKDTree();
            if (topKDTree != this)
                topKDTree.AddPoint(thePoint);
            else
            {
                if (ReadOnly)
                    throw new Exception("Attempted to add a point to a read-only tree.");
                bool didAdd = AddPointIfInKDTree(thePoint);
                if (!didAdd)
                {
                    StretchKDTreeToFitPoint(thePoint);
                    AddPointIfInKDTree(thePoint);
                }
            }
        }

        public void RemovePoint(Point thePoint)
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Attempted to remove a point from a read-only tree.");
            GetSmallestContainingKDTree(thePoint).RemovePointHelper(thePoint);
        }

        private void RemovePointHelper(Point thePoint)
        {
            PointsWithin.Remove(thePoint);
            if (Parent != null)
                Parent.RemovePointHelper(thePoint);
        }

        internal void StretchKDTreeToFitPoint(Point thePoint)
        {
            //if (GetTopKDTree().ReadOnly)
            //    throw new Exception("Cannot stretch a read only tree."); Note: We will allow stretching since it doesn't change any of the points within the tree.
            lock (changeLock)
            {
                for (int d = 0; d < NumDimensions; d++)
                {
                    double theValue = thePoint.GetValue(d);
                    if (double.IsNaN(theValue))
                        throw new Exception("Invalid number.");
                    if (theValue > UpperBounds[d] || theValue < LowerBounds[d])
                        StretchKDTreeHierarchyToFitPoint(theValue, d);
                }
            }
        }

        internal void StretchKDTreeHierarchyToFitPoint(double theValue, int dimension)
        {
            if (theValue > UpperBounds[dimension])
            {
                UpperBounds[dimension] = theValue; 
            }
            else if (theValue < LowerBounds[dimension])
            {
                LowerBounds[dimension] = theValue; 
            }

            if (ChildHigherValue != null)
                StretchChildrenKDTreesToFitThis(dimension);
        }

        internal void StretchChildrenKDTreesToFitThis(int dimension)
        {
            if (ChildLowerValue == null)
                return;
            if (ChildLowerValue.LowerBounds[dimension] == ChildHigherValue.LowerBounds[dimension])
            { // children are same on this dimension, should be same as here
                ChildLowerValue.LowerBounds[dimension] = ChildHigherValue.LowerBounds[dimension] = LowerBounds[dimension];
                ChildLowerValue.UpperBounds[dimension] = ChildHigherValue.UpperBounds[dimension] = UpperBounds[dimension];
            }
            else
            { // there is a split on this dimension. we should not change the midpoint.
                ChildLowerValue.LowerBounds[dimension] = LowerBounds[dimension];
                ChildHigherValue.UpperBounds[dimension] = UpperBounds[dimension];
            }
            ChildHigherValue.StretchChildrenKDTreesToFitThis(dimension);
            ChildLowerValue.StretchChildrenKDTreesToFitThis(dimension);
        }

        public bool AddPointIfInKDTree(Point thePoint)
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot add a point to a read only tree.");
            bool isIn = (thePoint.IsInKDTree(this));
            if (isIn)
            {
                PointsWithin.Add(thePoint);
                bool didAdd = false;
                if (ChildHigherValue != null)
                    didAdd = ChildHigherValue.AddPointIfInKDTree(thePoint);
                if (ChildLowerValue != null && !didAdd)
                    didAdd = ChildLowerValue.AddPointIfInKDTree(thePoint);
                SplitKDTreeIfNecessary();
                return true;
            }
            else
                return false;
        }

        public double GetClosestDistanceToPoint(Point thePoint, bool useSquaredDistance = false, double? stopIfSquaredDistanceGreaterThan = null)
        {
            double squaredDistance = 0;
            for (int d = 0; d < NumDimensions && (stopIfSquaredDistanceGreaterThan == null || stopIfSquaredDistanceGreaterThan > squaredDistance); d++)
            {
                double unsquaredDistanceThisDimension = 0;
                double pointLoc = thePoint.GetValue(d);
                if (pointLoc >= LowerBounds[d] && pointLoc <= UpperBounds[d])
                    unsquaredDistanceThisDimension = 0;
                else if (pointLoc < LowerBounds[d])
                    unsquaredDistanceThisDimension = LowerBounds[d] - pointLoc;
                else
                    unsquaredDistanceThisDimension = pointLoc - UpperBounds[d];
                squaredDistance += unsquaredDistanceThisDimension * unsquaredDistanceThisDimension;
            }
            if (useSquaredDistance)
                return squaredDistance;
            return Math.Sqrt(squaredDistance);
        }

        internal Point GetNearestNeighborFromList(Point measureFrom, bool excludeExactMatch, List<Point> pointsToCheck)
        {
            List<Point> ordered = pointsToCheck.OrderBy(x => x.DistanceTo(measureFrom)).ToList();
            while (ordered.Any() && excludeExactMatch && ordered[0].IsColocated(measureFrom))
                ordered.RemoveAt(0);
            if (ordered.Any())
                return ordered[0];
            return null;
        }

        //List<KDTree> AllKDTrees;
        //public List<KDTree> GetAllKDTrees()
        //{
        //    KDTree top = GetTopKDTree();
        //    if (top == this)
        //    {
        //        if (AllKDTrees == null)
        //        {
        //            AllKDTrees = new List<KDTree>();
        //            GetAllKDTreeHelper(AllKDTrees);
        //        }
        //        return AllKDTrees;
        //    }
        //    else
        //    {
        //        if (AllKDTrees == null)
        //        {
        //            AllKDTrees = top.GetAllKDTrees().OrderBy(x => x.D
        //    }
        //}
            
        //public void GetAllKDTreeHelper(List<KDTree> theList)
        //{
        //    theList.Add(this);
        //    if (ChildHigherValue != null)
        //    {
        //        ChildHigherValue.GetAllKDTreeHelper(theList);
        //        ChildLowerValue.GetAllKDTreeHelper(theList);
        //    }
        //}

        //public void ResetAllKDTreesTracking()
        //{
        //    KDTree top = GetTopKDTree();
        //    if (top == this)
        //    {
        //        AllKDTrees = null;
        //        ChildHigherValue.
        //        }
        //}



        public List<Point> GetKNearestNeighborsByCheckingAllPointsWithin(Point measureFrom, bool excludeExactMatch, int k)
        {
            return PointsWithin.Where(x => !excludeExactMatch || measureFrom.DistanceTo(x) != 0).OrderBy(x => measureFrom.DistanceTo(x)).Take(k).ToList();
        }

        public Point GetNearestNeighborByCheckingAllPointsWithin(Point measureFrom, bool excludeExactMatch)
        {
            double bestDistance = 9E+99;
            Point bestPoint = null;
            foreach (var otherPoint in PointsWithin)
            {
                double distance = measureFrom.DistanceTo(otherPoint, useSquaredDistance: true, stopIfSquaredDistanceGreaterThan: bestDistance);
                if (bestDistance == -1 || (distance < bestDistance && (!excludeExactMatch || distance != 0.0)))
                {
                    bestDistance = distance;
                    bestPoint = otherPoint;
                }
            }
            //if (bestPoint != GetNearestNeighbor(measureFrom, excludeExactMatch))
            //    throw new Exception();
            return bestPoint;
        }

        //public KDTree GetSmallestContainingKDTree(Point thePoint)
        //{
        //    ProfileSimple.Start("GetSmallestContainingKDTree", true);
        //    KDTree returnVal = GetSmallestContainingKDTreeHelper(thePoint);
        //    ProfileSimple.End("GetSmallestContainingKDTree", true, true);
        //    return returnVal;
        //}

        public KDTree GetSmallestContainingKDTree(Point thePoint)
        {
            // Profiling indicates this takes roughly 1% of the time of the nearest neighbor algorithm to run
            if (Parent == null)
            {
                bool mustStretch = !thePoint.IsInKDTree(this);
                if (mustStretch)
                    StretchKDTreeToFitPoint(thePoint);
                return GetSmallestContainingKDTreeWhenWeKnowPointIsInThisKDTree(thePoint);
            }
            else return GetTopKDTree().GetSmallestContainingKDTree(thePoint);
        }

        public KDTree GetSmallestContainingKDTreeWithAtLeastOnePoint(Point thePoint, bool excludeExactMatch)
        {
            // Profiling indicates this takes roughly 1% of the time of the nearest neighbor algorithm to run
            if (Parent == null)
            {
                bool mustStretch = !thePoint.IsInKDTree(this);
                if (mustStretch)
                    StretchKDTreeToFitPoint(thePoint);
                return GetSmallestContainingKDTreeWithAtLeastOnePointWhenWeKnowPointIsInThisKDTree(thePoint);
            }
            else return GetTopKDTree().GetSmallestContainingKDTreeWithAtLeastOnePoint(thePoint, excludeExactMatch);
        }

        internal KDTree GetSmallestContainingKDTreeWithAtLeastOnePointWhenWeKnowPointIsInThisKDTree(Point thePoint)
        {
            if (ChildHigherValue == null)
                return this;
            if (thePoint.GetLocation()[NewDimensionSplit] > Splitpoint)
            {
                int pointsWithin = ChildHigherValue.PointsWithin.Count;
                if (pointsWithin == 0 || (pointsWithin == 1 && ChildHigherValue.PointsWithin[0].IsColocated(thePoint)))
                    return this;
                return ChildHigherValue.GetSmallestContainingKDTreeWithAtLeastOnePointWhenWeKnowPointIsInThisKDTree(thePoint);
            }
            else
            {
                int pointsWithin = ChildLowerValue.PointsWithin.Count;
                if (pointsWithin == 0 || (pointsWithin == 1 && ChildLowerValue.PointsWithin[0].IsColocated(thePoint)))
                    return this;
                return ChildLowerValue.GetSmallestContainingKDTreeWithAtLeastOnePointWhenWeKnowPointIsInThisKDTree(thePoint);
            }
        }

        internal KDTree GetSmallestContainingKDTreeWhenWeKnowPointIsInThisKDTree(Point thePoint)
        {
            if (ChildHigherValue == null)
                return this;
            if (thePoint.GetLocation()[NewDimensionSplit] > Splitpoint)
                return ChildHigherValue.GetSmallestContainingKDTreeWhenWeKnowPointIsInThisKDTree(thePoint);
            else
                return ChildLowerValue.GetSmallestContainingKDTreeWhenWeKnowPointIsInThisKDTree(thePoint);
        }

        public KDTree GetTopKDTree()
        {
            if (TopOfKDTree != null)
                return TopOfKDTree;
            if (Parent == null)
                TopOfKDTree = this;
            else
                TopOfKDTree = Parent.GetTopKDTree();
            return TopOfKDTree;
        }

        public void RecursiveAction(Action<KDTree> action)
        {
            Stack<KDTree> treesToProcess = new Stack<KDTree>();
            treesToProcess.Push(GetTopKDTree());
            while (treesToProcess.Any())
            {
                KDTree toProcess = treesToProcess.Pop();
                action(toProcess);
                if (toProcess.ChildHigherValue != null)
                {
                    treesToProcess.Push(toProcess.ChildHigherValue);
                    treesToProcess.Push(toProcess.ChildLowerValue);
                }
            }
        }

        public void CompleteInitializationAfterAddingAllPoints()
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot initialize a read only tree.");
            RecursiveAction(x => x.CompleteInitializationHelper());
            RecursiveAction(x => x.AssignNeighborsAndNearNeighbors());
            ReadOnly = true;
        }

        public void CompleteInitializationHelper()
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot change a read only tree.");
            SuspendSplit = false;
            SplitKDTreeIfNecessary();
            GetTopKDTree();
            CalculateNeighbors(); // first get all regular neighbors calculated, then do this
        }

        public Point GetApproximateNearestNeighbor(Point measureFrom, bool excludeExactMatch)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            var smallestContainingKDTree = GetSmallestContainingKDTreeWithAtLeastOnePoint(measureFrom, excludeExactMatch);
            Point theResult = smallestContainingKDTree.GetNearestNeighborByCheckingAllPointsWithin(measureFrom, excludeExactMatch);
            return theResult;
        }

        public Point GetNearestNeighbor(Point measureFrom, bool excludeExactMatch)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            //ProfileSimple.Start("GetSmallestContainingKDTree", true); // Note that this will not work when running in parallel -- not thread-safe
            var smallestContainingKDTree = GetSmallestContainingKDTree(measureFrom);
            Point theResult = smallestContainingKDTree.GetNearestNeighborStartingFromSmallestContainingKDTree(measureFrom, excludeExactMatch);
            return theResult;
            //return GetKNearestNeighbors(measureFrom, excludeExactMatch, 1).First();
        }

        public List<Point> GetKNearestNeighbors(Point measureFrom, bool excludeExactMatch, int k)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            //ProfileSimple.Start("GetSmallestContainingKDTree", true); // Note that this will not work when running in parallel -- not thread-safe
            var smallestContainingKDTree = GetSmallestContainingKDTree(measureFrom);
            //ProfileSimple.End("GetSmallestContainingKDTree", true);
            List<Point> theResult = smallestContainingKDTree.GetKNearestNeighborsStartingFromSmallestContainingKDTree(measureFrom, excludeExactMatch, k);
            //List<Point> alternativeMethod = GetTopKDTree().GetKNearestNeighborsByCheckingAllPointsWithin(measureFrom, excludeExactMatch, k);
            //if (!theResult.SequenceEqual<Point>(alternativeMethod))
            //    Debug.WriteLine("Failure!");
            return theResult;
        }

        internal class PointAndDistance
        {
            public Point ThePoint;
            public double Distance;
            public PointAndDistance(Point measureFrom, Point thePoint, bool useSquaredDistance = false, double? stopIfSquaredDistanceGreaterThan = null)
            {
                ThePoint = thePoint;
                Distance = measureFrom.DistanceTo(thePoint, useSquaredDistance, stopIfSquaredDistanceGreaterThan);
            }
            public PointAndDistance(Point measureFrom, Point thePoint, double distanceOrSquaredDistance)
            {
                ThePoint = thePoint;
                Distance = distanceOrSquaredDistance;
            }
        }

        //[NonSerialized]
        //ThreadLocal<KDTreeAndDistance[]> KDTreeList = new ThreadLocal<KDTreeAndDistance[]>(); // reuse the same array to save time
        //public void FreeKDTreeMemory()
        //{
        //    KDTreeList = null;
        //    KDTreeList = new ThreadLocal<KDTreeAndDistance[]>();
        //}


        internal Point GetNearestNeighborStartingFromSmallestContainingKDTree(Point measureFrom, bool excludeExactMatch)
        {
            KDTreeAndDistance[] KDTreeList = null;
            if (!NeighborsCalculated)
                CalculateNeighbors();
            List<KDTree> KDTreesAlreadyTried = null;
            Point bestPointSoFar = null;
            PointAndDistance bestPointSoFarWithDistance = null;
            double? maximumSquaredDistance = null;
            if (PointsWithin.Count > 0 && !excludeExactMatch)
            {
                foreach (var point in PointsWithin)
                {
                    double? maxSquaredDistanceForThisPoint = maximumSquaredDistance;
                    if (maximumSquaredDistance == null || point.MaximumSquaredDistanceForNearestNeighbor < maximumSquaredDistance)
                        maxSquaredDistanceForThisPoint = point.MaximumSquaredDistanceForNearestNeighbor;
                    double squaredDistanceForThisPoint = measureFrom.DistanceTo(point, true, maxSquaredDistanceForThisPoint); // may return less than actual squared distance if the running total by dimension exceeds maxSquaredDistanceForThisPoint
                    if (maxSquaredDistanceForThisPoint == null || squaredDistanceForThisPoint < maxSquaredDistanceForThisPoint)
                    {
                        bestPointSoFar = point;
                        maximumSquaredDistance = squaredDistanceForThisPoint; // we don't want anything going over this now
                    }
                }
                if (bestPointSoFar != null)
                    bestPointSoFarWithDistance = new PointAndDistance(measureFrom, bestPointSoFar, (double)maximumSquaredDistance);
            }
            else
            {
                List<PointAndDistance> candidates = GetAtLeastKNearbyPoints(1, excludeExactMatch ? measureFrom : null, out KDTreesAlreadyTried).Select(x => new PointAndDistance(measureFrom, x, true)).ToList();
                foreach (var candidate in candidates)
                {
                    if (maximumSquaredDistance == null || candidate.Distance < maximumSquaredDistance)
                    {
                        bestPointSoFarWithDistance = candidate;
                        maximumSquaredDistance = bestPointSoFarWithDistance.Distance;
                    }
                }
            }
            int laterCandidatesListSize = 0;
            if (PointsWithin.Count > 0 && NeighborsAndNearNeighbors != null)
            {
                if (KDTreeList == null || KDTreeList.Length != NeighborsAndNearNeighbors.Count)
                    KDTreeList = new KDTreeAndDistance[NeighborsAndNearNeighbors.Count];
                for (int n = 0; n < NeighborsAndNearNeighbors.Count; n++)
                {
                    var neighbor = NeighborsAndNearNeighbors[n];
                    if (neighbor.PointsWithin.Count == 0)
                        KDTreeList[n] = null;
                    else
                    {
                        double closestDistance = neighbor.GetClosestDistanceToPoint(measureFrom, true, maximumSquaredDistance);
                        if (maximumSquaredDistance == null || closestDistance < maximumSquaredDistance)
                            KDTreeList[n] = new KDTreeAndDistance() { KDTree = neighbor, Distance = closestDistance };
                        else
                            KDTreeList[n] = null;
                    }
                }
            }
            else
            {
                KDTreesAlreadyTried = new List<KDTree>();
                KDTreeList = GetKDTreesWithinDistanceOfPoint(measureFrom, (double)maximumSquaredDistance).Where(x => !KDTreesAlreadyTried.Contains(x.KDTree)).ToArray();
            }
            int numCandidatesAdded = 0;
            for (int n = 0; n < KDTreeList.Count(); n++)
            {
                KDTreeAndDistance KDTree = KDTreeList[n];
                if (KDTree != null && KDTree.Distance < maximumSquaredDistance)
                {
                    for (int n2 = 0; n2 < KDTree.KDTree.PointsWithin.Count; n2++)
                    {
                        Point point = KDTree.KDTree.PointsWithin[n2];

                        // We could just use this, but since this is time sensitive, we'll save the function call. double distance = point.DistanceTo(measureFrom, true, bestSoFar.Distance);
                        double? maxSquaredDistanceForThisPoint = maximumSquaredDistance;
                        if (maximumSquaredDistance == null || point.MaximumSquaredDistanceForNearestNeighbor < maximumSquaredDistance)
                            maxSquaredDistanceForThisPoint = point.MaximumSquaredDistanceForNearestNeighbor;
                        double squaredDistance = 0;
                        for (int d = 0; d < NumDimensions && (maxSquaredDistanceForThisPoint == null || maxSquaredDistanceForThisPoint > squaredDistance); d++)
                        {
                            double unsquaredDistance = point.GetValue(d) - measureFrom.GetValue(d);
                            double squaredDistanceThisDimension = unsquaredDistance * unsquaredDistance;
                            squaredDistance += squaredDistanceThisDimension;
                        }
                        if (squaredDistance < maxSquaredDistanceForThisPoint)
                        {
                            bestPointSoFarWithDistance = new PointAndDistance(measureFrom, point, squaredDistance);
                            maximumSquaredDistance = bestPointSoFarWithDistance.Distance;
                        }
                    }
                }
            }
            return bestPointSoFarWithDistance == null ? (Point)null : bestPointSoFarWithDistance.ThePoint;
        }

        internal List<Point> GetKNearestNeighborsStartingFromSmallestContainingKDTree(Point measureFrom, bool excludeExactMatch, int k)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            //ProfileSimple.Start("GetNearbyKPoints", true);
            List<KDTree> KDTreesAlreadyTried = null;
            IEnumerable<PointAndDistance> candidates = GetAtLeastKNearbyPoints(k, excludeExactMatch ? measureFrom : null, out KDTreesAlreadyTried).Select(x => new PointAndDistance(measureFrom, x));
            SortedList<double, Point> sortedCandidates = new SortedList<double, Point>();
            foreach (var candidate in candidates)
            {
                while (sortedCandidates.ContainsKey(candidate.Distance)) // shouldn't happen much, but we need a hack to get around the fact that SortedList doesn't allow duplicate keys
                    candidate.Distance *= 1.000001;
                sortedCandidates.Add(candidate.Distance, candidate.ThePoint);
            }
            //ProfileSimple.End("GetNearbyKPoints", true, true);
            //ProfileSimple.Start("GetKDTreesWithinDistanceOfPoint", true);
            double farthestDistance = sortedCandidates.Keys[k - 1]; // candidates.OrderBy(x => x.Distance).Take(k).ToList()[k - 1].Distance;
            int laterCandidatesListSize = 0;
            List<KDTreeAndDistance> KDTreeList = GetKDTreesWithinDistanceOfPoint(measureFrom, farthestDistance).Where(x => !KDTreesAlreadyTried.Contains(x.KDTree)).ToList();
            //ProfileSimple.End("GetKDTreesWithinDistanceOfPoint", true, true);
            //ProfileSimple.Start("AddingPointsFromKDTrees", true);
            int numCandidatesAdded = 0;
            foreach (KDTreeAndDistance KDTree in KDTreeList)
            {
                if (KDTree.Distance < farthestDistance)
                {
                    foreach (var point in KDTree.KDTree.PointsWithin)
                    {
                        double distance = point.DistanceTo(measureFrom);
                        if (distance < farthestDistance)
                        {
                            bool zeroDistanceAlreadyKeyed = false;
                            while (sortedCandidates.ContainsKey(distance) && !zeroDistanceAlreadyKeyed) // shouldn't happen much, but we need a hack to get around the fact that SortedList doesn't allow duplicate keys
                            {
                                if (distance == 0)
                                    zeroDistanceAlreadyKeyed = true;
                                else
                                    distance *= 1.000001;
                            }
                            if (!zeroDistanceAlreadyKeyed)
                                sortedCandidates.Add(distance, point);
                        }
                    }
                    farthestDistance = sortedCandidates.Keys[k - 1];
                }
            }
            //ProfileSimple.End("AddingPointsFromKDTrees", true, true);
            //ProfileSimple.Start("Compiling final", true);
            var finalList = sortedCandidates.Values.Take(k).ToList();
            //ProfileSimple.End("Compiling final", true, true);
            if (finalList.Count != k)
                throw new Exception("Internal error on GetKNearestNeighbors.");
            return finalList;
        }

        internal class KDTreeToTry
        {
            public KDTree KDTree;
            public bool PointsHaveBeenAdded = false;
            public bool NeighborsHaveBeenAdded = false;

            public void AddPoints(List<Point> listToAddTo, Point excludePoint)
            {
                foreach (var point in KDTree.PointsWithin)
                {
                    if (excludePoint == null || !point.IsColocated(excludePoint))
                        listToAddTo.Add(point);
                }
                //listToAddTo.AddRange(KDTree.PointsWithin.Where(x => excludePoint == null || !x.IsColocated(excludePoint)));
                PointsHaveBeenAdded = true;
            }

            public void AddUntriedNeighborsToList(List<KDTreeToTry> KDTreesToTry)
            {
                foreach (KDTree neighbor in KDTree.Neighbors)
                {
                    if (!KDTreesToTry.Any(x => x.KDTree == neighbor))
                        KDTreesToTry.Add(new KDTreeToTry() { KDTree = neighbor });
                }
                NeighborsHaveBeenAdded = true;
            }
        }

        internal List<Point> GetAtLeastKNearbyPoints(int k, Point excludePoint, out List<KDTree> KDTreesAlreadyTried)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            List<Point> listOfPoints = new List<Point>();
            List<KDTreeToTry> KDTreesToTry = new List<KDTreeToTry>() { new KDTreeToTry() { KDTree = this } };
            bool done = false;
            while (!done)
            {
                bool inadequateNumberFound = false;
                KDTreeToTry nextKDTreeToTry = null; 
                while (nextKDTreeToTry == null && !inadequateNumberFound)
                {
                    nextKDTreeToTry = KDTreesToTry.FirstOrDefault(x => x.PointsHaveBeenAdded == false);
                    if (nextKDTreeToTry == null)
                    {
                        KDTreeToTry nextToAddNeighborsOf = KDTreesToTry.FirstOrDefault(x => x.NeighborsHaveBeenAdded == false);
                        bool throwExceptionIfInadequate = false;
                        if (nextToAddNeighborsOf == null)
                        {
                            if (throwExceptionIfInadequate)
                                throw new Exception("Adequate number of points count not be found in nearest neighbors search. Check whether you are looking for more neighbors than exist in the smoothing set (e.g., your iterations to run < iterations to smooth).");
                            else
                                inadequateNumberFound = true;
                        }
                        else
                            nextToAddNeighborsOf.AddUntriedNeighborsToList(KDTreesToTry);
                    }
                }
                if (!inadequateNumberFound)
                    nextKDTreeToTry.AddPoints(listOfPoints, excludePoint);
                done = listOfPoints.Count >= k || inadequateNumberFound;
            }
            KDTreesAlreadyTried = KDTreesToTry.Where(x => x.PointsHaveBeenAdded).Select(x => x.KDTree).ToList();
            return listOfPoints;
        }

        internal class KDTreeToTry2
        {
            public KDTree KDTree;
            public bool HasBeenMeasured = false;
            public bool FitsWithinDistance = false;
            public bool NeighborsHaveBeenAdded = false;
            public double DistanceToThisKDTree;

            public void MeasureDistanceToKDTreeAndPossiblyAddNeighborsToList(Point measureFrom, double distance, List<KDTreeToTry2> KDTreesTriedSoFar)
            {
                DistanceToThisKDTree = KDTree.GetClosestDistanceToPoint(measureFrom);
                if (DistanceToThisKDTree <= distance)
                {
                    FitsWithinDistance = true; 
                    // Now, add untried neighbors to the list
                    foreach (KDTree neighbor in KDTree.Neighbors)
                    {
                        bool hasAlreadyBeenTried = false;
                        for (int i = 0; i < KDTreesTriedSoFar.Count && !hasAlreadyBeenTried; i++)
                        {
                            hasAlreadyBeenTried = KDTreesTriedSoFar[i].KDTree == neighbor;
                        }
                        if (!hasAlreadyBeenTried) // the above is faster than: !KDTreesTriedSoFar.Any(x => x.KDTree == neighbor))
                            KDTreesTriedSoFar.Add(new KDTreeToTry2() { KDTree = neighbor });
                    }
                    NeighborsHaveBeenAdded = true;
                }
                HasBeenMeasured = true;
            }
        }

        [Serializable]
        public class KDTreeAndDistance
        {
            public KDTree KDTree;
            public double Distance;
        }

        internal List<KDTreeAndDistance> GetKDTreesWithinDistanceOfPoint(Point pointWithinThisKDTree, double distance)
        {
            double[] lowerBoundsForGetKDTreesWithinDistanceOfPoint;
            double[] upperBoundsForGetKDTreesWithinDistanceOfPoint;
            lowerBoundsForGetKDTreesWithinDistanceOfPoint = new double[NumDimensions];
            upperBoundsForGetKDTreesWithinDistanceOfPoint = new double[NumDimensions];

            double[] pointLoc = pointWithinThisKDTree.GetLocation();
            for (int d = 0; d < NumDimensions; d++)
            {
                double locInDimension = pointLoc[d];
                lowerBoundsForGetKDTreesWithinDistanceOfPoint[d] = locInDimension - distance;
                upperBoundsForGetKDTreesWithinDistanceOfPoint[d] = locInDimension + distance;
            }
            List<KDTreeAndDistance> returnList = GetTopKDTree().GetKDTreesWithinDistanceOfPointHelper(pointWithinThisKDTree, distance, lowerBoundsForGetKDTreesWithinDistanceOfPoint, upperBoundsForGetKDTreesWithinDistanceOfPoint);
            return returnList;
        }

        internal enum KDTreeIntersectionStatus
        {
            FitsCompletelyWithinBounds,
            Intersects,
            DoesNotIntersect
        }

        internal KDTreeIntersectionStatus AssessIntersectionWithHypotheticalKDTree(double[] lowerBounds, double[] upperBounds)
        {
            bool fitsCompletelyWithin = true; // assume this fits completely within until shown otherwise
            bool intersects = true; // assume intersection until shown otherwise
            for (int d = 0; d < NumDimensions && (fitsCompletelyWithin || intersects); d++)
            {
                if (fitsCompletelyWithin)
                { // determine if we are still completely within the bounds
                    if (LowerBounds[d] < lowerBounds[d] || UpperBounds[d] > upperBounds[d])
                        fitsCompletelyWithin = false;
                };
                if (!fitsCompletelyWithin)
                { // determine if we still intersect
                    if (UpperBounds[d] < lowerBounds[d] || LowerBounds[d] > upperBounds[d])
                        intersects = false;
                };
            }
            if (fitsCompletelyWithin)
                return KDTreeIntersectionStatus.FitsCompletelyWithinBounds;
            if (intersects)
                return KDTreeIntersectionStatus.Intersects;
            return KDTreeIntersectionStatus.DoesNotIntersect;
        }

        internal List<KDTreeAndDistance> GetKDTreesWithinDistanceOfPointHelper(Point thePoint, double distance, double[] lowerBounds, double[] upperBounds)
        {
            Stack<KDTree> kdToTry = new Stack<KDTree>();
            kdToTry.Push(this);
            List<KDTreeAndDistance> hcList = new List<KDTreeAndDistance>();
            while (kdToTry.Any())
            {
                KDTree candidate = kdToTry.Pop();
                bool intersects = true; // assume intersection until shown otherwise
                for (int d = 0; d < NumDimensions && intersects; d++)
                {
                    if (candidate.UpperBounds[d] < lowerBounds[d] || candidate.LowerBounds[d] > upperBounds[d])
                        intersects = false;
                }
                if (intersects)
                {
                    if (candidate.ChildHigherValue != null)
                    {
                        kdToTry.Push(candidate.ChildHigherValue);
                        kdToTry.Push(candidate.ChildLowerValue);
                    }
                    else
                    { // Must confirm whether to add this candidate KDTree. Just because it's in the giant KDTree represented by lowerBounds and upperBounds doesn't mean that it's actually within the relevant distance (since it could be a KDTree outside the hypersphere).let's see how far it is from the point.
                        double closestDistanceToPoint = 0; // we could just call candidate.GetClosestDistanceToPoint, but because this is such a tight loop, we copy in the relevant code instead
                        double[] closestSpot = new double[NumDimensions];
                        double squaredDistance = 0;
                        for (int d = 0; d < NumDimensions; d++)
                        {
                            double unsquaredDistanceThisDimension = 0;
                            double pointLoc = thePoint.GetValue(d);
                            if (pointLoc >= LowerBounds[d] && pointLoc <= candidate.UpperBounds[d])
                                unsquaredDistanceThisDimension = 0;
                            else if (pointLoc < candidate.LowerBounds[d])
                                unsquaredDistanceThisDimension = candidate.LowerBounds[d] - pointLoc;
                            else
                                unsquaredDistanceThisDimension = pointLoc - candidate.UpperBounds[d];
                            squaredDistance += unsquaredDistanceThisDimension * unsquaredDistanceThisDimension;
                        }
                        closestDistanceToPoint = Math.Sqrt(squaredDistance);
                        if (closestDistanceToPoint < distance)
                            hcList.Add(new KDTreeAndDistance() { KDTree = candidate, Distance = closestDistanceToPoint });
                    }
                }
            }
            return hcList.OrderBy(x => x.Distance).ToList();
        }

        internal List<KDTreeAndDistance> GetKDTreesWithinDistanceOfPoint_Old(Point pointWithinThisKDTree, double distance)
        {
            if (!NeighborsCalculated)
                CalculateNeighbors();
            List<KDTreeToTry2> KDTreesToTry = new List<KDTreeToTry2>() { new KDTreeToTry2() { KDTree = this } };
            int nextCandidateKDTree = -1;
            bool done = false;
            while (!done)
            {
                //ProfileSimple.Start("FindNextUnmeasuredKDTree");
                bool keepLookingForUnmeasuredKDTree = true;
                KDTreeToTry2 nextKDTree = null;
                while (keepLookingForUnmeasuredKDTree)
                {
                    nextCandidateKDTree++;
                    if (nextCandidateKDTree + 1 > KDTreesToTry.Count)
                        keepLookingForUnmeasuredKDTree = false;
                    else
                    {
                        KDTreeToTry2 candidateKDTree = KDTreesToTry[nextCandidateKDTree];
                        keepLookingForUnmeasuredKDTree = candidateKDTree.HasBeenMeasured;
                        if (!keepLookingForUnmeasuredKDTree)
                            nextKDTree = candidateKDTree;
                    }
                }
                //ProfileSimple.End("FindNextUnmeasuredKDTree", true);
                if (nextKDTree == null)
                    done = true;
                else
                {
                    //ProfileSimple.Start("ProcessNextKDTree", true);
                    nextKDTree.MeasureDistanceToKDTreeAndPossiblyAddNeighborsToList(pointWithinThisKDTree, distance, KDTreesToTry);
                    //ProfileSimple.End("ProcessNextKDTree", true, true);
                }
            }
            //ProfileSimple.Start("CalculateReturnList", true);
            List<KDTreeAndDistance> returnList = KDTreesToTry.Where(x => x.FitsWithinDistance).OrderBy(x => x.DistanceToThisKDTree).Select(x => new KDTreeAndDistance() { KDTree = x.KDTree, Distance = x.DistanceToThisKDTree }).ToList();
            //ProfileSimple.End("CalculateReturnList", true, true);
            return returnList;
        }

        public void ResetNeighborsInfo()
        {
            NeighborsCalculated = false;
            foreach (KDTree neighbor in Neighbors)
                neighbor.NeighborsCalculated = false;
        }

        public bool Contains(KDTree potentiallyContainedKDTree)
        {
            bool goodSoFar = true;
            for (int d = 0; goodSoFar && d < NumDimensions; d++)
            {
                goodSoFar = (LowerBounds[d] <= potentiallyContainedKDTree.LowerBounds[d] && potentiallyContainedKDTree.LowerBounds[d] <= UpperBounds[d]) &&
(LowerBounds[d] <= potentiallyContainedKDTree.UpperBounds[d] && potentiallyContainedKDTree.UpperBounds[d] <= UpperBounds[d]);
            }
            return goodSoFar;
        }

        public bool Overlaps(KDTree otherKDTree)
        {
            bool overlapsSoFar = true;
            for (int d = 0; overlapsSoFar && d < NumDimensions; d++)
            {
                bool noOverlap = (LowerBounds[d] < otherKDTree.LowerBounds[d] && UpperBounds[d] < otherKDTree.LowerBounds[d]) || (otherKDTree.LowerBounds[d] < LowerBounds[d] && otherKDTree.UpperBounds[d] < LowerBounds[d]);
                overlapsSoFar = !noOverlap;
            }
            return overlapsSoFar;
        }

        public void CalculateNeighborsRecursively()
        {
            CalculateNeighbors();
            if (ChildHigherValue != null)
            {
                ChildHigherValue.CalculateNeighborsRecursively();
                ChildLowerValue.CalculateNeighborsRecursively();
            }
        }

        public void CalculateNeighbors()
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot change a read only tree.");
                //ProfileSimple.Start("CalculateNeighbors", true);
            Neighbors = GetNeighbors(true);
            NeighborsCalculated = true;
            //ProfileSimple.End("CalculateNeighbors", true, true);
        }

        public List<KDTree> GetNeighbors(bool leafNeighborsOnly)
        {
            return GetTopKDTree().GetNeighborsRecursiveStartingAtTopOfKDTree(this, leafNeighborsOnly, new List<KDTree>());
        }


        public void AssignNeighborsAndNearNeighbors()
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot change a read only tree.");
            NeighborsAndNearNeighbors = GetNeighborsAndNearNeighbors();
        }

        public List<KDTree> GetNeighborsAndNearNeighbors()
        {
            ArbitrarySpot centerPoint = new ArbitrarySpot(LowerBounds.Zip(UpperBounds, (l, u) => (l + u) / 2.0).ToArray(), NumDimensions);
            ArbitrarySpot lowestPoint = new ArbitrarySpot(LowerBounds, NumDimensions);
            ArbitrarySpot highestPoint = new ArbitrarySpot(UpperBounds, NumDimensions);
            double distanceFromLowestToHighest = lowestPoint.DistanceTo(highestPoint);
            return GetKDTreesWithinDistanceOfPoint(centerPoint, distanceFromLowestToHighest).Select(x => x.KDTree).Where(x => x != this).ToList();
        }

        internal List<KDTree> GetNeighborsRecursiveStartingAtTopOfKDTree(KDTree originalKDTree, bool leafNeighborsOnly, List<KDTree> currentList, int recursionLevel = 1)
        {
            if (recursionLevel > 500)
                Debug.WriteLine("Problem is here.");
            bool iHaveChildren = ChildHigherValue != null;
            if (Overlaps(originalKDTree) && !(this == originalKDTree))
            {
                if (!leafNeighborsOnly || !iHaveChildren)
                    currentList.Add(this);
                if (iHaveChildren)
                {
                    ChildHigherValue.GetNeighborsRecursiveStartingAtTopOfKDTree(originalKDTree, leafNeighborsOnly, currentList, recursionLevel + 1);
                    ChildLowerValue.GetNeighborsRecursiveStartingAtTopOfKDTree(originalKDTree, leafNeighborsOnly, currentList, recursionLevel + 1);
                }
            }
            return currentList;
        }

        public void SplitKDTreeIfNecessary(int recursionLevel = 0)
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Cannot change a read only tree.");
            if (SuspendSplit)
                return;
            bool shouldSplit = ChildLowerValue == null && PointsWithin.Count >= SplitThreshold;
            if (shouldSplit)
                SplitKDTree(recursionLevel);
        }

        public void SplitKDTree(int recursionLevel)
        {
            lock (changeLock)
                SplitKDTreeHelper(recursionLevel);
        }

        internal double GetMedian(double[] sourceNumbers)
        {
            //Framework 2.0 version of this method. there is an easier way in F4        
            if (sourceNumbers == null || sourceNumbers.Length == 0)
                return 0D;

            //make sure the list is sorted, but use a new array
            double[] sortedPNumbers = (double[])sourceNumbers.Clone();
            sourceNumbers.CopyTo(sortedPNumbers, 0);
            Array.Sort(sortedPNumbers);

            //get the median
            int size = sortedPNumbers.Length;
            int mid = size / 2;
            double median = (size % 2 != 0) ? (double)sortedPNumbers[mid] : ((double)sortedPNumbers[mid] + (double)sortedPNumbers[mid - 1]) / 2;
            return median;
        }

        private void SplitKDTreeHelper(int recursionLevel)
        {
            if (GetTopKDTree().ReadOnly)
                throw new Exception("Attempted to split a read-only tree.");
            int numTries = 0;
            bool success = false; 
            List<Point> pointsInChildLowerValue = null;
            List<Point> pointsInChildHigherValue = null;
            double lowerBound = 0;
            double upperBound = 0;
            while (numTries < NumDimensions && !success)
            {
                lowerBound = LowerBounds[NewDimensionSplit];
                upperBound = UpperBounds[NewDimensionSplit];
                bool useMidpoint = false;
                if (useMidpoint)
                    Splitpoint = (lowerBound + upperBound) / 2;
                else
                    Splitpoint = GetMedian(PointsWithin.Select(x => x.GetValue(NewDimensionSplit)).ToArray());
                pointsInChildLowerValue = new List<Point>();
                pointsInChildHigherValue = new List<Point>();
                foreach (Point point in PointsWithin)
                {
                    double value = point.GetValue(NewDimensionSplit);
                    // if the tree is perfectly symmetrical, we want to have a perfect mirror, so we must take into account where the median point should go
                    bool addToLowerValue = (value < Splitpoint) ||
                                            (value == Splitpoint &&
                                                (Parent == null ||
                                                this == Parent.ChildHigherValue
                                                )
                                                );
                    if (addToLowerValue)
                        pointsInChildLowerValue.Add(point);
                    else 
                        pointsInChildHigherValue.Add(point);
                }
                success = pointsInChildHigherValue.Count != PointsWithin.Count && pointsInChildLowerValue.Count != PointsWithin.Count;
                if (!success)
                {
                    numTries++;
                    NewDimensionSplit++;
                    if (NewDimensionSplit == NumDimensions)
                        NewDimensionSplit = 0;
                }
            }
            if (success)
            {
                double[] lowerBoundsChildLowerValue = new double[NumDimensions];
                double[] upperBoundsChildLowerValue = new double[NumDimensions];
                double[] lowerBoundsChildHigherValue = new double[NumDimensions];
                double[] upperBoundsChildHigherValue = new double[NumDimensions];
                for (int d = 0; d < NumDimensions; d++)
                {
                    if (d == NewDimensionSplit)
                    {
                        lowerBoundsChildLowerValue[d] = lowerBound;
                        lowerBoundsChildHigherValue[d] = (double)Splitpoint;
                        upperBoundsChildLowerValue[d] = (double)Splitpoint;
                        upperBoundsChildHigherValue[d] = upperBound;
                    }
                    else
                    {
                        lowerBoundsChildLowerValue[d] = upperBoundsChildLowerValue[d] = LowerBounds[d];
                        lowerBoundsChildHigherValue[d] = upperBoundsChildHigherValue[d] = UpperBounds[d];
                    }
                }
                ChildLowerValue = new KDTree(NumDimensions, pointsInChildLowerValue, this, lowerBoundsChildLowerValue, lowerBoundsChildHigherValue, NewDimensionSplit, SplitThreshold, false);
                ChildHigherValue = new KDTree(NumDimensions, pointsInChildHigherValue, this, upperBoundsChildLowerValue, upperBoundsChildHigherValue, NewDimensionSplit, SplitThreshold, false);
                // could be dividing all points in one direction, so we may need to further subdivide; also may need further subdivision if split was previously suspended
                const int maxRecursionLevel = 5;
                if (recursionLevel <= maxRecursionLevel)
                {
                    ChildLowerValue.SplitKDTreeIfNecessary(recursionLevel + 1);
                    ChildHigherValue.SplitKDTreeIfNecessary(recursionLevel + 1);
                }
            }
        }

        public void ConfirmEqual(KDTree anotherTree)
        {
            if (ChildHigherValue == null)
            {
                if (!PointsWithin.SequenceEqual(anotherTree.PointsWithin))
                    throw new Exception();
                if (!LowerBounds.SequenceEqual(anotherTree.LowerBounds))
                    throw new Exception();
                if (!UpperBounds.SequenceEqual(anotherTree.UpperBounds))
                    throw new Exception();
                if (!Neighbors.Select(x => x.ToString()).SequenceEqual(anotherTree.Neighbors.Select(x => x.ToString())))
                    throw new Exception();
                if (!NeighborsAndNearNeighbors.Select(x => x.ToString()).SequenceEqual(anotherTree.NeighborsAndNearNeighbors.Select(x => x.ToString())))
                    throw new Exception();
                if (!(Level == anotherTree.Level))
                    throw new Exception();
                if (!(NumDimensions == anotherTree.NumDimensions))
                    throw new Exception();
                if (!(NeighborsCalculated == anotherTree.NeighborsCalculated))
                    throw new Exception();
                if (!(DimensionSplitFromParent == anotherTree.DimensionSplitFromParent))
                    throw new Exception();
                if (!(NewDimensionSplit == anotherTree.NewDimensionSplit))
                    throw new Exception();
                if (!(Splitpoint == anotherTree.Splitpoint))
                    throw new Exception();
                if (!(SplitThreshold == anotherTree.SplitThreshold))
                    throw new Exception();
                if (!(SuspendSplit == anotherTree.SuspendSplit))
                    throw new Exception();
            }
            else
            {
                ChildHigherValue.ConfirmEqual(anotherTree.ChildHigherValue);
                ChildLowerValue.ConfirmEqual(anotherTree.ChildLowerValue);
            }

        }


    }

    

}


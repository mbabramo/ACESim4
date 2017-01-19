using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public enum OrderingConstraint
    {
        Before,
        CloseBefore, // not necessarily immediately before, but as close as possible
        ImmediatelyBefore,
        IdeallyBefore,
        IdeallyAfter,
        ImmediatelyAfter,
        CloseAfter,
        After
    }

    public class ConstrainedPair<T>
    {
        public T First;
        public T Second;
        public OrderingConstraint Constraint;

        public override string ToString()
        {
            return First.ToString() + " " + Constraint.ToString() + " " + Second.ToString();
        }

        public ConstrainedPair<T> AsBeforeRelation()
        {
            if (FirstMustBeBeforeSecond() || Constraint == OrderingConstraint.IdeallyBefore)
                return this;

            OrderingConstraint newConstraint;
            if (Constraint == OrderingConstraint.ImmediatelyAfter)
                newConstraint = OrderingConstraint.ImmediatelyBefore;
            else if (Constraint == OrderingConstraint.IdeallyAfter)
                newConstraint = OrderingConstraint.IdeallyBefore;
            else if (Constraint == OrderingConstraint.CloseAfter)
                newConstraint = OrderingConstraint.CloseBefore;
            else if (Constraint == OrderingConstraint.After)
                newConstraint = OrderingConstraint.Before;
            else
                throw new Exception("Internal error.");
            return new ConstrainedPair<T> { First = Second, Second = First, Constraint = newConstraint };
        }

        public ConstrainedPair<int> AsPositionConstraint(List<T> items)
        {
            int first = items.IndexOf(First, 0);
            int second = items.IndexOf(Second, 0);
            if (first == -1 || second == -1)
                throw new Exception("List must contain item.");
            return new ConstrainedPair<int>() { First = first, Second = second, Constraint = Constraint };
        }

        public bool FirstMustBeBeforeSecond()
        {
            return (Constraint == OrderingConstraint.Before || Constraint == OrderingConstraint.CloseBefore || Constraint == OrderingConstraint.ImmediatelyBefore);
        }
    }

    public static class ConstrainedOrder<T>
    {

        public static List<T> Order(List<T> items, List<ConstrainedPair<T>> constraints)
        {
            List<ConstrainedPair<T>> Constraints;
            List<ConstrainedPair<int>> ConstraintsByPosition;
            bool[,] MustBeBefore;

            Constraints = constraints.Select(x => x.AsBeforeRelation()).Distinct().ToList();

            foreach (var constraint in Constraints)
                if (constraint.Constraint == OrderingConstraint.ImmediatelyBefore && constraints.Any(x => x != constraint && (x.Second.Equals(constraint.Second) || x.First.Equals(constraint.First)) && x.Constraint == OrderingConstraint.ImmediatelyBefore))
                    throw new Exception("Cannot have two or more items that must be immediately before the same item, or two items that an item must be immediately before.");

            ConstraintsByPosition = Constraints.Select(x => x.AsPositionConstraint(items)).ToList();

            // Create a table indicating all the binary before relations and looking for inconsistencies.
            MustBeBefore = new bool[items.Count, items.Count];
            foreach (var c in ConstraintsByPosition)
                if (c.FirstMustBeBeforeSecond())
                    MustBeBefore[c.First, c.Second] = true;
            FindDerivativeRelations(items, MustBeBefore);
            
            // Now, where possible, move those that ideally should be earlier in the list to where they ideally would go.
            foreach (var c in ConstraintsByPosition)
                if (c.Constraint == OrderingConstraint.IdeallyBefore && !MustBeBefore[c.First, c.Second] && !MustBeBefore[c.Second, c.First] && !Constraints.Any(c2 => (c2.Constraint == OrderingConstraint.CloseBefore || c2.Constraint == OrderingConstraint.ImmediatelyBefore) && (c2.First.Equals(items[c.First]) || c2.Second.Equals(items[c.Second])))) // we would like to have c.First before c.Second, but not if c.Second must be before c.First or if c.First needs to be close or immediately before anything else or if there is anything else that needs to be close or immediately before c.Second
                {
                    //Debug.WriteLine(items[c.First] + " will be preserved before " + items[c.Second] + " based on ideal specification");
                    MustBeBefore[c.First, c.Second] = true;
                    FindDerivativeRelations(items, MustBeBefore);
                }

            // And finally, let's preserve original order as much as possible.
            for (int i = 0; i < items.Count - 1; i++)
                if (!MustBeBefore[i, i + 1] && !MustBeBefore[i + 1, i] && !Constraints.Any(c2 => (c2.Constraint == OrderingConstraint.CloseBefore || c2.Constraint == OrderingConstraint.ImmediatelyBefore) && (c2.First.Equals(items[i]) || c2.Second.Equals(items[i + 1])))) // we would like to have i before i + 1, but not if i + 1 must be before i or if i needs to be close or immediately before something else or if there is anything else that needs to be close or immediately before i + 1.
                {
                    //Debug.WriteLine(items[i] + " will be preserved before " + items[i + 1]);
                    MustBeBefore[i, i + 1] = true;
                    FindDerivativeRelations(items, MustBeBefore);
                }

            // Count the number of items something must be before. Those with the most will go first.
            int[] MustBeBeforeCount = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
                for (int j = 0; j < items.Count; j++)
                    if (MustBeBefore[i, j])
                        MustBeBeforeCount[i]++;
            List<T> orderedItems = items
                .Select((item, index) => new { Item = item, Index = index })
                .OrderByDescending(x => MustBeBeforeCount[x.Index])
                .ThenBy(x => Constraints.Any(
                    c => c.Constraint == OrderingConstraint.ImmediatelyBefore && c.First.Equals(x.Item))) // those that are immediately before something else should be last in a group
                .ThenBy(x => Constraints.Any(
                    c => c.Constraint == OrderingConstraint.CloseBefore && c.First.Equals(x.Item))) // those that are immediately before something else should be last in a group
                .ThenByDescending(x => Constraints.Any(
                    c => c.Constraint == OrderingConstraint.ImmediatelyBefore && c.Second.Equals(x.Item))) // those that are immediately after something else (represented here by "before" in the other order) should be first in a group
                .ThenByDescending(x => Constraints.Any(
                    c => c.Constraint == OrderingConstraint.CloseBefore && c.Second.Equals(x.Item))) // those that are immediately after something else should be first in a group
                //.ThenByDescending(x => Constraints.Any(
                //    c => c.Constraint == OrderingConstraint.IdeallyBefore && c.First.Equals(x.Item)))
                .Select(x => x.Item)
                .ToList();

            return orderedItems;
        }

        private static void FindDerivativeRelations(List<T> items, bool[,] MustBeBefore)
        {
            // If A is before B and B is before C, then we mark A as before C. 
            // We continue doing this until we get no more changes. 
            // Not an efficient algorithm, but it should work fine for a relatively small number of items.
            bool additionalRelationFound = true;
            while (additionalRelationFound)
            {
                additionalRelationFound = false;
                for (int i = 0; i < items.Count; i++)
                    for (int j = 0; j < items.Count; j++)
                        for (int k = 0; k < items.Count; k++)
                            if (i != j && j != k)
                                if (MustBeBefore[i, j] && MustBeBefore[j, k] && !MustBeBefore[i, k])
                                {
                                    if (i == k)
                                        throw new Exception("The constraints imply that an item must be before itself. Inconsistency among " + items[i].ToString() + ", " + items[j].ToString());
                                    additionalRelationFound = true;
                                    MustBeBefore[i, k] = true;
                                }
            }
        }
    }

    public static class ConstrainedOrderTest
    {
        public static void DoTest()
        {
            List<string> results = ConstrainedOrder<string>.Order(
                new List<string>() { "A", "B", "C", "D", "E", "F", "G" }, 
                new List<ConstrainedPair<string>> () { 
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "C", Second = "B" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.ImmediatelyBefore, First = "E", Second = "B" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.CloseAfter, First = "B", Second = "F" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "D", Second = "E" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.After, First = "A", Second = "D" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.After, First = "F", Second = "C" },
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.After, First = "A", Second = "D" }, // redundant, shouldn't be problem
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "D", Second = "A" }, // redundant when reversed
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.IdeallyBefore, First = "G", Second = "C" }, // should move G to front of C
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.IdeallyAfter, First = "G", Second = "D" }, // but still after D
                    new ConstrainedPair<string>() { Constraint = OrderingConstraint.IdeallyBefore, First = "F", Second = "D" } // should have no effect since D must be before F (derivative of other relations)
                });
            if (!results.SequenceEqual<string>(new List<string>() { "D", "G", "C", "F", "E", "B", "A" }))
                throw new Exception();

            // the following should throw an exception
        //    results = ConstrainedOrder<string>.Order(
        //        new List<string>() { "A", "B", "C" },
        //        new List<ConstrainedPair<string>>() { 
        //            new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "A", Second = "B" },
        //            new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "B", Second = "C" },
        //            new ConstrainedPair<string>() { Constraint = OrderingConstraint.Before, First = "C", Second = "A" } 
        //        });
        //}

            // this should throw a different exception, for two items immediately before the same item
            //results = ConstrainedOrder<string>.Order(
            //new List<string>() { "A", "B", "C" },
            //new List<ConstrainedPair<string>>() { 
            //        new ConstrainedPair<string>() { Constraint = OrderingConstraint.ImmediatelyBefore, First = "A", Second = "C" },
            //        new ConstrainedPair<string>() { Constraint = OrderingConstraint.ImmediatelyBefore, First = "B", Second = "C" }
            //    });
        }
    }
}

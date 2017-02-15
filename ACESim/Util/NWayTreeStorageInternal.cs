using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class NWayTreeStorageInternal<T> : NWayTreeStorage<T>
    {
        public List<Tuple<byte, NWayTreeStorage<T>>> Storage = new List<Tuple<byte, NWayTreeStorage<T>>>();

        public T GetValue(IEnumerator<byte> restOfSequence)
        {
            NWayTreeStorage<T> tree = this;
            bool moreInSequence = restOfSequence.MoveNext();
            while (moreInSequence)
                tree = ((NWayTreeStorageInternal<T>)tree).Storage.First(x => x.Item1 == restOfSequence.Current).Item2;
            return tree.StoredValue;
        }

        public void AddValue(IEnumerator<byte> restOfSequence, bool historyComplete, T valueToAdd)
        {
            byte nextInSequence = restOfSequence.Current;
            bool anotherExistsAfterNext = restOfSequence.MoveNext();
            var nextTree = Storage.FirstOrDefault(x => x.Item1 == nextInSequence)?.Item2;
            if (nextTree == null)
            {
                if (anotherExistsAfterNext || !historyComplete)
                    nextTree = new NWayTreeStorageInternal<T>();
                else
                {
                    nextTree = new NWayTreeStorage<T>(); // leaf node for last item in history
                    nextTree.StoredValue = valueToAdd;
                    return;
                }
                Storage.Add(new Tuple<byte, NWayTreeStorage<T>>(nextInSequence, nextTree));
                if (Storage.Any(x => x.Item1 > nextInSequence))
                    Storage = Storage.OrderBy(x => x.Item1).ToList(); // put back into order
            }
            if (anotherExistsAfterNext)
                ((NWayTreeStorageInternal<T>)nextTree).AddValue(restOfSequence, historyComplete, valueToAdd);
            else
                StoredValue = valueToAdd;
        }
    }
}

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

        public virtual void AddValue(IEnumerator<byte> restOfSequence, bool historyComplete, T valueToAdd)
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
            }
            if (anotherExistsAfterNext)
                ((NWayTreeStorageInternal<T>)nextTree).AddValue(restOfSequence, historyComplete, valueToAdd);
            else
                StoredValue = valueToAdd;
        }
    }
}

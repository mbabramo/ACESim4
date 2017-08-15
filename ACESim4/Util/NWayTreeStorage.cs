using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class NWayTreeStorage<T>
    {
        public NWayTreeStorageInternal<T> Parent;

        private T _StoredValue;
        public T StoredValue
        {
            get
            {
                lock (this)
                    return _StoredValue;
            }
            set
            {
                lock (this)
                {
                    if (_StoredValue == null || _StoredValue.Equals(default(T)))
                        _StoredValue = value;
                }
            }
        }

        public NWayTreeStorage(NWayTreeStorageInternal<T> parent)
        {
            Parent = parent;
        }

        public virtual List<byte> GetSequenceToHere(NWayTreeStorage<T> child = null)
        {
            List<byte> p = Parent?.GetSequenceToHere(this) ?? new List<byte>();
            return p;
        }

        public string SequenceToHereString => String.Join(",", GetSequenceToHere());

        public virtual NWayTreeStorage<T> GetBranch(byte index)
        {
            return null; // leaf node has no child; internal node overrides this
        }
        public virtual void SetBranch(byte index, NWayTreeStorage<T> tree)
        {
            throw new Exception("Cannot set branch on leaf node.");
        }

        public virtual bool IsLeaf()
        {
            return true; // this is a leaf node if not overriden
        }

        public List<(T storedValue, List<byte> sequenceToHere)> GetAllTreeNodes()
        {
            List<(T storedValue, List<byte> sequenceToHere)> l = new List<(T storedValue, List<byte> sequenceToHere)>();
            WalkTree(t =>
            {
                l.Add((t.StoredValue, t.GetSequenceToHere()));
            });
            return l;
        }

        public virtual void WalkTree(Action<NWayTreeStorage<T>> action)
        {
            action(this);
        }

        public string ToTreeString()
        {
            StringBuilder s = new StringBuilder();
            ToTreeString(s, null, 0);
            return s.ToString();
        }

        internal virtual void ToTreeString(StringBuilder s, int? branch, int level)
        {
            for (int i = 0; i < level * 5; i++)
                s.Append(" ");
            if (branch == null)
                s.Append("Root");
            else
                s.Append(branch);
            s.Append(": ");
            s.Append($"{StoredValue}");
            s.Append(Environment.NewLine);
        }
    }
}

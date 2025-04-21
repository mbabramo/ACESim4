using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;

namespace ACESimBase.Util.Collections
{
    public class FixedSizedQueue<T>
    {
        ConcurrentQueue<T> q = new ConcurrentQueue<T>();

        public int Limit { get; set; }
        public void Enqueue(T obj)
        {
            q.Enqueue(obj);
            while (q.Count > Limit)
            {
                q.TryDequeue(out T itemDequeued);
            }
        }

        public IEnumerable<T> AsEnumerable()
        {
            return q.AsEnumerable();
        }


    }

}

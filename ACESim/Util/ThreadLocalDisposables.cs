using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim.Util
{

    public class IndirectData<T> where T : class
    {
        public T Data;

        public IndirectData(T data)
        {
            Data = data;
        }

        public void Nullify()
        {
            Data = null;
        }
    }

    public class ThreadLocalFixed<T> where T : class
    {
        private ThreadLocal<IndirectData<T>> _threadLocal = null;
        static ConcurrentBag<IndirectData<T>> _values = new ConcurrentBag<IndirectData<T>>();

        public ThreadLocalFixed()
        {
            _threadLocal = new ThreadLocal<IndirectData<T>>();
        }

        public ThreadLocalFixed(Func<T> valueFactory)
        {
            _threadLocal = new ThreadLocal<IndirectData<T>>(() =>
            {
                var value = valueFactory();
                IndirectData<T> id = new IndirectData<T>(value);
                _values.Add(id);
                return id;
            });
        }

        public void Reset()
        {
            if (_values != null)
            {
                ExplicitlySetNullReferences();
            }
            _values = new ConcurrentBag<IndirectData<T>>();
        }


        private static void ExplicitlySetNullReferences()
        {
            Array.ForEach(_values.ToArray(), t => { t.Nullify(); });
        }

        public override string ToString()
        {
            return _threadLocal.ToString();
        }

        public bool IsValueCreated
        {
            get { return _threadLocal.IsValueCreated; }
        }

        public T Value
        {
            get 
            { 
                return _threadLocal.Value.Data; 
            }
            set
            {
                IndirectData<T> existing = _values.FirstOrDefault(x => object.ReferenceEquals(x.Data, value));
                IndirectData<T> id = new IndirectData<T>(value);
                _values.Add(id);
            }
        }
    }
}

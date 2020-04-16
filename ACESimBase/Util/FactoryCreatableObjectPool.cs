using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util
{
    public class FactoryCreatableObjectPool<T>
    {
        Func<T> Factory;
        private ConcurrentBag<T> _objects;
        private int itemCount = 0;
        private int maxPoolSize = 1000;

        public FactoryCreatableObjectPool(Func<T> factory)
        {
            Factory = factory;
            _objects = new ConcurrentBag<T>();
        }

        public T GetObject()
        {
            if (_objects.TryTake(out T item))
            {
                itemCount--;
                return item;
            }
            item = Factory();
            return item;
        }

        public void Return(T item)
        {
            if (itemCount < maxPoolSize)
            {
                _objects.Add(item);
                itemCount++;
            }
        }
    }
}

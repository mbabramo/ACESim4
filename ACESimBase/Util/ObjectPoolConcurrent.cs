using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{

    public class RecyclePool<T> where T : IRecyclable, new()
    {
        private ConcurrentBag<T> _objects;
        private int itemCount = 0;
        private int maxPoolSize = 1000;

        public RecyclePool()
        {
            _objects = new ConcurrentBag<T>();
        }

        public T GetObject(object[] args)
        {
            if (_objects.TryTake(out T item))
            {
                itemCount--;
                item.AfterRecycled(args);
                return item;
            }
            item = new T();
            item.AfterCreated(args);
            return item;
        }

        public void PutObject(T item)
        {
            if (itemCount < maxPoolSize)
            {
                item.BeforeRecycling();
                _objects.Add(item);
                itemCount++;
            }
        }
    }

    public interface IRecyclable
    {
        void AfterCreated(params object[] args);
        void BeforeRecycling();
        void AfterRecycled(params object[] args);
    }

    public static class RecyclingManager
    {
        private static ServiceContainer container = new ServiceContainer();
        private static object lockObj = new object();

        private static RecyclePool<T> GetRecyclePool<T>() where T : IRecyclable, new()
        {
            RecyclePool<T> theObjPool = container.GetService(typeof(RecyclePool<T>)) as RecyclePool<T>;
            if (theObjPool == null)
            {
                lock (lockObj)
                {
                    theObjPool = container.GetService(typeof(RecyclePool<T>)) as RecyclePool<T>;
                    if (theObjPool == null)
                    {
                        theObjPool = new RecyclePool<T>();
                        container.AddService(typeof(RecyclePool<T>), theObjPool);
                    }
                }
            }
            return theObjPool;
        }

        public static T Get<T>(object[] args) where T : IRecyclable, new()
        {
            RecyclePool<T> theObjPool = GetRecyclePool<T>();
            return theObjPool.GetObject(args);
        }

        public static void Recycle<T>(T recyclableObject) where T : IRecyclable, new()
        {
            RecyclePool<T> theObjPool = GetRecyclePool<T>();
            theObjPool.PutObject(recyclableObject);
        }
    }
}

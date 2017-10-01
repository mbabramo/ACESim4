/*
 * MSDN Magazine Memory Utilization Article
 * by Erik Brown
 */
using System;
using System.Collections;
using System.Threading;

namespace MSDN.Memory
{
    public class ObjectPoolException : Exception
    {
        public ObjectPoolException() : base() { }
        public ObjectPoolException(string message) : base(message) { }
        public ObjectPoolException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Class to support object pooling of reference types
    /// </summary>
    public class ObjectPool
    {
        private class ObjectData
        {
            public Type objectType;
            public short minPoolSize;
            public short maxPoolSize;
            public int creationTimeout;
            public short inPool;
            public short inUse;
            public short inWait;

            public Queue pool;
            public CreateObject createDelegate;
        }

        #region Constructor and Members
        private Hashtable _table;
        protected Hashtable Table { get { return _table; } }

        public ObjectPool()
        {
            _table = new Hashtable();
        }

        #endregion // Constructor and Members

        #region Delegates
        /// <summary>
        /// Represents a method the creates a new object of type t.
        /// </summary>
        public delegate object CreateObject();

        /// <summary>
        /// Represents a method that uses a given object from the pool.
        /// </summary>
        public delegate void UseObject(object obj, object[] args);
        #endregion // Delegates

        #region Static Members
        private static ObjectPool _pool;

        static ObjectPool()
        {
            _pool = new ObjectPool();
        }

        /// <summary>
        /// Retrieves the shared ObjectPool instance.
        /// </summary>
        /// <returns>the shared ObjectPool instance</returns>
        public static ObjectPool GetInstance()
        {
            return _pool;
        }
        #endregion // Static Members

        public void RegisterType(Type t,
            CreateObject createDelegate,
            short minPoolSize,
            short maxPoolSize,
            int creationTimeout)
        {
            // Validate the parameters
            if (t == null) throw new ArgumentNullException("t");
            if (createDelegate == null) throw new ArgumentNullException("createDelegate");

            if (minPoolSize < 0)
                throw new ArgumentException("minPoolSize cannot be negative", "minPoolSize");
            if (maxPoolSize < 0)
                throw new ArgumentException("maxPoolSize cannot be negative", "maxPoolSize");
            if (maxPoolSize != 0 && minPoolSize > maxPoolSize)
                throw new ArgumentException("minPoolSize cannot be greater than maxPoolSize");

            if (creationTimeout < 0)
                throw new ArgumentException("creationTimeout cannot be negative", "creationTimeout");

            if (Table[t.FullName] != null)
                throw new ArgumentException("Type " + t.FullName + " is already registered in the object pool", "t");

            // Create the object data for this type
            ObjectData data = new ObjectData();
            data.objectType = t;
            data.minPoolSize = minPoolSize;
            data.maxPoolSize = maxPoolSize;
            data.creationTimeout = creationTimeout;
            data.inPool = 0;
            data.inUse = 0;
            data.inWait = 0;

            data.pool = new Queue(minPoolSize);
            data.createDelegate = createDelegate;

            // Add the new data to the hash table
            lock (Table)
            {
                Table.Add(t.FullName, data);
            }

            // Pre-populate the pool with the minimum number of objects
            if (minPoolSize > 0)
            {
                // We presume caller will not request objects until this method returns
                for (int i = 0; i < minPoolSize; i++)
                {
                    data.pool.Enqueue(AllocateObject(data));
                    data.inPool++;
                }
            }
        } // RegisterType

        /// <summary>
        /// Terminate pooling for the given type
        /// </summary>
        /// <param name="t">pooled type</param>
        public void UnregisterType(Type t)
        {
            ObjectData data = GetObjectData(t);

            Monitor.Enter(data);
            try
            {
                if (data.inWait > 0)
                {
                    // It is an error unregister a type with active waiters
                    throw new ArgumentException("The type " + t.FullName
                        + " cannot be unregistered because there are active threads waiting for an object.",
                        "t");
                }

                // Remove the type from the hash table
                // so no further pooling will be permitted
                Table.Remove(t);

                // Any types still in use are abandoned

                // Clean up any objects still in the pool.
                foreach (object o in data.pool)
                {
                    object obj = o;
                    if (o is WeakReference)
                        obj = ((WeakReference)o).Target;

                    if (obj != null && obj is IDisposable)
                    {
                        // Dispose of the object
                        IDisposable d = (IDisposable)obj;
                        d.Dispose();
                    }
                }
                data.inPool = 0;
            }
            finally
            {
                Monitor.Exit(data);
            }
        }

        /// <summary>
        /// Get an object of the given type from the object pool
        /// </summary>
        /// <param name="t">type to retrieve from the pool</param>
        /// <returns>object of the given type</returns>
        /// <remarks>
        /// Note that this returns an object from the pool, or creates a new object
        /// for the pool, if possible.  If the maximum number of objects have been
        /// created, then this method waits for one to become available.
        /// </remarks>
        /// <exception cref="ObjectPoolException">A creation timeout occurred.</exception>
        public object GetObject(Type t)
        {
            ObjectData data = GetObjectData(t);

            // Retrieve an object of the desired type from the pool
            return RetrieveFromPool(data);
        }

        public void ReleaseObject(object obj)
        {
            Type t = obj.GetType();
            ObjectData data = GetObjectData(t);

            // Add this object back into the pool.
            ReturnToPool(obj, data);
        }

        /// <summary>
        /// Execute a method using an object of the requested type from the object pool.
        /// </summary>
        /// <param name="executeDelegate">delegate to invoke with object from pool</param>
        /// <param name="t">desired type of object from pool</param>
        /// <param name="args">argument to supply to delegate</param>
        /// <remarks>
        /// This method ensures that an object is always returned to the pool
        /// after it is used, even if an exception occurs.  This alleviates the
        /// programmer from having to call ReleaseObject() to return the object
        /// to the pool.
        /// </remarks>
        public void ExecuteFromPool(UseObject executeDelegate,
            Type t, object[] args)
        {
            if (executeDelegate == null)
                throw new ArgumentNullException("executeDelegate");

            ObjectData data = GetObjectData(t);

            object obj = null;
            try
            {
                // Obtain an object from the pool
                obj = RetrieveFromPool(data);

                /*
                 * This presumes RetrieveFromPool returns null when no objects
                 * are available.
                 * If RetreiveFromPool is modified to throw an exception
                 * in this case, then this code can be removed.
                 */
                if (obj == null)
                {
                    throw new ObjectPoolException("An object of type "
                        + t.FullName + " was not available from the pool");
                }

                executeDelegate(obj, args);
            }
            finally
            {
                // Return the object to the pool, if required
                if (obj != null)
                    ReturnToPool(obj, data);
            }
        } // ExecuteFromPool method

        #region Private Methods

        /// <summary>
        /// Gets the ObjectData for the given type
        /// </summary>
        /// <param name="t">pooled type</param>
        /// <returns>ObjectData associated with given type</returns>
        private ObjectData GetObjectData(Type t)
        {
            ObjectData data = Table[t.FullName] as ObjectData;
            if (data == null)
                throw new ArgumentException("Type " + t.FullName + " is not registered in the object pool.", "t");

            return data;
        }

        /// <summary>
        /// Creates a new object of the indicated type
        /// </summary>
        /// <param name="data">object pool data for desired type</param>
        /// <returns>an object of the associated type</returns>
        private object AllocateObject(ObjectData data)
        {
            return data.createDelegate();
        }

        /// <summary>
        /// Retreives an object of the indicated type
        /// </summary>
        /// <param name="data">object pool data for desired type</param>
        /// <returns>an object of the associated type</returns>
        /// <exception cref="ObjectPoolException">A creation timeout occurred.</exception>
        private object RetrieveFromPool(ObjectData data)
        {
            object result = null;
            int waitTime = (data.creationTimeout > 0)
                ? data.creationTimeout : Timeout.Infinite;

            try
            {
                // Aquire the object's lock, if possible
                int startTick = Environment.TickCount;
                if (Monitor.TryEnter(data, waitTime) == false)
                {
                    // Unable to obtain the lock in the requested period
                    return null;
                }

                // Retrieve an object from the pool, if possible
                // Note that we cannot rely on data.inPool for this check
                if (data.pool.Count > 0)
                {
                    result = DequeueFromPool(data);
                } // if pool non-empty

                // The result is null if pool was empty,
                // or only contained collected weak references.
                if (result == null)
                {
                    if (data.maxPoolSize == 0 || data.inUse < data.maxPoolSize)
                    {
                        // Create a new object to satisfy the request
                        result = AllocateObject(data);
                    }
                    else
                    {
                        // Adjust waitTime based on current span
                        if (waitTime != Timeout.Infinite)
                        {
                            // This presumes Environment.TickCount does not wrap around.
                            // If it does, an immediate timeout will occur.
                            waitTime -= (Environment.TickCount - startTick);
                        }
                        result = WaitForObject(data, waitTime);
                    } // else have to wait
                } // if result is null

                // Update inUse counter.
                if (result != null)
                    data.inUse++;

            } // try
            finally
            {
                Monitor.Exit(data);
            }

            return result;
        } // RetrieveFromPool method

        /// <summary>
        /// Dequeue an object from the pool.
        /// </summary>
        /// <param name="data"></param>
        /// <returns>The first available object in the queue, or null if none is available.</returns>
        /// <remarks>
        /// This method assumes that the Monitor lock is held.
        /// </remarks>
        private object DequeueFromPool(ObjectData data)
        {
            object result;
            do
            {
                result = data.pool.Dequeue();
                if (result is WeakReference)
                {
                    // Obtain the actual object reference, if any
                    result = ((WeakReference)result).Target;
                }
                else
                {
                    // Actual references are tracked
                    data.inPool--;
                }
            } while (result == null && data.pool.Count > 0);

            return result;
        }

        private object WaitForObject(ObjectData data, int waitTime)
        {
            // Wait for an object to be available
            bool isAvailable;
            object result = null;

            // Wait for an available object of this type
            int startTick = Environment.TickCount;
            while (result == null)
            {
                // Actual wait time depends on how long we have been waiting thus far
                data.inWait++;
                if (waitTime == Timeout.Infinite)
                    isAvailable = Monitor.Wait(data, waitTime, true);
                else
                {
                    // This presumes Environment.TickCount does not wrap around.
                    // If it does, an immediate timeout will occur.
                    int actualWait = Math.Max(0,
                        waitTime - (Environment.TickCount - startTick));
                    isAvailable = Monitor.Wait(data, waitTime, true);
                }
                data.inWait--;

                // Retrieve any available result
                if (isAvailable)
                {
                    // There should be an item in the queue now
                    if (data.pool.Count > 0)
                    {
                        result = DequeueFromPool(data);
                    }
                }

                /*
                 * There is a race condition here when waiting for an object,
                 * since another thread may enter RetrieveFromPool() between
                 * the pulse and the wake up, and steal "our" object.  If
                 * this happens, result will be null, but the timeout
                 * period will not have elapsed.  We check for this and
                 * re-enter the wait loop when this happens.  Ideally, .NET
                 * would guarantee the lock ordering and not allow this to
                 * happen, but unfortunately this is not the case.
                 */
                if (result == null
                    && waitTime != Timeout.Infinite
                    && (Environment.TickCount - startTick > waitTime))
                {
                    return null;
                }
            } // while

            return result;
        }

        /// <summary>
        /// Returns the given object to the object pool
        /// </summary>
        /// <param name="obj">object to return to the pool</param>
        /// <param name="data">object data asosciated with object's type</param>
        /// <remarks>
        /// This method queues the object into the pool.  If the minimum number
        /// of objects are not available, then a reference to the object is
        /// enqueued to ensure the object remains available.  If the minimum
        /// number of objects are already available, then only a weak reference
        /// to the object is enqueued.  This permits the garbage collector to
        /// reclaim this memory so that the pool will eventually return to
        /// the minimum size.  In a busy system, however, the object can be
        /// reclaimed from the weak reference.
        /// 
        /// Note that the data.inPool value only tracks strong reference in
        /// the pool, so we can track the actual number of real objects.
        /// </remarks>
        private void ReturnToPool(object obj, ObjectData data)
        {
            Monitor.Enter(data);
            try
            {
                // Reduce inUse counter for returned object
                data.inUse--;

                int size = data.inUse + data.inPool;
                //if (size < data.minPoolSize || data.inWait > 0)
                if (size < data.minPoolSize)
                {
                    // Return actual object to the pool
                    data.pool.Enqueue(obj);
                    data.inPool++;
                }
                else
                {
                    // Min objects are available, so enqueue as weak reference
                    WeakReference weakRef = new WeakReference(obj);
                    data.pool.Enqueue(weakRef);
                }

                // Notify any waiting threads that a new object is available
                if (data.inWait > 0)
                    Monitor.Pulse(data);
            } // try
            finally
            {
                Monitor.Exit(data);
            }

        } // ReturnToPool method
        #endregion // Private Methods

    } // ObjectPool class
}

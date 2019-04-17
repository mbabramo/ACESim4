using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ACESimBase.Util
{
    public sealed class SimpleExclusiveLock : IDisposable
    {
        public void Dispose()
        {
            semaphore.Close();
        }

        public void Enter()
        {
            while (true)
            {
                int state = lockState;
                if ((state & OwnedFlag) == 0) // if the lock is not owned...
                {
                    // try to acquire it. if we succeed, then we're done
                    if (Interlocked.CompareExchange(ref lockState, state | OwnedFlag, state) == state) return;
                }
                // the lock is owned, so try to add ourselves to the count of waiting threads
                else if (Interlocked.CompareExchange(ref lockState, state + 1, state) == state)
                {
                    semaphore.WaitOne(); // we succeeded in adding ourselves, so wait until we're awakened
                }
            }
        }

        public void Exit()
        {
            // throw an exception if Exit() is called when the lock is not held
            if ((lockState & OwnedFlag) == 0) throw new SynchronizationLockException();

            // we want to free the lock by clearing the owned flag. if the result is not zero, then
            // another thread is waiting, and we'll release it, so we'll subtract one from the wait count
            int state, freeState;
            do
            {
                state = lockState;
                freeState = state & ~OwnedFlag;
            }
            while (Interlocked.CompareExchange(ref lockState, freeState == 0 ? 0 : freeState - 1, state) != state);

            if (freeState != 0) semaphore.Release(); // if other threads are waiting, release one of them
        }

        const int OwnedFlag = unchecked((int)0x80000000);
        int lockState; // the high bit is set if the lock is held. the lower 31 bits hold the number of threads waiting
        readonly Semaphore semaphore = new Semaphore(0, int.MaxValue);
    }
}

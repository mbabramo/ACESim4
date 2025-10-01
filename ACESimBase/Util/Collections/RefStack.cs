using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Collections
{
    /// <summary>
    /// Simple stack that supports by-ref access to the top element.
    /// Intended for small depth control flows (e.g., IF/ENDIF).
    /// </summary>
    public sealed class RefStack<T> where T : struct
    {
        private T[] _items;
        private int _count;

        public RefStack(int initialCapacity = 4)
        {
            if (initialCapacity < 1)
                initialCapacity = 4;

            _items = new T[initialCapacity];
            _count = 0;
        }

        public int Count => _count;

        public void Push(in T value)
        {
            if (_count == _items.Length)
                Array.Resize(ref _items, _items.Length * 2);

            _items[_count++] = value;
        }

        public T Pop()
        {
            if (_count == 0)
                throw new InvalidOperationException("Stack is empty.");

            _count--;
            var value = _items[_count];
            _items[_count] = default; // clear for GC friendliness
            return value;
        }

        public ref T PeekRef()
        {
            if (_count == 0)
                throw new InvalidOperationException("Stack is empty.");

            return ref _items[_count - 1];
        }

        public T Peek()
        {
            if (_count == 0)
                throw new InvalidOperationException("Stack is empty.");

            return _items[_count - 1];
        }

        public bool TryPeek(out T value)
        {
            if (_count == 0)
            {
                value = default;
                return false;
            }

            value = _items[_count - 1];
            return true;
        }
    }
}

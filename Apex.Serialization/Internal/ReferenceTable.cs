using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// The slots are structs because a store into an array of a reference type goes through the runtime's covariance
    /// check, and every object a read creates in graph mode is added here.
    /// </summary>
    internal sealed class ReferenceTable<T> where T : class
    {
        private struct Slot
        {
            public T? Value;
        }

        private Slot[] _slots;
        private int _count;

        public ReferenceTable(int capacity)
        {
            _slots = new Slot[capacity];
        }

        public int Count => _count;

        public T? this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    ThrowHelper.ThrowIndexOutOfRange();
                }

                return _slots[index].Value;
            }
            set
            {
                if ((uint)index >= (uint)_count)
                {
                    ThrowHelper.ThrowIndexOutOfRange();
                }

                _slots[index].Value = value;
            }
        }

        /// <summary>
        /// The entry a reference read from the data names, counting from 1 as the writer does.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetReferenced(int reference, string kind)
        {
            var index = reference - 1;
            if ((uint)index >= (uint)_count)
            {
                ThrowHelper.ThrowInvalidReference(kind, reference);
            }

            return _slots[index].Value!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T? value)
        {
            var slots = _slots;
            var count = _count;
            if ((uint)count < (uint)slots.Length)
            {
                slots[count].Value = value;
                _count = count + 1;
                return;
            }

            AddWithResize(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T? value)
        {
            Array.Resize(ref _slots, (int)Math.Min(Math.Max(4L, 2L * _slots.Length), Array.MaxLength));
            _slots[_count++].Value = value;
        }

        public void AddRange(List<T> values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public void Clear()
        {
            _slots.AsSpan(0, _count).Clear();
            _count = 0;
        }
    }
}

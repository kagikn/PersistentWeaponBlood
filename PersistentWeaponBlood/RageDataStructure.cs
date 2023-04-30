using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PersistentWeaponBlood
{
    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    public unsafe struct atArray<T> where T : unmanaged
    {
        public T* Items;
        public ushort Count;
        public ushort Size;

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index >= 0 && index < Count)
                {
                    return Items[index];
                }

                throw new ArgumentOutOfRangeException(nameof(index), index, $"Out of Range (Count:{Count.ToString("G")}, Size:{Count.ToString("G")})");
            }
        }

        public T GetValueUnsafe(int index)
        {
            return Items[index];
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public ref struct Enumerator
        {
            private readonly atArray<T> array;
            private int index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(atArray<T> arr)
            {
                array = arr;
                index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                int newIndex = index + 1;
                if (newIndex < array.Count)
                {
                    index = newIndex;
                    return true;
                }

                return false;
            }

            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => array.GetValueUnsafe(index);
            }
        }

        public sealed class EnumeratorObject : IEnumerator<T>
        {
            private readonly atArray<T> array;
            private int index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public EnumeratorObject(atArray<T> arr)
            {
                array = arr;
                index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                int newIndex = index + 1;
                if (newIndex < array.Count)
                {
                    index = newIndex;
                    return true;
                }

                return false;
            }

            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => array.GetValueUnsafe(index);
            }

            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() => throw new NotImplementedException();
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x18)]
    public struct atBinaryMap
    {
        [StructLayout(LayoutKind.Explicit, Size = 0x10)]
        public struct DataPair
        {
            [FieldOffset(0x0)] public uint Key;
            [FieldOffset(0x8)] public IntPtr Value;
        }

        [FieldOffset(0x00), MarshalAs(UnmanagedType.I1)] public bool IsSorted;
        [FieldOffset(0x08)] public atArray<DataPair> Pairs;

        public ushort Count => Pairs.Count;
    }
}

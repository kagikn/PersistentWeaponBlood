using Rage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PersistentWeaponBlood
{
    internal class PickupUtils
    {
        [StructLayout(LayoutKind.Explicit)]
        struct FwScriptGuidPool
        {
            // The max count value should be at least 3072 as long as ScriptHookV is installed.
            // Without ScriptHookV, the default value is hardcoded and may be different between different game versions (the value is 300 in b372 and 700 in b2824).
            // The default value (when running without ScriptHookV) can be found by searching the dumped exe or the game memory with "D7 A8 11 73" (0x7311A8D7).
            [FieldOffset(0x10)]
            internal uint maxCount;
            [FieldOffset(0x14)]
            internal int itemSize;
            [FieldOffset(0x18)]
            internal int firstEmptySlot;
            [FieldOffset(0x1C)]
            internal int emptySlots;
            [FieldOffset(0x20)]
            internal uint itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsFull()
            {
                return maxCount - (itemCount & 0x3FFFFFFF) <= 256;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct GenericPool
        {
            [FieldOffset(0x00)]
            public ulong poolStartAddress;
            [FieldOffset(0x08)]
            public IntPtr byteArray;
            [FieldOffset(0x10)]
            public uint size;
            [FieldOffset(0x14)]
            public uint itemSize;
            [FieldOffset(0x20)]
            public ushort itemCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsValid(uint index)
            {
                return Mask(index) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsHandleValid(int handle)
            {
                uint handleUInt = (uint)handle;
                var index = handleUInt >> 8;
                return GetCounter(index) == (handleUInt & 0xFFu);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ulong GetAddress(uint index)
            {
                return ((Mask(index) & (poolStartAddress + index * itemSize)));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal IntPtr GetAddressFromHandle(int handle)
            {
                return IsHandleValid(handle) ? new IntPtr((long)GetAddress((uint)handle >> 8)) : IntPtr.Zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal int GetGuidHandleByIndex(uint index)
            {
                return IsValid(index) ? (int)((index << 8) + GetCounter(index)) : 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal int GetGuidHandleFromAddress(ulong address)
            {
                if (address < poolStartAddress || address >= poolStartAddress + size * itemSize)
                    return 0;

                var offset = address - poolStartAddress;
                if (offset % itemSize != 0)
                    return 0;

                var indexOfPool = (uint)(offset / itemSize);
                return (int)((indexOfPool << 8) + GetCounter(indexOfPool));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private byte GetCounter(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    return (byte)(byteArrayPtr[index] & 0x7F);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ulong Mask(uint index)
            {
                unsafe
                {
                    byte* byteArrayPtr = (byte*)byteArray.ToPointer();
                    long num1 = byteArrayPtr[index] & 0x80;
                    return (ulong)(~((num1 | -num1) >> 63));
                }
            }
        }

        internal static Rage.Object[] GetPickupObjectHandles()
        {
            var pickupObjectPoolPtrOfPtrAddress = Memory.PickupObjectPoolPointerOfPointerAddress;
            var fwScriptGuidPoolPointerOfPointerAddress = Memory.FwScriptGuidPoolPointerOfPointerAddress;
            var createGuidFuncAddress = Memory.CreateGuidFuncAddress;

            if (pickupObjectPoolPtrOfPtrAddress == IntPtr.Zero || fwScriptGuidPoolPointerOfPointerAddress == IntPtr.Zero || createGuidFuncAddress == IntPtr.Zero)
                return Array.Empty<Rage.Object>();

            unsafe
            {
                IntPtr pickupObjectPoolPtr = new IntPtr((long)*(ulong*)pickupObjectPoolPtrOfPtrAddress);
                if (pickupObjectPoolPtr == IntPtr.Zero)
                    return Array.Empty<Rage.Object>();

                IntPtr fwScriptGuidPoolPtr = new IntPtr((long)*(ulong*)fwScriptGuidPoolPointerOfPointerAddress);
                if (fwScriptGuidPoolPtr == null)
                    return Array.Empty<Rage.Object>();

                var task = new FwScriptGuidGenericPoolTask(pickupObjectPoolPtr, fwScriptGuidPoolPtr, createGuidFuncAddress);
                using (var tlsScope = UsingTls.Scope())
                {
                    task.Run();
                }

                // Fucking shame that RPH has World.GetPickupObjects but its accessor is set to internal
                // This method takes more than 10x slower than one World.GetPickupObjects call in SHVDN (because of World.GetEntityByHandle!)
                var createdObjects = task.resultHandles.Select(x => World.GetEntityByHandle<Rage.Object>(new PoolHandle(x))).Where(x => x != null).ToArray();
                return createdObjects;
            }
        }

        internal sealed class FwScriptGuidGenericPoolTask
        {
            #region Fields
            internal IntPtr _poolAddress;
            internal IntPtr _fwScriptGuidPoolAddress;
            internal IntPtr _createGuidFuncAddress;
            internal uint[] resultHandles = Array.Empty<uint>();

            #endregion

            internal FwScriptGuidGenericPoolTask(IntPtr poolAddress, IntPtr fwScriptGuidPoolAddress, IntPtr createGuidFuncAddress)
            {
                _poolAddress = poolAddress;
                _fwScriptGuidPoolAddress = fwScriptGuidPoolAddress;
                _createGuidFuncAddress = createGuidFuncAddress;
            }

            internal void Run()
            {
                unsafe
                {
                    resultHandles = GetGuidHandlesFromGenericPool((FwScriptGuidPool*)_fwScriptGuidPoolAddress, (GenericPool*)_poolAddress);
                }
            }

            unsafe uint[] GetGuidHandlesFromGenericPool(FwScriptGuidPool* fwScriptGuidPool, GenericPool* genericPool)
            {
                List<uint> resultList = new List<uint>(genericPool->itemCount);

                uint genericPoolSize = genericPool->size;
                for (uint i = 0; i < genericPoolSize; i++)
                {
                    if (fwScriptGuidPool->IsFull())
                        throw new InvalidOperationException("The fwScriptGuid pool is full. The pool must be extended to retrieve all entity handles.");

                    if (!genericPool->IsValid(i))
                    {
                        continue;
                    }

                    ulong address = genericPool->GetAddress(i);

                    uint createdHandle = CreateGuid(address);
                    resultList.Add(createdHandle);
                }

                return resultList.ToArray();
            }

            // this is a one that needs a SysMemAllocator instance
            unsafe uint CreateGuid(ulong address)
            {
                var handle = ((delegate* unmanaged[Stdcall]<ulong, uint>)_createGuidFuncAddress)(address);
                return handle;
            }
        }
    }
}

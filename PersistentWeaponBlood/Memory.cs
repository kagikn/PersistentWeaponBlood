#if SHVDN
using GTA.Native;
using GTA;
using Weapon = GTA.Prop;
#endif
#if RPH
using Rage;
using Prop = Rage.Object;
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;

using EasyHook;
using static PersistentWeaponBlood.WeaponCamoDiffuseTexTracker;
using System.Reflection;
using System.IO;
using static PersistentWeaponBlood.Memory;

namespace PersistentWeaponBlood
{
    public static class AssemblyResolutionUtils
    {
        //#if SHVDN
        public static Assembly MyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            var exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            return Assembly.LoadFrom(Path.Combine(exePath, "EasyHook.dll"));
        }
//#endif
#if RPH
        public static Assembly LoadEasyHook()
        {
            var exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            return Assembly.LoadFrom(Path.Combine(exePath, "EasyHook.dll"));
        }
#endif
    }

    public static unsafe class Memory
    {
        public readonly static delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void> _origRewardPedWithWeaponFunc;
        public readonly static LocalHook? _hookedrewardPedWithWeaponFunc;

        static readonly delegate* unmanaged[Stdcall]<IntPtr> _getLocalPlayerPedAddressFunc;

        internal static int CPed_CPedInventoryOffset;
        internal static int CObject_CWeaponOffset;
        internal static int CPickup_OwnerCPedOffset;
        internal static readonly int TLS_AllocatorOffset = -1;

        private static readonly HashSet<WeaponHash> weaponHashesWithCamoDiffuseTexIdxsBinaryMap;

        static readonly List<WeaponHashAndCamoTexIdIndexTuple> desiredCamoDiffusesForLocalPlayerPed = new();
        static Dictionary<IntPtr, List<WeaponHashAndCamoTexIdIndexTuple>>? desiredCamoDiffusesFallback;

#if RPH
        static readonly IntPtr pickupObjectPoolPointerOfPointerAddress;
        static readonly IntPtr fwScriptGuidPoolPointerOfPointerAddress;
        static readonly IntPtr createGuidFuncAddress;
#endif

        public delegate void RewardPedWithWeaponDelegate(IntPtr cPickupRewardWeaponPtr, IntPtr cPickupPtrOfWeaponToRewardWith, IntPtr cPedPtrToReward);
        public static void RewardPedWithWeaponHook(IntPtr cPickupRewardWeaponPtr, IntPtr cPickupPtrOfWeaponToRewardWith, IntPtr cPedPtrToReward)
        {
            // Note: No GtaThread instances should be allowed to run when this method is called, so there's no native function calls here
            var cPickupRewardWeaponPtrNative = (CPickupRewardWeapon*)cPickupRewardWeaponPtr;
            var hashOfRewardedWeapon = (WeaponHash)cPickupRewardWeaponPtrNative->weaponRefHash;
            if (!weaponHashesWithCamoDiffuseTexIdxsBinaryMap.Contains(hashOfRewardedWeapon))
            {
                _origRewardPedWithWeaponFunc(cPickupRewardWeaponPtr, cPickupPtrOfWeaponToRewardWith, cPedPtrToReward);
                return;
            }

            var camoDiffuseTexId = WeaponPropExtensions.GetCamoDiffuseTexIdOfCObject(cPickupPtrOfWeaponToRewardWith);
            if (camoDiffuseTexId == 0)
            {
                _origRewardPedWithWeaponFunc(cPickupRewardWeaponPtr, cPickupPtrOfWeaponToRewardWith, cPedPtrToReward);
                return;
            }

            if (_getLocalPlayerPedAddressFunc != null && _getLocalPlayerPedAddressFunc() == cPedPtrToReward)
            {
                UpdateOrCreateNewEntryForLocalPlayerPed(desiredCamoDiffusesForLocalPlayerPed, hashOfRewardedWeapon, camoDiffuseTexId);
            }
            else
            {
                // Edge case! Won't come here unless the game code is modified so it can reward a ped other than the local player ped with a weapon!
                if (desiredCamoDiffusesFallback == null)
                {
                    desiredCamoDiffusesFallback = new();
                }

                UpdateOrCreateNewEntry(desiredCamoDiffusesFallback, cPedPtrToReward, hashOfRewardedWeapon, camoDiffuseTexId);
            }
            

            _origRewardPedWithWeaponFunc(cPickupRewardWeaponPtr, cPickupPtrOfWeaponToRewardWith, cPedPtrToReward);
            return;

            static void UpdateOrCreateNewEntryForLocalPlayerPed(List<WeaponHashAndCamoTexIdIndexTuple> camoDiffusesTuples, WeaponHash rewardedWeaponHash, byte camoDiffuseTexId)
            {
                camoDiffusesTuples.Add(new WeaponHashAndCamoTexIdIndexTuple(rewardedWeaponHash, camoDiffuseTexId));
            }

            static void UpdateOrCreateNewEntry(Dictionary<IntPtr, List<WeaponHashAndCamoTexIdIndexTuple>> camoDiffusesDict, IntPtr cPedPointerToReward, WeaponHash rewardedWeaponHash, byte camoDiffuseTexId)
            {
                if (!camoDiffusesDict.TryGetValue(cPedPointerToReward, out var weaponHashAndCamotexIdTuples))
                {
                    var newList = new List<WeaponHashAndCamoTexIdIndexTuple>
                    {
                        new WeaponHashAndCamoTexIdIndexTuple(rewardedWeaponHash, camoDiffuseTexId)
                    };
                    camoDiffusesDict[cPedPointerToReward] = newList;
                }
                else
                {
                    weaponHashAndCamotexIdTuples.Add(new WeaponHashAndCamoTexIdIndexTuple(rewardedWeaponHash, camoDiffuseTexId));
                }
            }
        }

        public static int GetProcessMainThreadId()
        {
            long lowestStartTime = long.MaxValue;
            ProcessThread? lowestStartTimeThread = null;
            foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
            {
                long startTime = thread.StartTime.Ticks;
                if (startTime < lowestStartTime)
                {
                    lowestStartTime = startTime;
                    lowestStartTimeThread = thread;
                }
            }

            return lowestStartTimeThread == null ? -1 : lowestStartTimeThread.Id;
        }

        static Memory()
        {
#if RPH
            AssemblyResolutionUtils.LoadEasyHook();

            var tlsAllocatorOffsetAddr = MemoryScan.FindPatternBmh("B9 ? ? ? ? 48 8B 0C 01 45 33 C9 49 8B D2");
            if (tlsAllocatorOffsetAddr != IntPtr.Zero)
            {
                TLS_AllocatorOffset = *(int*)(tlsAllocatorOffsetAddr + 1);
            }

            var address = (byte*)MemoryScan.FindPatternBmh("\x4C\x8B\x05\x00\x00\x00\x00\x40\x8A\xF2\x8B\xE9", "xxx????xxxxx");
            pickupObjectPoolPointerOfPointerAddress = new IntPtr((long)(*(int*)(address + 3) + address + 7));

            address = (byte*)MemoryScan.FindPatternBmh("\x4C\x8B\x0D\x00\x00\x00\x00\x44\x8B\xC1\x49\x8B\x41\x08", "xxx????xxxxxxx");
            fwScriptGuidPoolPointerOfPointerAddress = new IntPtr((long)(*(int*)(address + 3) + address + 7));

            address = (byte*)MemoryScan.FindPatternBmh("\x48\xF7\xF9\x49\x8B\x48\x08\x48\x63\xD0\xC1\xE0\x08\x0F\xB6\x1C\x11\x03\xD8", "xxxxxxxxxxxxxxxxxxx");
            createGuidFuncAddress = new IntPtr(address - 0x68);
#endif
            IntPtr weaponAndAmmoInfoArrayAddress = IntPtr.Zero;
            int weaponInfoCamoDiffuseTexIdxsOffset = 0;

            var patternAddr = MemoryScan.FindPatternBmh("\x48\x8B\x05\x00\x00\x00\x00\x41\x8B\x1E", "xxx????xxx");
            if (patternAddr != IntPtr.Zero)
            {
                weaponAndAmmoInfoArrayAddress = (patternAddr + *(int*)(patternAddr + 3) + 7);
            }

            patternAddr = MemoryScan.FindPatternBmh("74 47 41 8B 41 18 48 8D 54 24 38 48 81 C1");
            if (patternAddr != IntPtr.Zero)
            {
                weaponInfoCamoDiffuseTexIdxsOffset = *(int*)(patternAddr + 14);
            }

            CWeaponInfo.SetUpStaticCamoDiffuseTexIdxsProperties(weaponAndAmmoInfoArrayAddress, weaponInfoCamoDiffuseTexIdxsOffset);
            weaponHashesWithCamoDiffuseTexIdxsBinaryMap = CWeaponInfo.WeaponHashesWithCamoDiffuseTexIdxs ?? new();

            patternAddr = MemoryScan.FindPatternBmh("74 65 80 78 28 04 75 5F 48 8B 88 ? ? 00 00 48 85 C9 74 53 8B 52 10 48 83 C1 18 E8");
            if (patternAddr != IntPtr.Zero)
            {
                CPed_CPedInventoryOffset = *(int*)(patternAddr + 11);
            }

            patternAddr = MemoryScan.FindPatternBmh("74 6A 48 8B 8A ? ? 00 00 41 80 E0 01 41 BB FF EF 00 00 66 44 21 9A D8 00 00 00");
            if (patternAddr != IntPtr.Zero)
            {
                CObject_CWeaponOffset = *(int*)(patternAddr + 5);
            }

            patternAddr = MemoryScan.FindPatternBmh("72 C8 33 F6 49 8B D7 48 8B CB E8");
            if (patternAddr != IntPtr.Zero)
            {
                var setOrClearOwnerPedOfCPickupFuncAddr = patternAddr + *(int*)(patternAddr + 11) + 15;
                CPickup_OwnerCPedOffset = *(int*)(setOrClearOwnerPedOfCPickupFuncAddr + 13);
            }

            // just in case for SHVDN
            var tempAssemblyResolver = new ResolveEventHandler(AssemblyResolutionUtils.MyResolveEventHandler);
            AppDomain.CurrentDomain.AssemblyResolve += tempAssemblyResolver;

            patternAddr = MemoryScan.FindPatternBmh("74 15 89 50 08 48 8D 0D ? ? ? ? 48 89 08 89 50 10 88 50 14 EB 03");
            if (patternAddr != IntPtr.Zero)
            {
                var cPickupRewardWeaponVTable = (patternAddr + *(int*)(patternAddr + 8) + 12);

                // just in case the offset is changed again, you can find one with "74 0F 48 8B 03 4C 8B C5 48 8B D6 48 8B CB FF 50 ? FF C7 41 3B FE 72 9C" (the offset value is the value at "?")
#if SHVDN
                var rewardPedWithWeaponFuncOffset = Game.Version >= GameVersion.v1_0_2802_0 ? 0x40 : 0x10;
#endif
#if RPH
                var rewardPedWithWeaponFuncOffset = Game.BuildNumber >= 2802 ? 0x40 : 0x10;
#endif

                var origRewardPedWithWeaponFuncAddr = new IntPtr(*(long**)(cPickupRewardWeaponVTable + rewardPedWithWeaponFuncOffset));
                _origRewardPedWithWeaponFunc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)origRewardPedWithWeaponFuncAddr;

                _hookedrewardPedWithWeaponFunc = LocalHook.Create(origRewardPedWithWeaponFuncAddr, new RewardPedWithWeaponDelegate(RewardPedWithWeaponHook), null);
            }

            AppDomain.CurrentDomain.AssemblyResolve -= tempAssemblyResolver;

            patternAddr = MemoryScan.FindPatternBmh("33 DB E8 ? ? ? ? 48 85 C0 74 07 48 8B 40 20 8B 58 18");
            if (patternAddr != IntPtr.Zero)
            {
                _getLocalPlayerPedAddressFunc = (delegate* unmanaged[Stdcall]<IntPtr>)(patternAddr + *(int*)(patternAddr + 3) + 7);
            }

            // The function we should hook will run only on the main thread
            _hookedrewardPedWithWeaponFunc?.ThreadACL.SetInclusiveACL(new int[] { GetProcessMainThreadId() });
        }

        public static void OnUnload()
        {
            _hookedrewardPedWithWeaponFunc?.Dispose();
        }

        public static List<WeaponHashAndCamoTexIdIndexTuple> DesiredCamoDiffusesForLocalPlayerPed
        {
            get => desiredCamoDiffusesForLocalPlayerPed;
        }

        public static Dictionary<IntPtr, List<WeaponHashAndCamoTexIdIndexTuple>>? DesiredCamoDiffusesFallback
        {
            get => desiredCamoDiffusesFallback;
        }

        public static HashSet<WeaponHash> WeaponHashesWithCamoDiffuseTexIdxsBinaryMap
        {
            get => weaponHashesWithCamoDiffuseTexIdxsBinaryMap;
        }

#if RPH
        public static IntPtr PickupObjectPoolPointerOfPointerAddress
        {
            get => pickupObjectPoolPointerOfPointerAddress;
        }
        public static IntPtr FwScriptGuidPoolPointerOfPointerAddress
        {
            get => fwScriptGuidPoolPointerOfPointerAddress;
        }
        public static IntPtr CreateGuidFuncAddress
        {
            get => createGuidFuncAddress;
        }
#endif
    }

    public static class WeaponPropExtensions
    {
        /// <summary>
        /// Get the weapon hash from a CObject instance that has a CWeapon pointer.
        /// </summary>
        public static IntPtr GetCWeaponAddress(IntPtr cObjectAddress)
        {
            unsafe
            {
                if (CObject_CWeaponOffset == 0)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(*(byte**)(cObjectAddress + CObject_CWeaponOffset));
            }
        }

        /// <summary>
        /// Get the weapon hash from a CObject instance that has a CWeapon pointer.
        /// </summary>
        // R*, you motherfucking piece of shit gang-banging cocksucker!
        public static WeaponHash GetWeaponHashFromWeaponProp(this Prop weaponProp)
        {
            const WeaponHash NULL_HASH = (WeaponHash)0;

            var cObjectAddr = weaponProp.MemoryAddress;
            if (cObjectAddr == IntPtr.Zero)
                return NULL_HASH;

            return GetWeaponHashFromCObjectWithCWeapon(cObjectAddr);
        }

        public static WeaponHash GetWeaponHashFromCObjectWithCWeapon(IntPtr cObjectAddress)
        {
            unsafe
            {
                const WeaponHash NULL_HASH = (WeaponHash)0;

                if (Memory.CObject_CWeaponOffset == 0)
                    return NULL_HASH;

                var cWeaponAddress = *(byte**)(cObjectAddress + CObject_CWeaponOffset);
                if (cWeaponAddress == null)
                {
                    return NULL_HASH;
                }

                var cWeaponInfoAddress = *(CWeaponInfo**)(cWeaponAddress + 0x40);
                if (cWeaponInfoAddress == null)
                {
                    return NULL_HASH;
                }

                return (WeaponHash)cWeaponInfoAddress->nameHash;
            }
        }

        public static bool SetCamoDiffuseTexId(this Prop prop, byte textureId)
        {
            unsafe
            {
                var cObjectAddr = prop.MemoryAddress;
                if (cObjectAddr == null)
                {
                    return false;
                }

                var cObjectDrawHandlerInst = *(byte**)(cObjectAddr + 0x48);
                if (cObjectDrawHandlerInst == null)
                {
                    return false;
                }

                // Note: Not all CObject with CWeapon instance pointers don't have pointers to CCustomShaderEffectBase
                var cCustomShaderEffectBaseInst = *(byte**)(cObjectDrawHandlerInst + 0x20);
                if (cCustomShaderEffectBaseInst == null)
                {
                    return false;
                }

                int cCustomShaderEffectTypeInst = (*(byte*)(cCustomShaderEffectBaseInst + 0xA) & 0xF);
                // Check if the shader effect is a CCustomShaderEffectWeapon instance just in case
                if (cCustomShaderEffectTypeInst != 0xA)
                {
                    return false;
                }

                // We have checked if the CCustomShaderEffectBase instance is a CCustomShaderEffectWeapon one, now check if a mandaroty flag is set
                var cCustomShaderEffectWeaponTypeInst = *(byte**)(cCustomShaderEffectBaseInst + 0x18);
                if (((*(cCustomShaderEffectWeaponTypeInst + 0x3F)) & 8) == 0)
                {
                    return false;
                }

                if (textureId >= 8)
                {
                    textureId = 7;
                }

                *(cCustomShaderEffectBaseInst + 0x41) = textureId;
                return true;
            }
        }

        public static byte GetCamoDiffuseTexId(this Prop prop)
        {
            unsafe
            {
                var cObjectAddr = prop.MemoryAddress;
                if (cObjectAddr == null)
                {
                    return 0;
                }

                return GetCamoDiffuseTexIdOfCObject(cObjectAddr);
            }
        }

        public static byte GetCamoDiffuseTexIdOfCObject(IntPtr cObjectAddress)
        {
            unsafe
            {
                var cObjectDrawHandlerInst = *(byte**)(cObjectAddress + 0x48);
                if (cObjectDrawHandlerInst == null)
                {
                    return 0;
                }

                // Note: Not all CObject with CWeapon instance pointers don't have pointers to CCustomShaderEffectBase
                var cCustomShaderEffectBaseInst = *(byte**)(cObjectDrawHandlerInst + 0x20);
                if (cCustomShaderEffectBaseInst == null)
                {
                    return 0;
                }

                int cCustomShaderEffectTypeInst = (*(byte*)(cCustomShaderEffectBaseInst + 0xA) & 0xF);
                // Check if the shader effect is a CCustomShaderEffectWeapon instance just in case
                if (cCustomShaderEffectTypeInst != 0xA)
                {
                    return 0;
                }

                // We have checked if the CCustomShaderEffectBase instance is a CCustomShaderEffectWeapon one, now check if a mandaroty flag is set
                var cCustomShaderEffectWeaponTypeInst = *(byte**)(cCustomShaderEffectBaseInst + 0x18);
                if (((*(cCustomShaderEffectWeaponTypeInst + 0x3F)) & 8) == 0)
                {
                    return 0;
                }

                return *(cCustomShaderEffectBaseInst + 0x41);
            }
        }
    }

    public static class PickupPropExtensions
    {
        public static IntPtr GetOwnerCPedAddress(IntPtr cPickupAddress)
        {
            unsafe
            {
                if (CPickup_OwnerCPedOffset == 0)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr(*(byte**)(cPickupAddress + CPickup_OwnerCPedOffset));
            }
        }
    }

    public static class WeaponInventoryExtensions
    {
        public static Weapon? GetCurrentWeaponProp(this Ped ped)
        {
#if SHVDN
            var entityInst = Entity.FromHandle(Function.Call<int>(Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX, ped, false));
            if (entityInst is Prop weaponProp)
            {
                return weaponProp;
            }

            return null;
#endif

#if RPH
            // ped.Inventory.EquippedWeaponObject takes about 10x slower than calling GET_CURRENT_PED_WEAPON_ENTITY_INDEX in SHVDN (with NoScriptThread of ScriptAttributes true in v3.6.0 or later or without NoScriptThread in v3.7.0 or later),
            // but calling GET_CURRENT_PED_WEAPON_ENTITY_INDEX via Rage.Native.DynamicNativeFunction, which can be accessed with "Rage.Native.NativeFunction.Natives", performs about 2–3 slower than PedInventory.EquippedWeaponObject
            // Fucking shame RPH devs adopted dynamic objects (CallSite) to call native functions and recommended plugin devs to use, which is not that good in performance, SHVDN and FiveM didn't adopt that way
            // You can't exactly predict what functions are available when you use with dynamic objects to call native functions, which is not in the case in SHVDN and FiveM

            // Also, thank to RPH for lazily using Activator.CreateInstance in the internal ContentCache.Get# methods (although Activator.CreateInstance will be called only once for the same handle in the same AppDomain),
            // that calls cost CPU cycles so much and SHVDN and FiveM use compiled dynamic methods to retrieve return values from natives since Sep 2018 (FiveM) or Jan 2019 (SHVDN)
            // The pull request SHVDN started to use a fast conversion with generic types from return values from native functions: https://github.com/crosire/scripthookvdotnet/pull/831
            // The commit FiveM started to use a fast conversion with generic types from return values from native functions: https://github.com/citizenfx/fivem/commit/fb5d89fccb9c9805174fd290fdb01355121b3703

            return ped.Inventory.EquippedWeaponObject;
#endif
        }

        public static unsafe CPedInventory* GetCPedInventory(this Ped ped)
        {
            return *(CPedInventory**)(ped.MemoryAddress + CPed_CPedInventoryOffset);
        }
    }
}

#if SHVDN
using GTA.Native;
using GTA;
using Weapon = GTA.Prop;
#endif
#if RPH
using Rage;
#endif

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PersistentWeaponBlood
{
    [StructLayout(LayoutKind.Explicit, Size = 0x80)]
    public struct CPedInventory
    {
        [FieldOffset(0x18)]
        public atArray<IntPtr> weaponInventoryArray;

        public bool TryGetWeaponInventoryItemAddressBySlotHash(uint slotHash, out IntPtr value)
        {
            int low = 0, high = weaponInventoryArray.Count - 1;
            while (true)
            {
                unsafe
                {
                    int indexToRead = (low + high) >> 1;
                    var currentItem = weaponInventoryArray.GetValueUnsafe(indexToRead);

                    var slotHashOfCurrentItem = *(uint*)(currentItem);
                    if (slotHashOfCurrentItem == slotHash)
                    {
                        value = currentItem;
                        return true;
                    }

                    // The array is sorted in ascending order
                    if (slotHashOfCurrentItem <= slotHash)
                        low = indexToRead + 1;
                    else
                        high = indexToRead - 1;

                    if (low > high)
                    {
                        value = default(IntPtr);
                        return false;
                    }
                }
            }
        }

        public bool TryGetCurrentWeaponHashBySlotHash(uint slotHash, out WeaponHash weaponHash)
        {
            int low = 0, high = weaponInventoryArray.Count - 1;
            while (true)
            {
                unsafe
                {
                    int indexToRead = (low + high) >> 1;
                    var currentItem = weaponInventoryArray.GetValueUnsafe(indexToRead);

                    var slotHashOfCurrentItem = *(uint*)(currentItem);
                    var weaponInfo = *(CWeaponInfo**)(currentItem + 0x8);
                    if (slotHashOfCurrentItem == slotHash || weaponInfo != null)
                    {
                        weaponHash = (WeaponHash)weaponInfo->nameHash;
                        return true;
                    }

                    // The array is sorted in ascending order
                    if (slotHashOfCurrentItem <= slotHash)
                        low = indexToRead + 1;
                    else
                        high = indexToRead - 1;

                    if (low > high)
                    {
                        weaponHash = default(WeaponHash);
                        return false;
                    }
                }
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x80)]
    public unsafe struct CWeapon
    {
        [FieldOffset(0x40)]
        public CWeaponInfo* weaponInfoPtr;

        [FieldOffset(0x58)]
        public void* cObjectPtr;
        [FieldOffset(0x60)]
        public void* cPedInventoryPtr;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    unsafe struct ItemInfo
    {
        [FieldOffset(0x0)]
        internal ulong* vTable;
        [FieldOffset(0x10)]
        internal uint nameHash;
        [FieldOffset(0x14)]
        internal uint modelHash;
        [FieldOffset(0x18)]
        internal uint audioHash;
        [FieldOffset(0x1C)]
        internal uint slot;

        internal uint GetClassNameHash()
        {
            // In the b2802 or a later version, the function returns a hash value (not a pointer value)

#if SHVDN
            if (Game.Version >= GameVersion.v1_0_2802_0)
#endif
#if RPH
            if (Game.BuildNumber >= 2802)
#endif
            {
                // The function is for the game version b2802 or later ones.
                // This one directly returns a hash value (not a pointer value) unlike the previous function.
                var GetClassNameHashFunc = (delegate* unmanaged[Stdcall]<uint>)(vTable[2]);
                return GetClassNameHashFunc();
            }
            else
            {
                // The function is for game versions prior to b2802.
                // The function uses rax and rdx registers in newer versions prior to b2802 (probably since b2189), and it uses only rax register in older versions.
                // The function returns the address where the class name hash is in all versions prior to (the address will be the outVal address in newer versions).
                var GetClassNameAddressHashFunc = (delegate* unmanaged[Stdcall]<ulong, uint*, uint*>)(vTable[2]);

                uint outVal = 0;
                var returnValueAddress = GetClassNameAddressHashFunc(0, &outVal);
                return *returnValueAddress;
            }
        }
    }

    // Specify dummy size to avoid unnecessary memory occupy by accident
    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public unsafe struct CWeaponInfo
    {
        [FieldOffset(0x0)]
        public ulong* vTable;
        [FieldOffset(0x10)]
        public uint nameHash;
        [FieldOffset(0x14)]
        public uint modelHash;
        [FieldOffset(0x18)]
        public uint audioHash;
        [FieldOffset(0x1C)]
        public uint slot;

        static HashSet<uint> blockWeaponHashSetForHumanPedsOnFoot = new HashSet<uint>()
            {
                0x1B79F17,  /* weapon_briefcase_02 */
			    0x166218FF, /* weapon_passenger_rocket */
			    0x32A888BD, /* weapon_tranquilizer */
			    0x687652CE, /* weapon_stinger */
			    0x6D5E2801, /* weapon_bird_crap */
			    0x88C78EB7, /* weapon_briefcase */
			    0xFDBADCED, /* weapon_digiscanner */
		    };

        static CWeaponInfo()
        {
            WeaponHashesWithCamoDiffuseTexIdxs = new();
            WeaponSlotHashDictionaryForNameHash = new();
        }

        internal static void SetUpStaticCamoDiffuseTexIdxsProperties(IntPtr weaponAndAmmoInfoArrayAddress, int weaponInfoCamoDiffuseTexIdxsOffset)
        {
            if (weaponAndAmmoInfoArrayAddress == null)
            {
                return;
            }

            atArray<IntPtr>* weaponAndAmmoInfoArrayPtr = (atArray<IntPtr>*)weaponAndAmmoInfoArrayAddress;

            var weaponAndAmmoInfoElementCount = weaponAndAmmoInfoArrayPtr->Count;
            var resultList = new List<WeaponHashAndSlotHashPair>();

            HashSet<WeaponHash> weaponHashesWithCamoDiffuseTexIdxs = new HashSet<WeaponHash>();
            Dictionary<WeaponHash, uint> weaponSlotHashDictionaryForNameHash = new Dictionary<WeaponHash, uint>();

            foreach (IntPtr itemInfoIntPtr in *weaponAndAmmoInfoArrayPtr)
            {
                var itemInfoClassPtr = (ItemInfo*)(itemInfoIntPtr);

                if (!CanPedEquip(itemInfoClassPtr) && !blockWeaponHashSetForHumanPedsOnFoot.Contains(itemInfoClassPtr->nameHash))
                    continue;

                var classNameHash = itemInfoClassPtr->GetClassNameHash();

                const uint CWEAPONINFO_NAME_HASH = 0x861905B4;
                if (classNameHash != CWEAPONINFO_NAME_HASH)
                {
                    continue;
                }
                var weaponInfoPtr = (CWeaponInfo*)(itemInfoClassPtr);
                var camoDiffuseTexIdxsBinaryMap = GetCamoDiffuseTexIdxs(weaponInfoPtr, weaponInfoCamoDiffuseTexIdxsOffset);
                if (camoDiffuseTexIdxsBinaryMap == null || camoDiffuseTexIdxsBinaryMap->Count == 0)
                {
                    continue;
                }

                var nameHash = (WeaponHash)itemInfoClassPtr->nameHash;
                var slotHash = itemInfoClassPtr->slot;
                weaponHashesWithCamoDiffuseTexIdxs.Add(nameHash);
                weaponSlotHashDictionaryForNameHash.Add(nameHash, slotHash);
            }

            WeaponHashesWithCamoDiffuseTexIdxs = weaponHashesWithCamoDiffuseTexIdxs;
            WeaponSlotHashDictionaryForNameHash = weaponSlotHashDictionaryForNameHash;

            static bool CanPedEquip(ItemInfo* weaponInfoAddress)
            {
                return weaponInfoAddress->modelHash != 0 && weaponInfoAddress->slot != 0;
            }

            static atBinaryMap* GetCamoDiffuseTexIdxs(CWeaponInfo* weaponInfo, int weaponInfoCamoDiffuseTexIdxsOffset)
            {
                if (weaponInfoCamoDiffuseTexIdxsOffset == 0)
                {
                    return null;
                }

                return (atBinaryMap*)((byte*)weaponInfo + weaponInfoCamoDiffuseTexIdxsOffset);
            }
        }

        internal static HashSet<WeaponHash> WeaponHashesWithCamoDiffuseTexIdxs { get; private set; }
        internal static Dictionary<WeaponHash, uint> WeaponSlotHashDictionaryForNameHash { get; private set; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public struct CPickupRewardWeapon
    {
        [FieldOffset(0x0)]
        public IntPtr vTable;
        [FieldOffset(0x8)]
        public uint nameHash; // case-insensitve
        [FieldOffset(0x10)]
        public uint weaponRefHash; // case-insensitve, used for a name of a CWeaponInfo instance
        [FieldOffset(0x14)]
        [MarshalAs(UnmanagedType.U1)]
        public bool equip;
    }
}

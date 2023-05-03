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
using System.Linq;

using System.Runtime.InteropServices;


namespace PersistentWeaponBlood
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct WeaponHashAndSlotHashPair
    {
        internal WeaponHash nameHash;
        internal uint slotHash;

        internal WeaponHashAndSlotHashPair(WeaponHash nameHash, uint slotHash)
        {
            this.nameHash = nameHash;
            this.slotHash = slotHash;
        }
    }

    internal sealed class WeaponCamoDiffuseTexTracker
    {
        internal static HashSet<WeaponHash>? _weaponHashesWithCamoDiffuseTexIdxsBinaryMap;
        internal static Dictionary<WeaponHash, uint>? _weaponSlotHashDictForNameHash;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct CamoDiffuseTexIdTrackerTuple
        {
            internal IntPtr weaponInventoryItemAddr;
            internal uint slotHash;
            internal byte camoDiffuseTexId;

            internal CamoDiffuseTexIdTrackerTuple(IntPtr weaponInventoryItemAddr, uint slotHash, byte camoDiffuseTexId)
            {
                this.weaponInventoryItemAddr = weaponInventoryItemAddr;
                this.slotHash = slotHash;
                this.camoDiffuseTexId = camoDiffuseTexId;
            }
        }

        internal Ped Ped { get; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal readonly struct WeaponHashAndCamoTexIdIndexTuple
        {
            internal readonly WeaponHash weaponHash;
            internal readonly byte camoTexIdIndex;

            internal WeaponHashAndCamoTexIdIndexTuple(WeaponHash weaponHash, byte camoTexIdIndex)
            {
                this.weaponHash = weaponHash;
                this.camoTexIdIndex = camoTexIdIndex;
            }
        }

        internal WeaponHashAndCamoTexIdIndexTuple[] PoppedWeaponHashAndCamoTexIdIndexTupleSinceLastUpdate
        {
            get
            {
                if (_poppedWeaponHashesSinceLastUpdate == null)
                {
                    return Array.Empty<WeaponHashAndCamoTexIdIndexTuple>();
                }

                return _poppedWeaponHashesSinceLastUpdate.ToArray();
            }
        }

        internal delegate void OnWatchedWeaponRemovedFromInventoryDelegate(Ped ped, WeaponHashAndCamoTexIdIndexTuple[] poppedWeapons, WeaponCamoDiffuseTexTracker sender);

        internal event OnWatchedWeaponRemovedFromInventoryDelegate? OnWatchedWeaponRemovedFromInventory;

        internal delegate void OnNoWatchedWeaponsLeftDelegate(Ped ped, WeaponCamoDiffuseTexTracker sender);

        internal event OnNoWatchedWeaponsLeftDelegate? OnNoWatchedWeaponsLeft;

        internal delegate void OnPedRemovedDelegate(Ped ped, WeaponCamoDiffuseTexTracker sender);

        internal event OnPedRemovedDelegate? OnPedRemoved;

        internal Func<Ped, bool>? PredicateToCleanAllWeapons { get; set; }

        private List<WeaponHashAndCamoTexIdIndexTuple>? _poppedWeaponHashesSinceLastUpdate;
        private readonly Dictionary<WeaponHash, CamoDiffuseTexIdTrackerTuple> _camoDiffuseTexIdCache;
        private Prop? _lastWeaponPropWithCamoTexIdxsCache;

        private bool _FiredOnPedRemovedEvent;
        private bool _FiredOnNoWatchedWeaponsLeftEvent;

        private readonly List<WeaponHashAndCamoTexIdIndexTuple> _requestedCamoTexIds = new();

        internal static void Init(HashSet<WeaponHash> weaponHashesWithCamoDiffuseTexIdxsBinaryMap, Dictionary<WeaponHash, uint> weaponSlotHashDictionaryForNameHash)
        {
            _weaponHashesWithCamoDiffuseTexIdxsBinaryMap = weaponHashesWithCamoDiffuseTexIdxsBinaryMap;
            _weaponSlotHashDictForNameHash = weaponSlotHashDictionaryForNameHash;
        }

        private WeaponCamoDiffuseTexTracker(Ped ped, Dictionary<WeaponHash, CamoDiffuseTexIdTrackerTuple> camoDiffuseTexIdCache, Prop? lastWeaponPropWithCamoTexIdxs)
        {
            Ped = ped;
            _camoDiffuseTexIdCache = camoDiffuseTexIdCache;
            _lastWeaponPropWithCamoTexIdxsCache = lastWeaponPropWithCamoTexIdxs;
        }

        internal void Update()
        {
            if (_FiredOnPedRemovedEvent)
            {
                return;
            }

            var ped = Ped;

            if (!ped.Exists())
            {
                _FiredOnPedRemovedEvent = true;
                OnPedRemoved?.Invoke(ped, this);
                return;
            }

            UpdateInternalState();
            FireRequestedEvents();
        }

        private void UpdateInternalState()
        {
            var ped = Ped;

            unsafe
            {
                var cPedInventoryAddress = new IntPtr(ped.GetCPedInventory());
                if (cPedInventoryAddress == IntPtr.Zero)
                {
                    _poppedWeaponHashesSinceLastUpdate = null;
                    return;
                }

                _poppedWeaponHashesSinceLastUpdate = PopRemovedWeaponHashesFromInventoryTracker(cPedInventoryAddress);
                SetRequestedCamoDiffuseTexIdxs(cPedInventoryAddress, _requestedCamoTexIds);

                if (PredicateToCleanAllWeapons != null && PredicateToCleanAllWeapons(ped))
                {
                    _camoDiffuseTexIdCache.Clear();
                    return;
                }

                var currentWeaponProp = ped.GetCurrentWeaponProp();
                if (currentWeaponProp == null)
                {
                    return;
                }

                var curWeapHashOfWeaponProp = currentWeaponProp.GetWeaponHashFromWeaponProp();
                if (curWeapHashOfWeaponProp == 0)
                {
                    return;
                }

                if (_camoDiffuseTexIdCache.TryGetValue(curWeapHashOfWeaponProp, out var desiredCamoDuffseTexIdTuple))
                {
                    if (currentWeaponProp != _lastWeaponPropWithCamoTexIdxsCache)
                    {
                        _lastWeaponPropWithCamoTexIdxsCache = currentWeaponProp;
                        currentWeaponProp.SetCamoDiffuseTexId(desiredCamoDuffseTexIdTuple.camoDiffuseTexId);
                    }
                    else
                    {
                        desiredCamoDuffseTexIdTuple.camoDiffuseTexId = currentWeaponProp.GetCamoDiffuseTexId();
                        _camoDiffuseTexIdCache[curWeapHashOfWeaponProp] = desiredCamoDuffseTexIdTuple;
                    }
                }
                else if (_weaponHashesWithCamoDiffuseTexIdxsBinaryMap != null && _weaponHashesWithCamoDiffuseTexIdxsBinaryMap.Contains(curWeapHashOfWeaponProp))
                {
                    if (currentWeaponProp != _lastWeaponPropWithCamoTexIdxsCache)
                    {
                        _lastWeaponPropWithCamoTexIdxsCache = currentWeaponProp;
                    }

                    var camoDiffuseTexutreId = currentWeaponProp.GetCamoDiffuseTexId();
                    AddCamoDiffuseTexIdCacheEntryForWeaponHash(cPedInventoryAddress, curWeapHashOfWeaponProp, camoDiffuseTexutreId);
                }
            }
        }

        private void SetRequestedCamoDiffuseTexIdxs(IntPtr cPedInventoryAddr, List<WeaponHashAndCamoTexIdIndexTuple> requestedCamoDiffuseTexIdTuples)
        {
            foreach (var requestedCamoTexIdTuple in requestedCamoDiffuseTexIdTuples)
            {
                AddCamoDiffuseTexIdCacheEntryForWeaponHash(cPedInventoryAddr, requestedCamoTexIdTuple.weaponHash, requestedCamoTexIdTuple.camoTexIdIndex);
            }
            if (_requestedCamoTexIds.Count > 0)
            {
                _requestedCamoTexIds.Clear();
            }
        }

        private void FireRequestedEvents()
        {
            var ped = Ped;

            if (_poppedWeaponHashesSinceLastUpdate != null)
            {
                OnWatchedWeaponRemovedFromInventory?.Invoke(ped, PoppedWeaponHashAndCamoTexIdIndexTupleSinceLastUpdate, this);
            }
            if (!_FiredOnNoWatchedWeaponsLeftEvent && _camoDiffuseTexIdCache.Count == 0)
            {
                OnNoWatchedWeaponsLeft?.Invoke(ped, this);
            }
        }
        private bool AddCamoDiffuseTexIdCacheEntryForWeaponHash(IntPtr cPedInventoryAddress, WeaponHash hash, byte camoDiffuseTexutreId)
        {
            unsafe
            {
                if (_weaponSlotHashDictForNameHash == null || !_weaponSlotHashDictForNameHash.TryGetValue(hash, out var slotHash))
                {
                    return false;
                }

                CPedInventory* cPedInventory = (CPedInventory*)(cPedInventoryAddress);

                if (!cPedInventory->TryGetWeaponInventoryItemAddressBySlotHash(slotHash, out var entryAddress))
                {
                    return false;
                }

                _FiredOnNoWatchedWeaponsLeftEvent = false;
                _camoDiffuseTexIdCache[hash] = new CamoDiffuseTexIdTrackerTuple(entryAddress, slotHash, camoDiffuseTexutreId);
                return true;
            }
        }

        internal void RequestCamoDiffuseTexIdForWeaponHash(WeaponHash hash, byte camoDiffuseTexutreId)
        {
            _requestedCamoTexIds.Add(new WeaponHashAndCamoTexIdIndexTuple(hash, camoDiffuseTexutreId));
        }

        private List<WeaponHashAndCamoTexIdIndexTuple>? PopRemovedWeaponHashesFromInventoryTracker(IntPtr cPedInventoryAddress)
        {
            unsafe
            {
                CPedInventory* cPedInventory = (CPedInventory*)(cPedInventoryAddress);

                List<WeaponHashAndCamoTexIdIndexTuple>? removedItems = null;
                foreach (var curCamoDiffuseCache in _camoDiffuseTexIdCache)
                {
                    var camoDiffuseTuple = curCamoDiffuseCache.Value;

                    // The entry address will be changed when a new weapon inventory entry replaces an existing entry with the same slot (this is an edge case since valilla weapons usually use unique slot hashes)
                    if (!cPedInventory->TryGetWeaponInventoryItemAddressBySlotHash(camoDiffuseTuple.slotHash, out var currentEntryAddress) || currentEntryAddress != camoDiffuseTuple.weaponInventoryItemAddr)
                    {
                        if (removedItems == null)
                        {
                            removedItems = new();
                        }
                        var newRemovedItem = new WeaponHashAndCamoTexIdIndexTuple(curCamoDiffuseCache.Key, curCamoDiffuseCache.Value.camoDiffuseTexId);
                        removedItems.Add(newRemovedItem);
                    }
                }
                if (removedItems != null)
                {
                    foreach (var removedItem in removedItems)
                    {
                        _camoDiffuseTexIdCache.Remove(removedItem.weaponHash);
                    }
                }

                return removedItems;
            }
        }

        internal static WeaponCamoDiffuseTexTracker? Create(Ped ped, Dictionary<WeaponHash, byte>? desiredCamoTexIds = null)
        {
            unsafe
            {
                var cPedInventory = ped.GetCPedInventory();
                if (cPedInventory == null)
                {
                    return null;
                }

                var inventoryItemArray = cPedInventory->weaponInventoryArray;

                Dictionary<WeaponHash, CamoDiffuseTexIdTrackerTuple>? tempTuple = null;
                foreach (var item in inventoryItemArray)
                {
                    var weaponInfo = *(CWeaponInfo**)(item + 0x8);

                    var nameHash = (WeaponHash)weaponInfo->nameHash;
                    if (_weaponHashesWithCamoDiffuseTexIdxsBinaryMap != null && _weaponHashesWithCamoDiffuseTexIdxsBinaryMap.Contains(nameHash))
                    {
                        var slotHash = *(uint*)(item);

                        if (tempTuple == null)
                        {
                            tempTuple = new Dictionary<WeaponHash, CamoDiffuseTexIdTrackerTuple>();
                        }

                        byte camoTexId = 0;

                        desiredCamoTexIds?.TryGetValue(nameHash, out camoTexId);

                        tempTuple[nameHash] = new CamoDiffuseTexIdTrackerTuple(item, slotHash, camoTexId);
                    }
                }

                if (tempTuple == null)
                {
                    return null;
                }

                var currentProp = ped.GetCurrentWeaponProp();
                return new WeaponCamoDiffuseTexTracker(ped, tempTuple, currentProp);
            }
        }
    }
}

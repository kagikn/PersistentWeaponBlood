#if SHVDN
using GTA;
#endif
#if RPH
using Rage;
using Prop = Rage.Object;
#endif

using System;
using System.Collections.Generic;
using System.Linq;

#if RPH
[assembly: Rage.Attributes.Plugin("Persistent Weapon Blood",
                                  Author = "kagikn",
                                  Description = "Tracks blood states (strictly CamoDiffuseTexIdxs values) of weapons and restores when switched to them again.",
                                  SupportUrl = "https://github.com/kagikn/PersistentWeaponBlood",
                                  PrefersSingleInstance = true)]
#endif

namespace PersistentWeaponBlood
{
#if SHVDN
    // No possible Script.Wait calls during the OnTick call, so specify NoScriptThread to true to avoid thread switching at all
    // Since SHVDN use the TLS swap trick to avoid non-negligible cpu-cycle comsuption by thread switch starting from v3.7.0,
    // probably NoScriptThread has non-negligible performance boost only in v3.6.0 aside from thread switching that happens twice per tick (right before OnTick and right after OnTick)
    [ScriptAttributes(Author = "kagikn", NoScriptThread = true)]
    internal unsafe sealed class PersistentWeaponBlood : Script
#endif
#if RPH
    internal unsafe static class EntryPoint
#endif
    {
        private static Dictionary<IntPtr, Dictionary<WeaponHash, byte>>? _removedWeaponHashDictAgainstCPed = null;
        private static readonly Dictionary<Ped, WeaponCamoDiffuseTexTracker> _weaponCamoTrackingInfo = new();
        private static readonly List<Ped> _pedsRequestedToRemoveFromTracking = new();
#pragma warning disable CS8618
        private static Prop[] _pickupPropsPrevFrame;
        private static HashSet<WeaponHash> _weaponHashesWithCamoDiffuseTexIdxs;
#pragma warning restore CS8618

        private const string NO_WEAP_WITH_CAMO_DIFFUSE_TEX_IDXS = "There are no weapons with valid CamoDiffuseTexIdxs info, terminating Persistent Weapon Blood.";

#if RPH
        internal static void Main()
        {
            if (!Init())
            {
                Game.LogTrivial(NO_WEAP_WITH_CAMO_DIFFUSE_TEX_IDXS);
                return;
            }

            while (true)
            {
                Rage.GameFiber.Yield();
                Update();
            }
        }

        // void OnUnload(bool) signature is required by RPH for the unload method, so keep the bool parameter
#pragma warning disable IDE0060
        internal static void OnUnload(bool isTerminating)
#pragma warning restore IDE0060
        {
            Memory.OnUnload();
        }
#endif
#if SHVDN

        public PersistentWeaponBlood()
        {
            Tick += OnTick;
            Aborted += OnAborted;

            if (!Init())
            {
                throw new InvalidOperationException(NO_WEAP_WITH_CAMO_DIFFUSE_TEX_IDXS);
            }
        }

        internal void OnTick(object sender, EventArgs e)
        {
            Update();
        }

        internal void OnAborted(object sender, EventArgs e)
        {
            Memory.OnUnload();
        }
#endif

        internal static bool Init()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Memory).TypeHandle);

            var weaponHashesWithCamoDiffuseTexIdxs = CWeaponInfo.WeaponHashesWithCamoDiffuseTexIdxs;
            var weaponSlotHashDictionaryForNameHash = CWeaponInfo.WeaponSlotHashDictionaryForNameHash;

            if (weaponHashesWithCamoDiffuseTexIdxs.Count == 0)
            {
                return false;
            }

            WeaponCamoDiffuseTexTracker.Init(weaponHashesWithCamoDiffuseTexIdxs, weaponSlotHashDictionaryForNameHash);

            _weaponHashesWithCamoDiffuseTexIdxs = weaponHashesWithCamoDiffuseTexIdxs;
            _pickupPropsPrevFrame = GetAllPickupObjects();

            return true;
        }

        internal static void Update() 
        {
            _removedWeaponHashDictAgainstCPed = null;

            UpdateCamoDiffuseTexTrackers();
            SetCamoDiffuseTexIdxsForNewPickup();
        }

        private static void OnWatchedWeaponsRemovedFromInventory(Ped ped, WeaponCamoDiffuseTexTracker.WeaponHashAndCamoTexIdIndexTuple[] removedWeapons, WeaponCamoDiffuseTexTracker sender)
        {
            _removedWeaponHashDictAgainstCPed ??= new Dictionary<IntPtr, Dictionary<WeaponHash, byte>>();

            _removedWeaponHashDictAgainstCPed[ped.MemoryAddress] = removedWeapons.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex);
        }

        private static void OnNoWatchedWeaponsLeft(Ped ped, WeaponCamoDiffuseTexTracker sender)
        {
            _pedsRequestedToRemoveFromTracking.Add(ped);
        }
        private static void OnPedRemovedForTracker(Ped ped, WeaponCamoDiffuseTexTracker sender)
        {
            _pedsRequestedToRemoveFromTracking.Add(ped);
        }

        static void UpdateCamoDiffuseTexTrackers()
        {
            // Easy on the CPU cache
            // Calling PLAYER_ID and GET_PLAYER_PED every time makes the CPU cache even less effective,
            // just compare a cached local variable with a iterated ped by their handle values
            var currentPlayerPed = GetLocalPlayerPed();
            var desiredCamoDiffusesForLocalPlayerPed = Memory.DesiredCamoDiffusesForLocalPlayerPed;
            var desiredCamoDiffusesFallback = Memory.DesiredCamoDiffusesFallback;

            foreach (var ped in World.GetAllPeds())
            {
                if (_weaponCamoTrackingInfo.TryGetValue(ped, out var tracker))
                {
                    if (ped == currentPlayerPed && desiredCamoDiffusesForLocalPlayerPed.Count > 0)
                    {
                        foreach (var desiredCamoDiffuse in desiredCamoDiffusesForLocalPlayerPed)
                        {
                            tracker.RequestCamoDiffuseTexIdForWeaponHash(desiredCamoDiffuse.weaponHash, desiredCamoDiffuse.camoTexIdIndex);
                        }
                        desiredCamoDiffusesForLocalPlayerPed.Clear();
                    }
                    else if (desiredCamoDiffusesFallback != null && desiredCamoDiffusesFallback.TryGetValue(ped.MemoryAddress, out var desiredCamoDiffuses))
                    {
                        // Edge case
                        foreach (var desiredCamoDiffuse in desiredCamoDiffuses)
                        {
                            tracker.RequestCamoDiffuseTexIdForWeaponHash(desiredCamoDiffuse.weaponHash, desiredCamoDiffuse.camoTexIdIndex);
                        }
                    }

                    tracker.Update();
                }             
                else
                {
                    if (ped == currentPlayerPed && desiredCamoDiffusesForLocalPlayerPed.Count > 0)
                    {
                        RegisterTrackingInfo(ped, desiredCamoDiffusesForLocalPlayerPed.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex));
                        desiredCamoDiffusesForLocalPlayerPed.Clear();
                    }
                    else if (desiredCamoDiffusesFallback != null && desiredCamoDiffusesFallback.TryGetValue(ped.MemoryAddress, out var desiredCamoDiffuses))
                    {
                        // Edge case
                        RegisterTrackingInfo(ped, desiredCamoDiffuses.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex));
                    }
                    else
                    {
                        var weaponProp = ped.GetCurrentWeaponProp();
                        if (weaponProp == null)
                        {
                            continue;
                        }

                        var weaponHashForWeaponProp = weaponProp.GetWeaponHashFromWeaponProp();
                        if (weaponHashForWeaponProp == 0)
                        {
                            continue;
                        }

                        if (_weaponHashesWithCamoDiffuseTexIdxs.Contains(weaponHashForWeaponProp))
                        {
                            RegisterTrackingInfo(ped, null);
                        }
                    }
                }
            }

            if (desiredCamoDiffusesFallback != null && desiredCamoDiffusesFallback.Count > 0)
            {
                // Edge case
                desiredCamoDiffusesFallback.Clear();
            }

            if (_pedsRequestedToRemoveFromTracking.Count > 0)
            {
                foreach (var pedToRemoveFromTracker in _pedsRequestedToRemoveFromTracking)
                {
                    _weaponCamoTrackingInfo.Remove(pedToRemoveFromTracker);
                }
                _pedsRequestedToRemoveFromTracking.Clear();
            }
        }

        static void SetCamoDiffuseTexIdxsForNewPickup()
        {
            var curPickupCollection = GetAllPickupObjects();
            var newPickupCollection = curPickupCollection.Except(_pickupPropsPrevFrame).ToArray();

            if (_removedWeaponHashDictAgainstCPed != null && newPickupCollection.Length > 0)
            {
                foreach (var newPickup in newPickupCollection)
                {
                    unsafe
                    {
                        var pickupAddr = newPickup.MemoryAddress;

                        var ownerCPedAddr = PickupPropExtensions.GetOwnerCPedAddress(pickupAddr);

                        if (!_removedWeaponHashDictAgainstCPed.TryGetValue(ownerCPedAddr, out var removedItems))
                        {
                            continue;
                        }

                        var cWeaponAddr = (CWeapon*)WeaponPropExtensions.GetCWeaponAddress(pickupAddr);
                        if (cWeaponAddr == null)
                        {
                            continue;
                        }

                        var cWeaponInfoAddr = cWeaponAddr->weaponInfoPtr;
                        if (cWeaponInfoAddr == null || !removedItems.TryGetValue((WeaponHash)cWeaponInfoAddr->nameHash, out var camoTexId))
                        {
                            continue;
                        }

                        newPickup.SetCamoDiffuseTexId(camoTexId);
                    }
                }
            }

            _pickupPropsPrevFrame = curPickupCollection;
        }

        static void RegisterTrackingInfo(Ped ped, Dictionary<WeaponHash, byte>? desiredCamoTexIdxs = null)
        {
            var trackingInfo = WeaponCamoDiffuseTexTracker.Create(ped, desiredCamoTexIdxs);
            if (trackingInfo == null) { return; }
            trackingInfo.OnWatchedWeaponRemovedFromInventory += OnWatchedWeaponsRemovedFromInventory;
            trackingInfo.OnNoWatchedWeaponsLeft += OnNoWatchedWeaponsLeft;
            trackingInfo.OnPedRemoved += OnPedRemovedForTracker;

            _weaponCamoTrackingInfo[ped] = trackingInfo;
        }

        static Ped GetLocalPlayerPed()
        {
#if SHVDN
            return Game.Player.Character;
#endif
#if RPH
            return Game.LocalPlayer.Character;
#endif
        }

        static Prop[] GetAllPickupObjects()
        {
#if SHVDN
            return World.GetAllPickupObjects();
#endif
#if RPH
            return PickupUtils.GetPickupObjectHandles();
#endif
        }
    }
}

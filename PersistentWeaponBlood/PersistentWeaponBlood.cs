#if SHVDN
using GTA;
using GTA.Native;
#endif
#if RPH
using Rage;
using Rage.Native;
using Prop = Rage.Object;
#endif

using System;
using System.Collections.Generic;
using System.IO;
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
    public unsafe sealed class PersistentWeaponBlood : Script
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
        private static Configs _configs;
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

            // While you can load an assembly in a subdirectory of the specified plugin folder, you can't find the folder the assembly was loaded from (at least with Assembly.Location or Assembly.CodeBase)
            // RPH should have provide ways to retrieve filenames and directories of plugins just in case users bother to load them from subdirectories of the specified plugin folder!

            // AppDomain.CurrentDomain.BaseDirectory will specify the specified plugin folder in RPH plugins
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(typeof(EntryPoint).Assembly.Location);
            var iniFilePathRelative = Path.Combine("Plugins", fileNameWithoutExtension + ".ini");

            _configs = new Configs(iniFilePathRelative);

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
            if (!Init())
            {
                throw new InvalidOperationException(NO_WEAP_WITH_CAMO_DIFFUSE_TEX_IDXS);
            }

            string iniFilePath = Path.Combine(BaseDirectory, Path.GetFileNameWithoutExtension(Filename) + ".ini");
            GTA.UI.Notification.Show(iniFilePath.ToString());
            _configs = new Configs(iniFilePath);

            Tick += OnTick;
            Aborted += OnAborted;
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

            if (_configs.RegisterCheatCodes)
            {
                ActivateCheatCodeForWeaponCleaningIfNecessary();
            }

            UpdateCamoDiffuseTexTrackers();
            SetCamoDiffuseTexIdxsForNewPickup();
        }

#if RPH
        [Rage.Attributes.ConsoleCommand(Name = "CleanAllPlayerMeleeWeapons")]
#endif
        public static void CleanAllPlayerMeleeWeapons()
        {
            var playerPed = GetLocalPlayerPed();
            if (playerPed == null)
            {
                return;
            }

            if (_weaponCamoTrackingInfo.ContainsKey(playerPed))
            {
                ClearCurrentWeaponPropBlood(playerPed);
                _weaponCamoTrackingInfo.Remove(playerPed);
            }
            else
            {
                ClearCurrentWeaponPropBlood(playerPed);
            }
        }

#if RPH
        [Rage.Attributes.ConsoleCommand(Name = "CleanCurrentPlayerMeleeWeapon")]
#endif
        public static void CleanCurrentPlayerMeleeWeapon()
        {
            var playerPed = GetLocalPlayerPed();
            if (playerPed == null)
            {
                return;
            }

            var weaponProp = playerPed.GetCurrentWeaponProp();
            if (weaponProp == null)
            {
                return;
            }

            var weaponHash = weaponProp.GetWeaponHashFromWeaponProp();
            if (weaponHash == 0)
            {
                return;
            }

            if (_weaponCamoTrackingInfo.TryGetValue(playerPed, out var tracker))
            {
                tracker.RequestCamoDiffuseTexIdForWeaponHash(weaponHash, 0);
            }

            weaponProp.SetCamoDiffuseTexId(0);
        }

#region Cheat Code Methods

        private static void ActivateCheatCodeForWeaponCleaningIfNecessary()
        {
            const uint CLEAN_OFF_ALL_PLAYER_WEAPONS_HASH = 0x5B3B68EA; /* CLEANALLPLAYERMELEEWEAPONS */
            const uint CLEAN_OFF_CURRENT_PLAYER_WEAPON_HASH = 0xF6BE4F24; /* CLEANCURRENTPLAYERMELEEWEAPON */

            if (HasPCCheatCodeWithHashBeenActivated(CLEAN_OFF_ALL_PLAYER_WEAPONS_HASH))
            {
                CleanAllPlayerMeleeWeapons();
                TheFeed.PostTickerToTheFeed(GetMessageForCleanAllPlayerMeleeWeapons(), false, false);
            }
            else if (HasPCCheatCodeWithHashBeenActivated(CLEAN_OFF_CURRENT_PLAYER_WEAPON_HASH))
            {
                CleanCurrentPlayerMeleeWeapon();
                TheFeed.PostTickerToTheFeed(GetMessageForCleanCurrentPlayerMeleeWeapon(), false, false);
            }
        }

        private static string GetMessageForCleanAllPlayerMeleeWeapons()
        {
            switch (GetLanguageIndex())
            {
                case 10: // japanese
                    return $"{MOD_NAME_FOR_CHEAT_CODE}: プレイヤーのすべての近接武器から血を除去しました。";
                default:
                    return $"{MOD_NAME_FOR_CHEAT_CODE}: Cleaned blood off all player melee weapons.";
            }
        }

        private static string GetMessageForCleanCurrentPlayerMeleeWeapon()
        {
            switch (GetLanguageIndex())
            {
                case 10: // japanese
                    return $"{MOD_NAME_FOR_CHEAT_CODE}: プレイヤーの現在の武器から血を除去しました。";
                default:
                    return $"{MOD_NAME_FOR_CHEAT_CODE}: Cleaned blood off all player melee weapons.";
            }
        }

        const string MOD_NAME_FOR_CHEAT_CODE = "Persistent Weapon Blood";

        private static int GetLanguageIndex()
        {
#if SHVDN
            return (int)Game.Language;
#endif
#if RPH
            return NativeFunction.CallByHash<int>(0x2BDD44CC428A7EAE);
#endif
        }

        private static bool HasPCCheatCodeWithHashBeenActivated(uint hash)
        {
#if SHVDN
            return Function.Call<bool>(Hash.HAS_PC_CHEAT_WITH_HASH_BEEN_ACTIVATED, hash);
#endif
#if RPH
            // Will be expected frequently, use NativeFunction.CallByHash on purpose rather than DynamicNativeFunction in favor of performance
            return NativeFunction.CallByHash<bool>(0x557E43C447E700A8, hash);
#endif
        }

#endregion

        private static void OnWatchedWeaponsRemovedFromInventory(Ped ped, WeaponCamoDiffuseTexTracker.WeaponHashAndCamoTexIdIndexTuple[] removedWeapons, WeaponCamoDiffuseTexTracker sender)
        {
            _removedWeaponHashDictAgainstCPed ??= new Dictionary<IntPtr, Dictionary<WeaponHash, byte>>();

            _removedWeaponHashDictAgainstCPed[ped.MemoryAddress] = removedWeapons.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex);
        }

        private static void RegisterPedToRemoveFromTracking(Ped ped, WeaponCamoDiffuseTexTracker sender)
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
            var configs = _configs;

            foreach (var ped in World.GetAllPeds())
            {
                if (_weaponCamoTrackingInfo.TryGetValue(ped, out var tracker))
                {
                    if (ped == currentPlayerPed && desiredCamoDiffusesForLocalPlayerPed.Count > 0)
                    {
                        SetDesiredCamoDiffuseTexIdxsToTracker(tracker, desiredCamoDiffusesForLocalPlayerPed);
                        desiredCamoDiffusesForLocalPlayerPed.Clear();
                    }
                    else if (desiredCamoDiffusesFallback != null && desiredCamoDiffusesFallback.TryGetValue(ped.MemoryAddress, out var desiredCamoDiffuses))
                    {
                        // Edge case
                        SetDesiredCamoDiffuseTexIdxsToTracker(tracker, desiredCamoDiffuses);
                    }

                    tracker.Update();
                }             
                else
                {
                    if (ShouldCleanCurrentWeaponBloodForBeingWater(ped, currentPlayerPed, configs))
                    {
                        var weaponProp = ped.GetCurrentWeaponProp();
                        weaponProp?.SetCamoDiffuseTexId(0);
                        continue;
                    }
                    else if (ped == currentPlayerPed && desiredCamoDiffusesForLocalPlayerPed.Count > 0)
                    {
                        RegisterTrackingInfo(ped, configs, desiredCamoDiffusesForLocalPlayerPed.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex));
                        desiredCamoDiffusesForLocalPlayerPed.Clear();
                    }
                    else if (desiredCamoDiffusesFallback != null && desiredCamoDiffusesFallback.TryGetValue(ped.MemoryAddress, out var desiredCamoDiffuses))
                    {
                        // Edge case
                        RegisterTrackingInfo(ped, configs, desiredCamoDiffuses.ToDictionary(x => x.weaponHash, x => x.camoTexIdIndex));
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
                            RegisterTrackingInfo(ped, configs, null);
                        }
                    }
                }
            }

            if (desiredCamoDiffusesForLocalPlayerPed.Count > 0)
            {
                desiredCamoDiffusesForLocalPlayerPed.Clear();
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

        static bool IsEntitySubmergedLevelGreater(Entity entity, float threshold) => entity.SubmersionLevel > threshold;
        static void SetDesiredCamoDiffuseTexIdxsToTracker(WeaponCamoDiffuseTexTracker tracker, List<WeaponCamoDiffuseTexTracker.WeaponHashAndCamoTexIdIndexTuple> desiredCamoTexIdxs)
        {
            foreach (var desiredCamoDiffuse in desiredCamoTexIdxs)
            {
                tracker.RequestCamoDiffuseTexIdForWeaponHash(desiredCamoDiffuse.weaponHash, desiredCamoDiffuse.camoTexIdIndex);
            }
        }

        static bool ShouldCleanCurrentWeaponBloodForBeingWater(Ped ped, Ped playerPed, Configs configs)
        {
            switch (configs.TargetPedsForWaterCleaningForPedSubmersion)
            {
                case Configs.TargetPeds.PlayerOnly:
                    if (ped != playerPed)
                    {
                        break;
                    }
                    goto case Configs.TargetPeds.AllPeds;

                case Configs.TargetPeds.AllPeds:
                    if (ped.SubmersionLevel >= configs.PedSubmersionLevelThreshold || ped.IsSwimming)
                    {
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static void ClearCurrentWeaponPropBlood(Ped ped)
            => ped.GetCurrentWeaponProp()?.SetCamoDiffuseTexId(0);

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

        static void RegisterTrackingInfo(Ped ped, Configs configs, Dictionary<WeaponHash, byte>? desiredCamoTexIdxs = null)
        {
            var trackingInfo = WeaponCamoDiffuseTexTracker.Create(ped, desiredCamoTexIdxs);
            if (trackingInfo == null) { return; }
            trackingInfo.OnWatchedWeaponRemovedFromInventory += OnWatchedWeaponsRemovedFromInventory;
            trackingInfo.OnNoWatchedWeaponsLeft += RegisterPedToRemoveFromTracking;
            trackingInfo.OnPedRemoved += RegisterPedToRemoveFromTracking;

            switch (configs.TargetPedsForWaterCleaningForPedSubmersion)
            {
                case Configs.TargetPeds.PlayerOnly:
                    if (ped != GetLocalPlayerPed())
                    {
                        break;
                    }
                    goto case Configs.TargetPeds.AllPeds;

                case Configs.TargetPeds.AllPeds:
                    var pedSubmersionLevelThreshold = configs.PedSubmersionLevelThreshold;
                    trackingInfo.PredicateToCleanAllWeapons = (ped => (ped.SubmersionLevel >= pedSubmersionLevelThreshold) || ped.IsSwimming);
                    break;
            }

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

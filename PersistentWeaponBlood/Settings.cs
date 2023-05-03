#if SHVDN
using GTA;
#endif

#if RPH
using Rage;
#endif

using System;

namespace PersistentWeaponBlood
{
    internal sealed class Configs
    {
        public enum TargetPeds
        {
            None,
            PlayerOnly,
            AllPeds,
        }

        internal readonly TargetPeds TargetPedsForWaterCleaningForPedSubmersion;
        internal readonly float PedSubmersionLevelThreshold;
        internal readonly bool RegisterCheatCodes;

        internal Configs(string iniFilePath)
        {
#if SHVDN
            var internalSettingReader = ScriptSettings.Load(iniFilePath);

            var targetPedsForWaterCleaningForPedSubmersionString = internalSettingReader.GetValue("WaterCleaningForPedSubmersion", "TargetPeds", "AllPeds");
            TargetPedsForWaterCleaningForPedSubmersion = ParseTargetPedsEnumString(targetPedsForWaterCleaningForPedSubmersionString);

            PedSubmersionLevelThreshold = internalSettingReader.GetValue("WaterCleaningForPedSubmersion", "SubmersionLevelThreshold", 0.875f);

            RegisterCheatCodes = internalSettingReader.GetValue("ClearWeaponBloodCommands", "RegisterCheatCodes", true);
#endif

#if RPH
            var internalSettingReader = new InitializationFile(iniFilePath);

            var targetPedsForWaterCleaningForPedSubmersionString = internalSettingReader.Read("WaterCleaningForPedSubmersion", "TargetPeds", "AllPeds");
            TargetPedsForWaterCleaningForPedSubmersion = ParseTargetPedsEnumString(targetPedsForWaterCleaningForPedSubmersionString);

            PedSubmersionLevelThreshold = internalSettingReader.Read("WaterCleaningForPedSubmersion", "SubmersionLevelThreshold", 0.875f);

            RegisterCheatCodes = internalSettingReader.Read("ClearWeaponBloodCommands", "RegisterCheatCodes", true);
#endif
        }

        private static TargetPeds ParseTargetPedsEnumString(string str)
        {
            if (str.Equals("AllPeds", StringComparison.OrdinalIgnoreCase))
            {
                return TargetPeds.AllPeds;
            }
            else if (str.Equals("PlayerOnly", StringComparison.OrdinalIgnoreCase))
            {
                return TargetPeds.PlayerOnly;
            }
            else if (str.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return TargetPeds.None;
            }

            return TargetPeds.AllPeds;
        }
    }
}
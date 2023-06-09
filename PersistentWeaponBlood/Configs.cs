#if SHVDN
using GTA;
#endif

#if RPH
using Rage;
#endif

using System;
using System.Globalization;

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
            // Current culture can vary by the user setting in SHVDN scripts when they got loaded
            // RPH changes the culture of the default app domain to "en-US" and sets add domains for plugins to it, so you should test the current culture without having RPH loaded
            // No need to do a hack like changing the current culture or getting values as string first and parsing string values into float ones with InvariantCulture later in RPH version
            // since InitializationFile of RPH always uses CultureInfo.InvariantCulture as the format provider when it reads some values
            // For writing, InitializationFile of RPH uses WritePrivateProfileStringA so CultureInfo.CurrentCulture is irrelevant, wtf?
            var internalSettingReader = ScriptSettings.Load(iniFilePath);

            var targetPedsForWaterCleaningForPedSubmersionString = internalSettingReader.GetValue("WaterCleaningForPedSubmersion", "TargetPeds", "AllPeds");
            TargetPedsForWaterCleaningForPedSubmersion = ParseTargetPedsEnumString(targetPedsForWaterCleaningForPedSubmersionString);

            PedSubmersionLevelThreshold = 0.875f;
            string tempPedSubmersionLevelThresholdValStr = internalSettingReader.GetValue("WaterCleaningForPedSubmersion", "SubmersionLevelThreshold", string.Empty);
            if (float.TryParse(tempPedSubmersionLevelThresholdValStr, NumberStyles.Number, CultureInfo.InvariantCulture, out float parsedPedSubmersionLevelThreshold))
            {
                PedSubmersionLevelThreshold = parsedPedSubmersionLevelThreshold;
            }

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
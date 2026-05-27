using Colossal.Localization;
using CustomChirps.Utils;
using HarmonyLib;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(LocalizationDictionary), nameof(LocalizationDictionary.TryGetValue))]
    internal static class RuntimeChirpLocalizationPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(string entryID, ref string value, ref bool __result)
        {
            if (!string.IsNullOrEmpty(entryID) &&
                entryID.StartsWith("customchirps:", System.StringComparison.Ordinal) &&
                RuntimeChirpLocalization.TryGetValue(entryID, out var runtimeValue))
            {
                value = runtimeValue;
                __result = true;
                return false;
            }

            return true;
        }
    }
}

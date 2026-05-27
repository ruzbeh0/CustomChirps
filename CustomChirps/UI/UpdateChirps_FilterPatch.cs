using Colossal.UI.Binding;
using Game.UI.InGame;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(ChirperUISystem), "UpdateChirps")]
    internal static class UpdateChirps_FilterPatch
    {
        private const string CustomChirpPrefix = "customchirps";

        private static readonly FieldInfo s_ChirpQueryField =
            AccessTools.Field(typeof(ChirperUISystem), "m_ChirpQuery");

        private static readonly MethodInfo s_GetSortedChirpsMethod =
            AccessTools.Method(typeof(ChirperUISystem), "GetSortedChirps");

        static bool Prefix(ChirperUISystem __instance, IJsonWriter binder)
        {
            if (Mod.m_Setting == null || !Mod.m_Setting.hide_vanilla_chirps_in_chirper_panel)
                return true;

            if (s_ChirpQueryField == null || s_GetSortedChirpsMethod == null)
                return true;

            var chirpQuery = (EntityQuery)s_ChirpQueryField.GetValue(__instance);
            var sortedChirps = (NativeArray<Entity>)s_GetSortedChirpsMethod.Invoke(
                __instance,
                new object[] { chirpQuery }
            );

            try
            {
                var customChirps = new List<Entity>(sortedChirps.Length);
                for (int i = 0; i < sortedChirps.Length; i++)
                {
                    var chirp = sortedChirps[i];
                    if (IsCustomChirp(__instance, chirp))
                        customChirps.Add(chirp);
                }

                JsonWriterExtensions.ArrayBegin(binder, customChirps.Count);
                for (int i = 0; i < customChirps.Count; i++)
                    __instance.BindChirp(binder, customChirps[i], false);
                binder.ArrayEnd();
            }
            finally
            {
                if (sortedChirps.IsCreated)
                    sortedChirps.Dispose();
            }

            return false;
        }

        private static bool IsCustomChirp(ChirperUISystem chirperUISystem, Entity chirp)
        {
            var key = chirperUISystem.GetMessageID(chirp);
            return !string.IsNullOrEmpty(key) &&
                   key.StartsWith(CustomChirpPrefix, StringComparison.Ordinal);
        }
    }
}

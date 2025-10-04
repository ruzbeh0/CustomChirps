using Colossal.UI.Binding;
using CustomChirps.Systems;
using Game.UI;
using Game.UI.InGame;
using Game.UI.Localization;
using HarmonyLib;
using System;
using Unity.Entities;

namespace CustomChirps.Patches
{
    // Method signature (from decompile):
    // private void BindChirpLink(IJsonWriter binder, Entity entity, NameSystem.Name name)
    [HarmonyPatch(typeof(ChirperUISystem), "BindChirpLink",
        new Type[] { typeof(IJsonWriter), typeof(Entity), typeof(NameSystem.Name) })]
    public static class ChirperNamePatch
    {
        // We only change the 'name' *value* the UI will write; we don't touch the binder.
        static void Prefix(Entity entity, ref NameSystem.Name name)
        {
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null) return;

                var api = world.GetExistingSystemManaged<CustomChirpApiSystem>();
                if (api == null) return;

                // If we have an override for this sender entity, replace the name
                if (api.TryGetSenderNameOverride(entity, out var overrideText) &&
                    !string.IsNullOrEmpty(overrideText))
                {
                    name = NameSystem.Name.CustomName(overrideText);
                    CustomChirpApiSystem.LogInfo($"[NamePatch] Sender display name → '{overrideText}'");
                }
            }
            catch (Exception ex)
            {
                CustomChirpApiSystem.LogInfo($"[NamePatch] Failed to override name: {ex}");
            }
        }
    }
}

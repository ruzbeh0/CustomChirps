// UI/ChirperNamePatch.cs
using Colossal.UI.Binding;
using Game.UI;
using Game.UI.InGame;
using Game.UI.Localization;
using HarmonyLib;
using System;
using Unity.Entities;

namespace CustomChirps.UI
{
    // We must patch the 3-parameter overload:
    // public void BindChirpLink(IJsonWriter binder, Entity entity, NameSystem.Name name)
    [HarmonyPatch(typeof(ChirperUISystem), "BindChirpLink",
        new Type[] { typeof(IJsonWriter), typeof(Entity), typeof(NameSystem.Name) })]
    public static class ChirperNamePatch
    {
        // Prefix runs inside BindChirpSender, so our ThreadStatic is in scope.
        // IMPORTANT: use 'ref NameSystem.Name name' so we can replace it.
        public static void Prefix(ref NameSystem.Name name)
        {
            var overrideText = ChirperSenderPatch.ScopedNameOverride;
            if (!string.IsNullOrEmpty(overrideText))
            {
                name = NameSystem.Name.CustomName(overrideText);
            }
        }
    }
}

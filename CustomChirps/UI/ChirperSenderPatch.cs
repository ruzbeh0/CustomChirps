// UI/ChirperSenderPatch.cs
using Colossal.UI.Binding;
using CustomChirps.Components;
using CustomChirps.Systems;
using Game.UI;
using Game.UI.InGame;
using Game.UI.Localization;
using HarmonyLib;
using Unity.Entities;

namespace CustomChirps.UI
{
    // BindChirpSender is private; use string literal in the Harmony attribute.
    [HarmonyPatch(typeof(ChirperUISystem), "BindChirpSender")]
    public static class ChirperSenderPatch
    {
        // Matches: private void BindChirpSender(IJsonWriter binder, Entity entity)
        public static bool Prefix(ChirperUISystem __instance, IJsonWriter binder, Entity entity)
        {
            var em = __instance.EntityManager;
            if (!em.Exists(entity)) return true;

            // Prefer attached payload (set by ChirperMessagePatch or spawner)
            if (RuntimeChirpTextBus.TryGetAttached(entity, out var payload) &&
                payload.OverrideSenderName.Length > 0 &&
                em.HasComponent<Game.Triggers.Chirp>(entity))
            {
                var chirp = em.GetComponentData<Game.Triggers.Chirp>(entity);

                // Write the sender row ourselves with the custom title, then skip vanilla.
                binder.TypeBegin("chirper.ChirpSender");
                binder.PropertyName("entity");
                binder.Write(chirp.m_Sender);

                binder.PropertyName("link");
                __instance.BindChirpLink(
                    binder,
                    chirp.m_Sender,
                    NameSystem.Name.CustomName(payload.OverrideSenderName.ToString())
                );
                binder.TypeEnd();

                return false; // we handled it
            }

            // Secondary path: sim-side component present
            if (em.HasComponent<OverrideSender>(entity) &&
                em.HasComponent<Game.Triggers.Chirp>(entity))
            {
                var ov = em.GetComponentData<OverrideSender>(entity);
                if (ov.Name.Length > 0)
                {
                    var chirp = em.GetComponentData<Game.Triggers.Chirp>(entity);

                    binder.TypeBegin("chirper.ChirpSender");
                    binder.PropertyName("entity");
                    binder.Write(chirp.m_Sender);

                    binder.PropertyName("link");
                    __instance.BindChirpLink(
                        binder,
                        chirp.m_Sender,
                        NameSystem.Name.CustomName(ov.Name.ToString())
                    );
                    binder.TypeEnd();

                    return false; // skip vanilla
                }
            }

            // No override → let vanilla build the title.
            return true;
        }
    }
}

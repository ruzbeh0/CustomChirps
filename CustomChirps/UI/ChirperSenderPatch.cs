// UI/ChirperSenderPatch.cs
using HarmonyLib;
using Unity.Entities;
using CustomChirps.Systems;

namespace CustomChirps.UI
{
    // Matches your existing signature:
    // private void BindChirpSender(IJsonWriter binder, Entity entity)
    [HarmonyPatch(typeof(Game.UI.InGame.ChirperUISystem), "BindChirpSender")]
    public static class ChirperSenderPatch
    {
        // Visible only during binding of THIS chirp; consumed by BindChirpLink patch
        [System.ThreadStatic] internal static string ScopedNameOverride;

        // BEFORE vanilla resolves account & calls BindChirpLink(...)
        public static void Prefix(Entity entity)
        {
            ScopedNameOverride = null;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || entity == Entity.Null) return;

            var em = world.EntityManager;
            if (!em.Exists(entity)) return;

            // We stash payload per instance in the spawner; grab it now
            if (RuntimeChirpTextBus.TryGetAttached(entity, out var payload))
            {
                // 1) Ensure THIS chirp instance uses the requested sender account (icon)
                if (payload.Sender != Entity.Null && em.HasComponent<Game.Triggers.Chirp>(entity))
                {
                    var c = em.GetComponentData<Game.Triggers.Chirp>(entity);
                    if (c.m_Sender != payload.Sender)
                    {
                        c.m_Sender = payload.Sender;
                        em.SetComponentData(entity, c);
                    }
                }

                // 2) Stage the custom sender label for the upcoming BindChirpLink call
                var label = payload.OverrideSenderName.ToString();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    ScopedNameOverride = label;
                    // Optional: lightweight trace to confirm it runs for linked chirps too
                    //Mod.log.Info($"[UI] Name override set for chirp {entity.Index}:{entity.Version} → \"{label}\"");
                }
            }
        }

        // AFTER sender binding for this chirp completes, clear so other chirps aren’t affected
        public static void Postfix()
        {
            ScopedNameOverride = null;
        }
    }
}

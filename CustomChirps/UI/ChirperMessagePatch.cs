// UI/ChirperMessagePatch.cs
using CustomChirps.Components;
using CustomChirps.Systems;
using HarmonyLib;
using Unity.Entities;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(Game.UI.InGame.ChirperUISystem), nameof(Game.UI.InGame.ChirperUISystem.GetMessageID))]
    public static class ChirperMessagePatch
    {
        // If we can resolve our payload (marker or attachment), return our runtime key.
        public static bool Prefix(ref string __result, Entity chirp)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || chirp == Entity.Null) return true;

            var em = world.EntityManager;
            if (!em.Exists(chirp)) return true;

            // 1) Try deterministic marker first (most reliable)
            if (RuntimeChirpTextBus.TryConsumeByMarker(em, chirp, out var payload))
            {
                // Set our custom text key on the entity
                em.AddComponentData(chirp, new ModChirpText { Key = payload.Key });

                // Force sender/icon if needed
                if (payload.Sender != Entity.Null && em.HasComponent<Game.Triggers.Chirp>(chirp))
                {
                    var c = em.GetComponentData<Game.Triggers.Chirp>(chirp);
                    if (c.m_Sender != payload.Sender)
                    {
                        c.m_Sender = payload.Sender;
                        em.SetComponentData(chirp, c);
                    }
                }

                // Ensure the final target is linked
                if (payload.Target != Entity.Null && em.HasBuffer<Game.Triggers.ChirpEntity>(chirp))
                {
                    var links = em.GetBuffer<Game.Triggers.ChirpEntity>(chirp);
                    bool alreadyLinked = false;
                    for (int i = 0; i < links.Length; i++)
                        if (links[i].m_Entity == payload.Target) { alreadyLinked = true; break; }
                    if (!alreadyLinked) links.Add(new Game.Triggers.ChirpEntity(payload.Target));
                }

                // >>> IMPORTANT: stash payload for the sender patch + stamp component
                RuntimeChirpTextBus.RememberAttached(chirp, payload);
                if (payload.OverrideSenderName.Length > 0)
                    em.AddComponentData(chirp, new OverrideSender { Name = payload.OverrideSenderName });

                // Provide our message id directly to UI
                __result = payload.Key.ToString();
                return false; // skip vanilla GetMessageID
            }

            // 2) If our ModChirpText is already present, return its key
            if (em.HasComponent<ModChirpText>(chirp))
            {
                var key = em.GetComponentData<ModChirpText>(chirp).Key.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    __result = key;
                    return false;
                }
            }

            return true; // fall back to vanilla
        }

        // If something cleared the key later, try to restore from our attachment.
        public static void Postfix(ref string __result, Entity chirp)
        {
            if (!string.IsNullOrEmpty(__result)) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || chirp == Entity.Null) return;

            if (RuntimeChirpTextBus.TryGetAttached(chirp, out var payload))
            {
                var key = payload.Key.ToString();
                if (!string.IsNullOrEmpty(key))
                    __result = key;
            }
        }
    }
}

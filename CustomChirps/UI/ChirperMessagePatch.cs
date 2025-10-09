// UI/ChirperMessagePatch.cs
using Colossal.Entities;
using CustomChirps.Components;
using CustomChirps.Systems;
using Game.Prefabs;
using HarmonyLib;
using Unity.Entities;
using PrefabRef = Game.Prefabs.PrefabRef;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(Game.UI.InGame.ChirperUISystem), nameof(Game.UI.InGame.ChirperUISystem.GetMessageID))]
    public static class ChirperMessagePatch
    {
        public static bool Prefix(ref string __result, Entity chirp)
        {
            var world = World.DefaultGameObjectInjectionWorld;

            if (world == null || chirp == Entity.Null) return true;

            var em = world.EntityManager;
            if (!em.Exists(chirp)) return true;

            // 1) If a queued payload exists for this chirp's prefab, consume and attach it now.
            if (em.HasComponent<PrefabRef>(chirp))
            {
                var prefab = em.GetComponentData<PrefabRef>(chirp).m_Prefab;
                if (prefab != Entity.Null &&
                    RuntimeChirpTextBus.TryDequeueForPrefab(prefab, out var payload))
                {
                    // Tag with our key so UI resolves text from our runtime locale
                    if (!em.HasComponent<ModChirpText>(chirp))
                        em.AddComponentData(chirp, new ModChirpText { Key = payload.Key });
                    else
                        em.SetComponentData(chirp, new ModChirpText { Key = payload.Key });

                    // Enforce sender on the instance
                    if (payload.Sender != Entity.Null && em.HasComponent<Game.Triggers.Chirp>(chirp))
                    {
                        var c = em.GetComponentData<Game.Triggers.Chirp>(chirp);
                        if (c.m_Sender != payload.Sender)
                        {
                            c.m_Sender = payload.Sender;
                            em.SetComponentData(chirp, c);
                        }
                    }

                    // Ensure TARGET exists in the ChirpEntity buffer (no m_Target on Chirp!)
                    if (payload.Target != Entity.Null)
                    {
                        DynamicBuffer<Game.Triggers.ChirpEntity> links;
                        if (!em.TryGetBuffer<Game.Triggers.ChirpEntity>(chirp, false, out links))
                            links = em.AddBuffer<Game.Triggers.ChirpEntity>(chirp);

                        bool alreadyLinked = false;
                        for (int i = 0; i < links.Length; i++)
                            if (links[i].m_Entity == payload.Target) { alreadyLinked = true; break; }

                        if (!alreadyLinked)
                            links.Add(new Game.Triggers.ChirpEntity(payload.Target));
                    }

                    // Remember full payload (for UI sender-name override)
                    RuntimeChirpTextBus.RememberAttached(chirp, payload);

                    __result = payload.Key.ToString();
                    return false; // skip vanilla
                }
            }

            // 2) If we've already tagged this chirp earlier, just return the key we set.
            if (em.HasComponent<ModChirpText>(chirp))
            {
                var key = em.GetComponentData<ModChirpText>(chirp).Key.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    __result = key;
                    return false; // skip vanilla
                }
            }

            // 3) Fall through; our Postfix will still try to enforce sender/target.
            return true;
        }

        public static void Postfix(ref string __result, Entity chirp)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || chirp == Entity.Null) return;

            var em = world.EntityManager;
            if (!em.Exists(chirp)) return;

            if (RuntimeChirpTextBus.TryGetAttached(chirp, out var payload))
            {
                // Enforce sender
                if (payload.Sender != Entity.Null && em.HasComponent<Game.Triggers.Chirp>(chirp))
                {
                    var c = em.GetComponentData<Game.Triggers.Chirp>(chirp);
                    if (c.m_Sender != payload.Sender)
                    {
                        c.m_Sender = payload.Sender;
                        em.SetComponentData(chirp, c);
                    }
                }

                // Ensure TARGET in ChirpEntity buffer
                if (payload.Target != Entity.Null)
                {
                    DynamicBuffer<Game.Triggers.ChirpEntity> links;
                    if (!em.TryGetBuffer<Game.Triggers.ChirpEntity>(chirp, false, out links))
                        links = em.AddBuffer<Game.Triggers.ChirpEntity>(chirp);

                    bool alreadyLinked = false;
                    for (int i = 0; i < links.Length; i++)
                        if (links[i].m_Entity == payload.Target) { alreadyLinked = true; break; }

                    if (!alreadyLinked)
                        links.Add(new Game.Triggers.ChirpEntity(payload.Target));
                }

                var key = payload.Key.ToString();
                if (!string.IsNullOrEmpty(key))
                    __result = key;
            }
        }
    }
}

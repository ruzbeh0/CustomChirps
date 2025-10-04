// UI/ChirperMessagePatch.cs
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

            // 1) If already attached, return the key
            if (em.HasComponent<ModChirpText>(chirp))
            {
                var key = em.GetComponentData<ModChirpText>(chirp).Key.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    __result = key;
                    return false;
                }
            }

            // 2) If spawner didn’t run yet, try to attach right here (self-heal)
            //    - lookup prefab of this chirp
            if (em.HasComponent<PrefabRef>(chirp))
            {
                var prefab = em.GetComponentData<PrefabRef>(chirp).m_Prefab;
                if (RuntimeChirpTextBus.TryConsumePending(prefab, out var keyFS))
                {
                    // Attach marker now so subsequent calls also see it
                    em.AddComponentData(chirp, new ModChirpText { Key = keyFS });
                    RuntimeChirpTextBus.RememberAttached(chirp, keyFS);

                    var key = keyFS.ToString();
                    if (!string.IsNullOrEmpty(key))
                    {
                        __result = key;
                        return false;
                    }
                }
            }

            // 3) As a last resort, see if the bus already cached a key for this exact entity
            if (RuntimeChirpTextBus.TryGetAttached(chirp, out var cachedFS))
            {
                var key = cachedFS.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    __result = key;
                    return false;
                }
            }

            // No custom text → vanilla
            return true;
        }
    }
}

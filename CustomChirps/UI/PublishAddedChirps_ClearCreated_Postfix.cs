using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Game.UI.InGame;
using Game.Common;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(ChirperUISystem), "PublishAddedChirps")]
    internal static class PublishAddedChirps_ClearTags_Postfix
    {
        static void Postfix(ChirperUISystem __instance)
        {
            var em = __instance.EntityManager;

            // 1) Clear on the "created" set (what you already had)
            var fCreated = AccessTools.Field(typeof(ChirperUISystem), "m_CreatedChirpQuery");
            var createdQuery = (EntityQuery)fCreated.GetValue(__instance);
            if (!createdQuery.IsEmptyIgnoreFilter)
            {
                using var created = createdQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in created)
                {
                    if (em.HasComponent<Created>(e)) em.RemoveComponent<Created>(e);
                    if (em.HasComponent<Updated>(e)) em.RemoveComponent<Updated>(e);
                }
            }

            // 2) ALSO clear on the "all chirps" set the panel binds from
            var fAll = AccessTools.Field(typeof(ChirperUISystem), "m_ChirpQuery");
            var allQuery = (EntityQuery)fAll.GetValue(__instance);
            if (!allQuery.IsEmptyIgnoreFilter)
            {
                using var all = allQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in all)
                {
                    // Don’t touch Deleted (those won’t show anyway)
                    if (em.HasComponent<Created>(e)) em.RemoveComponent<Created>(e);
                    if (em.HasComponent<Updated>(e)) em.RemoveComponent<Updated>(e);
                }
            }
        }
    }
}

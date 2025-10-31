using CustomChirps.Utils;
using Game.Common;
using Game.UI.InGame;
using HarmonyLib;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.InputSystem;

// aliases
using TriggerChirp = Game.Triggers.Chirp;

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(ChirperUISystem), "PublishAddedChirps")]
    internal static class PublishAddedChirps_FilterPatch
    {
        static void Prefix(ChirperUISystem __instance)
        {
            if (!Mod.m_Setting.disable_vanilla_chirps)
            {
                return;
            }
            // 1) get the exact query the UI uses for "new chirps"
            var fQuery = AccessTools.Field(typeof(ChirperUISystem), "m_CreatedChirpQuery");
            var createdQuery = (EntityQuery)fQuery.GetValue(__instance);

            if (createdQuery.IsEmptyIgnoreFilter)
                return;

            var em = __instance.EntityManager;
            var list = createdQuery.ToEntityArray(Allocator.Temp); // snapshot so we can mutate

            try
            {
                foreach (var e in list)
                {
                    // 2) build the same message id the UI will use
                    var key = __instance.GetMessageID(e);
                    if (key.StartsWith("customchirps"))
                        continue;

                    // 3) remove from "new" set right now so vanilla code won't see it
                    if (!em.HasComponent<Deleted>(e))
                        em.AddComponent<Deleted>(e);
                    if (em.HasComponent<Created>(e))
                        em.RemoveComponent<Created>(e);
                    if (em.HasComponent<Updated>(e))
                        em.RemoveComponent<Updated>(e);

                    Mod.log.Info($"[Chirp/Filtered@Publish] entity={e} key={key}");
                }
            }
            finally
            {
                list.Dispose();
            }
        }
    }
}

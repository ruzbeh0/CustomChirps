// UI/ChirperSenderPatch.cs
using HarmonyLib;
using Unity.Entities;
using CustomChirps.Systems;
using Colossal.UI.Binding; // IJsonWriter

namespace CustomChirps.UI
{
    [HarmonyPatch(typeof(Game.UI.InGame.ChirperUISystem), "BindChirpSender")]
    public static class ChirperSenderPatch
    {
        // Make sure the instance carries our desired sender before the UI binds
        public static void Prefix(Entity entity)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || entity == Entity.Null) return;

            var em = world.EntityManager;
            if (!em.Exists(entity)) return;

            if (RuntimeChirpTextBus.TryGetAttached(entity, out var payload) &&
                payload.Sender != Entity.Null &&
                em.HasComponent<Game.Triggers.Chirp>(entity))
            {
                var c = em.GetComponentData<Game.Triggers.Chirp>(entity);
                if (c.m_Sender != payload.Sender)
                {
                    c.m_Sender = payload.Sender;
                    em.SetComponentData(entity, c);
                }
            }
        }

    }
}

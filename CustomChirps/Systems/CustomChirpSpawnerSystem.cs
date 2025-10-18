// Systems/CustomChirpSpawnerSystem.cs
using CustomChirps.Components;
using CustomChirps.Systems;
using Game;
using Game.Prefabs;
using Game.Triggers;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    public partial class CustomChirpSpawnerSystem : GameSystemBase
    {
        private EntityQuery _newChirps;
        private Unity.Mathematics.Random _rng;

        protected override void OnCreate()
        {
            base.OnCreate();
            _newChirps = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Triggers.Chirp, PrefabRef>()
                .WithNone<ModChirpText>()
                .Build(EntityManager);

            uint seed = (uint)System.Environment.TickCount;
            if (seed == 0) seed = 1;
            _rng = new Unity.Mathematics.Random(seed);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var allowPct = 100; // keep vanilla when no payload matched

            using var entities = _newChirps.ToEntityArray(Allocator.Temp);
            for (int idx = 0; idx < entities.Length; idx++)
            {
                var e = entities[idx];
                if (!em.Exists(e)) continue;

                var current = em.GetComponentData<Game.Triggers.Chirp>(e);
                var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;

                // Try deterministic marker first
                ChirpPayload payload;
                bool matched = RuntimeChirpTextBus.TryConsumeByMarker(em, e, out payload);

                // If not, fall back to old matching (target → variant → prefab)
                if (!matched)
                {
                    Entity actualSender = current.m_Sender;
                    Entity actualTarget = Entity.Null;

                    if (em.HasBuffer<ChirpEntity>(e))
                    {
                        var links = em.GetBuffer<ChirpEntity>(e, true);
                        for (int k = 0; k < links.Length; k++)
                        {
                            var cand = links[k].m_Entity;
                            if (cand != Entity.Null) { actualTarget = cand; break; }
                        }
                    }

                    matched =
                        RuntimeChirpTextBus.TryDequeueForTarget(actualTarget, out payload) ||
                        RuntimeChirpTextBus.TryDequeueBestMatchConsideringVariants(em, prefab, actualSender, actualTarget, out payload) ||
                        RuntimeChirpTextBus.TryDequeueForPrefab(prefab, out payload);
                }

                if (!matched)
                {
                    if (allowPct <= 0) { ecb.DestroyEntity(e); continue; }
                    if (allowPct < 100)
                    {
                        int roll = _rng.NextInt(100);
                        if (roll >= allowPct) { ecb.DestroyEntity(e); continue; }
                    }
                    continue; // keep vanilla as-is
                }

                // Attach our text key
                ecb.AddComponent(e, new ModChirpText { Key = payload.Key });

                // Force sender (icon) if requested
                if (payload.Sender != Entity.Null && current.m_Sender != payload.Sender)
                {
                    current.m_Sender = payload.Sender;
                    ecb.SetComponent(e, current);
                }

                // Stamp override label if present
                if (payload.OverrideSenderName.Length > 0)
                {
                    ecb.AddComponent(e, new OverrideSender { Name = payload.OverrideSenderName });
                }

                RuntimeChirpTextBus.RememberAttached(e, payload);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}

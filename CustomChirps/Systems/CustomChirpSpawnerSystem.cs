// Systems/CustomChirpSpawnerSystem.cs
using CustomChirps.Components;   // ModChirpText
using CustomChirps.Systems;      // RuntimeChirpTextBus
using Game.Prefabs;              // PrefabRef
using Game.Triggers;             // Chirp, ChirpEntity, CreateChirpSystem
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    // Ensure we see instances after vanilla has created them (buffer links populated)
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreateChirpSystem))]
    public partial class CustomChirpSpawnerSystem : SystemBase
    {
        private EntityQuery _newChirps;

        protected override void OnCreate()
        {
            base.OnCreate();
            _newChirps = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Triggers.Chirp, PrefabRef>()
                .WithNone<ModChirpText>()   // untouched new instances
                .Build(EntityManager);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            using var entities = _newChirps.ToEntityArray(Allocator.Temp);
            using var prefabRefs = _newChirps.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            using var chirps = _newChirps.ToComponentDataArray<Game.Triggers.Chirp>(Allocator.Temp);
            if (entities.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var prefab = prefabRefs[i].m_Prefab;
                if (prefab == Entity.Null) continue;

                var current = chirps[i];
                var actualSender = current.m_Sender;

                // Discover the building target actually attached by vanilla creation
                Entity actualBuilding = Entity.Null;
                if (em.HasBuffer<Game.Triggers.ChirpEntity>(e))
                {
                    var links = em.GetBuffer<Game.Triggers.ChirpEntity>(e, true);
                    for (int k = 0; k < links.Length; k++)
                    {
                        var cand = links[k].m_Entity;
                        if (cand != Entity.Null && em.HasComponent<Game.Buildings.Building>(cand))
                        {
                            actualBuilding = cand;
                            break;
                        }
                    }
                }

                // 1) Prefer a target-keyed pending payload (guarantees the chirp with the link gets your payload)
                ChirpPayload payload;
                if (!RuntimeChirpTextBus.TryDequeueForTarget(actualBuilding, out payload))
                {
                    // 2) Fallback: match by prefab+sender+target (variant-aware if you already added that helper)
                    if (!RuntimeChirpTextBus.TryDequeueBestMatchConsideringVariants(em, prefab, actualSender, actualBuilding, out payload))
                    {
                        // 3) Last resort: prefab-only
                        if (!RuntimeChirpTextBus.TryDequeueForPrefab(prefab, out payload))
                            continue;
                    }
                }

                // Attach runtime text
                // inside CustomChirpSpawnerSystem.OnUpdate(), after you've matched the payload and before RememberAttached(...)
                ecb.AddComponent(e, new CustomChirps.Components.ModChirpText { Key = payload.Key });

                // If you still want to control the icon, keep setting m_Sender here (optional):
                if (payload.Sender != Entity.Null && current.m_Sender != payload.Sender)
                {
                    current.m_Sender = payload.Sender;
                    ecb.SetComponent(e, current);
                }

                // <-- NEW: force a custom label regardless of which account prefab was resolved
                if (payload.OverrideSenderName.Length > 0)
                {
                    ecb.AddComponent(e, new CustomChirps.Components.OverrideSender
                    {
                        Name = payload.OverrideSenderName
                    });
                }

                RuntimeChirpTextBus.RememberAttached(e, payload);


            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}

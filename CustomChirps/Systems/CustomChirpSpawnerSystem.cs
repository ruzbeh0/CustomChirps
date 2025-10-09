// Systems/CustomChirpSpawnerSystem.cs
using CustomChirps.Components;
using CustomChirps.Systems;
using Game.Prefabs;
using Game.Triggers;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;

namespace CustomChirps.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreateChirpSystem))]
    public partial class CustomChirpSpawnerSystem : SystemBase
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

            // seed RNG (must be non-zero)
            uint seed = (uint)System.Environment.TickCount;
            if (seed == 0) seed = 1;
            _rng = new Unity.Mathematics.Random(seed);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            using var entities = _newChirps.ToEntityArray(Allocator.Temp);
            using var prefabRefs = _newChirps.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            using var chirps = _newChirps.ToComponentDataArray<Game.Triggers.Chirp>(Allocator.Temp);
            if (entities.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            int allowPct = 100; // 0..100 from settings UI
            allowPct = math.clamp(allowPct, 0, 100);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var prefab = prefabRefs[i].m_Prefab;
                if (prefab == Entity.Null) continue;

                var current = chirps[i];
                var actualSender = current.m_Sender;

                // Discover ANY target from the ChirpEntity buffer (no longer Building-only)
                Entity actualTarget = Entity.Null;
                if (em.HasBuffer<ChirpEntity>(e))
                {
                    var links = em.GetBuffer<ChirpEntity>(e, true);
                    for (int k = 0; k < links.Length; k++)
                    {
                        var cand = links[k].m_Entity;
                        if (cand != Entity.Null)
                        {
                            actualTarget = cand;
                            break;
                        }
                    }
                }

                // Try to match our payload (target-first, then variant-aware, then prefab-only)
                ChirpPayload payload;
                bool matched =
                    RuntimeChirpTextBus.TryDequeueForTarget(actualTarget, out payload) ||
                    RuntimeChirpTextBus.TryDequeueBestMatchConsideringVariants(em, prefab, actualSender, actualTarget, out payload) ||
                    RuntimeChirpTextBus.TryDequeueForPrefab(prefab, out payload);

                if (!matched)
                {
                    // ---- VANILLA FILTER HERE ----
                    if (allowPct <= 0)
                    {
                        ecb.DestroyEntity(e);
                        continue;
                    }
                    if (allowPct < 100)
                    {
                        // draw 0..99; keep if < allowPct
                        int roll = _rng.NextInt(100);
                        if (roll >= allowPct)
                        {
                            ecb.DestroyEntity(e);
                            continue;
                        }
                    }

                    // keep vanilla as-is
                    continue;
                }

                // Attach our text key
                ecb.AddComponent(e, new ModChirpText { Key = payload.Key });

                // Optionally force sender (icon) to requested account
                if (payload.Sender != Entity.Null && actualSender != payload.Sender)
                {
                    current.m_Sender = payload.Sender;
                    ecb.SetComponent(e, current);
                }

                // Optionally stamp override label component if your UI patch uses it
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

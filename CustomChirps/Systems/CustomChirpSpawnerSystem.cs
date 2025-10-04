// Systems/CustomChirpSpawnerSystem.cs
using CustomChirps.Components;   // ModChirpText
using CustomChirps.Systems;      // RuntimeChirpTextBus
using Game.Prefabs;              // PrefabRef
using Game.Triggers;             // Chirp, ChirpEntity
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    /// <summary>
    /// Moves queued runtime payloads (text key, sender, target) from RuntimeChirpTextBus
    /// onto newly spawned chirp *instances*.
    ///
    /// Key bits:
    ///   • Adds ModChirpText { Key = runtimeKey } so the UI resolves text from your runtime locale
    ///   • Sets Chirp.m_Sender to the desired account entity
    ///   • Ensures the *target* is stored in the ChirpEntity buffer (this is what makes the link clickable)
    ///
    /// Uses plain for-loops (no Entities.ForEach/Burst) to match your earlier request.
    /// </summary>
    public partial class CustomChirpSpawnerSystem : SystemBase
    {
        private EntityQuery _newChirpsQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            // New chirps (instances) have Chirp + PrefabRef; we only process those
            // that don't yet have our ModChirpText tag.
            _newChirpsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Prefabs.Chirp, PrefabRef>()
                .WithNone<ModChirpText>()
                .Build(EntityManager);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            using var entities = _newChirpsQuery.ToEntityArray(Allocator.Temp);
            using var prefabRefs = _newChirpsQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            if (entities.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var prefab = prefabRefs[i].m_Prefab;
                if (prefab == Entity.Null)
                    continue;

                // 1) Pull the next payload queued for THIS prefab.
                if (!RuntimeChirpTextBus.TryDequeueForPrefab(prefab, out var payload))
                    continue;

                // 2) Add the runtime text key to the instance so UI uses your text.
                ecb.AddComponent(e, new ModChirpText { Key = payload.Key });

                // 3) Set the sender (account entity) on the instance.
                if (payload.Sender != Entity.Null && em.HasComponent<Game.Prefabs.Chirp>(e))
                {
                    var c = em.GetComponentData<Game.Triggers.Chirp>(e);
                    if (c.m_Sender != payload.Sender)
                    {
                        c.m_Sender = payload.Sender;
                        ecb.SetComponent(e, c);
                    }
                }

                // 4) Ensure the clickable TARGET is present in the ChirpEntity buffer.
                //    (Chirp has NO 'm_Target' field — targets are stored as buffer entries.)
                if (payload.Target != Entity.Null)
                {
                    bool alreadyLinked = false;

                    // Check any existing links *now* (before playback), to avoid duplicates.
                    if (em.HasBuffer<ChirpEntity>(e))
                    {
                        var current = em.GetBuffer<ChirpEntity>(e, true); // readonly view is fine for checking
                        for (int k = 0; k < current.Length; k++)
                        {
                            if (current[k].m_Entity == payload.Target)
                            {
                                alreadyLinked = true;
                                break;
                            }
                        }
                    }

                    if (!alreadyLinked)
                    {
                        // Create/overwrite a buffer to append our target link.
                        // Using SetBuffer<T> is safe here; it creates the buffer if missing and returns a writer.
                        var buf = ecb.SetBuffer<ChirpEntity>(e);
                        buf.Add(new ChirpEntity(payload.Target));
                    }
                }

                // 5) Remember the full payload per *instance* so UI patches (sender-name override, etc.)
                //    can read it deterministically later.
                RuntimeChirpTextBus.RememberAttached(e, payload);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}

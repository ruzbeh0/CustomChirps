// Systems/CustomChirpSpawnerSystem.cs
using CustomChirps.Components;
using CustomChirps.Systems;            // ModChirpText, RuntimeChirpTextBus
using Game.Prefabs;                    // PrefabRef
using Game.Triggers;                   // Chirp
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    /// <summary>
    /// Moves the pending runtime key (from RuntimeChirpTextBus) onto the
    /// actual spawned chirp *instance* entity (as ModChirpText).
    /// Uses plain for-loops (no Entities.ForEach).
    /// </summary>
    public partial class CustomChirpSpawnerSystem : SystemBase
    {
        private EntityQuery _newChirpsQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Query: chirp instances that have PrefabRef and don't yet have our marker.
            _newChirpsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Prefabs.Chirp, PrefabRef>()
                .WithNone<ModChirpText>()
                .Build(EntityManager);
        }

        protected override void OnUpdate()
        {
            // Nothing pending? Nothing to do.
            if (RuntimeChirpTextBus.PendingByPrefab.Count == 0)
                return;

            var em = EntityManager;

            // Get matching entities & their PrefabRef in parallel arrays
            using var entities = _newChirpsQuery.ToEntityArray(Allocator.Temp);
            using var prefabRefs = _newChirpsQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp);

            if (entities.Length == 0)
                return;

            // We’ll batch mutations via ECB then playback
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Plain for-loop iteration
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var prefab = prefabRefs[i].m_Prefab;
                if (prefab == Entity.Null)
                    continue;

                // Consume the next pending key for this prefab (one key -> one chirp)
                if (RuntimeChirpTextBus.TryConsumePending(prefab, out var keyFS))
                {
                    ecb.AddComponent(e, new ModChirpText { Key = keyFS });
                    RuntimeChirpTextBus.RememberAttached(e, keyFS);
                }
            }

            // Apply all adds now
            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}

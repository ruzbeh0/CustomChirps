// Systems/RuntimeChirpTextBus.cs
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    public struct ChirpPayload
    {
        public FixedString512Bytes Key;          // runtime text key
        public Entity Sender;                    // desired sender entity (account)
        public Entity Target;                    // optional target (building/entity)
        public FixedString128Bytes OverrideSenderName; // "Realistic Trips Mod" (optional)
    }

    public static class RuntimeChirpTextBus
    {
        // Prefab -> queue of payloads (so multiple posts from the same prefab won't collide)
        private static readonly ConcurrentDictionary<Entity, ConcurrentQueue<ChirpPayload>> _byPrefab =
            new();

        // Instance -> payload attached (for UI self-heal and name override)
        private static readonly ConcurrentDictionary<Entity, ChirpPayload> _attachedByInstance =
            new();

        public static void EnqueuePending(Entity prefab, in ChirpPayload payload)
        {
            if (prefab == Entity.Null) return;
            var q = _byPrefab.GetOrAdd(prefab, _ => new ConcurrentQueue<ChirpPayload>());
            q.Enqueue(payload);
        }

        public static bool TryDequeueForPrefab(Entity prefab, out ChirpPayload payload)
        {
            payload = default;
            if (prefab == Entity.Null) return false;
            if (_byPrefab.TryGetValue(prefab, out var q))
                return q.TryDequeue(out payload);
            return false;
        }

        public static void RememberAttached(Entity instance, in ChirpPayload payload)
        {
            if (instance == Entity.Null) return;
            _attachedByInstance[instance] = payload;
        }

        public static bool TryGetAttached(Entity instance, out ChirpPayload payload)
            => _attachedByInstance.TryGetValue(instance, out payload);
    }
}

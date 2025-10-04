// Systems/RuntimeChirpTextBus.cs
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    /// <summary>
    /// Shared store so API, spawner, and UI patch can exchange keys.
    /// - PendingByPrefab: next key for a chirp that will spawn from that prefab.
    /// - AttachedByChirp: key already associated with a spawned chirp entity.
    /// </summary>
    public static class RuntimeChirpTextBus
    {
        // prefab -> next runtime key
        public static readonly ConcurrentDictionary<Entity, FixedString512Bytes> PendingByPrefab
            = new ConcurrentDictionary<Entity, FixedString512Bytes>();

        // instance chirp entity -> key (optional helper cache)
        public static readonly ConcurrentDictionary<Entity, FixedString512Bytes> AttachedByChirp
            = new ConcurrentDictionary<Entity, FixedString512Bytes>();

        public static void RegisterPending(Entity prefab, string key)
        {
            if (prefab == Entity.Null || string.IsNullOrEmpty(key)) return;
            PendingByPrefab[prefab] = (FixedString512Bytes)key;
        }

        public static bool TryConsumePending(Entity prefab, out FixedString512Bytes key)
        {
            if (prefab == Entity.Null) { key = default; return false; }
            return PendingByPrefab.TryRemove(prefab, out key);
        }

        public static void RememberAttached(Entity chirp, FixedString512Bytes key)
        {
            if (chirp == Entity.Null) return;
            AttachedByChirp[chirp] = key;
        }

        public static bool TryGetAttached(Entity chirp, out FixedString512Bytes key)
        {
            return AttachedByChirp.TryGetValue(chirp, out key);
        }
    }
}

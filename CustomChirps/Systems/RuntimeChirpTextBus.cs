// Systems/RuntimeChirpTextBus.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Components
{
    // Tiny marker component that rides on a temporary entity to bind chirp -> payload deterministically.
    public struct CustomChirpMarker : IComponentData
    {
        public ulong Token;        // unique id to look up the payload
        public Entity FinalTarget; // the real building/entity we want linked in the chirp
    }
}

namespace CustomChirps.Systems
{
    public struct ChirpPayload
    {
        public FixedString512Bytes Key;                 // custom text
        public Entity Sender;                           // desired account/icon
        public Entity Target;                           // optional real building to link
        public FixedString128Bytes OverrideSenderName;  // custom sender label for UI
    }

    internal struct PendingItem
    {
        public Entity Prefab;
        public ChirpPayload Payload;
        public ulong Ticket;
    }

    /// <summary>
    /// Thread-safe in-memory bus handing payloads from API → spawner/UI.
    /// Now also supports deterministic token-based matching via a marker entity.
    /// </summary>
    public static class RuntimeChirpTextBus
    {
        private static readonly object _lock = new();

        // Legacy pending (kept for compatibility)
        private static readonly List<PendingItem> _pending = new();
        private static ulong _nextTicket;

        private static readonly Dictionary<Entity, List<PendingItem>> _pendingByTarget = new();
        private static readonly Dictionary<Entity, ChirpPayload> _attached = new();

        // Deterministic token → payload
        private static ulong _nextToken = 1; // 0 reserved
        private static readonly Dictionary<ulong, ChirpPayload> _byToken = new();

        // -------- Token/marker helpers --------
        public static Entity CreateMarker(EntityManager em, Entity finalTarget, out ulong token)
        {
            lock (_lock) { token = _nextToken++; }

            var e = em.CreateEntity(typeof(CustomChirps.Components.CustomChirpMarker));
            em.SetComponentData(e, new CustomChirps.Components.CustomChirpMarker
            {
                Token = token,
                FinalTarget = finalTarget
            });
            return e;
        }

        public static void AddPendingByToken(ulong token, in ChirpPayload payload)
        {
            lock (_lock) { _byToken[token] = payload; }
        }

        public static bool TryConsumeByMarker(EntityManager em, Entity chirp, out ChirpPayload payload)
        {
            payload = default;

            if (!em.HasBuffer<Game.Triggers.ChirpEntity>(chirp))
                return false;

            var links = em.GetBuffer<Game.Triggers.ChirpEntity>(chirp, true);
            for (int i = 0; i < links.Length; i++)
            {
                var cand = links[i].m_Entity;
                if (cand == Entity.Null || !em.Exists(cand)) continue;

                if (em.HasComponent<CustomChirps.Components.CustomChirpMarker>(cand))
                {
                    var marker = em.GetComponentData<CustomChirps.Components.CustomChirpMarker>(cand);

                    bool found;
                    lock (_lock) { found = _byToken.Remove(marker.Token, out payload); }
                    if (!found) return false;

                    // Replace marker with real target (if any)
                    if (marker.FinalTarget != Entity.Null)
                        links[i] = new Game.Triggers.ChirpEntity(marker.FinalTarget);
                    else
                        links.RemoveAt(i);

                    em.DestroyEntity(cand);
                    return true;
                }
            }
            return false;
        }

        // -------- Legacy pending paths (unchanged) --------
        public static void EnqueuePending(Entity prefab, in ChirpPayload payload)
        {
            if (prefab == Entity.Null) return;
            lock (_lock)
            {
                var item = new PendingItem { Prefab = prefab, Payload = payload, Ticket = _nextTicket++ };
                _pending.Add(item);

                if (payload.Target != Entity.Null)
                {
                    if (!_pendingByTarget.TryGetValue(payload.Target, out var list))
                        _pendingByTarget[payload.Target] = list = new List<PendingItem>(2);
                    list.Add(item);
                }
            }
        }

        public static bool TryDequeueForTarget(Entity target, out ChirpPayload payload)
        {
            lock (_lock)
            {
                if (target != Entity.Null && _pendingByTarget.TryGetValue(target, out var list) && list.Count > 0)
                {
                    int bestIdx = 0;
                    ulong bestTicket = list[0].Ticket;
                    for (int i = 1; i < list.Count; i++)
                        if (list[i].Ticket < bestTicket) { bestIdx = i; bestTicket = list[i].Ticket; }

                    var chosen = list[bestIdx];
                    list.RemoveAt(bestIdx);
                    if (list.Count == 0) _pendingByTarget.Remove(target);

                    for (int i = 0; i < _pending.Count; i++)
                        if (_pending[i].Ticket == chosen.Ticket) { _pending.RemoveAt(i); break; }

                    payload = chosen.Payload;
                    return true;
                }
                payload = default;
                return false;
            }
        }

        public static bool TryDequeueForPrefab(Entity prefab, out ChirpPayload payload)
        {
            lock (_lock)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    if (_pending[i].Prefab == prefab)
                    {
                        payload = _pending[i].Payload;
                        _pending.RemoveAt(i);
                        return true;
                    }
                }
                payload = default;
                return false;
            }
        }

        public static bool TryDequeueBestMatchConsideringVariants(
            EntityManager em, Entity instanceChirpPrefab, Entity sender, Entity target, out ChirpPayload payload)
        {
            lock (_lock)
            {
                int bestIndex = -1;
                int bestScore = -1;
                ulong bestTicket = ulong.MaxValue;

                for (int i = 0; i < _pending.Count; i++)
                {
                    var item = _pending[i];
                    bool prefabMatches = item.Prefab == instanceChirpPrefab;

                    if (!prefabMatches && em.HasBuffer<Game.Prefabs.TriggerChirpData>(item.Prefab))
                    {
                        var buf = em.GetBuffer<Game.Prefabs.TriggerChirpData>(item.Prefab, true);
                        for (int k = 0; k < buf.Length; k++)
                            if (buf[k].m_Chirp == instanceChirpPrefab) { prefabMatches = true; break; }
                    }

                    if (!prefabMatches) continue;

                    int score = 0;
                    if (item.Payload.Target != Entity.Null && target != Entity.Null && item.Payload.Target == target) score += 2;
                    if (item.Payload.Sender != Entity.Null && sender != Entity.Null && item.Payload.Sender == sender) score += 1;

                    if (score > bestScore || (score == bestScore && item.Ticket < bestTicket))
                    {
                        bestScore = score; bestTicket = item.Ticket; bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    payload = _pending[bestIndex].Payload;
                    _pending.RemoveAt(bestIndex);
                    return true;
                }

                payload = default;
                return false;
            }
        }

        // -------- Per-instance attachment for UI --------
        public static void RememberAttached(Entity instance, in ChirpPayload payload)
        {
            if (instance == Entity.Null) return;
            lock (_lock) { _attached[instance] = payload; }
        }

        public static bool TryGetAttached(Entity instance, out ChirpPayload payload)
        {
            lock (_lock) { return _attached.TryGetValue(instance, out payload); }
        }

        public static void Forget(Entity instance)
        {
            if (instance == Entity.Null) return;
            lock (_lock) { _attached.Remove(instance); }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _pending.Clear();
                _pendingByTarget.Clear();
                _attached.Clear();
                _byToken.Clear();
                _nextTicket = 0;
                _nextToken = 1;
            }
        }
    }
}

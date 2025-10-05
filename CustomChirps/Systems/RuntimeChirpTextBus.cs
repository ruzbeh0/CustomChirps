// Systems/RuntimeChirpTextBus.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    public struct ChirpPayload
    {
        public FixedString512Bytes Key;                 // custom text
        public Entity Sender;              // desired account/icon
        public Entity Target;              // optional building to link
        public FixedString128Bytes OverrideSenderName;  // custom sender label for UI
    }

    internal struct PendingItem
    {
        public Entity Prefab;     // trigger/service/brand prefab used to queue
        public ChirpPayload Payload;
        public ulong Ticket;      // FIFO for tie-breaking
    }

    public static class RuntimeChirpTextBus
    {
        // Global pending list (for fallbacks / scans)
        private static readonly List<PendingItem> _pending = new();
        private static ulong _nextTicket;

        // Pending items indexed by building target (most reliable discriminator)
        private static readonly Dictionary<Entity, List<PendingItem>> _pendingByTarget = new();

        // Attached payloads (per spawned chirp instance)
        private static readonly Dictionary<Entity, ChirpPayload> _attached = new();

        // --------------------------------------------------------------------
        // Enqueue payload for a prefab (and optionally index by target)
        // --------------------------------------------------------------------
        public static void EnqueuePending(Entity prefab, in ChirpPayload payload)
        {
            if (prefab == Entity.Null) return;

            var item = new PendingItem
            {
                Prefab = prefab,
                Payload = payload,
                Ticket = _nextTicket++
            };

            _pending.Add(item);

            if (payload.Target != Entity.Null)
            {
                if (!_pendingByTarget.TryGetValue(payload.Target, out var list))
                    _pendingByTarget[payload.Target] = list = new List<PendingItem>(2);
                list.Add(item);
            }
        }

        // --------------------------------------------------------------------
        // Target-first dequeue: exact building match wins
        // --------------------------------------------------------------------
        public static bool TryDequeueForTarget(Entity target, out ChirpPayload payload)
        {
            if (target != Entity.Null && _pendingByTarget.TryGetValue(target, out var list) && list.Count > 0)
            {
                int bestIdx = 0;
                ulong bestTicket = list[0].Ticket;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].Ticket < bestTicket) { bestIdx = i; bestTicket = list[i].Ticket; }
                }

                var chosen = list[bestIdx];
                list.RemoveAt(bestIdx);
                if (list.Count == 0) _pendingByTarget.Remove(target);

                // remove the same item from the global list
                for (int i = 0; i < _pending.Count; i++)
                {
                    if (_pending[i].Ticket == chosen.Ticket) { _pending.RemoveAt(i); break; }
                }

                payload = chosen.Payload;
                return true;
            }

            payload = default;
            return false;
        }

        // --------------------------------------------------------------------
        // Plain prefab-only dequeue (compat / last resort)
        // --------------------------------------------------------------------
        public static bool TryDequeueForPrefab(Entity prefab, out ChirpPayload payload)
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

        // --------------------------------------------------------------------
        // Variant-aware best match:
        //   match if pending.Prefab == instanceChirpPrefab OR
        //   pending.Prefab has TriggerChirpData listing instanceChirpPrefab
        // Score by target (strong) + sender (weak)
        // --------------------------------------------------------------------
        public static bool TryDequeueBestMatchConsideringVariants(
            EntityManager em,
            Entity instanceChirpPrefab,
            Entity sender,
            Entity target,
            out ChirpPayload payload)
        {
            int bestIndex = -1;
            int bestScore = -1;
            ulong bestTicket = ulong.MaxValue;

            for (int i = 0; i < _pending.Count; i++)
            {
                var item = _pending[i];

                bool prefabMatches = item.Prefab == instanceChirpPrefab;

                // If the queued prefab was a trigger/service prefab, check its variants
                if (!prefabMatches && em.HasBuffer<Game.Prefabs.TriggerChirpData>(item.Prefab))
                {
                    var buf = em.GetBuffer<Game.Prefabs.TriggerChirpData>(item.Prefab, true);
                    for (int k = 0; k < buf.Length; k++)
                    {
                        if (buf[k].m_Chirp == instanceChirpPrefab)
                        {
                            prefabMatches = true;
                            break;
                        }
                    }
                }

                if (!prefabMatches) continue;

                int score = 0;
                if (item.Payload.Target != Entity.Null && target != Entity.Null && item.Payload.Target == target)
                    score += 2; // strongest signal
                if (item.Payload.Sender != Entity.Null && sender != Entity.Null && item.Payload.Sender == sender)
                    score += 1;

                if (score > bestScore || (score == bestScore && item.Ticket < bestTicket))
                {
                    bestScore = score;
                    bestTicket = item.Ticket;
                    bestIndex = i;
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

        // --------------------------------------------------------------------
        // Per-instance storage for UI patches
        // --------------------------------------------------------------------
        public static void RememberAttached(Entity instance, in ChirpPayload payload)
        {
            if (instance != Entity.Null)
                _attached[instance] = payload;
        }

        public static bool TryGetAttached(Entity instance, out ChirpPayload payload)
            => _attached.TryGetValue(instance, out payload);

        public static void Forget(Entity instance)
        {
            if (instance != Entity.Null)
                _attached.Remove(instance);
        }
    }
}

// Systems/RuntimeChirpTextBus.cs

using System.Collections.Generic;

using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems;

public struct ChirpPayload
{
    public FixedString512Bytes Key; // custom text
    public Entity Sender; // desired account/icon
    public Entity Target; // optional building to link
    public FixedString128Bytes OverrideSenderName; // custom sender label for UI
}

internal class PendingItem
{
    public Entity Prefab; // trigger/service/brand prefab used to queue
    public ChirpPayload Payload;
    public ulong Ticket; // FIFO for tie-breaking
}
internal class PendingItemComparer : IComparer<PendingItem>
{
    public int Compare(PendingItem x, PendingItem y)
    {
        if (x == null || y == null)
            return x == y ? 0 : (x == null ? -1 : 1);
        return x.Ticket.CompareTo(y.Ticket);
    }
}

public static class RuntimeChirpTextBus
{
    private static ulong _nextTicket;
    private static readonly PendingItemComparer Comparer = new();

    private static readonly SortedSet<PendingItem> Pending = new(Comparer);
    private static readonly Dictionary<Entity, SortedSet<PendingItem>> PendingByTarget = new();
    private static readonly Dictionary<Entity, SortedSet<PendingItem>> PendingByPrefab = new();
    private static readonly Dictionary<ulong, PendingItem> TicketToItem = new();
    private static readonly Dictionary<Entity, ChirpPayload> Attached = new();

    public static void EnqueuePending(Entity prefab, in ChirpPayload payload)
    {
        if (prefab == Entity.Null) return;

        ulong ticket = _nextTicket++;

        var item = new PendingItem
        {
            Prefab = prefab,
            Payload = payload,
            Ticket = ticket
        };

        Pending.Add(item);
        TicketToItem[ticket] = item;

        if (payload.Target != Entity.Null)
        {
            if (!PendingByTarget.TryGetValue(payload.Target, out var set))
            {
                set = new SortedSet<PendingItem>(Comparer);
                PendingByTarget[payload.Target] = set;
            }
            set.Add(item);
        }

        if (!PendingByPrefab.TryGetValue(prefab, out var prefabSet))
        {
            prefabSet = new SortedSet<PendingItem>(Comparer);
            PendingByPrefab[prefab] = prefabSet;
        }
        prefabSet.Add(item);
    }

    public static bool TryDequeueForTarget(Entity target, out ChirpPayload payload)
    {
        if (target == Entity.Null || !PendingByTarget.TryGetValue(target, out var set) || set.Count == 0)
        {
            payload = default;
            return false;
        }

        var item = set.Min;
        set.Remove(item);

        if (set.Count == 0)
            PendingByTarget.Remove(target);

        RemoveFromAllIndices(item);

        payload = item.Payload;
        return true;
    }

    public static bool TryDequeueForPrefab(Entity prefab, out ChirpPayload payload)
    {
        if (!PendingByPrefab.TryGetValue(prefab, out var set) || set.Count == 0)
        {
            payload = default;
            return false;
        }

        var item = set.Min;
        set.Remove(item);

        if (set.Count == 0)
            PendingByPrefab.Remove(prefab);

        RemoveFromAllIndices(item);

        payload = item.Payload;
        return true;
    }

    public static bool TryDequeueBestMatchConsideringVariants(
        EntityManager em,
        Entity instanceChirpPrefab,
        Entity sender,
        Entity target,
        out ChirpPayload payload)
    {
        PendingItem bestItem = null;
        int bestScore = -1;
        ulong bestTicket = ulong.MaxValue;

        var candidates = new List<PendingItem>(16);

        if (PendingByPrefab.TryGetValue(instanceChirpPrefab, out var directSet))
        {
            candidates.AddRange(directSet);
        }

        foreach (var kvp in PendingByPrefab)
        {
            if (kvp.Key == instanceChirpPrefab) continue;

            if (em.HasBuffer<Game.Prefabs.TriggerChirpData>(kvp.Key))
            {
                var buf = em.GetBuffer<Game.Prefabs.TriggerChirpData>(kvp.Key, true);
                bool hasVariant = false;

                for (int k = 0; k < buf.Length; k++)
                {
                    if (buf[k].m_Chirp == instanceChirpPrefab)
                    {
                        hasVariant = true;
                        break;
                    }
                }

                if (hasVariant)
                {
                    candidates.AddRange(kvp.Value);
                }
            }
        }

        foreach (var item in candidates)
        {
            var score = 0;
            if (item.Payload.Target != Entity.Null && target != Entity.Null && item.Payload.Target == target)
                score += 2;
            if (item.Payload.Sender != Entity.Null && sender != Entity.Null && item.Payload.Sender == sender)
                score += 1;

            if (score <= bestScore && (score != bestScore || item.Ticket >= bestTicket)) continue;
            bestScore = score;
            bestTicket = item.Ticket;
            bestItem = item;
        }

        if (bestItem != null)
        {
            RemoveFromAllIndices(bestItem);

            payload = bestItem.Payload;
            return true;
        }

        payload = default;
        return false;
    }

    private static void RemoveFromAllIndices(PendingItem item)
    {
        Pending.Remove(item);
        TicketToItem.Remove(item.Ticket);

        if (item.Payload.Target != Entity.Null &&
            PendingByTarget.TryGetValue(item.Payload.Target, out var targetSet))
        {
            targetSet.Remove(item);
            if (targetSet.Count == 0)
                PendingByTarget.Remove(item.Payload.Target);
        }

        if (PendingByPrefab.TryGetValue(item.Prefab, out var prefabSet))
        {
            prefabSet.Remove(item);
            if (prefabSet.Count == 0)
                PendingByPrefab.Remove(item.Prefab);
        }
    }

    public static void RememberAttached(Entity instance, in ChirpPayload payload)
    {
        if (instance != Entity.Null)
            Attached[instance] = payload;
    }

    public static bool TryGetAttached(Entity instance, out ChirpPayload payload)
        => Attached.TryGetValue(instance, out payload);

    public static void Forget(Entity instance)
    {
        if (instance != Entity.Null)
            Attached.Remove(instance);
    }

    public static void Clear()
    {
        Pending.Clear();
        PendingByTarget.Clear();
        PendingByPrefab.Clear();
        TicketToItem.Clear();
        Attached.Clear();
        _nextTicket = 0;
    }

    internal static int GetPendingCount() => Pending.Count;

    internal static IEnumerable<PendingItem> GetPendingItems() => Pending;
}
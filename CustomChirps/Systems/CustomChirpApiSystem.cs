// Systems/CustomChirpApiSystem.cs
using Colossal;
using Colossal.Localization;
using Colossal.Logging;
using CustomChirps.Systems; // RuntimeChirpTextBus & ChirpPayload live here
using Game.Prefabs;
using Game.SceneFlow;
using Game.Triggers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    /// <summary>
    /// Departments available as "sender accounts" (icon source).
    /// </summary>
    public enum DepartmentAccount
    {
        Electricity,
        FireRescue,
        Roads,
        Water,
        Communications,
        Police,
        PropertyAssessmentOffice,
        Post,
        BusinessNews,
        CensusBureau,
        ParkAndRec,
        EnvironmentalProtectionAgency,
        Healthcare,
        LivingStandardsAssociation,
        Garbage,
        TourismBoard,
        Transportation,
        Education
    }

    /// <summary>
    /// Minimal public API for other mods to post Chirper messages.
    /// - Department selects the icon (underlying Chirper account).
    /// - customSenderName (if provided) is what the UI shows as the sender label (your patches apply it).
    /// - If a non-null target entity is provided and no {LINK_*} token exists, "{LINK_1}" is appended automatically.
    /// </summary>
    public sealed partial class CustomChirpApiSystem : SystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = Mod.log;

        // Fallback generic chirp prefab (any with ChirpData) if none explicitly configured
        private Entity _chirpPrefabEntity;
        private bool _didInit;

        // Runtime localization hot-source
        private LocalizationManager _locMgr;
        private RuntimeLocaleSource _runtimeLocale;

        // ----------------------- Public API -----------------------

        /// <summary>
        /// Post a chirp selecting department (icon) and a target ENTITY (any ECS entity).
        /// If a target is provided and "{LINK_*}" is not present in the text, "{LINK_1}" is appended automatically.
        /// </summary>
        public static void PostChirp(
            string text,
            DepartmentAccount department,
            Entity targetEntity,
            string customSenderName = null)
        {
            var inst = _instance;
            if (inst == null)
            {
                _log.Warn("[CustomChirps] API system not ready.");
                return;
            }

            inst.EnqueueChirp(text, department, targetEntity, customSenderName);
        }

        // ----------------------- Internals ------------------------

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;

            // Install a runtime locale source so we can add keys at runtime
            try
            {
                _locMgr = GameManager.instance.localizationManager;
                _runtimeLocale = RuntimeLocaleSource.InstallIfNeeded(_locMgr, "en-US");
                _log.Info("[CustomChirps] Runtime locale source installed.");
            }
            catch (Exception ex)
            {
                _log.Error($"[CustomChirps] Failed to install runtime locale source: {ex}");
            }
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureInit();
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnDestroy();
        }

        protected override void OnUpdate() { /* no per-frame work needed */ }

        private void EnsureInit()
        {
            if (_didInit) return;

            // Auto-discover a generic chirp prefab if none configured
            if (_chirpPrefabEntity == Entity.Null)
            {
                try
                {
                    using var chirpPfs = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ChirpData>())
                                                      .ToEntityArray(Allocator.Temp);
                    if (chirpPfs.Length > 0) _chirpPrefabEntity = chirpPfs[0];
                }
                catch { /* ignore */ }
            }

            _log.Info($"[CustomChirps] Api init — chirpPf={(_chirpPrefabEntity != Entity.Null)}");
            _didInit = true;
        }

        private void EnqueueChirp(string text, DepartmentAccount dept, Entity anyTarget, string customSenderName)
        {
            EnsureInit();

            // Resolve the department account (icon source)
            var senderAccount = ResolveDepartment(dept);
            if (senderAccount == Entity.Null)
            {
                _log.Warn($"[CustomChirps] Department '{dept}' account not present in this save.");
                return;
            }

            if (_chirpPrefabEntity == Entity.Null)
            {
                _log.Warn("[CustomChirps] No chirp prefab available. Load a city/save first.");
                return;
            }

            // Auto-insert {LINK_1} if a target is supplied and the text has no link token
            string finalText = text ?? string.Empty;
            if (anyTarget != Entity.Null &&
                finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
            {
                finalText = finalText + " {LINK_1}";
            }

            // Create a runtime localization key
            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";
            _runtimeLocale?.AddOrUpdate(key, string.IsNullOrEmpty(finalText) ? "(empty)" : finalText);
            try { _locMgr?.ReloadActiveLocale(); } catch { /* ignore */ }

            // Hand payload to our spawner via the bus
            var payload = new ChirpPayload
            {
                Key = new FixedString512Bytes(key),
                Sender = senderAccount,                                // icon source
                Target = anyTarget,                                     // clickable link (via CreateChirpSystem)
                OverrideSenderName = new FixedString128Bytes(customSenderName ?? "")// visible label (your UI patch uses it)
            };
            RuntimeChirpTextBus.EnqueuePending(_chirpPrefabEntity, in payload);

            // Let the vanilla create path instantiate the chirp + link buffer
            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);
            queue.Enqueue(new ChirpCreationData
            {
                m_TriggerPrefab = _chirpPrefabEntity,
                m_Sender = senderAccount,
                m_Target = anyTarget
            });
            create.AddQueueWriter(deps);

            _log.Info($"[CustomChirps] Queued chirp (dept={dept}, sender={senderAccount}, target={(anyTarget == Entity.Null ? "None" : anyTarget.ToString())}).");
        }

        // Try to find an instance entity for the given prefab by matching PrefabRef.m_Prefab
        private bool TryResolveEntityFromPrefab(PrefabBase prefab, out Entity entity)
        {
            entity = Entity.Null;
            if (prefab == null) return false;

            var wanted = Util.PrefabLookup.GetPrefabEntity(World, prefab);
            if (wanted == Entity.Null) return false;

            var em = EntityManager;

            using var q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PrefabRef>()
                .Build(em);

            using var chunks = q.ToArchetypeChunkArray(Allocator.Temp);
            var typePrefabRef = GetComponentTypeHandle<PrefabRef>(true);
            var typeEntity = GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var prefs = chunk.GetNativeArray(typePrefabRef);
                var ents = chunk.GetNativeArray(typeEntity);

                for (int i = 0; i < ents.Length; i++)
                {
                    if (prefs[i].m_Prefab == wanted)
                    {
                        entity = ents[i];
                        return true;
                    }
                }
            }
            return false;
        }

        // ---- Department resolver ----

        private static readonly Dictionary<DepartmentAccount, string> s_DepartmentPrefabNames =
            new()
            {
                { DepartmentAccount.Electricity,                    "ElectricityChirperAccount" },
                { DepartmentAccount.FireRescue,                     "FireRescueChirperAccount" },
                { DepartmentAccount.Roads,                          "RoadChirperAccount" },
                { DepartmentAccount.Water,                          "WaterChirperAccount" },
                { DepartmentAccount.Communications,                 "CommunicationsChirperAccount" },
                { DepartmentAccount.Police,                         "PoliceChirperAccount" },
                { DepartmentAccount.PropertyAssessmentOffice,       "PropertyAssessmentOfficeAccount" },
                { DepartmentAccount.Post,                           "PostChirperAccount" },
                { DepartmentAccount.BusinessNews,                   "BusinessNewsChirperAccount" },
                { DepartmentAccount.CensusBureau,                   "CensusBureauChirperAccount" },
                { DepartmentAccount.ParkAndRec,                     "ParkAndRecChirperAccount" },
                { DepartmentAccount.EnvironmentalProtectionAgency,  "EnvironmentalProtectionAgencyChirperAccount" },
                { DepartmentAccount.Healthcare,                     "HealthcareChirperAccount" },
                { DepartmentAccount.LivingStandardsAssociation,     "LivingStandardsAssociationChirperAccount" },
                { DepartmentAccount.Garbage,                        "GarbageChirperAccount" },
                { DepartmentAccount.TourismBoard,                   "TourismBoardChirperAccount" },
                { DepartmentAccount.Transportation,                 "TransportationChirperAccount" },
                { DepartmentAccount.Education,                      "EducationChirperAccount" },
            };

        /// <summary>Resolve department account entity present in the current save.</summary>
        private static Entity ResolveDepartment(DepartmentAccount dept)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return Entity.Null;

            var em = world.EntityManager;
            var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();

            if (!s_DepartmentPrefabNames.TryGetValue(dept, out var wantedName) || string.IsNullOrEmpty(wantedName))
                return Entity.Null;

            using var accounts = em.CreateEntityQuery(ComponentType.ReadOnly<ChirperAccountData>())
                                   .ToEntityArray(Allocator.Temp);

            for (int i = 0; i < accounts.Length; i++)
            {
                var acc = accounts[i];
                if (!em.HasComponent<PrefabData>(acc)) continue;

                var pd = em.GetComponentData<PrefabData>(acc);
                if (!prefabSystem.TryGetPrefab<ChirperAccount>(pd, out var prefab)) continue;

                if (string.Equals(prefab.name, wantedName, StringComparison.OrdinalIgnoreCase))
                    return acc;
            }

            return Entity.Null;
        }

        // --------------- Runtime locale source ----------------

        private sealed class RuntimeLocaleSource : IDictionarySource
        {
            private readonly ConcurrentDictionary<string, string> _entries = new();

            private RuntimeLocaleSource() { }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(
                IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts) => _entries;

            public void Unload() { }
            public override string ToString() => "CustomChirps.RuntimeLocale";

            public void AddOrUpdate(string key, string value) => _entries[key] = value;

            public static RuntimeLocaleSource InstallIfNeeded(LocalizationManager mgr, string localeId)
            {
                var src = new RuntimeLocaleSource();
                mgr.AddSource(localeId, src);
                return src;
            }
        }
    }
}

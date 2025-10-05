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
    /// Minimal, public API for other mods to post Chirper messages.
    /// One entry point: PostChirp(text, department, optional building, optional custom sender name)
    /// - Department selects the icon (underlying account).
    /// - customSenderName (if provided) is what the UI shows as the sender label (your patches will apply it).
    /// - If building != Entity.Null, we auto-append "{LINK_1}" to the text (if not already present) so it's clickable.
    /// Register this system in Mod.OnLoad:
    ///   updateSystem.UpdateAt<CustomChirpApiSystem>(SystemUpdatePhase.GameSimulation);
    /// </summary>
    public sealed partial class CustomChirpApiSystem : SystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = LogManager.GetLogger("CustomChirps.Api");

        // Fallback generic chirp prefab (any with ChirpData) if none explicitly configured
        private Entity _chirpPrefabEntity;
        private bool _didInit;

        // Runtime localization hot-source
        private LocalizationManager _locMgr;
        private RuntimeLocaleSource _runtimeLocale;

        // ----------------------- Public API -----------------------

        /// <summary>
        /// Post a free-text chirp selecting department (icon), optional building link, and optional visible sender name override.
        /// </summary>
        public static void PostChirp(
            string text,
            DepartmentAccount department,
            Entity building = default,
            string customSenderName = null)
        {
            var inst = _instance;
            if (inst == null)
            {
                _log.Warn("[CustomChirps] API system not ready.");
                return;
            }

            inst.EnqueueChirp(text, department, building, customSenderName);
        }

        /// <summary>
        /// Optional: configure a specific chirp prefab asset to use as the trigger source.
        /// If not called, the system will auto-discover a generic ChirpData prefab.
        /// </summary>
        public void ConfigureDefaultChirpPrefab(PrefabBase chirpPrefab)
        {
            if (chirpPrefab == null)
            {
                _log.Warn("[CustomChirps] ConfigureDefaultChirpPrefab called with null asset.");
                return;
            }
            _chirpPrefabEntity = Util.PrefabLookup.GetPrefabEntity(World, chirpPrefab);
            _log.Info($"[CustomChirps] Default chirp prefab set to {_chirpPrefabEntity}.");
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

        private void EnqueueChirp(string text, DepartmentAccount dept, Entity building, string customSenderName)
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

            // Auto-insert {LINK_1} if a building is supplied and the text has no link token
            string finalText = text ?? string.Empty;
            if (building != Entity.Null &&
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
                Target = building,                                      // clickable link (via CreateChirpSystem)
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
                m_Target = building
            });
            create.AddQueueWriter(deps);

            _log.Info($"[CustomChirps] Queued chirp (dept={dept}, sender={senderAccount}, target={(building == Entity.Null ? "None" : building.ToString())}).");
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

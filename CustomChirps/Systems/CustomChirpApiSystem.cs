// Systems/CustomChirpApiSystem.cs
using Colossal;
using Colossal.Localization;
using Colossal.Logging;

using Game.Prefabs;
using Game.SceneFlow;
using Game.Triggers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace CustomChirps.Systems
{
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
    /// Public API for other mods to post Chirper messages.
    /// - Thread-safe: PostChirp can be called from any thread.
    /// - Work is drained on the main thread during OnUpdate.
    /// - Uses a deferred writer job to enqueue into CreateChirpSystem without blocking.
    /// </summary>
    public sealed partial class CustomChirpApiSystem : SystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = Mod.log;

        // ======= Thread-safe request queue (producer: any thread, consumer: main thread) =======
        private struct PendingRequest
        {
            public string Text;
            public DepartmentAccount Dept;
            public Entity Target;
            public string CustomSenderName;
        }

        private static readonly ConcurrentQueue<PendingRequest> s_requests = new ConcurrentQueue<PendingRequest>();

        // Drain cap per frame to avoid long stalls if a flood happens.
        private const int MaxRequestsPerFrame = 512;

        // ======= Runtime state (main thread only) =======
        private Entity _chirpPrefabEntity;
        private bool _didInit;

        private LocalizationManager _locMgr;
        private RuntimeLocaleSource _runtimeLocale;

        // ======= Public API (thread-safe) =======
        public static void PostChirp(string text, DepartmentAccount dept, Entity targetEntity, string customSenderName = null)
        {
            // Only enqueue immutable data here; do not touch Unity/ECS state.
            s_requests.Enqueue(new PendingRequest
            {
                Text = text,
                Dept = dept,
                Target = targetEntity,
                CustomSenderName = customSenderName
            });
        }

        // ======= System lifecycle =======
        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;

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
            if (ReferenceEquals(_instance, this)) _instance = null;
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            EnsureInit();
            if (!_didInit) return;

            // Drain queued requests on the main thread
            int processed = 0;
            while (processed < MaxRequestsPerFrame && s_requests.TryDequeue(out var req))
            {
                try
                {
                    ProcessRequestOnMainThread(req);
                }
                catch (Exception ex)
                {
                    _log.Error($"[CustomChirps] Failed to process chirp request: {ex}");
                }
                processed++;
            }
        }

        private void EnsureInit()
        {
            if (_didInit) return;

            if (_chirpPrefabEntity == Entity.Null)
            {
                try
                {
                    using var chirpPfs = EntityManager
                        .CreateEntityQuery(ComponentType.ReadOnly<ChirpData>())
                        .ToEntityArray(Allocator.Temp);
                    if (chirpPfs.Length > 0) _chirpPrefabEntity = chirpPfs[0];
                }
                catch { /* ignore */ }
            }

            _didInit = _chirpPrefabEntity != Entity.Null;
            _log.Info($"[CustomChirps] Api init — chirpPf={_didInit}");
        }

        // ======= Main-thread processing =======
        private void ProcessRequestOnMainThread(in PendingRequest req)
        {
            var senderAccount = ResolveDepartment(req.Dept);
            if (senderAccount == Entity.Null)
            {
                _log.Warn($"[CustomChirps] Department '{req.Dept}' account not present in this save.");
                return;
            }

            if (_chirpPrefabEntity == Entity.Null)
            {
                _log.Warn("[CustomChirps] No chirp prefab available. Load a city/save first.");
                return;
            }

            // Ensure there is a link placeholder if a target exists.
            string finalText = req.Text ?? string.Empty;
            if (req.Target != Entity.Null && finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
                finalText += " {LINK_1}";

            // Create runtime localization key (unique per request)
            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";
            _runtimeLocale?.AddOrUpdate(key, string.IsNullOrEmpty(finalText) ? "(empty)" : finalText);
            try { _locMgr?.ReloadActiveLocale(); } catch { /* ignore */ }

            // Build payload and create a marker that will later be swapped to the real target
            var payload = new ChirpPayload
            {
                Key = new FixedString512Bytes(key),
                Sender = senderAccount,
                Target = req.Target,
                OverrideSenderName = new FixedString128Bytes(req.CustomSenderName ?? "")
            };

            var em = EntityManager;
            var marker = RuntimeChirpTextBus.CreateMarker(em, req.Target, out var token);
            RuntimeChirpTextBus.AddPendingByToken(token, in payload);

            // Prepare the creation data for the vanilla chirp queue
            var data = new ChirpCreationData
            {
                m_TriggerPrefab = _chirpPrefabEntity,
                m_Sender = senderAccount,
                m_Target = marker
            };

            // Non-blocking: schedule a tiny job that enqueues AFTER existing writers
            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);

            var job = new EnqueueChirpJob
            {
                Writer = queue.AsParallelWriter(),
                Data = data
            };
            var writerHandle = job.Schedule(deps);

            // Inform CreateChirpSystem that we added a writer
            create.AddQueueWriter(writerHandle);

            var targetLabel = req.Target == Entity.Null ? "None" : req.Target.ToString();
            _log.Info($"[CustomChirps] Queued chirp (dept={req.Dept}, sender={senderAccount}, target={targetLabel}) [deferred, main-thread].");
        }

        [BurstCompile] // optional; safe to keep even if Burst isn't available at runtime
        private struct EnqueueChirpJob : IJob
        {
            public NativeQueue<ChirpCreationData>.ParallelWriter Writer;
            public ChirpCreationData Data;

            public void Execute()
            {
                Writer.Enqueue(Data);
            }
        }

        // ======= Helpers =======
        private static readonly Dictionary<DepartmentAccount, string> s_DepartmentPrefabNames = new()
        {
            { DepartmentAccount.Electricity,                   "ElectricityChirperAccount" },
            { DepartmentAccount.FireRescue,                    "FireRescueChirperAccount" },
            { DepartmentAccount.Roads,                         "RoadChirperAccount" },
            { DepartmentAccount.Water,                         "WaterChirperAccount" },
            { DepartmentAccount.Communications,                "CommunicationsChirperAccount" },
            { DepartmentAccount.Police,                        "PoliceChirperAccount" },
            { DepartmentAccount.PropertyAssessmentOffice,      "PropertyAssessmentOfficeAccount" },
            { DepartmentAccount.Post,                          "PostChirperAccount" },
            { DepartmentAccount.BusinessNews,                  "BusinessNewsChirperAccount" },
            { DepartmentAccount.CensusBureau,                  "CensusBureauChirperAccount" },
            { DepartmentAccount.ParkAndRec,                    "ParkAndRecChirperAccount" },
            { DepartmentAccount.EnvironmentalProtectionAgency, "EnvironmentalProtectionAgencyChirperAccount" },
            { DepartmentAccount.Healthcare,                    "HealthcareChirperAccount" },
            { DepartmentAccount.LivingStandardsAssociation,    "LivingStandardsAssociationChirperAccount" },
            { DepartmentAccount.Garbage,                       "GarbageChirperAccount" },
            { DepartmentAccount.TourismBoard,                  "TourismBoardChirperAccount" },
            { DepartmentAccount.Transportation,                "TransportationChirperAccount" },
            { DepartmentAccount.Education,                     "EducationChirperAccount" },
        };

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

        // Minimal runtime locale source to avoid file IO; lives only in memory.
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

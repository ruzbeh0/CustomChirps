// Systems/CustomChirpApiSystem.cs
using Colossal;
using Colossal.Localization;
using Colossal.Logging;
using CustomChirps.Utils; // I18NBridge + RuntimeLocalizationWindow
using Game;
using Game.Citizens;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Triggers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// Thread-safe chirp API with dual localization:
    /// - I18NEverywhere (RuntimeLocalizationWindow) for mod ecosystem,
    /// - Vanilla LocalizationManager proxy so Chirper UI resolves the message immediately.
    /// </summary>
    public sealed partial class CustomChirpApiSystem : GameSystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = Mod.log;
        private bool _useVanillaProxy; // true only when I18NEverywhere is NOT available

        // ======= Thread-safe request queue (producer: any thread, consumer: main thread) =======
        private struct PendingRequest
        {
            public string Text;
            public DepartmentAccount Dept;
            public Entity Target;
            public string CustomSenderName;
            public Entity SenderEntityOverride;
        }

        private static readonly ConcurrentQueue<PendingRequest> s_requests = new ConcurrentQueue<PendingRequest>();
        private const int MaxRequestsPerFrame = 512;

        // ======= Runtime state (main thread only) =======
        private Entity _chirpPrefabEntity;
        private bool _didInit;

        // I18N bridge & sliding window
        private Dictionary<string, string> _i18nDict;                 // shared-or-private dictionary
        private RuntimeLocalizationWindow _i18nWindow;
        private bool _i18nEverywhereAvailable;

        // Vanilla localization bridge (proxy over a private concurrent dict)
        private LocalizationManager _locMgr;
        private RuntimeLocaleSourceProxy _vanillaSource;
        private RuntimeLocalizationWindow _vanillaWindow;
        private Dictionary<string, string> _vanillaDict = new(StringComparer.Ordinal);

        // ======= Public API (thread-safe) =======
        public static void PostChirp(string text, DepartmentAccount dept, Entity targetEntity, string customSenderName = null)
        {
            s_requests.Enqueue(new PendingRequest
            {
                Text = text,
                Dept = dept,
                Target = targetEntity,
                CustomSenderName = customSenderName
            });
        }

        /// <summary>
        /// Post a chirp from a cim (citizen).
        /// The cim will be shown as the sender with their icon and will be clickable in the UI.
        /// </summary>
        /// <param name="text">The chirp message text. Use {LINK_1} for target entity link.</param>
        /// <param name="citizenSenderEntity">A cim entity (must have Game.Citizens.Citizen component).</param>
        /// <param name="targetEntity">Target entity for {LINK_1} in the message (e.g., a building or park).</param>
        /// <param name="customSenderName">Optional display name for the sender (e.g., "John Smith").</param>
        public static void PostChirpFromEntity(string text, Entity citizenSenderEntity, Entity targetEntity, string customSenderName = null)
        {
            s_requests.Enqueue(new PendingRequest
            {
                Text = text,
                Dept = default,
                Target = targetEntity,
                CustomSenderName = customSenderName,
                SenderEntityOverride = citizenSenderEntity
            });
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;

            // Decide backend once (i18n wins; vanilla is fallback)
            var shared = I18NBridge.GetDictionary();
            if (shared != null)
            {
                _i18nEverywhereAvailable = true;
                _useVanillaProxy = false;

                _i18nDict = shared;
                _i18nWindow = new RuntimeLocalizationWindow(_i18nDict);
            }
            else
            {
                _i18nEverywhereAvailable = false;
                _useVanillaProxy = true;

                _i18nDict = new Dictionary<string, string>(StringComparer.Ordinal);
                _i18nWindow = new RuntimeLocalizationWindow(_i18nDict);
            }

            if (_useVanillaProxy)
            {
                _locMgr = GameManager.instance.localizationManager;

                // activeLocaleId is guaranteed non-null in your environment
                var activeLocale = _locMgr.activeLocaleId;

                _vanillaSource = new RuntimeLocaleSourceProxy(_vanillaDict);
                _locMgr.AddSource(activeLocale, _vanillaSource);

                // sliding window for vanilla too (prevents unbounded memory growth)
                _vanillaWindow = new RuntimeLocalizationWindow(_vanillaDict);
            }
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

            // If i18n is active and the shared dict instance changed (e.g., locale switch), rebind the window.
            if (_i18nEverywhereAvailable)
            {
                var latest = I18NBridge.GetDictionary();
                if (latest != null && !ReferenceEquals(latest, _i18nDict))
                {
                    _i18nDict = latest;
                    _i18nWindow = new RuntimeLocalizationWindow(_i18nDict);
                    _log.Info("[CustomChirps] I18N dict swapped due to locale change.");
                }
            }


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
            _log.Info($"[CustomChirps] Api init — chirpPf={_didInit}, i18n={(_i18nEverywhereAvailable ? "I18NEverywhere" : "private")}");
        }

        // ======= Main-thread processing =======
        private void ProcessRequestOnMainThread(in PendingRequest req)
        {
            // Resolve sender: use override entity if provided, otherwise resolve from department
            Entity senderAccount;
            if (req.SenderEntityOverride != Entity.Null)
            {
                // Validate sender is a cim (has Citizen component)
                if (!EntityManager.HasComponent<Citizen>(req.SenderEntityOverride))
                {
                    _log.Warn("[CustomChirps] PostChirpFromEntity requires a cim (entity with Game.Citizens.Citizen component) as sender");
                    return;
                }
                senderAccount = req.SenderEntityOverride;
            }
            else
            {
                senderAccount = ResolveDepartment(req.Dept);
            }

            if (senderAccount == Entity.Null || _chirpPrefabEntity == Entity.Null)
                return;

            // Compose final text (keep your existing formatting; below is the same minimal link insert)
            string finalText = req.Text ?? string.Empty;
            if (req.Target != Entity.Null && finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
                finalText += " {LINK_1}";

            // 1) Collision-resistant key
            var key = $"customchirps:{Guid.NewGuid():N}";
            var value = string.IsNullOrWhiteSpace(finalText) ? "(empty)" : finalText;

            // 2) Single-backend write with a sliding window in BOTH paths
            if (_i18nEverywhereAvailable)
            {
                // i18n: add + window trim; no reloads, no vanilla writes
                _i18nWindow.AddWithWindowManagement(key, value);
            }
            else if (_useVanillaProxy)
            {
                // vanilla: add + window trim; then refresh so UI resolves the new key
                _vanillaWindow.AddWithWindowManagement(key, value);
                _locMgr.ReloadActiveLocale();
            }
            else
            {
                // ultra-fallback (shouldn’t really happen, but cheap to keep)
                _i18nDict[key] = value;
            }

            // 3) Build and enqueue payload (keep your existing code)
            var payload = new ChirpPayload
            {
                Key = new Unity.Collections.FixedString512Bytes(key),
                Sender = senderAccount,
                Target = req.Target,
                OverrideSenderName = new Unity.Collections.FixedString128Bytes(req.CustomSenderName ?? "")
            };

            var em = EntityManager;
            var marker = RuntimeChirpTextBus.CreateMarker(em, req.Target, out var token);
            RuntimeChirpTextBus.AddPendingByToken(token, in payload);

            var data = new ChirpCreationData
            {
                m_TriggerPrefab = _chirpPrefabEntity,
                m_Sender = senderAccount,
                m_Target = marker
            };

            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);
            var job = new EnqueueChirpJob { Writer = queue.AsParallelWriter(), Data = data };
            var writerHandle = job.Schedule(deps);
            create.AddQueueWriter(writerHandle);
        }



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

        /// <summary>
        /// Minimal proxy source that exposes an in-memory dictionary to the vanilla LocalizationManager.
        /// </summary>
        class RuntimeLocaleSourceProxy : IDictionarySource
        {
            private readonly Dictionary<string, string> _src;

            public RuntimeLocaleSourceProxy(Dictionary<string, string> src)
            {
                _src = src ?? throw new ArgumentNullException(nameof(src));
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(
                IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts)
            {
                // Snapshot the dictionary at call time (enumeration over Dictionary is fine).
                return _src;
            }

            public void Unload() { }

            public override string ToString() => "CustomChirps.RuntimeLocaleProxy";
        }

    }
}
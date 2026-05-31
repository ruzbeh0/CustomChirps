// Systems/CustomChirpApiSystem.cs
using Colossal.Logging;
using CustomChirps.Utils;
using Game;
using Game.Citizens;
using Game.Prefabs;
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

    public enum ChirpDisplayMode
    {
        Compact = 0,
        Large = 1
    }

    /// <summary>
    /// Thread-safe chirp API.
    /// Runtime chirp text is resolved by RuntimeChirpLocalizationPatch without reloading the active locale.
    /// </summary>
    public sealed partial class CustomChirpApiSystem : GameSystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = Mod.log;

        // ======= Thread-safe request queue (producer: any thread, consumer: main thread) =======
        private struct PendingRequest
        {
            public string Text;
            public DepartmentAccount Dept;
            public Entity Target;
            public Entity Target2;
            public string CustomSenderName;
            public Entity SenderEntityOverride;
            public ChirpDisplayMode DisplayMode;
            public string PortraitImageSource;
        }

        private static readonly ConcurrentQueue<PendingRequest> s_requests = new ConcurrentQueue<PendingRequest>();
        private const int MaxRequestsPerFrame = 512;

        // ======= Runtime state (main thread only) =======
        private Entity _chirpPrefabEntity;
        private bool _didInit;

        // ======= Public API (thread-safe) =======
        public static void PostChirp(string text, DepartmentAccount dept, Entity targetEntity, string customSenderName = null)
        {
            EnqueueChirp(text, dept, targetEntity, customSenderName, Entity.Null, ChirpDisplayMode.Compact);
        }

        public static void PostLargeChirp(string text, DepartmentAccount dept, Entity targetEntity, string customSenderName = null)
        {
            EnqueueChirp(text, dept, targetEntity, customSenderName, Entity.Null, ChirpDisplayMode.Large);
        }

        public static void PostLargeChirpWithPortraitImage(
            string text,
            DepartmentAccount dept,
            Entity targetEntity,
            string portraitImageSource,
            string customSenderName = null)
        {
            EnqueueChirp(text, dept, targetEntity, customSenderName, Entity.Null, ChirpDisplayMode.Large, portraitImageSource);
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
            EnqueueChirp(text, default, targetEntity, customSenderName, citizenSenderEntity, ChirpDisplayMode.Compact);
        }

        public static void PostLargeChirpFromEntity(string text, Entity citizenSenderEntity, Entity targetEntity, string customSenderName = null)
        {
            EnqueueChirp(text, default, targetEntity, customSenderName, citizenSenderEntity, ChirpDisplayMode.Large);
        }

        public static void PostLargeChirpFromEntityWithPortraitImage(
            string text,
            Entity citizenSenderEntity,
            Entity targetEntity,
            string portraitImageSource,
            string customSenderName = null)
        {
            EnqueueChirp(text, default, targetEntity, customSenderName, citizenSenderEntity, ChirpDisplayMode.Large, portraitImageSource);
        }

        public static void PostLargeChirpFromEntityWithPortraitImage(
            string text,
            Entity citizenSenderEntity,
            Entity targetEntity,
            Entity targetEntity2,
            string portraitImageSource,
            string customSenderName = null)
        {
            EnqueueChirp(text, default, targetEntity, customSenderName, citizenSenderEntity, ChirpDisplayMode.Large, portraitImageSource, targetEntity2);
        }

        private static void EnqueueChirp(
            string text,
            DepartmentAccount dept,
            Entity targetEntity,
            string customSenderName,
            Entity senderEntityOverride,
            ChirpDisplayMode displayMode,
            string portraitImageSource = null,
            Entity targetEntity2 = default)
        {
            s_requests.Enqueue(new PendingRequest
            {
                Text = text,
                Dept = dept,
                Target = targetEntity,
                Target2 = targetEntity2,
                CustomSenderName = customSenderName,
                SenderEntityOverride = senderEntityOverride,
                DisplayMode = displayMode,
                PortraitImageSource = portraitImageSource
            });
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            RuntimeChirpLocalization.Clear();
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
            _log.Info($"[CustomChirps] Api init - chirpPf={_didInit}, runtimeLocalization=true");
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
            if ((req.Target != Entity.Null || req.Target2 != Entity.Null) && finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
                finalText += " {LINK_1}";

            // 1) Collision-resistant key
            var portraitImageSource = req.PortraitImageSource;
            var hasPortraitImage = req.DisplayMode == ChirpDisplayMode.Large && !string.IsNullOrWhiteSpace(portraitImageSource);
            var imageKey = hasPortraitImage
                ? CustomChirpImageSourceRegistry.Register(portraitImageSource)
                : null;
            var keyPrefix = hasPortraitImage
                ? $"customchirps:portraitimg:{imageKey}:"
                : req.DisplayMode == ChirpDisplayMode.Large
                    ? "customchirps:large:"
                    : "customchirps:";
            var key = $"{keyPrefix}{Guid.NewGuid():N}";
            var value = string.IsNullOrWhiteSpace(finalText) ? "(empty)" : finalText;

            // 2) Register text locally. The UI localization lookup patch resolves these keys directly,
            // avoiding LocalizationManager.ReloadActiveLocale() and the stutter it can cause.
            RuntimeChirpLocalization.Add(key, value);

            // 3) Build and enqueue payload (keep your existing code)
            var payload = new ChirpPayload
            {
                Key = new Unity.Collections.FixedString512Bytes(key),
                Sender = senderAccount,
                Target = req.Target,
                Target2 = req.Target2,
                OverrideSenderName = new Unity.Collections.FixedString128Bytes(req.CustomSenderName ?? ""),
                DisplayMode = req.DisplayMode
            };

            var em = EntityManager;
            var marker = RuntimeChirpTextBus.CreateMarker(em, req.Target, req.Target2, out var token);
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
    }
}

#nullable enable
using Colossal.Logging;
using Game.Prefabs;
using Game.Triggers;
using System;
using System.Collections.Generic;
using CustomChirps.Utils;
using Game;
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
    public sealed partial class CustomChirpApiSystem : GameSystemBase
    {
        private static CustomChirpApiSystem? _instance;
        private static readonly ILog LOG = Mod.log;

        private Entity _chirpPrefabEntity;
        private bool _didInit;
        private RuntimeLocalizationWindow? _localizationWindow;

        /// <summary>
        /// Post a chirp selecting department (icon) and a target ENTITY (any ECS entity).
        /// If a target is provided and "{LINK_*}" is not present in the text, "{LINK_1}" is appended automatically.
        /// </summary>
        public static void PostChirp(
            string text,
            DepartmentAccount department,
            Entity targetEntity,
            string? customSenderName = null)
        {
            var inst = _instance;
            if (inst == null)
            {
                LOG.Warn("[CustomChirps] API system not ready.");
                return;
            }

            if (customSenderName != null) inst.EnqueueChirp(text, department, targetEntity, customSenderName);
        }

        public static void ClearWindow()
        {
            var inst = _instance;
            inst?._localizationWindow?.Clear();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;
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
                catch
                {
                    /* ignore */
                }
            }

            LOG.Info($"[CustomChirps] Api init — chirpPf={(_chirpPrefabEntity != Entity.Null)}");
            _didInit = true;
        }

        private void EnqueueChirp(string? text, DepartmentAccount dept, Entity anyTarget, string customSenderName)
        {
            EnsureInit();

            // Resolve the department account (icon source)
            var senderAccount = ResolveDepartment(dept);
            if (senderAccount == Entity.Null)
            {
                LOG.Warn($"[CustomChirps] Department '{dept}' account not present in this save.");
                return;
            }

            if (_chirpPrefabEntity == Entity.Null)
            {
                LOG.Warn("[CustomChirps] No chirp prefab available. Load a city/save first.");
                return;
            }

            // Auto-insert {LINK_1} if a target is supplied and the text has no link token
            var finalText = text ?? string.Empty;
            if (anyTarget != Entity.Null &&
                finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
            {
                finalText = finalText + " {LINK_1}";
            }

            // Create a runtime localization key
            var key = $"CustomChirps:{Guid.NewGuid().ToString()}";
            var runtimeDict = I18NBridge.GetDictionary();
            if (runtimeDict is null)
            {
                return;
            }

            _localizationWindow ??= new RuntimeLocalizationWindow(runtimeDict);
            _localizationWindow.AddWithWindowManagement(key, finalText);

            // Hand payload to our spawner via the bus
            var payload = new ChirpPayload
            {
                Key = new FixedString512Bytes(key),
                Sender = senderAccount, // icon source
                Target = anyTarget, // clickable link (via CreateChirpSystem)
                OverrideSenderName =
                    new FixedString128Bytes(customSenderName ?? "") // visible label (your UI patch uses it)
            };
            RuntimeChirpTextBus.EnqueuePending(_chirpPrefabEntity, in payload);

            // Let the vanilla create path instantiate the chirp + link buffer
            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var jobHandle);
            queue.Enqueue(new ChirpCreationData
            {
                m_TriggerPrefab = _chirpPrefabEntity,
                m_Sender = senderAccount,
                m_Target = anyTarget
            });
            create.AddQueueWriter(jobHandle);

            LOG.Info(
                $"[CustomChirps] Queued chirp (dept={dept}, sender={senderAccount}, target={(anyTarget == Entity.Null ? "None" : anyTarget.ToString())}).");
        }

        private static readonly Dictionary<DepartmentAccount, string> SDepartmentPrefabNames =
            new()
            {
                {DepartmentAccount.Electricity, "ElectricityChirperAccount"},
                {DepartmentAccount.FireRescue, "FireRescueChirperAccount"},
                {DepartmentAccount.Roads, "RoadChirperAccount"},
                {DepartmentAccount.Water, "WaterChirperAccount"},
                {DepartmentAccount.Communications, "CommunicationsChirperAccount"},
                {DepartmentAccount.Police, "PoliceChirperAccount"},
                {DepartmentAccount.PropertyAssessmentOffice, "PropertyAssessmentOfficeAccount"},
                {DepartmentAccount.Post, "PostChirperAccount"},
                {DepartmentAccount.BusinessNews, "BusinessNewsChirperAccount"},
                {DepartmentAccount.CensusBureau, "CensusBureauChirperAccount"},
                {DepartmentAccount.ParkAndRec, "ParkAndRecChirperAccount"},
                {DepartmentAccount.EnvironmentalProtectionAgency, "EnvironmentalProtectionAgencyChirperAccount"},
                {DepartmentAccount.Healthcare, "HealthcareChirperAccount"},
                {DepartmentAccount.LivingStandardsAssociation, "LivingStandardsAssociationChirperAccount"},
                {DepartmentAccount.Garbage, "GarbageChirperAccount"},
                {DepartmentAccount.TourismBoard, "TourismBoardChirperAccount"},
                {DepartmentAccount.Transportation, "TransportationChirperAccount"},
                {DepartmentAccount.Education, "EducationChirperAccount"},
            };

        /// <summary>Resolve department account entity present in the current save.</summary>
        private static Entity ResolveDepartment(DepartmentAccount dept)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return Entity.Null;

            var em = world.EntityManager;
            var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();

            if (!SDepartmentPrefabNames.TryGetValue(dept, out var wantedName) || string.IsNullOrEmpty(wantedName))
                return Entity.Null;

            using var accounts = em.CreateEntityQuery(ComponentType.ReadOnly<ChirperAccountData>())
                .ToEntityArray(Allocator.Temp);

            foreach (var acc in accounts)
            {
                if (!em.HasComponent<PrefabData>(acc)) continue;

                var pd = em.GetComponentData<PrefabData>(acc);
                if (!prefabSystem.TryGetPrefab<ChirperAccount>(pd, out var prefab)) continue;

                if (string.Equals(prefab.name, wantedName, StringComparison.OrdinalIgnoreCase))
                    return acc;
            }

            return Entity.Null;
        }

        protected override void OnUpdate()
        {
            // noop
        }
    }
}

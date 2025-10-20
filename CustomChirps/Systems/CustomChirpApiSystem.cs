// Systems/CustomChirpApiSystem.cs
using Colossal;
using Colossal.Localization;
using Colossal.Logging;
using CustomChirps.Utils; // I18NBridge + RuntimeLocalizationWindow
using Game;
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
        private readonly ConcurrentDictionary<string, string> _vanillaDict = new ConcurrentDictionary<string, string>();

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

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;

            // Decide backend: i18n wins; vanilla is fallback
            var shared = I18NBridge.GetDictionary();
            if (shared != null)
            {
                _i18nDict = shared;
                _i18nEverywhereAvailable = true;
                _useVanillaProxy = false; // disable vanilla proxy entirely when i18n is present
                _log?.Info("[CustomChirps] Connected to I18NEverywhere runtime dictionary.");
            }
            else
            {
                _i18nDict = new Dictionary<string, string>(StringComparer.Ordinal);
                _i18nEverywhereAvailable = false;
                _useVanillaProxy = true; // fallback to vanilla proxy only if i18n is missing
                _log?.Warn("[CustomChirps] I18NEverywhere not found. Using private runtime dictionary.");
            }

            _i18nWindow = new RuntimeLocalizationWindow(_i18nDict);

            // Install vanilla localization proxy ONLY when we're actually using it
            if (_useVanillaProxy)
            {
                try
                {
                    _vanillaDict.Clear();
                    _locMgr = GameManager.instance.localizationManager;
                    _vanillaSource = new RuntimeLocaleSourceProxy(_vanillaDict);

                    // IMPORTANT: register under the CURRENT ACTIVE LOCALE, not a hard-coded "en-US"
                    var activeLocale = _locMgr.activeLocaleId;
                    if (string.IsNullOrWhiteSpace(activeLocale))
                        activeLocale = "en-US"; // conservative default

                    _locMgr.AddSource(activeLocale, _vanillaSource);
                    _log?.Info($"[CustomChirps] Vanilla localization proxy installed for '{activeLocale}'.");
                }
                catch (Exception ex)
                {
                    _useVanillaProxy = false; // hard-disable if setup fails
                    _log?.Error($"[CustomChirps] Failed to set up vanilla localization proxy: {ex}");
                }
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

            // Ensure there is a link placeholder if a target exists (matches your previous behavior)
            string finalText = req.Text ?? string.Empty;
            if (req.Target != Entity.Null && finalText.IndexOf("{LINK_", StringComparison.Ordinal) < 0)
                finalText += " {LINK_1}";

            // Unique runtime key (GUID avoids collisions better than ticks)
            var key = $"customchirps:{Guid.NewGuid():N}";
            var value = string.IsNullOrWhiteSpace(finalText) ? "(empty)" : finalText;

            // Write to exactly one backend
            if (_i18nEverywhereAvailable)
            {
                // i18n path: no vanilla writes, no reloads
                _i18nWindow.AddWithWindowManagement(key, value);
                _log?.Debug($"[CustomChirps] (i18n) Added {key}");
            }
            else if (_useVanillaProxy)
            {
                // VANILLA path: add to proxy dict AND refresh the active locale so the UI can resolve it
                _vanillaDict[key] = value;

                try
                {
                    _locMgr?.ReloadActiveLocale(); // <-- critical for vanilla visibility
                }
                catch (Exception ex)
                {
                    _log?.Warn($"[CustomChirps] ReloadActiveLocale failed: {ex}");
                }

                _log?.Debug($"[CustomChirps] (vanilla) Added {key}");
            }
            else
            {
                // Last-resort fallback
                _i18nDict[key] = value;
                _log?.Warn($"[CustomChirps] No localization backend active; stored {key} in private dict.");
            }

            // Build payload
            var payload = new ChirpPayload
            {
                Key = new Unity.Collections.FixedString512Bytes(key),
                Sender = senderAccount,
                Target = req.Target,
                OverrideSenderName = new Unity.Collections.FixedString128Bytes(req.CustomSenderName ?? "")
            };

            // Defer to runtime bus + vanilla queue
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

            var targetLabel = req.Target == Entity.Null ? "None" : req.Target.ToString();
            _log.Info($"[CustomChirps] Queued chirp (dept={req.Dept}, sender={senderAccount}, target={targetLabel}).");
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
        private sealed class RuntimeLocaleSourceProxy : IDictionarySource
        {
            private readonly ConcurrentDictionary<string, string> _src;

            public RuntimeLocaleSourceProxy(ConcurrentDictionary<string, string> src)
            {
                _src = src ?? throw new ArgumentNullException(nameof(src));
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(
                IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts)
            {
                // Snapshot the dictionary at call time.
                return _src;
            }

            public void Unload() { }

            public override string ToString() => "CustomChirps.RuntimeLocaleProxy";
        }
    }
}

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
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Systems
{
    static class PrefabNameUtil
    {
        public static string TryGetPrefabName(World world, Entity prefabEntity)
        {
            if (world == null || prefabEntity == Entity.Null) return "(null)";
            var em = world.EntityManager;
            if (!em.HasComponent<PrefabData>(prefabEntity)) return "(no PrefabData)";

            var pd = em.GetComponentData<PrefabData>(prefabEntity);
            var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (prefabSystem.TryGetPrefab<PrefabBase>(pd, out var prefab))
                return prefab?.name ?? "(unnamed)";
            return "(not in PrefabSystem)";
        }
    }
   

    /// <summary>
    /// API for posting runtime free-text chirps via a localization key.
    /// Register in Mod.OnLoad:
    ///   updateSystem.UpdateAt<CustomChirpApiSystem>(SystemUpdatePhase.GameSimulation);
    /// </summary>
    public sealed partial class CustomChirpApiSystem : SystemBase
    {
        private static CustomChirpApiSystem _instance;
        private static readonly ILog _log = LogManager.GetLogger("CustomChirps.Api");

        // Optional fixed prefabs (can be configured), else we auto-discover
        private Entity _senderAccountEntity;
        private Entity _chirpPrefabEntity;

        // Fallbacks discovered from the live world
        private Entity _fallbackSender;
        private Entity _fallbackChirpPf;

        private bool _didInit;

        // Runtime localization
        private LocalizationManager _locMgr;
        private RuntimeLocaleSource _runtimeLocale;

        // --- Name overrides (used by Localization patch or UI Postfix) ---
        private static readonly ConcurrentDictionary<Entity, string> _nameOverrides = new();

        private static readonly Dictionary<Entity, string> _senderNameOverrides = new();
        // ...
        public bool TryGetSenderNameOverride(Entity senderEntity, out string text)
            => _senderNameOverrides.TryGetValue(senderEntity, out text);

        public static void SetSenderNameOverride(Entity senderEntity, string displayName)
        {
            if (senderEntity == Entity.Null || string.IsNullOrEmpty(displayName)) return;
            _senderNameOverrides[senderEntity] = displayName;
        }

        // Call this when another mod wants custom sender text:
        public static void PostFromSender(Entity senderAccount, string text, string senderDisplayName, Entity optionalTarget = default)
        {
            if (_instance == null || senderAccount == Entity.Null || string.IsNullOrEmpty(text)) return;

            SetSenderNameOverride(senderAccount, senderDisplayName);
            _instance.EnqueueFromSender(senderAccount, text, optionalTarget);
        }


        public static void SetDisplayNameOverride(Entity senderAccount, string displayName)
        {
            if (senderAccount == Entity.Null || string.IsNullOrEmpty(displayName)) return;
            _nameOverrides[senderAccount] = displayName;
        }
        public static bool TryGetNameOverride(Entity e, out string name) => _nameOverrides.TryGetValue(e, out name);

        // Map enum -> expected prefab name (departments)
        private static readonly Dictionary<DepartmentAccount, string> s_DepartmentPrefabNames =
            new()
            {
                { DepartmentAccount.Electricity, "ElectricityChirperAccount" },
                { DepartmentAccount.FireRescue, "FireRescueChirperAccount" },
                { DepartmentAccount.Roads, "RoadChirperAccount" },
                { DepartmentAccount.Water, "WaterChirperAccount" },
                { DepartmentAccount.Communications, "CommunicationsChirperAccount" },
                { DepartmentAccount.Police, "PoliceChirperAccount" },
                { DepartmentAccount.PropertyAssessmentOffice, "PropertyAssessmentOfficeAccount" },
                { DepartmentAccount.Post, "PostChirperAccount" },
                { DepartmentAccount.BusinessNews, "BusinessNewsChirperAccount" },
                { DepartmentAccount.CensusBureau, "CensusBureauChirperAccount" },
                { DepartmentAccount.ParkAndRec, "ParkAndRecChirperAccount" },
                { DepartmentAccount.EnvironmentalProtectionAgency, "EnvironmentalProtectionAgencyChirperAccount" },
                { DepartmentAccount.Healthcare, "HealthcareChirperAccount" },
                { DepartmentAccount.LivingStandardsAssociation, "LivingStandardsAssociationChirperAccount" },
                { DepartmentAccount.Garbage, "GarbageChirperAccount" },
                { DepartmentAccount.TourismBoard, "TourismBoardChirperAccount" },
                { DepartmentAccount.Transportation, "TransportationChirperAccount" },
                { DepartmentAccount.Education, "EducationChirperAccount" },
            };

        // ---------------- Public API ----------------

        /// <summary>Optional: pin explicit prefabs (must be PrefabBase assets).</summary>
        public void ConfigurePrefabs(PrefabBase senderAccountPrefab, PrefabBase chirpPrefab)
        {
            if (senderAccountPrefab == null || chirpPrefab == null)
            {
                _log.Warn("[CustomChirps] ConfigurePrefabs called with null assets.");
                return;
            }

            _senderAccountEntity = Util.PrefabLookup.GetPrefabEntity(World, senderAccountPrefab);
            _chirpPrefabEntity = Util.PrefabLookup.GetPrefabEntity(World, chirpPrefab);

            _log.Info($"[CustomChirps] Prefabs configured: sender={_senderAccountEntity}, chirpPf={_chirpPrefabEntity}");
        }

        /// <summary>Post a runtime free-text chirp. Target is optional.</summary>
        public static void Post(string text, Entity optionalTarget = default) => _instance?.Enqueue(text, optionalTarget);

        /// <summary>Post from a specific sender account entity.</summary>
        public static void PostFromSender(Entity senderAccount, string text, Entity optionalTarget = default)
            => _instance?.EnqueueFromSender(senderAccount, text, optionalTarget);

        /// <summary>Post a runtime free-text chirp from a chosen department.</summary>
        public static void PostFromDepartment(DepartmentAccount dept, string text, Entity optionalTarget = default)
        {
            var sender = ResolveDepartment(dept);
            if (sender == Entity.Null)
            {
                LogInfo($"[CustomChirps] Department '{dept}' prefab not found in this save. Skipping post.");
                return;
            }
            PostFromSender(sender, text, optionalTarget);
        }

        /// <summary>Resolve a department account prefab present in the loaded save.</summary>
        public static Entity ResolveDepartment(DepartmentAccount dept)
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

        // ---- “Custom sender name” helpers ----

        public static Entity ResolveAnyAccount(string preferredIconHint = "")
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return Entity.Null;
            var em = world.EntityManager;

            using var accounts = em.CreateEntityQuery(typeof(ChirperAccountData))
                                   .ToEntityArray(Allocator.Temp);
            if (accounts.Length == 0) return Entity.Null;

            if (string.IsNullOrWhiteSpace(preferredIconHint))
                return accounts[0];

            var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            foreach (var acc in accounts)
            {
                if (!em.HasComponent<PrefabData>(acc)) continue;
                var pd = em.GetComponentData<PrefabData>(acc);
                if (prefabSystem.TryGetPrefab<PrefabBase>(pd, out var prefab) && prefab != null)
                {
                    var n = prefab.name ?? "";
                    if (n.IndexOf(preferredIconHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        return acc;
                }
            }
            return accounts[0];
        }

        public static Entity EnsureCustomSender(string displayName, string iconHint = "")
        {
            var sender = ResolveAnyAccount(iconHint);
            if (sender == Entity.Null) return Entity.Null;
            SetDisplayNameOverride(sender, displayName);
            return sender;
        }

        public static void PostAsCustomSender(string displayName, Entity brandPrefab, string text, Entity optionalTarget = default, string iconHint = "")
        {
            var sender = EnsureCustomSender(displayName, iconHint);
            if (sender == Entity.Null)
            {
                _log.Warn("[CustomChirps] No ChirperAccount available to host custom sender name.");
                return;
            }
            PostViaBrand(brandPrefab, sender, text, optionalTarget);
        }

        // ---- Brand helpers ----

        public static Entity ResolveBrandChirpPrefab(string nameContains)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) { _log.Warn("[CustomChirps] ResolveBrand: no world"); return Entity.Null; }

            var em = world.EntityManager;

            using var brands = em.CreateEntityQuery(ComponentType.ReadOnly<BrandChirpData>())
                                 .ToEntityArray(Allocator.Temp);
            if (brands.Length == 0) { _log.Warn("[CustomChirps] ResolveBrand: no BrandChirpData in this save"); return Entity.Null; }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                for (int i = 0; i < brands.Length; i++)
                {
                    var pf = brands[i];
                    var name = PrefabNameUtil.TryGetPrefabName(world, pf);
                    if (name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _log.Info($"[CustomChirps] ResolveBrand: picked '{name}'");
                        return pf;
                    }
                }
                _log.Warn($"[CustomChirps] ResolveBrand: no brand matched '{nameContains}', using first available");
            }

            var fallbackName = PrefabNameUtil.TryGetPrefabName(world, brands[0]);
            _log.Info($"[CustomChirps] ResolveBrand: picked '{fallbackName}' (fallback)");
            return brands[0];
        }

        public static void PostViaBrand(Entity brandPrefab, Entity senderAccount, string text, Entity optionalTarget = default)
        {
            var inst = _instance;
            if (inst == null) { _log.Warn("[CustomChirps] PostViaBrand: API not ready"); return; }

            var world = inst.World;
            var brandName = PrefabNameUtil.TryGetPrefabName(world, brandPrefab);
            var senderName = PrefabNameUtil.TryGetPrefabName(world, senderAccount);

            if (brandPrefab == Entity.Null || !inst.EntityManager.HasComponent<BrandChirpData>(brandPrefab))
            {
                _log.Warn($"[CustomChirps] PostViaBrand: brandPrefab invalid. Got '{brandName}'");
                return;
            }
            if (senderAccount == Entity.Null || !inst.EntityManager.HasComponent<ChirperAccountData>(senderAccount))
            {
                _log.Warn($"[CustomChirps] PostViaBrand: senderAccount invalid. Got '{senderName}'");
                return;
            }

            inst.EnqueueBrand(senderAccount, brandPrefab, text ?? "(empty)", optionalTarget, senderDisplayName: null);
            _log.Info($"[CustomChirps] PostViaBrand: queued. brand='{brandName}', sender='{senderName}', target={(optionalTarget == Entity.Null ? "None" : optionalTarget.ToString())}");
        }

        public static void PostViaBrandWithCustomSenderName(Entity brandPrefab, Entity senderAccount, string senderName, string text, Entity optionalTarget = default)
        {
            var inst = _instance;
            if (inst == null) return;
            inst.EnqueueBrand(senderAccount, brandPrefab, text, optionalTarget, senderName);
        }

        // ---------------- Internals ----------------

        protected override void OnCreate()
        {
            base.OnCreate();
            _instance = this;

            // Hook localization
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

            if (_senderAccountEntity == Entity.Null || _chirpPrefabEntity == Entity.Null)
                AutoDiscoverPrefabs();

            var (s, p) = GetActivePrefabs();
            _log.Info($"[CustomChirps] Api init — sender={(s != Entity.Null)}, chirpPf={(p != Entity.Null)}");
            _didInit = true;
        }

        private (Entity sender, Entity chirpPf) GetActivePrefabs()
        {
            if (_senderAccountEntity != Entity.Null && _chirpPrefabEntity != Entity.Null)
                return (_senderAccountEntity, _chirpPrefabEntity);
            return (_fallbackSender, _fallbackChirpPf);
        }

        private void AutoDiscoverPrefabs()
        {
            var em = World.EntityManager;

            try
            {
                using var accounts = em.CreateEntityQuery(ComponentType.ReadOnly<ChirperAccountData>())
                                       .ToEntityArray(Allocator.Temp);
                if (accounts.Length > 0) _fallbackSender = accounts[0];
            }
            catch { }

            try
            {
                using var chirpPfs = em.CreateEntityQuery(ComponentType.ReadOnly<ChirpData>())
                                       .ToEntityArray(Allocator.Temp);
                if (chirpPfs.Length > 0) _fallbackChirpPf = chirpPfs[0];
            }
            catch { }
        }

        // General enqueue using auto-discovered sender/chirp
        private void Enqueue(string text, Entity target)
        {
            EnsureInit();

            var (sender, chirpPf) = GetActivePrefabs();
            if (sender == Entity.Null || chirpPf == Entity.Null)
            {
                _log.Warn("[CustomChirps] No sender/chirp prefab available. Load a city/save first.");
                return;
            }

            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";
            _runtimeLocale?.AddOrUpdate(key, string.IsNullOrEmpty(text) ? "(empty)" : text);
            try { _locMgr?.ReloadActiveLocale(); } catch { }

            var payload = new ChirpPayload
            {
                Key = key,
                Sender = sender,
                Target = target,
                OverrideSenderName = default // none
            };
            RuntimeChirpTextBus.EnqueuePending(chirpPf, payload);

            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);
            queue.Enqueue(new ChirpCreationData
            {
                m_TriggerPrefab = chirpPf,
                m_Sender = sender,
                m_Target = target
            });
            create.AddQueueWriter(deps);

            _log.Info("[CustomChirps] Queued chirp with runtime key.");
        }

        // Enqueue using provided sender
        private void EnqueueFromSender(Entity senderAccount, string text, Entity target)
        {
            EnsureInit();

            var (_, chirpPf) = GetActivePrefabs();
            if (chirpPf == Entity.Null)
            {
                _log.Warn("[CustomChirps] No chirp prefab available. Load a city/save first.");
                return;
            }

            if (senderAccount == Entity.Null || !World.EntityManager.HasComponent<ChirperAccountData>(senderAccount))
            {
                var (fallbackSender, _) = GetActivePrefabs();
                senderAccount = fallbackSender;
            }
            if (senderAccount == Entity.Null)
            {
                _log.Warn("[CustomChirps] No valid sender account available.");
                return;
            }

            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";
            _runtimeLocale?.AddOrUpdate(key, string.IsNullOrEmpty(text) ? "(empty)" : text);
            try { _locMgr?.ReloadActiveLocale(); } catch { }

            var payload = new ChirpPayload
            {
                Key = key,
                Sender = senderAccount,
                Target = target,
                OverrideSenderName = default
            };
            RuntimeChirpTextBus.EnqueuePending(chirpPf, payload);

            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);
            queue.Enqueue(new ChirpCreationData
            {
                m_TriggerPrefab = chirpPf,
                m_Sender = senderAccount,
                m_Target = target
            });
            create.AddQueueWriter(deps);

            _log.Info("[CustomChirps] Queued chirp with runtime key (sender-forced).");
        }

        // Brand variant; supports custom sender display name
        private void EnqueueBrand(Entity senderAccount, Entity brandChirpPrefab, string text, Entity target, string senderDisplayName)
        {
            EnsureInit();

            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";
            _runtimeLocale?.AddOrUpdate(key, string.IsNullOrEmpty(text) ? "(empty)" : text);
            try { _locMgr?.ReloadActiveLocale(); } catch { }

            var payload = new ChirpPayload
            {
                Key = key,
                Sender = senderAccount,
                Target = target,
                OverrideSenderName = senderDisplayName ?? default
            };
            RuntimeChirpTextBus.EnqueuePending(brandChirpPrefab, payload);

            var create = World.GetExistingSystemManaged<CreateChirpSystem>();
            var queue = create.GetQueue(out var deps);
            queue.Enqueue(new ChirpCreationData
            {
                m_TriggerPrefab = brandChirpPrefab,
                m_Sender = senderAccount,
                m_Target = target
            });
            create.AddQueueWriter(deps);

            _log.Info($"[CustomChirps] Queued brand chirp with runtime key (name='{senderDisplayName ?? "(vanilla)"}').");
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

        public static void LogInfo(string msg) => LogManager.GetLogger("CustomChirps.Api").Info(msg);
    }
}

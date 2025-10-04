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
        public static void Post(string text, Entity optionalTarget = default)
        {
            _instance?.Enqueue(text, optionalTarget);
        }

        // -------------- Internals -------------------

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
                using var accounts = em.CreateEntityQuery(
                        ComponentType.ReadOnly<ChirperAccountData>())
                    .ToEntityArray(Allocator.Temp);
                if (accounts.Length > 0) _fallbackSender = accounts[0];
            }
            catch { }

            try
            {
                using var chirpPfs = em.CreateEntityQuery(
                        ComponentType.ReadOnly<ChirpData>())
                    .ToEntityArray(Allocator.Temp);
                if (chirpPfs.Length > 0) _fallbackChirpPf = chirpPfs[0];
            }
            catch { }
        }

        private void Enqueue(string text, Entity target)
        {
            EnsureInit();

            var (sender, chirpPf) = GetActivePrefabs();
            if (sender == Entity.Null || chirpPf == Entity.Null)
            {
                _log.Warn("[CustomChirps] No sender/chirp prefab available. Load a city/save first.");
                return;
            }

            if (string.IsNullOrEmpty(text))
                text = "(empty)";

            // Generate a unique localization key; the UI patch will return this key.
            var key = $"customchirps:{DateTime.UtcNow.Ticks:x}";

            // Register key → text in our runtime locale
            if (_runtimeLocale != null && _locMgr != null)
            {
                _runtimeLocale.AddOrUpdate(key, text);
                try { _locMgr.ReloadActiveLocale(); } catch { /* non-fatal */ }
            }

            // Tell spawner/UI (via the shared bus) that the next chirp from this prefab
            // should carry this key as ModChirpText.Key.
            RuntimeChirpTextBus.RegisterPending(chirpPf, key);

            // Enqueue the chirp the vanilla way
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

        // --------------- Runtime locale source ----------------

        private sealed class RuntimeLocaleSource : IDictionarySource
        {
            private readonly ConcurrentDictionary<string, string> _entries
                = new ConcurrentDictionary<string, string>();

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

// CustomChirps/UI/ChirperTextPatchInstaller.cs
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Entities;
using CustomChirps.Components;

namespace CustomChirps.UI
{
    public static class ChirperTextPatchInstaller
    {
        private static World _world;
        public static void SetWorld(World world) => _world = world;

        // runtime map: fakeKey -> final text
        internal static readonly ConcurrentDictionary<string, string> RuntimeTextByKey = new();

        private const string KeyPrefix = "customchirps:";

        public static void Install(Harmony harmony, Colossal.Logging.ILog log)
        {
            try
            {
                var uiType = AccessTools.TypeByName("Game.UI.InGame.ChirperUISystem");
                if (uiType == null)
                {
                    log.Warn("[CustomChirps] UI type not found: ChirperUISystem");
                    return;
                }

                // We’re on the ID pipeline: string GetMessageID(Entity)
                var getMessageId = uiType.GetMethod("GetMessageID",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null, types: new[] { typeof(Entity) }, modifiers: null);

                if (getMessageId == null || getMessageId.ReturnType != typeof(string))
                {
                    log.Warn("[CustomChirps] GetMessageID(Entity) not found; cannot inject text.");
                    return;
                }

                var idPrefix = new HarmonyMethod(typeof(ChirperTextPrefixes).GetMethod(
                    nameof(ChirperTextPrefixes.PrefixForIdWithCustomKey),
                    BindingFlags.Public | BindingFlags.Static));
                harmony.Patch(getMessageId, prefix: idPrefix);
                log.Info($"[CustomChirps] UI patch installed (ID path): {getMessageId.DeclaringType?.FullName}.{getMessageId.Name}(Entity)");

                // Patch localization lookups broadly so our key always resolves
                InstallLocalizationHooks(harmony, log);
            }
            catch (Exception ex)
            {
                log.Error($"[CustomChirps] Failed to install UI patch: {ex}");
            }
        }

        private static void InstallLocalizationHooks(Harmony harmony, Colossal.Logging.ILog log)
        {
            int installed = 0, skipped = 0;

            void PatchIfMatch(MethodInfo mi)
            {
                // We only care about methods that return string and have first parameter string "key".
                var ps = mi.GetParameters();
                if (mi.ReturnType != typeof(string) || ps.Length == 0 || ps[0].ParameterType != typeof(string))
                {
                    skipped++;
                    return;
                }

                try
                {
                    var pre = new HarmonyMethod(typeof(LocalizationHook).GetMethod(
                        nameof(LocalizationHook.Prefix),
                        BindingFlags.Public | BindingFlags.Static));
                    harmony.Patch(mi, prefix: pre);
                    installed++;
                }
                catch
                {
                    skipped++;
                }
            }

            try
            {
                // 1) Instance methods on the active localization manager
                var manager = Game.SceneFlow.GameManager.instance.localizationManager;
                var lmType = manager?.GetType();
                if (lmType != null)
                {
                    var cand = lmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     .Where(m => !m.IsSpecialName); // skip property getters
                    foreach (var mi in cand) PatchIfMatch(mi);
                }
                else
                {
                    log.Warn("[CustomChirps] localizationManager is null; will try static helpers only.");
                }

                // 2) Common static helper classes (try a handful of likely namespaces)
                string[] staticTypes =
                {
                    "Colossal.Localization.Localizer",
                    "Colossal.Localization.LocalizationManager",
                    "Game.UI.InGame.ChirperLocalization",
                    "Game.UI.LocalizationUtils",
                };

                foreach (var tn in staticTypes)
                {
                    var t = AccessTools.TypeByName(tn);
                    if (t == null) continue;

                    var cand = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                .Where(m => !m.IsSpecialName);
                    foreach (var mi in cand) PatchIfMatch(mi);
                }

                log.Info($"[CustomChirps] Localization hooks installed: {installed} patched, {skipped} skipped (non-matching).");
            }
            catch (Exception ex)
            {
                log.Error($"[CustomChirps] Failed while installing localization hooks: {ex}");
            }
        }

        public static class ChirperTextPrefixes
        {
            // ID path: emit a fake key and store mapping so the localization hook can return free text
            public static bool PrefixForIdWithCustomKey(ref string __result, Entity chirp)
            {
                try
                {
                    if (_world == null || chirp == Entity.Null) return true;
                    var em = _world.EntityManager;
                    if (!em.Exists(chirp)) return true;

                    if (em.HasComponent<ModChirpText>(chirp))
                    {
                        var text = em.GetComponentData<ModChirpText>(chirp).Key.ToString();
                        if (em.HasComponent<Game.Triggers.Chirp>(chirp))
                        {
                            var c = em.GetComponentData<Game.Triggers.Chirp>(chirp);
                            if (c.m_Sender != Entity.Null && em.HasComponent<ModSenderTag>(c.m_Sender))
                                text = "[Other Mod] " + text;
                        }

                        // Unique & stable per entity version
                        var key = KeyPrefix + chirp.Index + ":" + chirp.Version;
                        RuntimeTextByKey[key] = text;

                        __result = key;
                        return false; // skip vanilla ID
                    }

                    return true; // no custom text → let vanilla return its normal key
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[CustomChirps] UI prefix (id) failed: {ex}");
                    return true;
                }
            }
        }

        public static class LocalizationHook
        {
            // Intercept localization key lookups; if it’s our key, return our text
            public static bool Prefix(ref string __result, string __0 /* key */)
            {
                try
                {
                    if (!string.IsNullOrEmpty(__0) &&
                        __0.StartsWith(KeyPrefix, StringComparison.Ordinal) &&
                        RuntimeTextByKey.TryGetValue(__0, out var text))
                    {
                        __result = text;
                        return false; // we handled it
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[CustomChirps] Localization hook failed on key='{__0}': {ex}");
                }

                return true; // let vanilla handle other keys
            }
        }
    }
}

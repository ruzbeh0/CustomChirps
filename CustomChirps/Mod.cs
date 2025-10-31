// Mod.cs  (only the middle changed; rest is your file)
using Colossal.IO.AssetDatabase;
using Colossal.Logging;

using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Triggers;
using Game.UI.InGame;
using HarmonyLib;

using System;
using System.Linq;
using System.Reflection;

using Unity.Entities;

namespace CustomChirps
{
    public class Mod : IMod
    {
        public static readonly string harmonyID = "CustomChirps";
        public static ILog log = LogManager.GetLogger($"{nameof(CustomChirps)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        // Provide these later when your assets load:
        public static PrefabBase OtherModChirperAccountPrefab;
        public static PrefabBase ChirpPrefab;
        public static int VanillaKeepPercent = 0;
        public static Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            AssetDatabase.global.LoadSettings(nameof(CustomChirps), m_Setting, new Setting(this));

            // Make our ECS systems run during the game simulation loop
            // 2) Your spawner stamps ModChirpText (and other markers) on matching chirps
            updateSystem.UpdateAfter<CustomChirps.Systems.CustomChirpSpawnerSystem,
                                     Game.Triggers.CreateChirpSystem>(SystemUpdatePhase.GameSimulation);

            updateSystem.UpdateAt<Systems.CustomChirpApiSystem>(SystemUpdatePhase.GameSimulation);


            var harmony = new Harmony(harmonyID);
            // Harmony.DEBUG = true;
            harmony.PatchAll(typeof(Mod).Assembly);


            var patchedMethods = harmony.GetPatchedMethods().ToArray();
            log.Info($"Plugin {harmonyID} made patches! Patched methods: " + patchedMethods.Length);
            foreach (var patchedMethod in patchedMethods)
                log.Info($"Patched: {patchedMethod.DeclaringType?.FullName}.{patchedMethod.Name}");

        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}

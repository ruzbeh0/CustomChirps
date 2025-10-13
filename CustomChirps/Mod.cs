using Colossal.Logging;

using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Triggers;

using HarmonyLib;

using System.Linq;

namespace CustomChirps
{
    public class Mod : IMod
    {
        public static readonly string harmonyID = "CustomChirps";
        public static ILog log = LogManager.GetLogger($"{nameof(CustomChirps)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        // Provide these later when your assets load:
        public static PrefabBase OtherModChirperAccountPrefab;
        public static PrefabBase ChirpPrefab;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            // Make our ECS systems run during the game simulation loop
            updateSystem.UpdateAt<Systems.CustomChirpApiSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<Systems.CustomChirpSpawnerSystem, CreateChirpSystem>(SystemUpdatePhase.ModificationEnd);
            //updateSystem.UpdateAt<CustomChirps.Systems.CustomChirpTestSystem>(SystemUpdatePhase.GameSimulation);


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
        }
    }
}

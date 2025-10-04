using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;

namespace CustomChirps.Util
{
    public static class PrefabLookup
    {
        // Pass the actual prefab asset (PrefabBase), not a Type.
        public static Entity GetPrefabEntity(World world, PrefabBase prefabAsset)
        {
            var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            return prefabSystem.GetEntity(prefabAsset); // same pattern the game uses. :contentReference[oaicite:1]{index=1}
        }
    }
}

using CustomChirps.Components;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;

namespace CustomChirps.Prefabs
{
    // A Chirper account with a visible display name and a tag that it's “another mod”.
    public class OtherModChirperAccount : ChirperAccount
    {
        // You can set an icon in the prefab asset (e.g., in your asset bundle).
        // For code-only sample we keep it lean.

        public override void GetPrefabComponents(HashSet<ComponentType> components)
        {
            base.GetPrefabComponents(components); // adds ChirperAccountData already  
            components.Add(ComponentType.ReadWrite<ModSenderTag>());
        }
    }
}

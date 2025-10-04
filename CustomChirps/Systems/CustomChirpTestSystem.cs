using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

namespace CustomChirps.Systems
{
    public partial class CustomChirpTestSystem : SystemBase
    {
        private float _timer;

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(typeof(Game.Triggers.Chirp))); // run in-game only
        }

        protected override void OnUpdate()
        {
            var api = World.GetExistingSystemManaged<CustomChirps.Systems.CustomChirpApiSystem>();
            if (api == null) return;

            // Light readiness check: do we have ANY Chirper accounts and chirp prefabs around?
            var em = World.EntityManager;
            bool hasSender = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.ChirperAccountData>())
                              .CalculateEntityCount() > 0;
            bool hasChirp = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.ChirpData>())
                              .CalculateEntityCount() > 0;
            if (!hasSender || !hasChirp) return;

            _timer += SystemAPI.Time.DeltaTime; // Updated to use SystemAPI.Time
            if (_timer >= 15f)
            {
                _timer = 0f;
                CustomChirps.Systems.CustomChirpApiSystem.Post($"Hello World! Time: {System.DateTime.Now:T} 🐦");
            }
        }

    }
}

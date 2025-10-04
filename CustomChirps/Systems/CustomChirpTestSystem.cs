using System;
using Unity.Collections;
using Unity.Entities;
using Game.Buildings;
using Game.Prefabs;

namespace CustomChirps.Systems
{
    public partial class CustomChirpTestSystem : SystemBase
    {
        private Entity _brandPf = Entity.Null;
        private Entity _sender = Entity.Null;
        private Entity _target = Entity.Null;
        private float _timer;

        private const string BrandNameHint = "News"; // change to match a BrandChirp prefab in your playset (e.g., "BusinessNews")

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(typeof(Game.Triggers.Chirp)));
        }

        // Systems/CustomChirpTestSystem.cs (OnStartRunning or after a short delay in OnUpdate)
        protected override void OnStartRunning()
        {
            var api = World.GetExistingSystemManaged<CustomChirpApiSystem>();
            var em = World.EntityManager;

            // pick any existing account as the technical sender (icon & infoview still come from it)
            Entity sender = Entity.Null;
            using (var accounts = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.ChirperAccountData>())
                                    .ToEntityArray(Unity.Collections.Allocator.Temp))
                if (accounts.Length > 0) sender = accounts[0];

            if (sender != Entity.Null)
            {
                // show custom *text* as sender
                CustomChirpApiSystem.PostFromSender(
                    senderAccount: sender,
                    text: "Custom msg – sent with a custom sender text 🎯",
                    senderDisplayName: "Realistic Trips Mod"
                );
            }
        }


        protected override void OnUpdate()
        {
            if (_brandPf == Entity.Null) return;

            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < 8f) return;
            _timer = 0f;

            // Pick/keep any account entity for the icon (or resolve one by hint)
            if (_sender == Entity.Null)
                _sender = CustomChirpApiSystem.ResolveAnyAccount("Education");
            // change the hint for a different avatar, or ""

            // Use your *custom* sender text here:
            const string CustomSender = "Realistic Trips Mod";

            CustomChirpApiSystem.PostViaBrandWithCustomSenderName(
                brandPrefab: _brandPf,
                senderAccount: _sender,
                senderName: CustomSender,
                text: $"[Brand Demo] {System.DateTime.Now:T} — custom sender text & stable target",
                optionalTarget: _target
            );
        }



    }
}

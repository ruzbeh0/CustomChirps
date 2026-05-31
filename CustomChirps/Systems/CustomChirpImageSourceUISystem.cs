using Colossal.UI.Binding;
using Game;
using Game.UI;

namespace CustomChirps.Systems
{
    public partial class CustomChirpImageSourceUISystem : UISystemBase
    {
        private const string Group = "customChirps";
        private RawValueBinding _imageSourcesBinding;
        private int _lastVersion = -1;

        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();
            AddBinding(_imageSourcesBinding = new RawValueBinding(
                Group,
                "imageSources",
                CustomChirpImageSourceRegistry.WriteSources));
        }

        protected override void OnUpdate()
        {
            int version = CustomChirpImageSourceRegistry.Version;
            if (version == _lastVersion)
                return;

            _lastVersion = version;
            _imageSourcesBinding.Update();
        }
    }
}

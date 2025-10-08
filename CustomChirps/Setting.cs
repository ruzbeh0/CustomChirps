using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

using CustomChirps.Systems; // for CustomChirpApiSystem

namespace CustomChirps
{
    /// <summary>
    /// Single-option settings: controls what percentage of vanilla chirps are allowed to show.
    /// Default = 30%.
    /// </summary>
    [FileLocation(nameof(CustomChirps))]
    [SettingsUIGroupOrder(kMainGroup)]
    [SettingsUIShowGroupName(kMainGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kMainGroup = "General";

        public Setting(IMod mod) : base(mod) { }

        /// <summary>
        /// 0..100 — percent of vanilla chirps that will be shown (the rest are dropped in the spawner).
        /// We keep this property *authoritative* and mirror to the runtime knob whenever it changes.
        /// </summary>
        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kMainGroup)]
        public int VanillaVisibilityPercent
        {
            get => _vanillaVisibilityPercent;
            set
            {
                // clamp defensively
                var v = value < 0 ? 0 : (value > 100 ? 100 : value);
                _vanillaVisibilityPercent = v;

                Mod.log?.Info($"[CustomChirps] VanillaVisibilityPercent set to {v}%");
            }
        }
        private int _vanillaVisibilityPercent = 30; // UI default

        public override void SetDefaults()
        {
            // apply default & sync to runtime
            VanillaVisibilityPercent = 30;
        }
    }

    /// <summary>
    /// Minimal English locale for the single option above.
    /// </summary>
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // Settings header & tab
                { m_Setting.GetSettingsLocaleID(),                        "CustomChirps" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection),       "Main" },

                // Group
                { m_Setting.GetOptionGroupLocaleID(Setting.kMainGroup),   "General" },

                // Single slider label/description
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VanillaVisibilityPercent)),
                    "Vanilla chirps shown (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VanillaVisibilityPercent)),
                    "Controls what fraction of vanilla (non-custom) Chirper messages are allowed to appear. "
                  + "0% hides all vanilla chirps; 100% shows all of them. Custom chirps are unaffected." },
            };
        }

        public void Unload() { }
    }
}

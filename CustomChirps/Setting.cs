using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;
using System.Net.Configuration;

namespace CustomChirps
{
    [FileLocation($"ModsSettings\\{nameof(CustomChirps)}\\{nameof(CustomChirps)}")]
    [SettingsUIGroupOrder(SettingsGroup)]
    //[SettingsUIShowGroupName(SettingsGroup, kToggleGroup, kSliderGroup, kDropdownGroup)]
    public class Setting : ModSetting
    {
        public const string SettingsSection = "SettingsSection";
        public const string SettingsGroup = "SettingsGroup";

        public Setting(IMod mod) : base(mod)
        {
            if (!disable_vanilla_chirps) SetDefaults();
        }

        public override void SetDefaults()
        {
            disable_vanilla_chirps = false;
        }

        [SettingsUISection(SettingsSection, SettingsGroup)]
        public bool disable_vanilla_chirps { get; set; }

    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // Mod / Tab
                { m_Setting.GetSettingsLocaleID(), "Custom Chirps" },
                { m_Setting.GetOptionTabLocaleID(Setting.SettingsSection), "Settings" },

                // Debug
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.disable_vanilla_chirps)), "Disable Vanilla Chirps" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.disable_vanilla_chirps)),  "Disables vanillas chirps. Chirps from other mods will still be shown" },


            };
        }

        public void Unload()
        {

        }
    }
}

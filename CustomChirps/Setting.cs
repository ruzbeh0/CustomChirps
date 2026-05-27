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
            SetDefaults();
        }

        public override void SetDefaults()
        {
            disable_vanilla_chirps = false;
            hide_vanilla_chirps_in_chirper_panel = false;
        }

        [SettingsUISection(SettingsSection, SettingsGroup)]
        public bool disable_vanilla_chirps { get; set; }

        [SettingsUISection(SettingsSection, SettingsGroup)]
        public bool hide_vanilla_chirps_in_chirper_panel { get; set; }

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
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.disable_vanilla_chirps)),  "Disables vanilla chirp pop-ups. Chirps from other mods will still be shown" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.hide_vanilla_chirps_in_chirper_panel)), "Hide Vanilla Chirps In Chirper Panel" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.hide_vanilla_chirps_in_chirper_panel)),  "Shows only custom chirps when opening the chirper panel" },


            };
        }

        public void Unload()
        {

        }
    }
}

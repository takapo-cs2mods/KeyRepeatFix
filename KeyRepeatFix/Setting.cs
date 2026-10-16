// Key Repeat Fix v0.1: オプション画面。今はバージョンとライセンスだけ(入れるだけで直る MOD なので、設定項目は置かない)。
// ON/OFF を設定に持っていくかは、テストの結果を見て決める(2026-10-16 たかし)。
// 出典: CS2 公式 Modding(ModSetting, SettingsUI 属性) https://cs2.paradoxwikis.com/Modding
namespace KeyRepeatFix
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Colossal;
    using Colossal.IO.AssetDatabase;
    using Game.Modding;
    using Game.SceneFlow;
    using Game.Settings;

    [FileLocation(nameof(KeyRepeatFix))]
    [SettingsUIGroupOrder(kAboutGroup)]
    [SettingsUIShowGroupName(kAboutGroup)]
    public class Setting : ModSetting
    {
        // ビルドごとに更新する(ゲーム内でどのビルドが読み込まれたか見分けるため)
        public const string kVersion = "v1.0.1";

        public const string kSection = "Main";
        public const string kAboutGroup = "About";

        public Setting(IMod mod)
            : base(mod)
        {
        }

        [SettingsUISection(kSection, kAboutGroup)]
        public string Version => kVersion;

        [SettingsUISection(kSection, kAboutGroup)]
        public string License => "GNU GPL v3.0";

        public override void SetDefaults()
        {
        }

        // 起動引数に -developerMode があるか(開発用の別窓を出すかどうか)
        public static bool IsDeveloperMode()
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                GameManager gm = GameManager.instance;
                if (gm == null)
                {
                    return false;
                }

                object config = typeof(GameManager).GetField("configuration", flags)?.GetValue(gm) ?? typeof(GameManager).GetProperty("configuration", flags)?.GetValue(gm);
                if (config == null)
                {
                    return false;
                }

                Type t = config.GetType();
                object value = t.GetField("developerMode", flags)?.GetValue(config) ?? t.GetProperty("developerMode", flags)?.GetValue(config);
                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }
    }

    // オプション画面の文字(英語・日本語)
    public class Locale : IDictionarySource
    {
        private readonly Setting m_Setting;
        private readonly bool m_Ja;

        public Locale(Setting setting, bool ja)
        {
            m_Setting = setting;
            m_Ja = ja;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            Setting s = m_Setting;
            bool ja = m_Ja;
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "Key Repeat Fix " + Setting.kVersion },
                { s.GetOptionTabLocaleID(Setting.kSection), ja ? "メイン" : "Main" },
                { s.GetOptionGroupLocaleID(Setting.kAboutGroup), ja ? "このMODについて" : "About" },
                { s.GetOptionLabelLocaleID(nameof(Setting.Version)), ja ? "バージョン" : "Version" },
                {
                    s.GetOptionDescLocaleID(nameof(Setting.Version)),
                    ja ? "入れるだけで、ゲーム内の入力欄の Backspace・矢印キーの連打や止まらない問題、IME の確定キーの問題を直します。設定はありません。ゲーム本体で直ったら外してください。"
                       : "Just enable it. Fixes runaway Backspace / arrow keys and IME confirm-key issues in in-game text fields. No settings. Remove it once the game fixes this."
                },
                { s.GetOptionLabelLocaleID(nameof(Setting.License)), ja ? "ライセンス" : "License" },
                {
                    s.GetOptionDescLocaleID(nameof(Setting.License)),
                    ja ? "Key Repeat Fix は GNU General Public License v3.0 のフリーソフトウェアです。" : "Key Repeat Fix is free software under the GNU General Public License v3.0."
                },
            };
        }

        public void Unload()
        {
        }
    }
}

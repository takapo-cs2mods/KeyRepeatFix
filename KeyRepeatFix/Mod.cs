// Key Repeat Fix v0.1: MOD の入口。キー入力の差し替え(Harmony)、オプション画面、開発用の別窓の登録。
// 出典: 公式 Mod テンプレート(Cities Skylines II mod)の Mod.cs、CS2 公式 Modding https://cs2.paradoxwikis.com/Modding
namespace KeyRepeatFix
{
    using System;
    using Colossal.IO.AssetDatabase;
    using Colossal.Logging;
    using Game;
    using Game.Modding;
    using Game.SceneFlow;

    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger(nameof(KeyRepeatFix)).SetShowsErrorsInUI(false);

        public static Setting Current;
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{nameof(OnLoad)} {Setting.kVersion}");

            try
            {
                KeyFix.Install();
            }
            catch (Exception e)
            {
                log.Info($"[ERROR] 差し替えに失敗(このMODは何もしない): {e}");
            }

            // 開発用の別窓(開発者モードのときだけ表示)
            updateSystem.UpdateAt<DevUISystem>(SystemUpdatePhase.UIUpdate);

            m_Setting = new Setting(this);
            Current = m_Setting;
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new Locale(m_Setting, false));
            GameManager.instance.localizationManager.AddSource("ja-JP", new Locale(m_Setting, true));
            AssetDatabase.global.LoadSettings(nameof(KeyRepeatFix), m_Setting, new Setting(this));
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            KeyFix.Uninstall();
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
                Current = null;
            }
        }
    }
}

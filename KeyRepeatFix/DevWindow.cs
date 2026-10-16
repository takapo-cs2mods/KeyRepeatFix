// Key Repeat Fix v0.1(v0.2 でタブをやめ、チェックリストと操作を左右に並べた): 開発用の別窓(開発者モードのときだけ)。直し方ごとの ON/OFF、状態の表示、試し打ち用の入力欄、チェックリスト。
// ・チェックリスト: Claude が書いた確認項目ファイルを読み込んで並べる。OK/NG・メモを付けて「送信」すると、結果をファイルとログに書く。
//     項目ファイル: (LocalLow)\Colossal Order\Cities Skylines II\ModsData\KeyRepeatFix\devtest\checklist.txt
//       1行目 "# タイトル"、以降 "ID<TAB>確認内容"(空行と # で始まる行は無視)
//     結果: 同じフォルダの result_<バージョン>_<日時>.json と Logs\KeyRepeatFix.log の [TEST] 行
// 出典: CS2 公式 UI Modding(ValueBinding / TriggerBinding) https://cs2.paradoxwikis.com/UI_Modding
namespace KeyRepeatFix
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Colossal.UI.Binding;
    using Game.UI;

    public partial class DevUISystem : UISystemBase
    {
        private const string kGroup = "KeyRepeatFixDev";

        public static bool Open;

        private static string s_ChecklistJson = "{\"title\":\"\",\"items\":[]}";

        public static string DevFolder => Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "KeyRepeatFix", "devtest");

        protected override void OnCreate()
        {
            base.OnCreate();

            // v1.0: 公開版(Release ビルド)では開発用の窓を出さない。開発中(Debug ビルド)は開発者モードのときだけ出す
#if DEBUG
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "DevMode", () => Setting.IsDeveloperMode()));
#else
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "DevMode", () => false));
#endif
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "Open", () => Open));
            AddBinding(new TriggerBinding(kGroup, "Toggle", () =>
            {
                Open = !Open;
                if (Open)
                {
                    LoadChecklist();
                }
            }));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Version", () => Setting.kVersion));
#if DEBUG
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "State", () => KeyFix.StateJson()));
#else
            // 公開版は窓を出さないので、毎フレームの状態づくり(文字列の組み立て)もしない
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "State", () => "{}"));
#endif

            AddBinding(new TriggerBinding<bool>(kGroup, "SetRepeat", v => Set("①リピート間隔", ref KeyFix.FixRepeat, v)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetStuck", v => Set("②押しっぱなしの掃除", ref KeyFix.FixStuck, v)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetIme", v => Set("③IMEの確定キー", ref KeyFix.FixIme, v)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetBsChar", v => Set("④BSの文字イベント", ref KeyFix.FixBsChar, v)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetDup", v => Set("⑤重複・偽の離した", ref KeyFix.FixDup, v)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetKeyLog", v => Set("キーログ", ref KeyFix.KeyLog, v)));
            AddBinding(new TriggerBinding(kGroup, "ResetCounts", () =>
            {
                KeyFix.CountRepeat = 0;
                KeyFix.CountStuck = 0;
                KeyFix.CountIme = 0;
                KeyFix.CountBsChar = 0;
                KeyFix.CountDup = 0;
                KeyFix.CountFakeUp = 0;
                KeyFix.CountPress = 0;
            }));
            AddBinding(new TriggerBinding<string>(kGroup, "Note", text => Mod.log.Info($"[NOTE] {text}")));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Checklist", () => s_ChecklistJson));
            AddBinding(new TriggerBinding(kGroup, "ReloadChecklist", () => LoadChecklist()));
            AddBinding(new TriggerBinding<string>(kGroup, "SubmitChecklist", json => Submit(json)));
        }

        private static void Set(string name, ref bool field, bool value)
        {
            field = value;
            Mod.log.Info($"[SWITCH] {name} = {(value ? "ON" : "OFF")}");
        }

        public static string Q(string v)
        {
            if (v == null)
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in v)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.Append('"').ToString();
        }

        private static void LoadChecklist()
        {
            try
            {
                string path = Path.Combine(DevFolder, "checklist.txt");
                if (!File.Exists(path))
                {
                    s_ChecklistJson = "{\"title\":" + Q("(checklist.txt が無い)") + ",\"items\":[]}";
                    return;
                }

                string title = string.Empty;
                List<string> items = new List<string>();
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.TrimEnd();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        if (title.Length == 0)
                        {
                            title = line.TrimStart('#').Trim();
                        }

                        continue;
                    }

                    int tab = line.IndexOf('\t');
                    string id = tab > 0 ? line.Substring(0, tab).Trim() : (items.Count + 1).ToString();
                    string text = tab > 0 ? line.Substring(tab + 1).Trim() : line.Trim();
                    items.Add("{\"id\":" + Q(id) + ",\"text\":" + Q(text) + "}");
                }

                s_ChecklistJson = "{\"title\":" + Q(title) + ",\"items\":[" + string.Join(",", items) + "]}";
            }
            catch (Exception e)
            {
                s_ChecklistJson = "{\"title\":" + Q("読み込みエラー: " + e.Message) + ",\"items\":[]}";
            }
        }

        private static void Submit(string json)
        {
            try
            {
                Directory.CreateDirectory(DevFolder);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(DevFolder, $"result_{Setting.kVersion}_{stamp}.json");
                File.WriteAllText(path, json ?? string.Empty, new UTF8Encoding(false));
                Mod.log.Info($"[TEST] チェックリスト送信 {Setting.kVersion} → {path}\n[TEST] {json}\n[TEST] 状態 {KeyFix.StateJson()}");
            }
            catch (Exception e)
            {
                Mod.log.Info($"[TEST] 送信に失敗: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}

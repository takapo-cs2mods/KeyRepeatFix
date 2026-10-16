// Key Repeat Fix v0.1(v0.2 で ⑤ を追加、v0.3 で押しっぱなしの表の型を直した = ①②⑤ が初めて動く、v0.4 でリピートは編集・移動キーだけに): ゲーム内の入力欄のキー入力のバグを直す本体(Harmony でゲームの処理を差し替える)。
//
// ゲームのキー入力の流れ(Colossal.UI.dll を読んで確認、2026-10-15〜16):
//   Unity の InputSystem → UIKeyboardModule.OnKeyboardAction(押した/離した)→ キューに積む
//   → 毎フレーム UIInputSystem.DispatchInputEvents → UIKeyboardModule.GenerateKeyEvents(押しっぱなしの自動リピート)
//   → ゲームの画面(Cohtml)へ。IME の変換中(m_ActiveIMEComposition が空でない間)は、文字以外のキーは画面に渡さない。
//
// 直すもの(どれも開発用の窓で個別に ON/OFF できる。初期値は全部 ON):
//   ① リピート間隔: ゲームは 0.5 秒押し続けると、以後「毎フレーム」リピートを送る(cohtml の DelayTimer.Update が
//      一度 Phase=1 になると間隔 0.033 秒を使わない。60fps で約2倍、144fps で約5倍の速さ)。
//      → GenerateKeyEvents を差し替え、Windows のキーボード設定(リピート開始までの時間・速さ)どおりに送る。
//        リピートするのは最後に押したキーだけ(Windows と同じ。ゲームは押している全部のキーをリピートしていた)。
//   ② 押しっぱなしの思い込み: キーを離した通知が届かず、押していないのに押しっぱなし扱いが残ることがある(IME 使用時に顕著。
//      Alt+Tab で直る)。→ 毎フレーム、押しっぱなし扱いのキーが本当に押されているかを Windows に聞き、離れていたら外す。
//   ③ IME の確定キー: 変換を確定する Enter(取り消しの Esc、最後の1文字を消す Backspace も)が、確定と同じフレームで
//      変換が終わるため、普通のキーとしても画面に届いていた(複数行の欄で改行が入る、Enter で送信される)。
//      → 変換中(または直前のフレームまで変換中だった)に押されたキーは、画面に渡さない(離したときの通知も)。
//   ⑤ 重複・偽の「離した」(v0.2): v0.1 の実機テストで、IME を一度使ったあと文字以外のキーが連射になった。
//      そのとき ① の自前リピートは一度も動いておらず(回数 0)、② の掃除も 0 → 連射の元は「押した」の通知そのもの。
//      Windows のキーリピートや IME が、押した(と離した)を何度も InputSystem に送っているとみている(v0.2 のキーログで確認)。
//      → すでに押しっぱなし扱いのキーの「押した」は捨てる。Windows ではまだ押しているのに来た「離した」も捨てる。
//        長押しのリピートは ① だけが送る(IME の ON/OFF に関係なく、いつも同じ速さになる)。
//   ④ Backspace の文字イベント: Windows は Backspace を文字(0x08)としても送ってくる。ゲームはこれも画面に渡している。
//      画面がこれでも1文字消すと二重に消える(未確認)。→ 0x08 / 0x7F の文字イベントは渡さない。
//      ただし Steam Deck などの画面キーボード(emulateBackspaceOnTextEvent)のときは元のまま。
//
// 出典: Harmony 2 https://harmony.pardeike.net/ 、Unity Input System の Keyboard / KeyControl、
//       Win32 GetAsyncKeyState / SystemParametersInfo(SPI_GETKEYBOARDDELAY / SPI_GETKEYBOARDSPEED)
namespace KeyRepeatFix
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using cohtml.InputSystem;
    using Colossal.UI;
    using HarmonyLib;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;

    public static class KeyFix
    {
        // ---- ON/OFF(開発用の窓から切り替える。保存しない) ----
        public static bool FixRepeat = true;   // ①
        public static bool FixStuck = true;    // ②
        public static bool FixIme = true;      // ③
        public static bool FixBsChar = true;   // ④
        public static bool FixDup = true;      // ⑤
        public static bool KeyLog;             // 全部のキーをログに書く(調査用。v0.4 から初期値 OFF)

        // ---- 状態(窓の表示用) ----
        public static int CountRepeat;
        public static int CountStuck;
        public static int CountIme;
        public static int CountBsChar;
        public static int CountDup;
        public static int CountFakeUp;
        public static int CountPress;
        public static string PressedText = string.Empty;
        public static bool Composing;
        public static float RepeatDelay = 0.5f;
        public static float RepeatInterval = 0.033f;
        public static string RepeatSource = "default";
        public static string LastError = string.Empty;

        private static Key s_RepeatKey = Key.None;
        private static float s_NextRepeat;
        private static bool s_ComposingLastFrame;
        private static readonly HashSet<Key> s_Swallowed = new HashSet<Key>();
        private static readonly List<int> s_Remove = new List<int>();
        private static string s_LastErrorLogged = string.Empty;

        // ゲームの中身(private)を読むための道具
        private static readonly FieldInfo s_PressedKeysField = AccessTools.Field(typeof(UIKeyboardModule), "m_PressedKeys");

        private static readonly FieldInfo s_SystemField = AccessTools.Field(typeof(UIInputModule), "m_System");
        private static readonly FieldInfo s_CompositionField = AccessTools.Field(typeof(UIInputSystem), "m_ActiveIMEComposition");

        private static Harmony s_Harmony;

        public static void Install()
        {
            ReadWindowsRepeatSettings();
            s_Harmony = new Harmony("KeyRepeatFix");
            s_Harmony.PatchAll(typeof(KeyFix).Assembly);
            int n = 0;
            foreach (MethodBase m in s_Harmony.GetPatchedMethods())
            {
                n++;
                Mod.log.Info($"差し替え: {m.DeclaringType?.Name}.{m.Name}");
            }

            Mod.log.Info($"差し替え {n} か所(3 か所のはず)。リピート: 開始 {RepeatDelay:0.000} 秒 / 間隔 {RepeatInterval:0.000} 秒({RepeatSource})");
        }

        public static void Uninstall()
        {
            s_Harmony?.UnpatchAll("KeyRepeatFix");
            s_Harmony = null;
        }

        // ---- ③ と ① の準備: キーを押した / 離した ----
        [HarmonyPatch(typeof(UIKeyboardModule), "OnKeyboardAction")]
        public static class OnKeyboardActionPatch
        {
            public static bool Prefix(UIKeyboardModule __instance, InputAction.CallbackContext __0)
            {
                try
                {
                    if (!(__0.control is KeyControl kc))
                    {
                        return true;
                    }

                    Key key = kc.keyCode;
                    bool pressed = kc.isPressed;
                    bool composingNow = IsComposing(__instance);

                    if (FixIme)
                    {
                        if (pressed)
                        {
                            if (composingNow || s_ComposingLastFrame)
                            {
                                s_Swallowed.Add(key);
                                CountIme++;
                                if (KeyLog)
                                {
                                    Mod.log.Info($"[③IME] 変換中のキーを画面に渡さない: {key}(変換中 今={composingNow} 前フレーム={s_ComposingLastFrame})");
                                }

                                return false;
                            }

                            s_Swallowed.Remove(key); // 前に渡さなかったキーの「離した」が届かなかった場合の後始末
                        }
                        else if (s_Swallowed.Remove(key))
                        {
                            if (KeyLog)
                            {
                                Mod.log.Info($"[KEY] 離した {key}(押したときに渡していないので、これも渡さない)");
                            }

                            return false;
                        }
                    }

                    Dictionary<int, KeyEventDataCached> keys = PressedKeys(__instance);
                    bool already = keys != null && keys.ContainsKey((int)key);
                    int vk = ToVirtualKey(key);
                    bool osKnown = vk != 0 && Win32.Available;
                    bool osDown = osKnown && Win32.IsDown(vk);

                    if (KeyLog)
                    {
                        Mod.log.Info($"[KEY] {(pressed ? "押した" : "離した")} {key}  押しっぱなし扱い済み={already}  Windows={(osKnown ? (osDown ? "押してる" : "離してる") : "?")}  変換中={composingNow}  f={Time.frameCount} t={Time.realtimeSinceStartup:0.000}");
                    }

                    if (pressed)
                    {
                        CountPress++;
                    }

                    if (FixDup)
                    {
                        // ⑤ すでに押しっぱなし扱いのキーの「押した」= Windows のリピートか重複。リピートは ① で自前で送るので捨てる
                        if (pressed && already)
                        {
                            CountDup++;
                            return false;
                        }

                        // ⑤ Windows ではまだ押しているのに来た「離した」= 偽物。捨てる(本当に離したら ② が外す)
                        if (!pressed && osKnown && osDown)
                        {
                            CountFakeUp++;
                            return false;
                        }
                    }

                    if (pressed && !IsModifier(key))
                    {
                        // ① 最後に押したキーだけをリピートする。押した瞬間から「開始までの時間」を数え直す。
                        // v0.4: リピートするのは編集・移動のキーだけ。文字のキーは Windows が文字を繰り返し送ってくるので不要。
                        //   v0.3 で、半角/全角キー(Unity では Backquote)の「離した」が遅れて届き、その間ずっとリピートしていた。
                        //   ほかのキーを押したら、前のキーのリピートは止める(Windows と同じ)。
                        s_RepeatKey = IsRepeatable(key) ? key : Key.None;
                        s_NextRepeat = Time.realtimeSinceStartup + RepeatDelay;
                    }
                }
                catch (Exception e)
                {
                    Error("OnKeyboardAction", e);
                }

                return true;
            }
        }

        // ---- ① ② 毎フレーム: 押しっぱなしの掃除と、自動リピート ----
        [HarmonyPatch(typeof(UIKeyboardModule), "GenerateKeyEvents")]
        public static class GenerateKeyEventsPatch
        {
            public static bool Prefix(UIKeyboardModule __instance)
            {
                try
                {
                    // ③ 用: このフレームの終わりの時点で変換中だったか(次のフレームのキーの判定に使う)
                    Composing = IsComposing(__instance);
                    s_ComposingLastFrame = Composing;

                    Dictionary<int, KeyEventDataCached> keys = PressedKeys(__instance);
                    if (keys == null)
                    {
                        return true;
                    }

                    if (FixStuck && keys.Count > 0)
                    {
                        RemoveStuckKeys(keys);
                    }

#if DEBUG
                    UpdatePressedText(keys); // 開発用の窓の表示用
#endif

                    if (!FixRepeat)
                    {
                        return true; // 元のリピート(毎フレーム連打)のまま
                    }

                    if (keys.Count == 0 || s_RepeatKey == Key.None || !keys.TryGetValue((int)s_RepeatKey, out KeyEventDataCached data) || data == null)
                    {
                        return false;
                    }

                    float now = Time.realtimeSinceStartup;
                    if (now < s_NextRepeat)
                    {
                        return false;
                    }

                    data.IsAutoRepeat = true;
                    Enqueue(__instance, data);
                    CountRepeat++;
                    if (KeyLog)
                    {
                        Mod.log.Info($"[KEY] リピート {s_RepeatKey}  t={now:0.000}");
                    }

                    s_NextRepeat += RepeatInterval;
                    if (s_NextRepeat < now)
                    {
                        // フレームが重くて遅れたときは、まとめて送らずに次から数え直す
                        s_NextRepeat = now + RepeatInterval;
                    }

                    return false;
                }
                catch (Exception e)
                {
                    Error("GenerateKeyEvents", e);
                    return true; // 何かあったらゲームの元の処理に任せる
                }
            }
        }

        // ---- ④ Backspace の文字イベント ----
        [HarmonyPatch(typeof(UIKeyboardModule), "OnSendCharKey")]
        public static class OnSendCharKeyPatch
        {
            public static bool Prefix(UIKeyboardModule __instance, char __0)
            {
                try
                {
                    if (KeyLog && __0 < 32)
                    {
                        Mod.log.Info($"[KEY] 文字イベント 0x{(int)__0:X2}  t={Time.realtimeSinceStartup:0.000}");
                    }

                    if (FixBsChar && (__0 == '\b' || __0 == (char)127) && !__instance.emulateBackspaceOnTextEvent)
                    {
                        CountBsChar++;
                        if (KeyLog)
                        {
                            Mod.log.Info($"[④BS] 文字イベント 0x{(int)__0:X2} を画面に渡さない");
                        }

                        return false;
                    }
                }
                catch (Exception e)
                {
                    Error("OnSendCharKey", e);
                }

                return true;
            }
        }

        // ---- 中身 ----

        private static void Enqueue(UIKeyboardModule module, KeyEventDataCached data)
        {
            UIInputSystem system = s_SystemField?.GetValue(module) as UIInputSystem;
            if (system == null)
            {
                return;
            }

            GenericInputEvent ev = new GenericInputEvent();
            ev.EventType = (GenericInputEvent.InputEventType)4; // 4 = キー(ゲームと同じ値)
            ev.KeyData = data;
            system.EnqueueInputEvent(ev);
        }

        private static bool IsComposing(UIKeyboardModule module)
        {
            object system = s_SystemField?.GetValue(module);
            if (system == null || s_CompositionField == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(s_CompositionField.GetValue(system) as string);
        }

        // v0.3: m_PressedKeys の中身は Dictionary<int, KeyEventDataCached>(キー番号は Key を int にしたもの)。
        // v0.1〜0.2 は Dictionary<Key, ...> として読もうとして毎回 null になり、①②⑤ が全く動いていなかった(元のゲームの処理のまま)。
        private static Dictionary<int, KeyEventDataCached> PressedKeys(UIKeyboardModule module)
        {
            object raw = s_PressedKeysField?.GetValue(module);
            Dictionary<int, KeyEventDataCached> keys = raw as Dictionary<int, KeyEventDataCached>;
            if (keys == null && raw != null && !s_WarnedType)
            {
                s_WarnedType = true;
                Mod.log.Info($"[ERROR] m_PressedKeys の型が想定外: {raw.GetType().FullName}(①②⑤ は動かない)");
                LastError = "m_PressedKeys の型が想定外: " + raw.GetType().Name;
            }

            return keys;
        }

        private static bool s_WarnedType;

        private static void RemoveStuckKeys(Dictionary<int, KeyEventDataCached> keys)
        {
            s_Remove.Clear();
            Keyboard kb = Keyboard.current;
            foreach (int key in keys.Keys)
            {
                if (!IsReallyDown((Key)key, kb))
                {
                    s_Remove.Add(key);
                }
            }

            foreach (int key in s_Remove)
            {
                keys.Remove(key);
                CountStuck++;
                Mod.log.Info($"[②STUCK] 押していないのに押しっぱなし扱いだったキーを外した: {(Key)key}");
            }
        }

        // 本当に押されているか。Windows のキーは Windows に直接聞く(InputSystem 側の状態も取りこぼしている可能性があるため)。
        // 文字のキー(A〜Z など)はキーボードの配列で Windows のキー番号が変わるので、InputSystem の状態で見る。
        private static bool IsReallyDown(Key key, Keyboard kb)
        {
            int vk = ToVirtualKey(key);
            if (vk != 0 && Win32.Available)
            {
                return Win32.IsDown(vk);
            }

            try
            {
                return kb != null && kb[key].isPressed;
            }
            catch
            {
                return true; // 分からないものは外さない
            }
        }

        // ① で自前リピートするキー(文字を出さない編集・移動のキー)
        private static bool IsRepeatable(Key key)
        {
            switch (key)
            {
                case Key.Backspace:
                case Key.Delete:
                case Key.LeftArrow:
                case Key.RightArrow:
                case Key.UpArrow:
                case Key.DownArrow:
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                case Key.Enter:
                case Key.NumpadEnter:
                case Key.Tab:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsModifier(Key key)
        {
            switch (key)
            {
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LeftMeta:
                case Key.RightMeta:
                case Key.CapsLock:
                case Key.NumLock:
                case Key.ScrollLock:
                    return true;
                default:
                    return false;
            }
        }

        // 配列に関係しないキーだけ Windows のキー番号(VK_*)に変える。0 = 対応なし
        private static int ToVirtualKey(Key key)
        {
            switch (key)
            {
                case Key.Backspace: return 0x08;
                case Key.Tab: return 0x09;
                case Key.Enter: return 0x0D;
                case Key.NumpadEnter: return 0x0D;
                case Key.Escape: return 0x1B;
                case Key.Space: return 0x20;
                case Key.PageUp: return 0x21;
                case Key.PageDown: return 0x22;
                case Key.End: return 0x23;
                case Key.Home: return 0x24;
                case Key.LeftArrow: return 0x25;
                case Key.UpArrow: return 0x26;
                case Key.RightArrow: return 0x27;
                case Key.DownArrow: return 0x28;
                case Key.Insert: return 0x2D;
                case Key.Delete: return 0x2E;
                case Key.LeftShift: return 0xA0;
                case Key.RightShift: return 0xA1;
                case Key.LeftCtrl: return 0xA2;
                case Key.RightCtrl: return 0xA3;
                case Key.LeftAlt: return 0xA4;
                case Key.RightAlt: return 0xA5;
                case Key.LeftMeta: return 0x5B;
                case Key.RightMeta: return 0x5C;
                case Key.F1: return 0x70;
                case Key.F2: return 0x71;
                case Key.F3: return 0x72;
                case Key.F4: return 0x73;
                case Key.F5: return 0x74;
                case Key.F6: return 0x75;
                case Key.F7: return 0x76;
                case Key.F8: return 0x77;
                case Key.F9: return 0x78;
                case Key.F10: return 0x79;
                case Key.F11: return 0x7A;
                case Key.F12: return 0x7B;
                default: return 0;
            }
        }

        private static void UpdatePressedText(Dictionary<int, KeyEventDataCached> keys)
        {
            if (keys.Count == 0)
            {
                PressedText = string.Empty;
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (int key in keys.Keys)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append((Key)key);
            }

            PressedText = sb.ToString();
        }

        // Windows の「キーボードのプロパティ」の設定を読む(表示までの待ち時間 0〜3 → 0.25〜1 秒、表示の間隔 0〜31 → 毎秒 約2.5〜30 回)
        private static void ReadWindowsRepeatSettings()
        {
            RepeatDelay = 0.5f;
            RepeatInterval = 0.033f;
            RepeatSource = "default";
            if (!Win32.Available)
            {
                return;
            }

            if (Win32.TryGetRepeat(out int delay, out int speed))
            {
                RepeatDelay = (Mathf.Clamp(delay, 0, 3) + 1) * 0.25f;
                float perSecond = 2.5f + (Mathf.Clamp(speed, 0, 31) * (27.5f / 31f));
                RepeatInterval = 1f / perSecond;
                RepeatSource = $"Windows delay={delay} speed={speed}";
            }
        }

        private static void Error(string where, Exception e)
        {
            LastError = $"{where}: {e.GetType().Name}: {e.Message}";
            if (LastError != s_LastErrorLogged)
            {
                s_LastErrorLogged = LastError;
                Mod.log.Info($"[ERROR] {LastError}\n{e}");
            }
        }

        public static string StateJson()
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"repeat\":").Append(FixRepeat ? "true" : "false");
            sb.Append(",\"stuck\":").Append(FixStuck ? "true" : "false");
            sb.Append(",\"ime\":").Append(FixIme ? "true" : "false");
            sb.Append(",\"bsChar\":").Append(FixBsChar ? "true" : "false");
            sb.Append(",\"keyLog\":").Append(KeyLog ? "true" : "false");
            sb.Append(",\"countRepeat\":").Append(CountRepeat);
            sb.Append(",\"countStuck\":").Append(CountStuck);
            sb.Append(",\"countIme\":").Append(CountIme);
            sb.Append(",\"countBsChar\":").Append(CountBsChar);
            sb.Append(",\"dup\":").Append(FixDup ? "true" : "false");
            sb.Append(",\"countDup\":").Append(CountDup);
            sb.Append(",\"countFakeUp\":").Append(CountFakeUp);
            sb.Append(",\"countPress\":").Append(CountPress);
            sb.Append(",\"pressed\":").Append(DevUISystem.Q(PressedText));
            sb.Append(",\"composing\":").Append(Composing ? "true" : "false");
            sb.Append(",\"delay\":").Append(RepeatDelay.ToString("0.000", ic));
            sb.Append(",\"interval\":").Append(RepeatInterval.ToString("0.000", ic));
            sb.Append(",\"source\":").Append(DevUISystem.Q(RepeatSource));
            sb.Append(",\"win32\":").Append(Win32.Available ? "true" : "false");
            sb.Append(",\"error\":").Append(DevUISystem.Q(LastError));
            sb.Append('}');
            return sb.ToString();
        }
    }

    // Windows に直接聞く部分。Windows 以外(Mac など)では使わない
    internal static class Win32
    {
        private const uint SPI_GETKEYBOARDSPEED = 0x000A;
        private const uint SPI_GETKEYBOARDDELAY = 0x0016;

        private static bool? s_Available;

        public static bool Available
        {
            get
            {
                if (s_Available == null)
                {
                    s_Available = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
                    if (s_Available == true)
                    {
                        try
                        {
                            GetAsyncKeyState(0x08);
                        }
                        catch (Exception e)
                        {
                            s_Available = false;
                            Mod.log.Info($"Windows のキー状態が読めない({e.GetType().Name})。InputSystem の状態で判定する");
                        }
                    }
                }

                return s_Available == true;
            }
        }

        public static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public static bool TryGetRepeat(out int delay, out int speed)
        {
            delay = 1;
            speed = 31;
            try
            {
                int d = 0;
                int s = 0;
                if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref d, 0) && SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, ref s, 0))
                {
                    delay = d;
                    speed = s;
                    return true;
                }
            }
            catch (Exception e)
            {
                Mod.log.Info($"Windows のリピート設定が読めない({e.GetType().Name})。0.5 秒 / 0.033 秒を使う");
            }

            return false;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
    }
}

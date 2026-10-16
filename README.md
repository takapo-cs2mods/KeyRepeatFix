# Key Repeat Fix (Backspace / IME)

A Cities: Skylines II mod that fixes key input bugs in in-game text fields, especially for IME users (Japanese, Chinese, Korean, ...).
A temporary patch until the game itself is fixed.

Source: https://github.com/takapo-cs2mods/KeyRepeatFix

[English](#english) / [日本語](#日本語)

## English

**A temporary patch until the game itself is fixed. Please remove it once the official fix arrives.**

Fixes key input bugs in in-game text fields (road and building names, text fields of other mods, etc.).

**Mainly for players who type with an IME (Japanese, Chinese, Korean and other input methods).** If you only type in English, this barely affects you. The only difference you may notice is that holding Backspace is too fast at high frame rates.

**Symptoms**
- After using the IME, one press of Backspace deletes several characters, sometimes everything
- The arrow keys keep moving the cursor and won't stop
- Once this starts, it continues even after turning the IME off. Alt+Tab out of the game and back fixes it
- The Enter key that confirms an IME conversion also inserts a line break or submits the field

**What is going on (bugs in the game)**
Confirmed by reading the game's code (Colossal.UI).
1. **Key repeat runs every frame**: after holding a key for 0.5 s, the game ignores the intended repeat interval (0.033 s) and sends the key every frame. That is about 2x the normal speed at 60 fps, and about 5x at 144 fps.
2. **Released keys are treated as still held**: while an IME is in use, the "key released" notice sometimes never reaches the game. The game then believes the key is still held, and together with bug 1 it keeps sending Backspace or arrow keys every frame while you touch nothing. Alt+Tab fixes it because the game clears its record of held keys when it loses focus.
3. **IME confirm keys leak into the field**: the game is supposed to keep keys pressed during an IME conversion away from the text field. But the "converting" flag is updated immediately, while key events are queued and delivered later. By the time the confirming Enter is delivered, the game already thinks the conversion is over, so the Enter reaches the text field.

**What this mod does**
- Key repeat follows your Windows keyboard settings (repeat delay and rate)
- Every frame, keys the game believes are held are checked against the real key state, and released keys are cleared
- Keys pressed during an IME conversion (the confirming Enter, Esc to cancel, Backspace) are not passed to the text field

It fixes the game's input handling itself, so it also works in other mods' text fields.

**How to use**
Just enable it. There are no settings.

**Made with AI**
I can't write code. But this bug kept bothering me and no official fix came, so I tracked down the cause and built this together with an AI (Claude), testing it in game and rebuilding it several times.

**Open source**
All source code is public on GitHub (GPL v3), so anyone can check exactly what it does. Forks, better fixes, and using this to report the bug to the developers are all very welcome.

**Bug reports**
Please use the comments or GitHub Issues.

## 日本語

**ゲーム本体が直るまでの暫定パッチです。公式で直ったら外してください。**

ゲーム内の入力欄(道路や建物の名前、MOD の入力欄など)で起きる、キー入力のバグを直します。

**特に、日本語・中国語・韓国語などの IME(変換して入力する仕組み)を使う人向けです。** 英語で打つだけの人にはほぼ関係ありません(高い fps だと Backspace の長押しが速すぎる、くらいの違いです)。

**こんな症状に**
- IME を使ったあと、Backspace を1回押しただけで何文字も、ひどいと全部消える
- 矢印キーのカーソルが止まらない
- 一度そうなると、IME を OFF にしても連射が続く。Alt+Tab でいったんゲームから離れると直る
- IME の変換を確定した Enter で、改行が入ったり、入力が確定されたりする

**何が起きているのか(ゲーム本体のバグ)**
ゲームのコード(Colossal.UI)を読んで確認しました。
1. **長押しのリピートが毎フレームになる**:キーを 0.5 秒押し続けると、以後は決められた間隔(0.033 秒)を無視して、毎フレームキーを送り続けます。60fps で本来の約2倍、144fps で約5倍の速さです。
2. **離したキーを押しっぱなしだと思い込む**:IME を使っていると、キーを離した知らせがゲームに届かないことがあります。するとゲームはそのキーを押しっぱなしだと思い込み、1 と合わさって、何も押していないのに毎フレーム Backspace や矢印を送り続けます。Alt+Tab で直るのは、ゲームから離れたときに押しっぱなしの記録が消えるからです。
3. **IME の確定キーが入力欄に漏れる**:ゲームは IME で変換中のキーを入力欄に渡さないようにしています。ただし「変換中かどうか」の印はその場で更新される一方、キーはいったん溜めてから後でまとめて渡されます。そのため確定の Enter を渡す時点では、もう「変換中ではない」扱いになっていて、Enter が入力欄に届いてしまいます。

**この MOD でやっていること**
- 長押しのリピートを、Windows のキーボード設定(表示までの待ち時間・表示の間隔)どおりにします
- 押しっぱなしだと思い込んでいるキーを、実際のキーの状態と毎フレーム照らし合わせて外します
- IME で変換中に押したキー(確定の Enter、取り消しの Esc、BS)は、入力欄に渡さないようにします

ゲームの入力の仕組みそのものを直しているので、他の MOD の入力欄にも効きます。

**使い方**
入れるだけです。設定はありません。

**AI で作りました**
作者はコードが書けません。でもこのバグに困っていて、公式の修正も来ないので、AI(Claude)と一緒に原因を調べて作りました。実機で確認しながら、何回か作り直しています。

**オープンソースです**
ソースコードは全部 GitHub で公開しています(GPL v3)。何をしているかは誰でも確認できます。改造も、もっといい直し方の提案も、これを使った公式への報告も大歓迎です。

**不具合の報告**
コメント欄か、GitHub の Issue へどうぞ。

## For developers / 開発者向け

How it works (Harmony prefixes on `Colossal.UI.UIKeyboardModule`, see `KeyRepeatFix/KeyFix.cs`):

| Patch | What it does |
|---|---|
| `OnKeyboardAction` | Drops keys pressed while an IME composition is active (or was active at the end of the previous frame), and their releases. Drops duplicate presses of already-held keys and fake releases while Windows still reports the key as down. Picks the key to auto-repeat. |
| `GenerateKeyEvents` | Replaces the game's repeat (cohtml `DelayTimer` never returns to its rate phase, so it fired every frame). Removes keys from `m_PressedKeys` that are not really down (Win32 `GetAsyncKeyState` for layout-independent keys, Input System state for the rest). Repeats only the last pressed editing/navigation key, using the Windows repeat delay and rate (`SystemParametersInfo`). |
| `OnSendCharKey` | Drops `0x08` / `0x7F` character events when the virtual keyboard backspace emulation is off (Windows also sends Backspace as a character). |

Build: Visual Studio 2022 with the official Cities: Skylines II modding toolchain. Harmony comes from the `Lib.Harmony` 2.2.2 NuGet package (`0Harmony.dll` is shipped next to the mod, like other CS2 mods). The `UI` folder (a developer-only test window, shown only with `-developerMode`) is built with webpack after the C# build.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).

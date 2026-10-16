// Key Repeat Fix v0.1: 開発用の別窓(開発者モードのときだけ)。直し方ごとの ON/OFF、状態、試し打ちの欄、チェックリスト。
// v0.2: タブをやめ、左にチェックリスト・右に操作を並べて両方いつも見えるように(たかし 2026-10-16、全部の開発用の窓で必須)。
// ・画面右端の「KeyFix Dev」タブで開閉。
// ・試し打ちの欄は、キーの処理を何もしない(ゲームの素の入力欄と同じ条件で試すため)。
//   1行の欄だけ、Enter が届いたら「Enter が届いた」と表示する(IME の確定 Enter が漏れていないかを見るため)。
// 出典: CS2 公式 UI Modding(bindValue / useValue / trigger / moduleRegistry.append) https://cs2.paradoxwikis.com/UI_Modding
import React, { useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";

const G = "KeyRepeatFixDev";

const devMode$ = bindValue<boolean>(G, "DevMode");
const open$ = bindValue<boolean>(G, "Open");
const version$ = bindValue<string>(G, "Version");
const state$ = bindValue<string>(G, "State");
const checklist$ = bindValue<string>(G, "Checklist");

const parse = (json: string, fallback: any) => {
  try {
    return json ? JSON.parse(json) : fallback;
  } catch (e) {
    return fallback;
  }
};

const now = () => {
  const d = new Date();
  const p2 = (n: number) => (n < 10 ? "0" : "") + n;
  return p2(d.getHours()) + ":" + p2(d.getMinutes()) + ":" + p2(d.getSeconds());
};

// ---- 見た目(この窓だけの自前スタイル) ----
const C = {
  bg: "rgba(18,24,32,0.92)",
  panel: "rgba(255,255,255,0.06)",
  text: "rgba(255,255,255,0.92)",
  dim: "rgba(255,255,255,0.5)",
  accent: "rgba(255,190,80,1)",
  ok: "rgba(90,200,120,1)",
  ng: "rgba(235,90,80,1)",
  border: "rgba(255,255,255,0.15)",
};

const sWindow: any = {
  position: "absolute",
  top: "70rem",
  bottom: "120rem",
  right: "40rem",
  width: "900rem",
  display: "flex",
  flexDirection: "column",
  background: C.bg,
  color: C.text,
  borderRadius: "6rem",
  padding: "10rem 12rem",
  fontSize: "13rem",
  pointerEvents: "auto",
  border: "1rem solid " + C.border,
};

// Mikiri Dev のタブ(上から 90rem)と重ならないように、少し下に置く
const sTab: any = {
  position: "absolute",
  top: "135rem",
  right: "0rem",
  padding: "6rem 10rem",
  background: C.bg,
  color: C.accent,
  fontSize: "12rem",
  borderRadius: "6rem 0 0 6rem",
  pointerEvents: "auto",
  cursor: "pointer",
  border: "1rem solid " + C.border,
};

const sSection: any = { background: C.panel, borderRadius: "4rem", padding: "8rem", marginTop: "8rem" };
const sH: any = { fontWeight: "bold", color: C.accent, marginBottom: "6rem" };
const sRow: any = { display: "flex", flexDirection: "row", alignItems: "center", marginBottom: "5rem", flexWrap: "wrap" };
const sLabel: any = { width: "130rem", color: C.dim, flex: "0 0 auto" };
const sSmall: any = { color: C.dim, fontSize: "11rem", marginBottom: "6rem" };
const sInput: any = {
  flex: 1,
  background: "rgba(0,0,0,0.4)",
  color: C.text,
  border: "1rem solid " + C.border,
  borderRadius: "3rem",
  padding: "3rem 6rem",
  fontSize: "13rem",
};

const Btn = ({ on, onClick, children, color, title }: { on?: boolean; onClick: () => void; children: any; color?: string; title?: string }) => (
  <div
    title={title}
    onClick={onClick}
    style={{
      padding: "3rem 9rem",
      marginRight: "4rem",
      marginBottom: "2rem",
      borderRadius: "3rem",
      cursor: "pointer",
      border: "1rem solid " + (on ? color || C.accent : C.border),
      background: on ? color || C.accent : "rgba(255,255,255,0.08)",
      color: on ? "rgba(0,0,0,0.9)" : C.text,
    }}
  >
    {children}
  </div>
);

// キーの処理はしない。ゲームのショートカットに渡らないように、伝わりだけ止める(Mikiri の窓と同じ)
const stopOnly = (e: any) => {
  if (e && e.stopPropagation) e.stopPropagation();
};

// ---- 直し方の ON/OFF ----
const Switch = ({ label, on, name, desc }: { label: string; on: boolean; name: string; desc: string }) => (
  <div style={{ marginBottom: "6rem" }}>
    <div style={sRow}>
      <div style={sLabel}>{label}</div>
      <Btn on={on} onClick={() => trigger(G, name, !on)}>
        {on ? "ON" : "OFF"}
      </Btn>
    </div>
    <div style={sSmall}>{desc}</div>
  </div>
);

const FixSection = () => {
  const st = parse(useValue(state$), {});
  return (
    <div style={sSection}>
      <div style={sH}>直し方(初期値は全部 ON。保存しない)</div>
      <Switch label="① リピート間隔" on={!!st.repeat} name="SetRepeat" desc="長押しのリピートを Windows の設定どおりに(OFF = ゲームの元のまま、0.5 秒後から毎フレーム連打)" />
      <Switch label="② 押しっぱなし掃除" on={!!st.stuck} name="SetStuck" desc="押していないのに押しっぱなし扱いのキーを毎フレーム外す(Alt+Tab で直っていたもの)" />
      <Switch label="③ IME の確定キー" on={!!st.ime} name="SetIme" desc="変換中に押した Enter / Esc / BS を入力欄に渡さない(確定の Enter で改行・送信されない)" />
      <Switch label="④ BS の文字イベント" on={!!st.bsChar} name="SetBsChar" desc="Backspace の文字(0x08)を入力欄に渡さない(二重に消える対策。効果は未確認)" />
      <Switch label="⑤ 重複・偽の離した" on={!!st.dup} name="SetDup" desc="押しっぱなし中にまた来る「押した」と、まだ押しているのに来る「離した」を捨てる(v0.2。IME 後の連射対策)" />
      <Switch label="キーログ" on={!!st.keyLog} name="SetKeyLog" desc="押した・離した・リピート・文字イベントを全部ログに書く(普段は OFF)" />
    </div>
  );
};

// ---- 状態 ----
const StateSection = () => {
  const st = parse(useValue(state$), {});
  return (
    <div style={sSection}>
      <div style={sRow}>
        <div style={{ ...sH, flex: 1, marginBottom: 0 }}>状態</div>
        <Btn onClick={() => trigger(G, "ResetCounts")}>数を 0 に</Btn>
      </div>
      <div style={sRow}>
        <div style={sLabel}>押しっぱなし扱い</div>
        <div style={{ flex: 1, color: st.pressed ? C.ng : C.dim }}>{st.pressed || "(なし)"}</div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>IME 変換中</div>
        <div style={{ color: st.composing ? C.accent : C.dim }}>{st.composing ? "はい" : "いいえ"}</div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>リピート</div>
        <div style={{ flex: 1, color: C.dim }}>
          開始 {st.delay} 秒 / 間隔 {st.interval} 秒({st.source})
        </div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>回数</div>
        <div style={{ flex: 1, color: C.dim }}>
          押した {st.countPress} / ①リピート {st.countRepeat} / ②外した {st.countStuck} / ③止めた {st.countIme} / ④捨てた {st.countBsChar} / ⑤重複 {st.countDup}・偽の離した {st.countFakeUp}
        </div>
      </div>
      {!st.win32 && <div style={{ color: C.ng }}>Windows のキー状態が読めない(② は InputSystem の状態で判定)</div>}
      {st.error && <div style={{ color: C.ng, wordBreak: "break-all" }}>エラー: {st.error}</div>}
    </div>
  );
};

// ---- 試し打ち ----
const TrySection = () => {
  const [one, setOne] = useState("");
  const [multi, setMulti] = useState("");
  const [enterAt, setEnterAt] = useState("");
  const [markAt, setMarkAt] = useState("");
  return (
    <div style={sSection}>
      <div style={sH}>試し打ち</div>
      <div style={sSmall}>キーの処理を何もしていない素の入力欄。1行の欄は Enter が届くと下に表示する。</div>
      <div style={sRow}>
        <div style={sLabel}>1行</div>
        <input
          type="text"
          style={sInput}
          value={one}
          onChange={(e: any) => setOne(e.target.value)}
          onKeyDown={(e: any) => {
            stopOnly(e);
            if (e && (e.key === "Enter" || e.keyCode === 13)) setEnterAt(now());
          }}
        />
      </div>
      <div style={{ ...sRow, color: enterAt ? C.ng : C.dim }}>{enterAt ? "Enter が届いた " + enterAt : "(Enter はまだ届いていない)"}</div>
      <div style={sRow}>
        <div style={sLabel}>複数行</div>
      </div>
      <textarea
        style={{ ...sInput, width: "100%", height: "90rem" }}
        value={multi}
        onChange={(e: any) => setMulti(e.target.value)}
        onKeyDown={stopOnly}
      />
      <div style={{ ...sRow, marginTop: "6rem" }}>
        <Btn
          onClick={() => {
            setOne("");
            setMulti("");
            setEnterAt("");
          }}
        >
          空にする
        </Btn>
        <Btn
          onClick={() => {
            trigger(G, "Note", "---- 印 ---- 1行=[" + one + "] 複数行=[" + multi.replace(/\n/g, "⏎") + "]");
            setMarkAt(now());
          }}
          title="ログに印と欄の中身を書く(ログのどこを見ればいいか分かるように)"
        >
          ログに印
        </Btn>
        <div style={{ color: C.dim }}>{markAt ? "書いた " + markAt : ""}</div>
      </div>
    </div>
  );
};

// ---- チェックリスト ----
type Result = { r: string; note: string };

const ChecklistSection = ({ setSubmit }: { setSubmit: (fn: () => void) => void }) => {
  const cl = parse(useValue(checklist$), { title: "", items: [] });
  const version = useValue(version$);
  const [results, setResults] = useState<{ [id: string]: Result }>({});
  const [memo, setMemo] = useState("");
  const [sentAt, setSentAt] = useState("");
  const [forTitle, setForTitle] = useState("");

  // 項目ファイルが変わったら入力を空にする
  if (forTitle !== cl.title) {
    setForTitle(cl.title);
    setResults({});
    setMemo("");
    setSentAt("");
  }

  const set = (id: string, patch: Partial<Result>) => {
    const cur = results[id] || { r: "", note: "" };
    setResults({ ...results, [id]: { ...cur, ...patch } });
  };

  const submit = () => {
    const items = (cl.items || []).map((it: any) => ({ id: it.id, text: it.text, result: (results[it.id] || {}).r || "", note: (results[it.id] || {}).note || "" }));
    const json = JSON.stringify({ version, title: cl.title, items, memo });
    trigger(G, "SubmitChecklist", json);
    setSentAt(now());
  };

  setSubmit(submit);

  return (
    <div style={sSection}>
      <div style={sRow}>
        <div style={{ ...sH, flex: 1, marginBottom: 0 }}>チェックリスト</div>
        <Btn onClick={() => trigger(G, "ReloadChecklist")} title="項目ファイルを読み直す">
          読み直し
        </Btn>
      </div>
      <div style={{ color: C.dim, marginBottom: "6rem" }}>{cl.title || "(項目なし)"}</div>
      {(cl.items || []).map((it: any) => {
        const res = results[it.id] || { r: "", note: "" };
        return (
          <div key={it.id} style={{ borderTop: "1rem solid " + C.border, paddingTop: "6rem", marginTop: "6rem" }}>
            <div style={{ marginBottom: "4rem" }}>
              <span style={{ color: C.accent }}>{it.id}. </span>
              {it.text}
            </div>
            <div style={sRow}>
              <Btn on={res.r === "OK"} color={C.ok} onClick={() => set(it.id, { r: res.r === "OK" ? "" : "OK" })}>
                OK
              </Btn>
              <Btn on={res.r === "NG"} color={C.ng} onClick={() => set(it.id, { r: res.r === "NG" ? "" : "NG" })}>
                NG
              </Btn>
              <Btn on={res.r === "-"} onClick={() => set(it.id, { r: res.r === "-" ? "" : "-" })}>
                未確認
              </Btn>
            </div>
            <input type="text" style={{ ...sInput, width: "100%" }} value={res.note} placeholder="ひとこと(任意)" onChange={(e: any) => set(it.id, { note: e.target.value })} onKeyDown={stopOnly} />
          </div>
        );
      })}
      <div style={{ marginTop: "10rem", color: C.dim }}>備考</div>
      <input type="text" style={{ ...sInput, width: "100%" }} value={memo} onChange={(e: any) => setMemo(String(e.target.value).replace(/[\r\n]+/g, " "))} onKeyDown={stopOnly} />
      <div style={{ ...sRow, marginTop: "8rem" }}>
        <Btn on onClick={submit}>
          送信
        </Btn>
        <div style={{ color: C.dim }}>{sentAt ? "送信しました " + sentAt + "(スレには「試した」だけでOK)" : ""}</div>
      </div>
      <div style={{ height: "40rem" }} />
    </div>
  );
};

let submitFn: () => void = () => {};

export const DevWindow = () => {
  const devMode = useValue(devMode$);
  const open = useValue(open$);
  const version = useValue(version$);
  const [sentMark, setSentMark] = useState("");
  if (!devMode) return null;

  if (!open) {
    return (
      <div style={sTab} onClick={() => trigger(G, "Toggle")}>
        KeyFix Dev
      </div>
    );
  }

  const sendNow = () => {
    submitFn();
    setSentMark(now());
  };

  return (
    <div style={sWindow}>
      <div style={{ ...sRow, flex: "0 0 auto" }}>
        <div style={{ flex: 1, fontWeight: "bold" }}>Key Repeat Fix Dev {version}</div>
        <Btn on onClick={sendNow} title="チェックリストの結果を送る">
          送信
        </Btn>
        <Btn onClick={() => trigger(G, "Toggle")}>閉じる</Btn>
      </div>
      {sentMark && <div style={{ color: C.ok, flex: "0 0 auto" }}>送信しました {sentMark}(スレには「試した」だけでOK)</div>}
      <div style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "row", marginTop: "4rem" }}>
        <div style={{ flex: 1, overflowY: "scroll", minHeight: 0, marginRight: "8rem", display: "flex", flexDirection: "column" }}>
          <ChecklistSection setSubmit={(fn) => (submitFn = fn)} />
        </div>
        <div style={{ flex: 1, overflowY: "scroll", minHeight: 0, display: "flex", flexDirection: "column" }}>
          <TrySection />
          <StateSection />
          <FixSection />
        </div>
      </div>
    </div>
  );
};

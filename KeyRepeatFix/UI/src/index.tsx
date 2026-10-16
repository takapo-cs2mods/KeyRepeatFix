// Key Repeat Fix UI の入口。開発用の別窓(開発者モードのときだけ表示)をゲーム画面に足す。
// 出典: CS2 公式 UI Modding(moduleRegistry.append) https://cs2.paradoxwikis.com/UI_Modding
import { ModRegistrar } from "cs2/modding";
import { DevWindow } from "./devWindow";

export const hasCSS = false;

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("Game", DevWindow);
  console.log("[KeyRepeatFix] UI module registered");
};

export default register;

# 帝國餘燼：百族爭霸

完全離線、零相依的繁體中文即時戰略遊戲。直接以 Chrome 雙擊開啟 `index.html` 即可遊玩，不需要安裝套件或啟動伺服器。

## 專案結構

- `index.html`：頁面語意結構與所有遊戲介面。
- `css/base.css`：全域色彩、字體與主選單。
- `css/hud.css`：戰場 HUD、指令臺、小地圖與提示。
- `css/overlays-responsive.css`：對話框及各種視窗尺寸的響應式規則。
- `js/core-data-audio.js`：共用工具、文明／兵種／建築資料，以及會隨文明、時代與戰況變化的程序音樂、環境聲與音效。
- `js/world.js`：世界生成、純 2D 俯視鏡頭、實體建立、資源配置與尋路。
- `js/simulation.js`：固定步長模擬、經濟、戰鬥、多方 AI、時代、軍令與勝負。
- `js/generated-art.js`：完全離線的 Imagegen 圖集載入器，將兵種、特色單位、建築、資源、場景與特效對應到透明精靈；載入失敗時自動保留程序繪圖後備。
- `js/render.js`：Canvas 俯視地形、建築、活潑單位動畫、光影、戰鬥特效、迷霧與小地圖；也是不支援 WebGL2 時的完整繪圖路徑。
- `js/webgl-effects.js`：可選的 WebGL2 氣氛層，負責水面焦散、浮塵、晝夜光色、投射物光暈與受擊脈衝，不接管操作或單位繪製。
- `js/ui-input.js`：HUD、教學、選取、指令、鍵盤／滑鼠／觸控、可攜式存檔與主迴圈。
- `assets/empire-dawn.jpg`：主選單原創背景。
- `assets/empire-icon.png`：頁籤圖示。
- `assets/isometric-material-atlas.jpg`：戰場使用的原創地表材質圖集（保留既有檔名以相容舊版）。
- `assets/medieval-terrain-atlas-v2.png`：草地、水面、泥土與石材的高解析度中世紀地表圖集。
- `assets/generated/units-common.png`：村民、斥候與共用軍事單位透明圖集。
- `assets/generated/units-unique-a.png`、`units-unique-b.png`：十三文明特色單位透明圖集。
- `assets/generated/buildings-common.png`、`buildings-advanced.png`：完整建築透明圖集。
- `assets/generated/environment.png`：樹木、資源、王旗、工地與營火圖集。
- `assets/generated/effects-ui.png`：攻擊、爆炸、選取、資源與指令圖示的螢幕混合圖集。
- `assets/source/empire-dawn-master.png`：高解析度主視覺來源檔。
- `assets/source/isometric-material-atlas-master.png`：高解析度材質來源檔。

## 維護原則

腳本使用依序載入的傳統 JavaScript，而不是 ES Module。這是刻意設計：Chrome 對 `file://` 載入模組有跨來源限制，傳統腳本可維持雙擊即玩的體驗。若加入新的腳本檔，請在 `index.html` 底部依相依順序載入。

所有玩家可見文字均使用繁體中文；CSS 與 Canvas 文字不得低於 12px。Imagegen 圖集、字型與音效皆不依賴網路，其中音樂與音效由 Web Audio 即時合成。WebGL2 僅用於透明氣氛特效；無法建立繪圖環境時會自動隱藏並保留完整 Canvas 戰場與程序繪圖後備。

## 遊戲內容與操作

- 可選原版《Age of Empires II: The Age of Kings》的十三個歷史文明、二至四方混戰，以及休閒、征戰、霸主、天命四種 AI 難度。
- 村民會依伐木、採食、採金、採石與施工使用不同工具動畫，目標也會同步震動、揚塵或噴出碎屑；施工、生產、時代晉升、奇觀倒數、軍令、再生與據點攻防皆有持續進度動畫。
- 每個單位都有深色接地陰影、隊色輪廓、亮邊與貼圖陰影，在草地、泥土、石地和密集建築旁仍能清楚辨識。
- 「新手教學」提供 13 個可略過、可保存進度的互動課程，涵蓋經濟、建造、生產、科技、戰術、軍令、勝利與存檔。
- 左鍵點選或拖曳框選；同一位置有多名單位時，連續點按會逐一切換。快速按一下右鍵可移動、攻擊、採集與協助建造。
- 按住滑鼠右鍵拖曳地圖；WASD 或方向鍵也可移動視角；滾輪縮放。滑鼠中鍵不使用。
- 戰場頂列的「全螢幕」按鈕可進入或離開 Chrome 原生全螢幕模式。
- A 攻擊移動、S 停止、F 文明軍令、H 返回城鎮中心。
- Ctrl＋1～4 建立編隊；1～4 召回編隊；空白鍵暫停。

## 存檔與攜帶

- 戰局每 30 秒自動保存，返回主選單或關閉頁面時也會保存。
- 主選單的「繼續戰局」會讀取同一路徑、同一個 Chrome 瀏覽器中的存檔。
- 暫停選單可手動保存、載入，或將 v4 戰局匯出為 JSON 檔；玩家數、所有 AI、教學進度與 2D 俯視鏡頭都會保留，舊版文明與投影存檔會自動遷移至目前規則。
- 「匯入存檔」可在主選單或暫停選單使用；JSON 會先經版本、文明、地圖尺寸、資源與實體資料驗證。
- 若移動整個遊戲資料夾、重新命名路徑或更換電腦，建議先匯出 JSON，再於新位置匯入。

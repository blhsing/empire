# 帝國餘燼：百族爭霸

《帝國餘燼》是以《世紀帝國 II：帝王世紀》為靈感、介面全繁體中文的 2D 即時戰略遊戲。主要版本已改寫為 .NET 10／C# 與 MonoGame DesktopGL：遊戲規則以固定步長執行，戰場使用 GPU 批次繪圖，同時完整沿用原有的 Imagegen 單位、建築、地形、環境與特效圖集。舊版 HTML／Canvas／WebGL2 仍保留，可直接以 Chrome 離線開啟。

遊戲包含十三個歷史文明、二至四名玩家、四種 AI 難度、四個時代、經濟與建造、生產與戰鬥、文明軍令、據點、奇觀勝利、戰爭迷霧，以及可保存進度的新手教學。所有遊戲內文字皆為繁體中文，顯示字級不低於 12px；音樂、環境聲與操作音效由程式即時合成，不需串流音訊。

## 執行原生版

從原始碼執行需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。首次建置會還原 MonoGame DesktopGL 與 FontStashSharp 套件，因此需要網路；套件還原完成後，遊戲本身與所有素材都可離線執行。

在 Windows PowerShell 中切換到專案根目錄後執行：

```powershell
.\run-native.ps1
```

腳本預設使用 `Release` 組態；若要偵錯：

```powershell
.\run-native.ps1 -Configuration Debug
```

也可以直接使用 .NET CLI：

```powershell
dotnet run --project .\src\Empire.Game\Empire.Game.csproj --configuration Release
```

## 發佈免安裝 SDK 的版本

下列命令會建立含 .NET 執行環境、遊戲程式與完整 `assets` 資料夾的自包含 Windows x64 版本：

```powershell
.\publish-native.ps1
```

輸出位於 `artifacts/win-x64`。也可指定支援的執行平台：

```powershell
.\publish-native.ps1 -Runtime win-x64
.\publish-native.ps1 -Runtime linux-x64
.\publish-native.ps1 -Runtime osx-x64
.\publish-native.ps1 -Runtime osx-arm64
```

將對應的整個 `artifacts/<執行平台>` 資料夾複製到另一部電腦即可遊玩；目標電腦不必安裝 .NET SDK。請勿只複製執行檔，因為圖集與字型會一併置於輸出資料夾的 `assets` 下。

## 操作方式

- 左鍵點選單位；按住左鍵拖曳可框選我方單位。按住 Shift 點選可加入或移除選取；連按同類單位可選取畫面內的同類單位。同一位置重疊多個目標時，連續點按會優先在單位間輪替。
- 快速按一下右鍵會依目標自動移動、攻擊、採集或協助施工；按住右鍵拖曳可平移地圖。遊戲不使用滑鼠中鍵。
- WASD 或方向鍵可平移視角；游標進入戰場邊緣也可捲動；滑鼠滾輪縮放。小地圖可直接定位視角。
- `R` 進入攻擊移動模式，`X` 停止所選單位，`F` 發動文明軍令，`H` 返回城鎮中心；這些按鍵不會與 WASD 視角控制衝突。
- `Shift+1`～`Shift+4` 建立編隊，`1`～`4` 選取並移至編隊；這能避開 Chrome 的 `Ctrl+數字` 分頁快捷鍵。`Space` 暫停或繼續，`Esc` 取消目前命令或開啟暫停選單。
- `F5` 立即保存，`F6` 匯出可攜式存檔，`F7` 匯入最新的可攜式存檔。
- `F11` 或介面中的「全螢幕」按鈕可切換全螢幕；主選單、暫停選單與戰場皆支援切換。

遊戲啟動時，鏡頭會直接置中於玩家城鎮，不會因預設游標位置自動平移。「新手教學」會逐步引導鏡頭、選取、經濟、建造、生產、時代、戰鬥、軍令、勝利與存檔機制。

## 持續保存、匯出與匯入

原生版會每 30 秒自動保存；正常離開尚未結束的戰局時也會保存。主選單的「繼續戰局」會讀取跨啟動工作階段保留的自動存檔。

Windows 的預設資料位置如下：

- 自動存檔：`%LOCALAPPDATA%\帝國餘燼\saves\autosave-v4.json`
- 偏好設定：`%LOCALAPPDATA%\帝國餘燼\preferences.json`
- 可攜式存檔：`%USERPROFILE%\Documents\帝國餘燼\存檔\帝國餘燼-存檔-YYYYMMDD-HHmmss.json`

Linux 與 macOS 會使用 .NET 對應的本機應用程式資料夾與文件資料夾。自動存檔會保留玩家數、所有 AI、文明、難度、鏡頭、戰爭迷霧、命令及教學進度；讀取器可遷移本專案既有的 v1～v4 JSON 存檔。

暫停選單可手動保存、載入、匯出或匯入。選擇「匯入存檔」時，遊戲會先載入文件存檔資料夾中最近修改的 JSON；若資料夾中沒有 JSON，介面會提示把檔案拖放到遊戲視窗。也可以在任何畫面把 JSON 拖入視窗，或在啟動原生程式時傳入 JSON 檔案路徑。匯入成功後會立即更新自動存檔，方便下次繼續。

## 專案結構

- `Empire.slnx`：原生遊戲、共用規則與冒煙測試的 .NET 解決方案。
- `src/Empire.Core/`：不依賴繪圖框架的文明／兵種／建築資料、世界生成、固定步長模擬、尋路、AI、經濟、戰鬥、迷霧、教學、勝負與 v1～v4 存檔遷移。
- `src/Empire.Game/EmpireGame.cs`：原生應用程式生命週期、畫面流程、固定步長整合、自動存檔、匯入／匯出與全螢幕。
- `src/Empire.Game/Graphics/`：圖集載入與精靈座標對應。
- `src/Empire.Game/Rendering/`：GPU 戰場繪製、可見範圍裁切、動畫、特效、迷霧及小地圖。
- `src/Empire.Game/Input/`：鏡頭、重疊單位選取、編隊、建造及即時戰術命令。
- `src/Empire.Game/Ui/`：主選單、HUD、指令臺、教學、指南、暫停與勝負介面。
- `src/Empire.Game/Platform/`：繁體中文字型、程序音訊與跨工作階段偏好設定。
- `tests/Empire.Core.Smoke/`：不啟動視窗的核心規則、存檔與長時間多方模擬測試。
- `assets/`：原有主視覺、地形、Imagegen 單位／特色單位／建築／環境／特效圖集，以及高解析度來源檔；原始圖片均予保留，建置與發佈時會原樣複製。
- `index.html`、`css/`、`js/`：仍可直接離線執行的舊版瀏覽器客戶端。
- `tests/verify.mjs`、`tests/smoke-runtime.mjs`：舊版瀏覽器客戶端的靜態與無頭執行驗證。

## 測試

在專案根目錄依序執行：

```powershell
dotnet build .\Empire.slnx --configuration Release
dotnet run --project .\tests\Empire.Core.Smoke\Empire.Core.Smoke.csproj --configuration Release
dotnet run --project .\src\Empire.Game\Empire.Game.csproj --configuration Release -- --smoke
node .\tests\verify.mjs
node .\tests\smoke-runtime.mjs
```

第三個命令會短暫建立真正的 MonoGame 視窗、載入全部資產並進入戰場，約兩秒後自行結束。最後兩個命令需要 Node.js，僅用來確保保留的瀏覽器版本仍可離線載入並通過規則與渲染後備測試。

## 舊版瀏覽器客戶端

若不執行原生版，仍可在 Chrome 直接雙擊開啟 `index.html`，不需要伺服器。此版本使用依序載入的傳統 JavaScript，以避免 `file://` 的 ES Module 跨來源限制；Canvas 負責完整 2D 戰場，WebGL2 僅增加不攔截操作的氣氛特效，無法建立 WebGL2 時會保留 Canvas 後備路徑。

瀏覽器版的存檔位於目前頁面路徑所對應的 Chrome `localStorage`，與原生版的自動存檔位置不同；若要換路徑、瀏覽器或電腦，請先從暫停選單匯出 JSON，再於新環境匯入。瀏覽器版同樣以右鍵拖曳平移地圖，不使用滑鼠中鍵。

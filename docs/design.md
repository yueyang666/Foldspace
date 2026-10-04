# Foldspace 設計說明

給想了解或修改程式的人。使用方式見 [README](../README.md)。

## 專案結構

```
Foldspace.slnx
Directory.Build.props        版本號與共用的組件資訊
src/
  Foldspace.Core/            協定、傳輸引擎、配對、設定（net10.0，不綁 Windows）
    Protocol/                  訊息定義、控制通道框格式、狀態代碼
    Identity/                  自簽憑證（ECDSA P-256）與指紋
    Pairing/                   6 位數配對碼（承諾/公開亂數）
    Net/                       PeerService：監聽、控制通道、心跳、重連、配對、傳輸協調
    Transfer/                  掃描、資料通道格式、接收暫存區、同名策略、路徑安全
    Settings/                  settings.json 的讀寫與驗證
  Foldspace.Localization/    介面文字：英文、繁體中文、簡體中文（net10.0，可測試）
  Foldspace.App/             WinForms：系統匣、設定/進度/配對視窗、通知、單一執行個體、
                             睡眠與網路事件、應用程式登記與解除安裝
    Assets/                    程式圖示（Foldspace.svg 為原始檔，Foldspace.ico 由它算圖產生）
tests/
  Foldspace.Core.Tests/      xUnit；整合測試在同一台機器用兩個 port 跑兩個實例
```

Core、Localization 與測試都是跨平台的，macOS / Linux 也能建置和測試。App 只能在 Windows 執行，但可以在其他平台編譯（`EnableWindowsTargeting`）。

## 版本號

產品版本採 SemVer（`主.次.修`），定義在 `Directory.Build.props` 的 `FoldspaceVersion`，只在正式發佈時才改：

| 用在哪裡 | 例子 |
| --- | --- |
| GitHub Release 標籤、發佈檔名 | `v1.0.0`、`Foldspace-1.0.0-win-x64.exe` |
| exe 的「產品版本」、設定視窗、「設定 > 應用程式」 | `1.0.0` |
| exe 的「檔案版本」（Windows 規定四段數字） | `1.0.0.0` |
| 測試建置的完整版本：exe 的「產品版本」、日誌、握手、設定視窗版本號的滑鼠提示 | `1.0.0+a3f9c2e`（提交編號）或 `1.0.0+202610041530`（建置時間） |

測試建置由本機的建置腳本以 `SourceRevisionId` 帶入建置編號：git 裡沒有未提交的修改時用提交編號，否則用建置時間，產品版本不變。正式發佈的 exe 不附加建置編號，產品版本就是 `1.0.0`。

**發佈**：把 `FoldspaceVersion` 加 1 並提交，再到 GitHub 的 Actions 頁面選「Release」→「Run workflow」。流程會跑測試、建置、在目前的提交打上 `v<版本>` 標籤並建立 Release；這個版本已經發佈過就直接失敗。

協定版本（`ProtocolConstants.ProtocolVersion`，目前 1.0）另外管理，主版號不同的兩台會顯示「版本不相容」。

## 檔案與系統位置

| 項目 | 位置 |
| --- | --- |
| 設定 | `%AppData%\Foldspace\settings.json`（損毀時備份成 `settings.bad.json`） |
| 設備憑證 | `%AppData%\Foldspace\identity.pfx.dpapi`（DPAPI 保護，不放在設定檔裡） |
| 日誌 | `%AppData%\Foldspace\logs\foldspace-YYYYMMDD.log`，一律英文，保留 14 天，單檔 10 MB |
| 接收暫存 | `<接收資料夾>\.foldspace-tmp\<jobId>\`（隱藏；程式啟動時全部清除） |
| 桌面捷徑 | `Foldspace.lnk`，圖示是 Windows 內建的資料夾（`imageres.dll,3`） |
| 應用程式登記 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Foldspace`（「設定 > 應用程式」） |
| 開始功能表 | `%AppData%\Microsoft\Windows\Start Menu\Programs\Foldspace\` |
| 開機啟動 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Foldspace` |
| AppUserModelID | `yueyang.Foldspace` |
| 防火牆規則 | 名稱 `Foldspace`：輸入、允許、只限本程式、所有通訊協定、所有網路設定檔、來源 `LocalSubnet` |

全部寫在目前使用者底下，不需要系統管理員權限。應用程式登記與開始功能表每次啟動都會重寫，exe 被移動或換了介面語言時會跟著更新。

**AppUserModelID**：程式一啟動就指定，開始功能表的程式捷徑帶同一個 ID，桌面捷徑不帶。沒有指定時，Windows 會把指向同一個 exe 的桌面捷徑當成程式本身，工作列按鈕與通知都會顯示資料夾圖示。

## 解除安裝

從「設定 > 應用程式」、開始功能表，或執行 `Foldspace.exe --uninstall`。程式執行中時由執行中的那一份處理。確認後依序：

1. 已連線時通知對方解除配對（對方顯示「未配對」），停止服務。
2. 防火牆裡有屬於 Foldspace 的規則時，透過 UAC 以系統管理員身分執行 `netsh` 刪除；使用者拒絕就保留，並在完成訊息裡說明如何手動刪除。
3. 移除開機啟動、桌面捷徑、開始功能表、應用程式登記、通知註冊、接收資料夾裡的暫存資料夾。
4. 刪除 `%AppData%\Foldspace`（設定、設備憑證、日誌）。
5. 結束後由隱藏的 PowerShell 刪除 exe、`%TEMP%\.net\Foldspace`（單一檔案執行時解壓縮的原生程式庫），以及 exe 所在的資料夾（只限名稱是 Foldspace 且已空）。

接收資料夾裡的檔案一律保留。

## 協定摘要

每台電腦只監聽一個 TCP port（預設 52500），每條連線的第一個位元組決定類型：

| 標記 | 用途 | 加密 |
| --- | --- | --- |
| `0x01` | 控制通道 | 永遠 TLS（雙向自簽憑證），身分在應用層以指紋驗證 |
| `0x02` | 資料通道，加密 | TLS，連線後先送 16 bytes jobId |
| `0x03` | 資料通道，不加密 | 先送 32 bytes 一次性 token（60 秒、用過即失效） |

- HELLO / HELLO_ACK 帶 `listenPort`（TCP 監聽 port）：由對方發起配對時，本機靠它連回去。
- **同網段搜尋（UDP 52500）**：搜尋端往綁定網卡所在網段的廣播位址與 255.255.255.255 送 `FOLDSPACE_DISCOVER`（送 3 次，等 2 秒），每台 Foldspace 單播回 `FOLDSPACE_HERE`：電腦名稱、憑證指紋、版本、TCP 監聽 port、是否已配對、是否已和搜尋端配對。內容都是公開資訊；回應每秒最多 20 次。訊息定義在 `Protocol/DiscoveryMessages.cs`。
- 控制訊息 = 4 bytes big-endian 長度 + UTF-8 JSON（上限 1 MB）。
- 網路上只傳狀態代碼（例如 `"reason": "insufficientSpace"`），不傳任何顯示用的文字，由收到的一方依自己的介面語言顯示。代碼定義在 `Protocol/Codes.cs`。
- 資料通道的紀錄格式見 `Transfer/DataStreamFormat.cs`：項目標頭 → 分段內容（≤ 1 MB）→ XxHash3，或「放棄」紀錄。一輪送完後，接收端回覆要重傳的檔案（雜湊不符），最多重傳一次。
- 接收端先寫到暫存區，驗證通過才移到接收資料夾；接收資料夾裡永遠不會出現不完整的檔案。

## 設計決策

1. **.NET 10 LTS**，單一 exe、內含 runtime，不需要安裝 .NET。
2. **TLS 1.2 以上自動協商**，不強制 TLS 1.3。Windows 10 的 Schannel 不支援 TLS 1.3 server；Win11 對 Win11 時會用 1.3。
3. **配對碼加上承諾/公開亂數。** 如果只用兩個指紋算出 6 位數，中間人產生約 1000 組金鑰就能讓兩端數字相同。流程是
   `PAIR_REQUEST{commitment=SHA256(Na)}` → `PAIR_NONCE{Nb}` → `PAIR_REVEAL{Na}`，
   配對碼 = SHA-256(排序後的兩個指紋 ‖ Na ‖ Nb) 取 6 位數。
4. **任一端開啟加密就加密**，兩端設定不同不算錯誤。
5. **`TRANSFER_OFFER` 帶 `items[]`**（每個頂層項目的名稱、類型、大小、檔案數）。一次可以拖入多個項目，同名策略以頂層項目判斷。
6. **重傳在資料通道內完成**，不另外增加控制訊息。
7. **檔案內容分段傳送。** 傳送中來源檔案大小改變時，送出「放棄」紀錄，該檔列為失敗，其他檔案繼續傳。
8. **從搜尋結果配對。** 按「配對…」搜尋同網段的電腦，選一台後本機改連那台並發起配對。為此，**本機還沒有配對時接受同網段任何電腦的連入與配對請求**（已配對後只認指紋）。配對視窗同時只有一個；本機拒絕或逾時的電腦 30 秒內再發起會直接回「忙碌」。由對方發起的配對成功後，本機從那條連線學到對方的 IP 與 `listenPort`，改連回對方並存回設定。
    **對方換了 IP**：已配對但連不上時每 30 秒搜尋一次，找到同一個指紋在新的位址就改連過去。搜尋結果沒有經過驗證，但連線時仍以 TLS 指紋確認身分，假冒的回應最多讓連線暫時失敗。
9. **單向連線提醒。** 本機能連到對方、但對方連不進來超過 20 秒時（通常是本機防火牆），「已連線」下方會顯示警告。
10. **收到 `UNPAIR` 時雙方都清除配對。**
11. **取消與中斷分開。** 有人主動取消（本機或對方使用者、停用服務、解除配對）顯示「已取消」；對方離線、連線中斷、磁碟寫滿顯示「失敗」。接收中 60 秒完全沒有資料、或送出任務的控制連線中斷，都會結束接收任務（資料連線可能是半開的）。
12. **介面支援英文、繁體中文、簡體中文**，預設跟隨 Windows 顯示語言。可在 `settings.json` 用 `"language": "en" | "zh-Hant" | "zh-Hans" | "auto"` 覆寫，重新啟動後生效。
13. **介面只用 Windows 原生元件**（WinForms、TaskDialog、系統圖示），外觀和 Windows 內建工具一致。對話框用 TaskDialog 而不用 MessageBox，按鈕文字才會跟著程式的語言。
14. **免安裝但可以解除安裝**，見上方「解除安裝」。
15. **自動設定防火牆。** 啟動時先讀取防火牆規則（不需要權限）。規則不對時，在開始監聽之前跳 UAC，由提升權限的子程序（`--setup-firewall`）用 `netsh` 刪掉這個 exe 的所有舊規則，包括使用者曾在 Windows 詢問視窗按「取消」時留下的封鎖規則，再加上 Foldspace 的規則。規則只允許同一個子網路連入，所以公用網路也能開放。使用者按「否」時，記住這個 exe 不再每次詢問（`%AppData%\Foldspace\firewall-declined`），可以在設定的「連線」分頁重試。

## 開發

### 在同一台 Windows 上跑兩個實例

`--profile <名稱>` 會使用獨立的設定資料夾、單一執行個體鎖、捷徑名稱與 AppUserModelID：

```bat
Foldspace.exe --profile a
Foldspace.exe --profile b
```

兩個實例設成不同的本機 port，並且綁定同一張網卡。設定視窗不允許「對方 IP」等於本機 IP，所以要直接編輯 `%AppData%\Foldspace\profiles\<名稱>\settings.json`，把 `peerIp` 填成本機 IP，`peerPort` 填成另一個實例的 port。

### 單一 exe 的注意事項

- `Assembly.Location` 在單一 exe 內是空字串。程式所在位置請用 `Environment.ProcessPath` 或 `AppContext.BaseDirectory`。
- 設定、日誌、配對資料寫到 `%AppData%\Foldspace\`，不寫在 exe 旁邊；使用者可能把 exe 放在唯讀位置或「下載」資料夾。
- WinForms 不支援 trimming，不要開 `PublishTrimmed`。

## 已知限制

- **「測試連線」無法分辨對方停用服務和被防火牆阻擋。** Windows 防火牆對沒有程式在監聽的連接埠直接丟棄連線，兩者看起來都是逾時，訊息會把兩種可能都列出來。
- **搜尋靠廣播，出不了子網路。** 開啟「用戶端隔離」的訪客 Wi-Fi 也收不到，這時要在「連線」分頁手動輸入對方 IP。
- **小檔案很多時，Windows Defender 的即時掃描是主要瓶頸。** 每個新檔案都會被掃描，一萬個小檔案可能慢上數倍。
- **接收資料夾無法使用時只會拒絕傳輸並通知**，設定視窗的狀態列不會顯示。
- **TLS 加密套件由 Windows 決定。** .NET 在 Windows 上不能限制加密套件，Win10 / 11 預設都是 ECDHE + AES-GCM。
- **exe 沒有程式碼簽章。** 第一次執行會出現 SmartScreen 警告，解除安裝時的 UAC 會顯示「未知的發行者」。
- **開始功能表的「解除安裝 Foldspace」捷徑不會出現在「所有應用程式」清單**（Windows 會隱藏名稱含「解除安裝」的捷徑），只能用搜尋找到。也可以在「Foldspace」上按右鍵選「解除安裝」。

\### 2. 繁体中文版 (`README.zh-TW.md`)



```markdown

<div align="right">

&#x20; <strong>語言 / Language:</strong>

&#x20; <a href="./README.md">English</a> | 繁體中文 | <a href="./README.zh-CN.md">简体中文</a>

</div>



\# CMP 30HX · Magpie 社區版 DLSSNR 一鍵相容性修補程式



本專案為 \*\*NVIDIA CMP 30HX\*\*（TU116 架構，無物理 Tensor Core）提供自動化相容修補程式與專用載入器，使其能夠在 \[SAOG0721/Magpie](https://github.com/SAOG0721/Magpie) 社區版中成功初始化並執行 DLSSNR（Feature 18）影像濾鏡\[cite: 11]。



> \*\*重要說明\*\*：本專案僅針對該軟體包中的 \*\*DLSSNR / Feature 18 影像濾鏡路徑\*\*\[cite: 11]。現有驗證不代表在 CMP 30HX 上實現了完整 DLSS5 功能集（例如獨立超解析度、光線重建或所有插格能力）\[cite: 11]。



\---



\## 一、 技術實現原理



CMP 30HX 在硬體層面缺少物理張量核心（Tensor Cores），且無官方顯示輸出\[cite: 11]。預設狀態下呼叫 DLSS 相關組件會遭遇多層軟硬體校驗阻斷\[cite: 11]：



1\. \*\*硬體白名單攔截繞過\*\*：

&#x20;  \* 原始 `\_nvngx.dll` 在初始化階段會檢查 GPU 架構，偵測到不支援的硬體時直接回傳 `0xbad00001`（`unsupported hw detected`）\[cite: 11]。

&#x20;  \* 本方案透過定位並修補核心二進位檔案中的 3 處條件跳轉分支（將 `0F 85` 修改為強制跳轉 `E9`，保留原跳轉目標），繞過硬體攔截邏輯\[cite: 11]。

&#x20;  \* 此過程不添加物理運算單元，亦未重新訓練模型\[cite: 11]。

2\. \*\*Magpie 簽名校驗繞過\*\*：

&#x20;  \* 修改後的 `\_nvngx.dll` 因雜湊改變，會被 Magpie 原生載入器以 `0x80096010`（`TRUST\_E\_BAD\_DIGEST`）拒絕，並退回系統驅動再次觸發錯誤\[cite: 11]。

&#x20;  \* 本工具透過在 `Magpie.exe` 中注入新增的 `.hxload` 節區，將本機 NGX 載入呼叫重定向至配套的 `30HXCoreLoader.dll`\[cite: 11]。

3\. \*\*固定路徑與 SHA256 鎖定\*\*：

&#x20;  \* `30HXCoreLoader.dll` 使用 Windows BCrypt 密碼學 API 計算並嚴格校對本機 `\_nvngx.dll` 的 SHA256 摘要\[cite: 11]。

&#x20;  \* 只有與已知相容雜湊完全符合時才會呼叫 `LoadLibraryExW`，既解除了系統簽名限制，又杜絕了任意未知 DLL 的注入風險\[cite: 11]。

4\. \*\*管線對接與輸出\*\*：

&#x20;  \* 沿用上游 Magpie 已有的 snippet 直接載入邏輯與模組偽裝機制，在 D3D12 管線中完成 Feature 18 建立、Evaluate 執行與 GPU Fence 同步呈現\[cite: 11]。



\---



\## 二、 支援版本與驗證狀態



本工具內建嚴格的指紋核對，僅支援 `versions.json` 列出的已知版本，\*\*不支援 Blinue 官方原版，亦不盲目匹配未來未知版本\*\*\[cite: 11]。



| Magpie 版本 | 適配狀態 | 對應主包下載 | 說明 |

| :--- | :--- | :--- | :--- |

| \*\*v0.6.11\*\* (實驗版) | 獨立 D3D12 測試通過\[cite: 11] | \[0.6.11 發布頁](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.11-experimental) · \[完整 ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.11-experimental/Magpie-Experimental-x64.zip)\[cite: 11] | 新版適配，內建 310.9.16 模型已過獨立驗證\[cite: 11] |

| \*\*v0.6.10\*\* (穩定版) | 應用內完整實測通過\[cite: 11] | \[0.6.10 發布頁](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.10-experimental) · \[完整 ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.10-experimental/Magpie-Experimental-x64.zip)\[cite: 11] | 經驗證持續穩定執行，支援三風格平滑切換\[cite: 11] |



\### 已知檔案指紋（SHA256）



\* \*\*NVIDIA 驅動核心\*\*（已驗證驅動 616.92）\[cite: 11]：

&#x20; \* 原始 `\_nvngx.dll`：`91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa`\[cite: 11]

&#x20; \* 修補後 `\_nvngx.dll`：`cf36298fb8bdcf46e0e5d2f558f9132f9aff86a95db5ce5ab849fbacb4083702`\[cite: 11]

\* \*\*已識別的社區模型\*\*\[cite: 11]：

&#x20; \* `310.8.2.0`：`6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927`\[cite: 11]

&#x20; \* `310.9.16.0`：`a8a237a967469c455a84654932b513ab656fbaa1e0c44504b1e1bf0890f902b8`\[cite: 11]



\---



\## 三、 使用教學



修補包內含全自動腳本，\*\*無需設定 Python、Visual Studio 或手動編譯\*\*，直接使用原生 PowerShell 5.1 執行\[cite: 11]。



\### 步驟 1：準備軟體

1\. 下載對應的社區版壓縮包（如 `v0.6.11-experimental`）並解壓到一個新目錄（例如 `D:\\Magpie-30HX\\`）\[cite: 11]。

2\. 確保已完全結束正在執行的 Magpie\[cite: 11]。



\### 步驟 2：一鍵安裝修補程式

1\. 解壓本修補包，雙擊執行 \*\*`Install-30HX.cmd`\*\*\[cite: 11]。

2\. 在彈出的檔案選擇器中，定位並選取新解壓目錄下的 `Magpie.exe`\[cite: 11]。

3\. 安裝腳本將自動執行以下操作\[cite: 11]：

&#x20;  \* 自動在系統 DriverStore 中唯讀檢索受支援的 `\_nvngx.dll` 核心（若未找到，腳本會提示您手動指定備份核心，絕不修改系統驅動）\[cite: 11]。

&#x20;  \* 在應用程式本機產生專用的相容核心副本\[cite: 11]。

&#x20;  \* 注入 `.hxload` 節區並部署 `30HXCoreLoader.dll`\[cite: 11]。

&#x20;  \* 核對社區 DLSSNR 模型指紋\[cite: 11]。

4\. 終端提示 \*\*`INSTALL\_OK`\*\* 即表示安裝成功\[cite: 11]。



\### 步驟 3：推薦參數設定

1\. 啟動已修補的 `Magpie.exe`\[cite: 11]。

2\. 在 \*\*效果組\*\* 中新建或編輯設定項\[cite: 11]：

&#x20;  \* 加入濾鏡：`DLSSNR\_AI\_Filter`\[cite: 11]

&#x20;  \* \*\*輸入縮放\*\*：將寬高各設為 \*\*25%\*\*（像素量為原圖 6.25%，平衡 TU116 算力開銷）\[cite: 11]

&#x20;  \* \*\*光流設定\*\*：初始測試請先\*\*關閉光流\*\*\[cite: 11]

&#x20;  \* \*\*內容幀率\*\*：在「畫面播放速率與重新整理」中設定較低的目標（如 10–15 FPS）起步測試\[cite: 11]

3\. 啟動縮放後，在日誌中確認初始化狀態，並嘗試切換三種濾鏡風格\[cite: 11]。



\---



\## 四、 維護與檢查工具



\* \*\*完整性檢查 (`Check-30HX.cmd`)\*\*：雙擊執行並選取目標 `Magpie.exe`，腳本會比對主程式、本機核心、載入器與模型的 SHA256，核對備份是否完整\[cite: 11]。

\* \*\*一鍵還原 (`Restore-30HX.cmd`)\*\*：結束 Magpie 後執行此腳本，將自動刪除新增的 `30HXCoreLoader.dll` 和本機核心，並從原始備份目錄（`.30hx-compat-backup-v1`）完整還原未修補的 `Magpie.exe`\[cite: 11]。



\---



\## 五、 原始碼與手動編譯



倉庫提供完整的注入引擎與載入器原始碼，供稽核與二次建置\[cite: 11]：



```text

Magpie-30HX-Compat/

├── src/

│   ├── PatchEngine.cs         # C# 編寫的 PE 注入、節區擴充與雜湊修補引擎

│   └── pinned\_loader.cpp      # 基於 Windows BCrypt 實現的固定雜湊核心載入代理

├── Install.ps1                # 自動化安裝與檢索邏輯

├── versions.json              # 支援的 EXE、核心與模型指紋清單

├── CHECKSUMS.json             # 修補包自身的雜湊清單

├── Install-30HX.cmd           # 一鍵安裝入口

├── Check-30HX.cmd             # 一鍵校驗入口

└── Restore-30HX.cmd           # 一鍵解除安裝與還原入口


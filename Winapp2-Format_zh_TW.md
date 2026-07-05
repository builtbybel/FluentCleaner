# Winapp2.ini 格式

FluentCleaner 讀取 winapp2.ini 檔案完整速查手冊

---

## 條目範例

```ini
[App Name *]
LangSecRef=3021
Detect=HKLM\Software\MyApp
DetectFile=%LocalAppData%\MyApp
SpecialDetect=DET_CHROME
Warning=此操作會刪除已儲存密碼
Default=False
FileKey1=%AppData%\MyApp|*.log;*.tmp
FileKey2=%AppData%\MyApp\Cache|*|REMOVESELF
RegKey1=HKCU\Software\MyApp\MRU
ExcludeKey1=FILE|%AppData%\MyApp\|important.db
```

---

## 偵測規則 Detection

至少填寫一條偵測欄位，否則該條目會完全隱藏。
多條 `Detect` / `DetectFile` 為**或邏輯**，任一條符合即判定軟體已安裝。

| 欄位 | 格式 | 偵測內容 |
|---|---|---|
| `Detect` | `HKLM\Software\Foo` | 偵測指定登錄機碼是否存在 |
| `Detect` | `HKLM\Software\Foo\|Value` | 偵測登錄機碼下指定值是否存在 |
| `DetectFile` | `%LocalAppData%\MyApp` | 偵測檔案或資料夾是否存在 |
| `DetectFile` | `%LocalAppData%\Chrome*` | 路徑支援通配字元比對 |
| `SpecialDetect` | `DET_CHROME` | 主流軟體快速偵測識別碼（見下表） |

### SpecialDetect 內建識別碼對照表

| 識別碼 | 偵測路徑 |
|---|---|
| `DET_CHROME` | `%LocalAppData%\Google\Chrome\User Data` |
| `DET_FIREFOX` | `%AppData%\Mozilla\Firefox` |
| `DET_EDGE` | `%LocalAppData%\Microsoft\Edge\User Data` |
| `DET_OPERA` | `%AppData%\Opera Software\Opera Stable` |
| `DET_THUNDERBIRD` | `%AppData%\Thunderbird` |
| `DET_IE` | IE 瀏覽器對應登錄檔路徑 |
| `DET_WINSTORE` | `%LocalAppData%\Packages` Microsoft Store 目錄 |

---

## FileKey

```
FileKeyN=<路徑>|<比對規則>[|RECURSE|REMOVESELF]
```

| 寫法類型 | 範例 | 執行邏輯 |
|---|---|---|
| 路徑 + 檔案通配字元 | `%Temp%\MyApp\|*.tmp` | 僅比對資料夾第一層目錄內的檔案 |
| 多重比對規則 | `%Temp%\|*.log;*.tmp;*.bak` | 以分號分隔，比對全部副檔名檔案 |
| RECURSE 遞迴 | `%AppData%\MyApp\|*.log|RECURSE` | 遞迴遍歷所有子目錄並比對 |
| REMOVESELF 清理空目錄 | `%AppData%\MyApp\Cache\|*|REMOVESELF` | 刪除檔案後自動移除空資料夾 |
| 無比對規則、僅帶標記 | `%AppData%\MyApp\Cache\|REMOVESELF` | 預設比對 `*.*`，標記仍會生效 |

### 內建路徑變數對照表

| 變數名稱 | 實際解析路徑 |
|---|---|
| `%AppData%` | `C:\Users\使用者名稱\AppData\Roaming` |
| `%LocalAppData%` | `C:\Users\使用者名稱\AppData\Local` |
| `%LocalLowAppData%` | `C:\Users\使用者名稱\AppData\LocalLow` |
| `%ProgramData%` / `%CommonAppData%` | `C:\ProgramData` |
| `%ProgramFiles%` | `C:\Program Files`，程式會自動相容 x86 目錄 |
| `%ProgramFiles(x86)%` / `%ProgramFilesX86%` | `C:\Program Files (x86)` |
| `%UserProfile%` | `C:\Users\使用者名稱` |
| `%SystemRoot%` / `%WinDir%` | `C:\Windows` |
| `%System%` | `C:\Windows\System32` |
| `%Temp%` / `%Tmp%` | 目前使用者暫存資料夾 |
| `%SystemDrive%` | 系統磁碟機代號，例如：`C:` |
| `%Documents%`、`%Desktop%`、`%Music%`、`%Pictures%`、`%Videos%` | 系統對應的常用個人資料夾 |

路徑分段內支援通配字元：
```
%LocalAppData%\Google\Chrome*\User Data\*\Cache
```
掃描時會自動展開並比對所有符合規則的實際目錄。

---

## RegKey

```
RegKeyN=<HIVE>\<path>[\|<value name>]
```

| 寫法類型 | 範例 | 執行邏輯 |
|---|---|---|
| 清理整個登錄機碼 | `HKCU\Software\MyApp\MRU` | 刪除該機碼及所有下層子機碼和值 |
| 僅刪除單一值 | `HKCU\Software\MyApp\|LastRun` | 只移除指定值，保留登錄機碼 |

支援根機碼縮寫：`HKCU`、`HKLM`、`HKU`、`HKCC`、`HKCR`，完整寫法 `HKEY_CURRENT_USER` 等也相容。

---

## ExcludeKey

```
ExcludeKeyN=<類型>|<路徑>\|[<比對規則>]
```

比對到的檔案 / 登錄機碼會直接略過，即使 FileKey / RegKey 命中也不會清理。

| 類型 | 範例 | 保護範圍 |
|---|---|---|
| `FILE` 精確檔名 | `FILE\|%AppData%\MyApp\|config.db` | 僅保護資料夾第一層目錄內的該檔案 |
| `FILE` 檔案通配字元 | `FILE\|%AppData%\MyApp\|*.db` | 僅保護該資料夾第一層目錄內的所有 db 檔案 |
| `PATH` 完整目錄 | `PATH\|%AppData%\MyApp\Profiles\` | 保護整個目錄樹的全部內容 |
| `PATH` 全比對 | `PATH\|%AppData%\MyApp\_Data\|*` | 遞迴保護目錄下所有檔案 |
| `PATH` 帶副檔名通配字元 | `PATH\|%AppData%\MyApp\Cache\|*.db` | 遞迴保護所有子目錄內的 db 檔案 |
| `REG` 登錄檔排除 | `REG\|HKCU\Software\MyApp\` | 排除登錄機碼，檔案掃描階段會略過 |

> `FILE` 僅比對資料夾的直接子檔案。 
> `PATH` 搭配通配字元會覆蓋整個子目錄樹。

---

## 其他設定欄位

| 欄位名稱 | 作用說明 |
|---|---|
| `LangSecRef` | 介面分類編號（例：`3029` 代表 Google Chrome），用於分組顯示 |
| `Section` | 自訂文字分類，當 LangSecRef 沒有對應的內建分類時作為備用分組 |
| `Warning` | 清理前向使用者彈出的風險提示文字 |
| `Default` | `True` / `False`，控制該清理項預設是否勾選 |

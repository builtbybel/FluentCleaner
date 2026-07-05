# FluentCleaner

[English](README.md) | [简体中文](README_zh_CN.md) | **繁體中文**

### 現代化、透明、沒有間諜軟體、沒有恐嚇軟體、沒有暗黑設計、沒有強迫推銷垃圾、沒有假的登錄檔魔法


<img width="1536" height="1024" alt="FluentCleaner" src="FluentCleaner/Assets/Banner.avif" />


_我做了一個自己的清理工具，靈感來自 2006 年左右那個老 CCleaner，只是把它改成今天本來就該有的樣子。現代化（以 WinUI 3 建置）、簡潔，並且專注於真正值得清理的內容，而不是那些常見的亂七八糟東西。_
 
我做這個，是因為你遲早會注意到一種模式。

那些曾經真的很好用的東西……會慢慢變差。小開發者做出一個很棒的東西，公司把它買走，最佳化到面目全非，然後你突然發現，一個簡單工具怎麼就變成了「這到底發生了什麼？」的故事。CCleaner 基本上已經是教科書案例了，大家都知道，沒必要再寫一整段。

有趣的是，CrapCleaner 之所以還能撐下來，很大程度其實是靠它周圍的社群，尤其是像 [winapp2.ini](https://github.com/moscadotto/winapp2) 這樣的簽章庫。這個生態系對工具的貢獻，比大多數官方決策都大。

我懶得把所有清理器都原生重寫一遍，所以乾脆為那種格式寫了一個解析器。結果發現它很快。是真的……意外地快。比我記憶裡老 Piriform 的實作還快（不知道當年為什麼那麼慢，可能是專有格式、過度工程化，或只是歷史因素。反正現在也不重要了）。

UI 是用 WinUI 3 做的，就是微軟那個「漂亮但慢」的框架。結果不知怎麼，它居然還是能跑得比原版快。真有意思。

現在的公司並不是真的在比誰能把東西做得更好。它們比的是誰能在不徹底搞壞的前提下塞進更多噪音。一路下來，「好工具」就變成了「人們懷念的東西」。

CCleaner 曾經很棒。現在它更像是一個警示案例。

總之，我不是想修復整個產業。只是想要一個不爛的東西。也許我以後會膩，也許它會演變成別的東西，然後我們像往常一樣又回到原點。

目前我就把它叫做 **FluentCleaner**。

它本來甚至沒打算公開，但很多真誠友善的人希望我釋出，所以我大概會釋出。
這裡先放一個預覽版，讓你感受一下方向。我之後可能會透過捐款來維持開發，到時候再說。

如果你喜歡，那很好。如果不喜歡，也完全沒問題。

## FAQ

<details>
<summary>這能讓我的電腦變快嗎？</summary>

老實說？看情況，這不是在敷衍。

在一台現代系統、剩餘空間充足的電腦上，你大概不會感受到明顯的速度提升。
但微軟自己也說過，儲存空間不足可能會拖慢系統，甚至阻止 Windows 更新（[來源](https://support.microsoft.com/en-us/windows/free-up-drive-space-in-windows-85529ccb-c365-490d-b548-831022bc9b32)），所以如果你的硬碟快滿了，清理比你想的更重要。

除了速度以外，定期清理也有一些很實際的理由：
- 回收過去幾個月裡悄悄被吃掉的磁碟空間
- 排查由快取損壞導致的應用程式問題
- 縮小備份體積
- 隱私，例如瀏覽器資料、最近開啟檔案清單、解除安裝應用程式後的殘留痕跡
- 保持 Windows 更新順利執行
- 或者只是因為一個整潔的系統讓人感覺更好。這也成立。

微軟建議每月做一次這類清理。儲存空間感知器可以自動完成。
FluentCleaner 只是讓你更精確地控制到底清理什麼。

</details>

</details>

<details>
<summary>winapp2.ini 到底是什麼？</summary>

它是一個由社群維護的 Windows 應用程式清理規則資料庫，
經過 15 年以上累積，包含數千個項目。它會告訴 FluentCleaner 每個應用程式到底要清理什麼：哪些暫存資料夾、哪些快取路徑、哪些登錄檔機碼。
不靠猜，不用掃過整個磁碟的粗暴萬用字元。
每個項目都是明確的、可檢查的、可稽核的。這就是它的重點。

</details>

<details>

<summary>flavors 是什麼？</summary>

winapp2.ini 會根據你使用的工具提供不同變體。
FluentCleaner 使用原版 CCleaner flavor，也就是當年那個工具還值得用時所使用的同一種變體。

</details>

</details>

<details>
<summary>安全嗎？</summary>

它的安全性取決於你啟用了什麼。你不先選擇，它什麼都不會執行。
winapp2.ini 項目只會處理自己被明確指定的目標，
不會搞那種寬泛的「刪除 temp 裡所有東西」的亂來操作。
話雖如此：它確實會刪除檔案。如果某些東西看起來重要，請先備份。

</details>

<details>
<summary>為什麼用 WinUI 3？</summary>

因為現在是 2026 年了，Windows 工具不該看起來像 2009 年做出來的。
而且 Fluent Design 字面上就在名字裡。挺合適。

</details>

<details>
<summary>CCleaner 7 移除了 winapp2.ini 支援，這對 FluentCleaner 代表什麼？</summary>

沒有任何影響。FluentCleaner 有自己的解析器，完全獨立於 CCleaner。
CCleaner 移除支援這件事，老實說也是我做這個工具的動機之一。

</details>

<details>
<summary>我能使用自訂 winapp2 資料庫嗎？</summary>

可以。FluentCleaner 不會鎖定到某一個來源。

像 BBleachBit 這樣的工具（主要是 Linux 清理工具，其實我是透過這個專案才發現它的，
但它的 UI 糟到讓我立刻又退回來了），以及其他工具，都有自己的 winapp2.ini flavor，
也就是針對各自需求略作修改的版本。你可以拿其中任意一個
（或自己建置一個），然後直接接入 FluentCleaner。

只需要把檔案放到系統中的某個位置，然後進入：
**Settings > Database > Custom**，指向你的檔案。就這樣。

winapp2 專案的官方資料庫在這裡：
https://github.com/MoscaDotTo/Winapp2，它由社群維護，
定期更新，涵蓋數千個應用程式。如果你想要比預設規則更多的涵蓋範圍，這是一個很好的起點。

</details>

<details>
<summary>我在哪裡可以追蹤開發進度？</summary>

我會在 **[x/twitter](https://x.com/builtbybel)** 上發布 insider 內容、早期建置版本，以及偶爾吐槽一下 WinUI。如果你想在正式發布前知道接下來會有什麼，那裡就是地方。

問題回報和功能請求照常在 GitHub 上提交。

</details>

<details>
<summary>我可以在沒有 UI 的情況下執行 FluentCleaner / 從工作排程器執行它嗎？</summary>

可以。

```powershell
FluentCleaner.exe /AUTO
```

使用你目前儲存的選擇執行靜默清理，並立即結束。  
沒有視窗、提示或互動。

```powershell
FluentCleaner.exe /AUTO /SHUTDOWN
```

行為相同，但會在清理完成後關閉 Windows。  
單獨使用 `/SHUTDOWN` 不會做任何事。

### 記錄

每次自動執行都會向以下位置附加一份詳細記錄：

```txt
%AppData%\FluentCleaner\auto.log
```

記錄包含：
- 時間戳記
- 按項目分組的每一個已刪除路徑
- 總清理大小

### 排程

要自動執行清理：

1. 開啟 **Windows 工作排程器**
2. 建立一個新工作
3. 加入 `FluentCleaner.exe`
4. 使用 `/AUTO` 作為參數

不需要內建的排程 UI。

</details>

<details>
<summary>支援哪些 Windows 版本？</summary>

FluentCleaner 官方支援：

- Windows 10 2004（Build 19041）及更高版本
- Windows 11

不要求 Windows 11。
雖然使用了 WinUI 3，但這個應用程式有意保持與現代 Windows 10 系統的相容性。

</details>

<details>
<summary>我可以支持開發嗎？</summary>

可以，如果你願意的話 😄

FluentCleaner 是一個單人專案，不是什麼擁有投資人和市場部門的千萬級公司。

如果你想從資金上支持開發，可以在這裡：
[PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN)


</details>

 ## 最佳化工具迷思
 
<details>
<summary>為什麼 FluentCleaner 沒有 X？</summary>

<details>
<summary>安全檔案刪除（DoD 7 次、Gutmann 35 次……）</summary>

簡短回答：它看起來很厲害，但沒有實際意義。

安全覆寫在 90 年代是有意義的，當時 HDD 是主流，鑑識還原確實是現實問題。今天：

- **SSD** 使用耗損平均和 TRIM。控制器決定資料位元實際落到哪裡，不是你的軟體決定。你可以把一個檔案覆寫 35 次，但控制器可能每次都寫到不同的 NAND 區塊。Gutmann 本人也在自己論文的補充說明中提過這一點。
- **FluentCleaner 刪除的檔案** 是瀏覽器快取、暫存檔和記錄項目。如果有人在鑑識還原你的 Discord 快取，那你要擔心的問題已經比清理器的刪除方式大多了。

普通檔案刪除在這裡就是正確做法。其他東西都是安全劇場。

</details>

<details>
<summary>登錄檔清理器</summary>

這是有意不做的，值得解釋一下。

這個前提聽起來很合理：孤立機碼會不斷累積，Windows 會變慢，清理能改善。實際情況是：

- Windows 按需載入登錄檔機碼。一萬個孤立的解除安裝程式項目，對開機時間或效能沒有可測量影響。這類東西已經被反覆跑分到爛了。
- 風險/收益完全倒掛。登錄檔清理器如果刪錯機碼，可能破壞應用程式，極端情況下甚至破壞作業系統。收益是安慰劑。壞處是系統壞掉。

CCleaner 有這個功能，是因為它是一個聽起來很技術的賣點。FluentCleaner 沒有它，是因為把一個為了好看而不是為了好用的功能做進去，是不誠實的。

如果你真的需要清理壞掉的解除安裝程式殘留；[Autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) 或有針對性的手動編輯才是正確工具，而不是批次清理器。

</details>


<details>
<summary>總體理念</summary>

FluentCleaner 只處理明確屬於垃圾的內容：快取檔案、暫存資料、殘留記錄。它刻意避開功能膨脹——正是這種膨脹讓 CCleaner 從一個專注的工具變成了每次啟動都推銷 VPN 的臃腫軟體。

更少的功能。誠實的功能。

</details>

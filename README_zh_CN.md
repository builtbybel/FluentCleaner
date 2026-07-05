# FluentCleaner

[English](README.md) | **简体中文** | [繁體中文](README_zh_TW.md)

### 现代、透明、无间谍软件、无恐吓软件、无暗黑模式、无强行推销垃圾、无伪注册表魔法


<img width="1536" height="1024" alt="FluentCleaner" src="FluentCleaner/Assets/Banner.avif" />


_我做了一个自己的清理工具，灵感来自 2006 年左右那个老 CCleaner，只是把它改成了今天本该有的样子。现代化（基于 WinUI 3 构建）、简洁，并且专注于真正值得清理的内容，而不是那些常见的乱七八糟的东西。_
 
我做这个，是因为你迟早会注意到一种规律。

那些曾经真的很好用的东西……会慢慢变差。小开发者做出一个很棒的东西，公司把它买走，优化到面目全非，然后你突然发现，一个简单工具怎么就变成了“这到底发生了什么？”的故事。CCleaner 基本已经是教科书案例了，大家都知道，没必要再写一整段。

有意思的是，CrapCleaner 之所以还能撑下来，很大程度上其实是靠它周围的社区，尤其是类似 [winapp2.ini](https://github.com/moscadotto/winapp2) 这样的签名库。这个生态系统对工具的贡献，比大多数官方决策都大。

我懒得把所有清理器都原生重写一遍，所以干脆为那种格式写了一个解析器。结果发现它很快。是真的……意外地快。比我记忆里老 Piriform 实现还快（不知道当年为什么那么慢，可能是专有格式、过度工程化，或者只是历史原因。反正现在也不重要了）。

UI 是用 WinUI 3 做的，就是微软那个“漂亮但慢”的框架。结果不知怎么，它居然还是能跑得比原版快。真有意思。

现在的公司并不是真的在比谁能把东西做得更好。它们比的是谁能在不彻底搞崩的前提下塞进更多噪音。一路下来，“好工具”就变成了“人们怀念的东西”。

CCleaner 曾经很棒。现在它更多像是一个警示案例。

总之，我不是想修复整个行业。只是想要一个不烂的东西。也许我以后会腻，也许它会演变成别的东西，然后我们像往常一样又回到原点。

目前我就把它叫做 **FluentCleaner**。

它本来甚至没打算公开，但很多真诚友善的人希望我发布，所以我大概会发布。
这里先放一个预览版，让你感受一下方向。我之后可能会通过捐赠来维持开发，到时候再说。

如果你喜欢，那很好。如果不喜欢，也完全没问题。

## FAQ

<details>
<summary>这能让我的电脑变快吗？</summary>

说实话？看情况，这不是在敷衍。

在一台现代系统、剩余空间充足的电脑上，你大概率不会感受到明显的速度提升。
但微软自己也说过，存储空间不足可能会拖慢系统，甚至阻止 Windows 更新（[来源](https://support.microsoft.com/en-us/windows/free-up-drive-space-in-windows-85529ccb-c365-490d-b548-831022bc9b32)），所以如果你的硬盘快满了，清理比你想的更重要。

除了速度以外，定期清理也有一些很实际的理由：
- 回收过去几个月里悄悄被吃掉的磁盘空间
- 排查由缓存损坏导致的应用问题
- 缩小备份体积
- 隐私，比如浏览器数据、最近打开文件列表、卸载应用后的残留痕迹
- 保持 Windows 更新顺利运行
- 或者只是因为一个整洁的系统让人感觉更好。这也成立。

微软建议每月做一次这类清理。存储感知可以自动完成。
FluentCleaner 只是让你更精确地控制到底清理什么。

</details>

</details>

<details>
<summary>winapp2.ini 到底是什么？</summary>

它是一个由社区维护的 Windows 应用清理规则数据库，
经过 15 年以上积累，包含数千条条目。它会告诉 FluentCleaner 每个应用到底要清理什么：哪些临时文件夹、哪些缓存路径、哪些注册表项。
不靠猜，不用覆盖整个磁盘的粗暴通配符。
每个条目都是明确的、可检查的、可审计的。这就是它的意义。

</details>

<details>

<summary>flavors 是什么？</summary>

winapp2.ini 会根据你使用的工具提供不同变体。
FluentCleaner 使用原版 CCleaner flavor，也就是当年那个工具还值得用时所使用的同一种变体。

</details>

</details>

<details>
<summary>安全吗？</summary>

它的安全性取决于你启用了什么。你不先选择，它什么都不会运行。
winapp2.ini 条目只会处理自己被明确指定的目标，
不会搞那种宽泛的“删除 temp 里的所有东西”的胡来操作。
话虽如此：它确实会删除文件。如果某些东西看起来重要，请先备份。

</details>

<details>
<summary>为什么用 WinUI 3？</summary>

因为现在是 2026 年了，Windows 工具不该看起来像 2009 年做出来的。
而且 Fluent Design 字面上就在名字里。挺合适。

</details>

<details>
<summary>CCleaner 7 移除了 winapp2.ini 支持，这对 FluentCleaner 意味着什么？</summary>

没有任何影响。FluentCleaner 有自己的解析器，完全独立于 CCleaner。
CCleaner 移除支持这件事，说实话也是我做这个工具的动机之一。

</details>

<details>
<summary>我能使用自定义 winapp2 数据库吗？</summary>

可以。FluentCleaner 不会锁定到某一个来源。

像 BBleachBit 这样的工具（主要是 Linux 清理工具，其实我是通过这个项目才发现它的，
但它的 UI 糟到让我立刻又退回来了），以及其他工具，都有自己的 winapp2.ini flavor，
也就是针对各自需求略作修改的版本。你可以拿其中任意一个
（或者自己构建一个），然后直接接入 FluentCleaner。

只需要把文件放到系统中的某个位置，然后进入：
**Settings > Database > Custom**，指向你的文件。就这样。

winapp2 项目的官方数据库在这里：
https://github.com/MoscaDotTo/Winapp2，它由社区维护，
定期更新，覆盖数千个应用。如果你想要比默认规则更多的覆盖范围，这是一个很好的起点。

</details>

<details>
<summary>我在哪里可以关注开发进展？</summary>

我会在 **[x/twitter](https://x.com/builtbybel)** 上发布 insider 内容、早期构建版本，以及偶尔吐槽一下 WinUI。如果你想在正式发布前知道接下来会有什么，那里就是地方。

问题反馈和功能请求照常在 GitHub 上提交。

</details>

<details>
<summary>我可以在没有 UI 的情况下运行 FluentCleaner / 从任务计划程序运行它吗？</summary>

可以。

```powershell
FluentCleaner.exe /AUTO
```

使用你当前保存的选择执行静默清理，并立即退出。  
没有窗口、提示或交互。

```powershell
FluentCleaner.exe /AUTO /SHUTDOWN
```

行为相同，但会在清理完成后关闭 Windows。  
单独使用 `/SHUTDOWN` 不会做任何事。

### 日志

每次自动运行都会向以下位置追加一份详细日志：

```txt
%AppData%\FluentCleaner\auto.log
```

日志包含：
- 时间戳
- 按条目分组的每一个已删除路径
- 总清理大小

### 计划任务

要自动执行清理：

1. 打开 **Windows 任务计划程序**
2. 创建一个新任务
3. 添加 `FluentCleaner.exe`
4. 使用 `/AUTO` 作为参数

不需要内置的计划任务 UI。

</details>

<details>
<summary>支持哪些 Windows 版本？</summary>

FluentCleaner 官方支持：

- Windows 10 2004（Build 19041）及更高版本
- Windows 11

不要求 Windows 11。
虽然使用了 WinUI 3，但这个应用有意保持与现代 Windows 10 系统的兼容。

</details>

<details>
<summary>我可以支持开发吗？</summary>

可以，如果你愿意的话 😄

FluentCleaner 是一个单人项目，不是什么拥有投资人和市场部门的千万级公司。

如果你想从资金上支持开发，可以在这里：
[PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN)


</details>

 ## 优化器迷思
 
<details>
<summary>为什么 FluentCleaner 没有 X？</summary>

<details>
<summary>安全文件删除（DoD 7 遍、Gutmann 35 遍……）</summary>

简短回答：它看起来很厉害，但没有实际意义。

安全覆写在 90 年代是有意义的，当时 HDD 是主流，取证恢复确实是现实问题。今天：

- **SSD** 使用磨损均衡和 TRIM。控制器决定数据位实际落到哪里，不是你的软件决定。你可以把一个文件覆写 35 次，但控制器可能每次都写到不同的 NAND 块。Gutmann 本人也在自己论文的附录说明过这一点。
- **FluentCleaner 删除的文件** 是浏览器缓存、临时文件和日志条目。如果有人在取证恢复你的 Discord 缓存，那你要担心的问题已经比清理器的删除方式大多了。

普通文件删除在这里就是正确做法。其他东西都是安全剧场。

</details>

<details>
<summary>注册表清理器</summary>

这是有意不做的，值得解释一下。

这个前提听起来很合理：孤立键会不断积累，Windows 会变慢，清理能改善。实际情况是：

- Windows 按需加载注册表键。一万个孤立的卸载程序条目，对启动时间或性能没有可测量影响。这类东西已经被反复基准测试到烂了。
- 风险/收益完全倒挂。注册表清理器如果删错键，可能破坏应用，极端情况下甚至破坏操作系统。收益是安慰剂。坏处是系统坏掉。

CCleaner 有这个功能，是因为它是一个听起来很技术的卖点。FluentCleaner 没有它，是因为把一个为了好看而不是为了好用的功能做进去，是不诚实的。

如果你真的需要清理坏掉的卸载程序残留；[Autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) 或有针对性的手动编辑才是正确工具，而不是批量清理器。

</details>


<details>
<summary>总体理念</summary>

FluentCleaner 只处理明确属于垃圾的内容：缓存文件、临时数据、残留日志。它刻意避开功能膨胀——正是这种膨胀让 CCleaner 从一个专注的工具变成了每次启动都推销 VPN 的臃肿软件。

更少的功能。诚实的功能。

</details>

# Capsyn —— Windows 灵动岛（时间岛 + 看板岛）

一个常驻屏幕顶部中央的悬浮小组件，模仿 iOS 14+ Dynamic Island 的视觉：

* **时间岛（收起态）**：一枚纯黑超圆角胶囊，固定在屏幕顶部居中，**三种形态**：
  * 平时：320 × 40，里面每秒跳动 `HH:MM:SS`；
  * **有计时占用时变成「监测区」**：时钟消失，换成「模式图标 + `mm:ss`」（172 宽），
    比如收起后依然能看见秒表在走、倒计时还剩多久；
  * **两个计时同时占用**：新开的那个在胶囊里（图标 + 读数），原来的**缩成旁边一颗独立的圆形小岛**
    （40 × 40，只有模式图标 + 一圈进度环）—— 是**两块分开的岛**，不是一个岛里塞个圆；
    两颗岛整体居中，胶囊为它让出半个圆的宽度；点小圆可以把两个计时换位。
    **闹钟不算占用**（不会把时钟挤掉），而且计时在收起 / 展开之间一直走。
* **看板岛（展开态）**：鼠标移到时间岛上，**它的下方**用弹簧动画长出一块看板，里面三个视图
  （只切可见性；**高度按视图变**：指标 350 / 时间工具与设置 430、300，切换时按弹簧长高、缩矮）：
  * **指标**（默认）：第一行 **CPU / 内存 / 网络上下行** 三张数据卡，下面一条横线，
    左下是「时间工具」按钮（时钟图标 + 文字），右下角两个圆形图标按钮 ——「设置」（进设置视图）
    和红色「关闭程序」（退出应用的唯一入口）；
  * **时间工具**（点左下「时间工具」进入，看板长高 80）：左上角「返回」+ 标题，下面是一条
    **分段控件**（正计时 / 倒计时 / 闹钟），再往下是 **环形进度 + 大字读数** 和模式自己的操作行：
    * **正计时**：环 = 当前这一分钟走过的比例，读数 `mm:ss.f`，操作行是「记一圈 / 上一圈」；
    * **倒计时**：环 = 剩余比例，读数 `mm:ss`，操作行是 1 / 3 / 5 / 10 分钟预设；
    * **闹钟**：读数 `HH:mm`，操作行是 ±1 / ±5 分钟微调，主按钮启用 / 取消；
    三个模式都**真的在计时**（不是只有 UI），右下角两个图标按钮照常，并跟着看板底边一起上下滑。
  * **设置**（点右下角 ⚙ 进入，看板换成 300 高）：目前是**铃声**板块 ——
    闹钟铃声 / 倒计时铃声**各 5 个内置音效**（默认都是第一个），点一下就选中并试听，
    选择立刻存盘（`%LOCALAPPDATA%\Capsyn\settings.json`）。
  三个视图之间是 **iOS 风格的 push / pop 过场**（旧视图往一侧滑出、新视图从另一侧滑入，带轻微视差）。
* **到点提醒**：倒计时归零 / 闹钟到点 → **岛自动展开成「提醒面板」**（第二级弹窗，挂在胶囊下面，
  不再是屏幕中央的独立窗口），内容 = 铃铛/沙漏 + 时间 + 「停止」（闹钟多一个「稍后 5 分钟」），
  同时循环播放对应铃声（倒计时最长 30 秒、闹钟最长 2 分钟），停铃 / 关提醒即静音。
  铃声可在设置里分开选（各 5 个内置音效，默认第一个），也可以选自己的音频文件。
* 鼠标移开 → 延迟一点点平滑收回胶囊。

* 技术栈：WinUI 3（C#）+ Windows App SDK 1.8，动画用 **Composition API**（弹簧 + 内容淡入淡出；条目错帧的「生长动画」已按需求移除）
* 模板形态：**Unpackaged（未打包）** —— 不需要 MSIX、不需要签名，直接跑 exe
* 目标环境：本机运行（.NET 9 或更高 + Windows App Runtime 1.8 已安装；本机没有 .NET 9 运行时，
  靠 `Capsyn.csproj` 里的 `<RollForward>Major</RollForward>` 落到已装的 .NET 10）

### 当前版本状态（快照）

* **GitHub Latest（稳定推荐）**：tag **`v4.0.0-beta`** —— 时间工具 / 顶部监测区 / 铃声 / 到点提醒，
  不含拖拽解压，行为最稳；网站首屏主按钮和 `releases/latest` 都指向它。
* **新功能版**：tag `v5.0.0-NewFeatures` —— 在 4.0.0 基础上加拖拽解压（`.zip`），已作为正式版发布但不是 Latest。
* **预发行 / 测试版**：tag `v5.1.0-ExtractionKernels` —— 解压内核扩展到 `.7z / .rar / .tar / .tar.gz / .xz / .bz2`
  等格式并加了进度条（GitHub 上标为 Pre-release）。详见第七节。
* **进行中（分支 `feature/music-control`，未打 tag）**：音乐控制 —— **播放开始时收起态胶囊直接变成音乐胶囊**（♪ 歌名 + 状态），
  鼠标移上去展开后是「**看板在左 + 音乐副岛在右**」两块；支持播放暂停 / 上一首 / 下一首（对着汽水音乐实测通过）。
  详见「三、关键实现点 → 音乐控制」。
* 「拖拽一开始就自动出现落点区」的**实验**做过三轮（WinEvent / 鼠标钩子 / 透明接收区），
  因安全前提下无法同时满足“即时 + 只认 zip + 零误触发”，已撤掉并删除实验分支，代码回到本版行为。
* **已实现**：时间岛胶囊（时钟 / 监测区，宽度会随占用变化）；看板岛（指标 / 时间工具 / 设置 / 到点提醒
  四个视图 + iOS 风格 push/pop 过场 + 视图高度弹簧；时间工具里的正计时 / 倒计时 / 闹钟**都是能用的**，
  含记一圈、预设、闹钟启用/取消；**到点提醒**：岛自动展开成提醒面板 + 响铃；
  设置页的**铃声板块**（各 5 个内置音效 + 自定义音频文件、分开设置、存盘）；
  **收起后顶部胶囊继续显示正在跑的计时**，两个计时同时占用时旧的缩成**旁边一颗独立小圆岛**、点小圆换位；
  弹簧驱动的窗口形状；置顶 / 不进任务栏 / 点击不抢焦点 / 位置启动算一次后焊死；单实例保护；
  未处理异常兜底（`capsyn-crash.log`）；显示器与 DPI 变化处理；固定深色主题。
* **刻意只做 UI / 没做**：托盘图标、开机自启、设置页里的其它板块（音量 / 主题等）、
  闹钟的贪睡次数上限、提醒面板里的「再来一次」按钮。
* 曾经实现过、后来又**整体撤销**的东西（独立的设置程序 `CapsynSettings.exe`、`--settings` 模式、
  `Microsoft.Win32.TaskScheduler` 注册的 `Capsyn_AutoStart` 计划任务、MOTW 清理等）**不在这份代码里**，
  需要时去 git 历史里找（相关 tag 已按要求删除，提交仍可按 SHA 查阅）。

### 视觉体系（Phase 1：统一 token，进行中）

> 状态：**工作区改动尚未提交**（代码与 `v0.3.1-ProjectChange-U2.1` 相比多了这一批视觉体系改造）。

设计依据（可追溯，全部为官方文档 / 官方样例仓库）：

| 依据 | 支持什么 |
| --- | --- |
| [System backdrops (Mica/Acrylic)](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops) | Mica = 不透明基础层；Acrylic = 半透明 transient 浮层；`Window.SystemBackdrop`、`IsSupported()` 运行时判断与回退（WinAppSDK 1.3+） |
| [Materials overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) | 材质与主题资源的整体规则 |
| [Fluent 2 Typography](https://fluent2.microsoft.design/typography) | Caption / Body / Subtitle / Title 的字号 + 行高 + 字重 |
| [Motion in practice](https://learn.microsoft.com/en-us/windows/apps/develop/motion/motion-in-practice) | 动效三档时长与缓动曲线 |
| [XAML 与 Composition 互操作](https://learn.microsoft.com/en-us/windows/apps/develop/composition/xaml-comp-interop) | hover/press 用 `ElementCompositionPreview` + 隐式动画的挂法 |
| [Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font) | 图标字形与 8px 网格对齐 |
| [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery) ／ [`SampleSystemBackdropsWindow`](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/SampleSupport/SamplePages/SampleSystemBackdropsWindow.xaml.cs) | 控件模板、视觉状态、SystemBackdrop 接线的**官方**参考实现 |
| [WindowsAppSDK-Samples / Mica](https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Mica) | Win32 窗口 + Mica 的官方样例 |

`Themes/Tokens.xaml`（230 行）是唯一的视觉数值来源：

* **颜色**：`Dark` / `Light` / `HighContrast` **三套主题字典**，每组 28 个 key（语义：岛面、卡片底/边、分隔线、文字三级、交互态、主色 + 辅助色 + Success/Warning/Critical）；
* **强调色状态层**：4 个强调色各 6 档 tint（0.18 / 0.25 / 0.32 / 0.40 / 0.50 / 0.60，与迁移前的 `#2E/#40/#52/#66/#80/#99` 一一对应）+ Hover/Pressed 文字色；
* **圆角** 4/8/12/16/20/胶囊 ｜ **间距** 4/8/12/16/24 + 3 个 Thickness ｜ **字体层级** Caption 12·Body 14·Subtitle 20·Title 28 ｜ **图标** 16/20/24；
* **动效**：三档时长 `Fast 150 / Normal 300 / Slow 500`（同时提供 ms 与 seconds 两套）+ 弹簧物理参数 `Period 420/220`、`Damping 0.62/0.9` + 统一缓动 `EasingDecelerate`(EaseOut) / `EasingStandard`。

**结构规则（踩过的坑，务必遵守）**：
1. `ThemeDictionaries` 内三个字典**各自写全同一组 key** 才是正确写法；
2. **外层**（ThemeDictionaries 之外）的 `x:Key` 必须全局唯一 —— 重复会让 XAML 编译器直接失败（`XamlCompiler.exe` 退出码 1，报错信息不指向具体行）；
3. `ResourceDictionary` 的文本**不要用脚本大范围替换** —— 会误删主题字典内容；改 token 请用逐段 edit + 下方自检。

**自检命令**（每次动 token 后跑一次）：
```powershell
# 外层 key 是否唯一 + 所有 ThemeResource 引用是否都有定义
$tk='G:\Capsyn\Themes\Tokens.xaml'; $t=Get-Content $tk -Raw
$outer=$t.Substring($t.IndexOf('</ResourceDictionary.ThemeDictionaries>'))
$dup=[regex]::Matches($outer,'x:Key="([^"]+)"')|%{$_.Groups[1].Value}|Group-Object|? Count -gt 1
if($dup){'❌ 外层重复: '+($dup.Name -join ', ')}else{'✅ 外层 key 唯一'}
```

**迁移进度**：XAML 侧硬编码颜色 **62 → 1**（剩余 1 处在注释里，属于说明文字）✓；`IslandShell.xaml` 已 0 处 ✓。
**待办**：C# 侧 10 处动画时长（收敛到三档）、4 处字号（走 type ramp）、控件模板重写（Phase 2）、窗口材质（Phase 4）。



---

## 一、怎么构建 / 运行

### 方式 1：Visual Studio（推荐）
用 VS 2022 打开 `Capsyn.sln`，选 `Debug | x64`，直接 F5。

### 方式 2：命令行
```powershell
cd G:\Capsyn
dotnet build Capsyn.csproj -c Debug -p:Platform=x64
.\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\Capsyn.exe
```

### 怎么用
把鼠标移到顶部的胶囊上 → 下方展开看板；鼠标移开 → 自动收回。
看板右下角有两个圆形图标按钮：左边齿轮是「设置」（目前只有 UI），右边红色的电源符号用来**退出程序**。
点哪儿都不会抢焦点（窗口带 `WS_EX_NOACTIVATE`）。

### 这个程序怎么退出
**看板岛右下角那个只有图标的红色按钮**（看板展开后可见），点一下就退出。
实在点不到（比如鼠标坏了）就用任务管理器结束 `Capsyn.exe`，或者：
```powershell
taskkill /IM Capsyn.exe /F
```
> 托盘图标 / 开机自启还写在 `MainWindow` 的 TODO 里，属于后续里程碑。

### 调试
设置环境变量 `CAPSYN_DIAG=1` 后启动，会在 exe 同目录写 `capsyn-diag.log`
（悬停判定、展开/收起、形状同步、采样耗时等时序信息），正常运行时不产生任何开销。

**崩溃日志**：`capsyn-crash.log`（同样在 exe 同目录）**不受开关限制、永远写** ——
未处理异常（UI 线程 / 后台线程 / 未观察的 Task）都会连调用栈一起记下来。
UI 线程上的异常记完会标记为「已处理」，让悬浮岛继续活着，不至于因为一次定时器回调出错就整只消失。

**只能开一个**：程序带单实例保护（命名 Mutex），第二个进程会直接退出，不会出现两个胶囊叠在一起。
所以想同时跑两个版本调试的话，得先 `taskkill /IM Capsyn.exe /F`。

---

## 二、文件清单

| 文件 | 职责 |
| --- | --- |
| `Capsyn.csproj` | 工程定义：`net9.0-windows10.0.19041.0`、`UseWinUI`、`WindowsPackageType=None`（未打包）、固定 x64、离线还原开关 |
| `Capsyn.sln` | 解决方案（Debug/Release × x64） |
| `app.manifest` | DPI（PerMonitorV2）、comctl32 v6 依赖 |
| `App.xaml` / `App.xaml.cs` | 应用入口；固定深色主题、把窗口宿主的页面背景色改成透明；单实例保护 + 未处理异常兜底 |
| `MainWindow.xaml(.cs)` | **只有壳**：把窗口、位置、置顶等系统行为与岛壳接起来 |
| `Controls/IslandShell.xaml(.cs)` | **岛的状态机与外轮廓**：悬停判定、展开/收起、弹簧驱动的窗口形状、Composition 动画 |
| `Controls/TimeIslandControl.xaml(.cs)` | **时间岛胶囊**：时钟 / 监测区两种形态，宽度 320 / 172；形态切换有 Fast 档过场 |
| `Controls/TimerDotControl.xaml(.cs)` | **第二个计时的独立小圆岛**：40 × 40 圆 + 进度环 + 模式图标，点一下换到主位 |
| `Controls/DashboardIslandControl.xaml(.cs)` | **看板岛**：550 宽，指标 350 / 时间工具 430 / 设置 360 / 到点提醒 180；四个视图只切可见性，分段控件 + 环形读数 + 铃声板块 + 提醒面板 + 跟着底边滑的共用页脚 |
| `ViewModels/TimeIslandViewModel.cs` | `CurrentTime`（可绑定属性，24 小时制）与字体/配色 |
| `ViewModels/TimeToolsViewModel.cs` | 时间工具的状态机（**单例 `Shared`**，工具页和顶部胶囊共用）：正计时（含记一圈）/ 倒计时（预设、deadline 计时）/ 闹钟；还管胶囊的占用顺序（主 / 次）与 `RingingChanged`（响铃开始/结束） |
| `ViewModels/DashboardViewModel.cs` | 指标的可绑定文本与进度条宽度；采样在线程池上跑 |
| `ViewModels/ObservableObject.cs` | 极简 `INotifyPropertyChanged` 基类（不引入第三方 MVVM 库） |
| `Models/SystemMetrics.cs` | 一次采样结果（CPU% / 内存 / 网络上下行） |
| `Services/SystemMetricsProvider.cs` | 指标采集：`GetSystemTimes`、`GlobalMemoryStatusEx`、网卡统计 |
| `Services/IslandAnimator.cs` | 内容淡入淡出（Composition）；条目错帧的「生长动画」已按需求移除 |
| `Services/SpringScalar.cs` | 阻尼弹簧积分器：驱动 Win32 窗口区域的外轮廓 |
| `Services/IslandWindowStyler.cs` | 窗口系统行为：无边框、置顶、不进任务栏、不抢焦点、窗口区域裁剪、锁死位置、缩放/分辨率变化重算 |
| `Services/SingleInstance.cs` | 单实例保护（命名 Mutex），避免两个胶囊叠在一起 |
| `Services/Diagnostics.cs` | 开关式诊断日志（`CAPSYN_DIAG=1`）+ **永远写**的崩溃日志（含 InnerException 链） |
| `Services/RingtoneLibrary.cs` | **10 个内置铃声**：代码合成的 WAV（闹钟 5 + 倒计时 5） |
| `Services/RingtoneService.cs` | 铃声播放：落盘 + winmm `PlaySound`（循环 / 停 / 试听 / 自动超时） |
| `Services/MediaMonitor.cs` | **音乐控制的数据层**：订阅系统媒体会话（SMTC），拿歌名 / 歌手 / 专辑 / 封面 / 播放状态 / 时长，并把播放 / 暂停 / 上一首 / 下一首发回去；事件统一编组到 UI 线程 |
| `Helpers/ColorHelper.cs` | `#RRGGBB` / `#AARRGGBB` 解析 |
| `Configuration/IslandOptions.cs` | 全部可调参数：尺寸、缝隙、回弹余量、弹簧参数、淡入淡出时长、采样间隔 |
| `Configuration/AppSettings.cs` | 用户设置（`%LOCALAPPDATA%\Capsyn\settings.json`）：闹钟 / 倒计时铃声序号 |
| `Interop/NativeMethods.cs` | 用到的 Win32 / DWM / GDI P/Invoke |
| `nuget.config` | 官方源 + 项目内包目录（见「六、环境说明」） |
| `tools/backup-to-github.ps1` | 一键备份脚本（见「七、GitHub 仓库」） |

---

## 三、关键实现点

### 1. 无边框、无标题栏、无白边
`OverlappedPresenter.SetBorderAndTitleBar(false, false)` 之后，窗口上仍然留着
`WS_DLGFRAME / WS_SYSMENU / WS_EX_WINDOWEDGE` 这些「非客户区」，浅色主题下就是**一圈可见的白边**，
而且会把客户区缩小几个像素。所以 `IslandWindowStyler.ApplyChrome()` 里把它们全部清掉：

```csharp
style &= ~(WS_CAPTION | WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_SYSMENU | ...);
exStyle &= ~(WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME | WS_EX_APPWINDOW);
SetWindowPos(..., SWP_FRAMECHANGED);   // 让改动立即生效
```

再加上 `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DONOTROUND)`（不要 Win11 自动圆角）
与 `DWMWA_BORDER_COLOR = NONE`（不要系统描边）。

### 2. 看得见的形状 = Win32 窗口区域（这是整个项目的关键约束）
WinUI 3 的窗口客户区是**不透明**的：框架会用主题色把客户区填满（浅色主题下是纯白）。
实测把 `SetWindowRgn` 去掉后，圆角处立刻变回 `#FFFFFF` —— 也就是说
**「透明」从来不是真的透明，而是靠窗口区域把像素裁掉**。

所以整个岛的可见轮廓由 `IslandWindowStyler.UpdateIslandShape()` 用区域给出：

```csharp
// 胶囊（固定）∪ 看板（按弹簧进度变化的圆角矩形）
var region = CreateRoundRectRgn(pillLeft, 0, pillRight, pillBottom, pillRadius * 2, pillRadius * 2);
var panel  = CreateRoundRectRgn(panelLeft, panelTop, ..., panelRadius * 2, panelRadius * 2);
CombineRgn(region, region, panel, RGN_OR);
SetWindowRgn(hwnd, region, true);
```

* 窗口本身比看板大 12%（`BounceMargin`），给弹簧回弹留余量；
* 窗口内铺满一块纯黑底板，所以**区域怎么变底下都是黑的，永远不会露白**；
* 区域同时决定命中测试：收起时只有胶囊可点，展开时整个看板可点，其余像素直接穿透到下面的窗口。

### 3. 展开 / 收起状态机
`IslandShell` 用 80ms 的光标轮询判断悬停（不用 XAML 的 `PointerEntered/Exited`：
路由事件无法区分「在子元素之间移动」和「离开窗口」，而窗口区域已经限定了可交互范围）：

* 光标落在**当前形态**内 → 展开（展开后判定框是整个窗口，避免缝隙处抖动）；
* 光标离开 → 160ms 宽限后收起，宽限期内移回则不收。

### 4. 动画：弹簧（保留）+ 内容淡入淡出
* **外轮廓层（C# 弹簧，保留）**：窗口区域是 Win32 的，Composition 动画碰不到它；
  而 XAML 元素视觉的属性读取**只返回基准值、拿不到动画中的当前值**
  （诊断日志里 `shape tick scale=(0.711,0.000)` 一直不变就是这个原因）。
  所以外轮廓用 `SpringScalar`（半隐式欧拉阻尼弹簧，固定子步长保证稳定），
  在 8ms 定时器里逐帧积分，换算成看板的缩放后交给窗口区域 —— 展开有回弹、收起平滑。

  实测展开轨迹（1920×1080@100%）：`320×40 → 342×79 → 392×155 → 473×278 → 531×368 → 568×423（过冲）→ 550×396`，
  约 300ms 长完并带一次回弹；收起同理平滑。
* **内容层（Composition，只做透明度）**：按需求**生长动画已删除** ——
  内容不再从 0.94 放大到 1，卡片 / 按钮也不再逐个错帧「上浮 + 淡入」，只做透明度过渡
  （`IslandAnimator` 现在只播 Opacity 关键帧）。

> 另外两个坑：`CompositionTarget.Rendering` 在 WinUI 3 里**不是持续触发的**
> （Composition 动画跑在合成线程，UI 线程不一定出帧），所以形状同步必须用定时器；
> 指标采样要放到线程池（`Task.Run`），否则 1 秒一次的系统调用会把 UI 定时器挤住。

### 5. 数据采集（不引第三方库）
* **CPU**：`kernel32!GetSystemTimes` 两次采样的 idle / kernel / user 差值算总占用率；
* **内存**：`kernel32!GlobalMemoryStatusEx` 算已用 / 总量；
* **网络**：`NetworkInterface` 汇总「已启用、非环回、非隧道」网卡的累计收发字节数差值 / 时间；
* 每秒采样一次，采样跑在线程池上，回到 UI 线程再刷新绑定。

### 6. 始终置顶
`OverlappedPresenter.IsAlwaysOnTop = true`（即 `WS_EX_TOPMOST`），显示后再补一次
`SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)`；实测在 Z 序里位于任务栏、Edge 等所有窗口之上。

### 7. 不出现在任务栏 / Alt+Tab
`AppWindow.IsShownInSwitchers = false` + `WS_EX_TOOLWINDOW` 双保险；窗口没有 owner，也没有任务栏按钮。

### 8. 点击不抢焦点
`WS_EX_NOACTIVATE`，并在子类化窗口过程里对 `WM_MOUSEACTIVATE` 直接返回 `MA_NOACTIVATE`。

### 9. 位置启动算一次，之后焊死
* 位置在启动时按 **当前 DPI 缩放** 换算：`x = 主屏中点 - 窗口宽/2`，`y = 主屏顶边 + 10 × 缩放`；
  用 `DisplayArea.Primary`（主屏）+ 全屏 `OuterBounds`（贴屏幕顶边而不是工作区）。
  窗口比看板宽 12%，但胶囊仍在窗口内居中，所以**胶囊的屏幕位置始终是 800,10**（1920 宽时）。
* 之后用 `SetWindowSubclass` 拦截 `WM_WINDOWPOSCHANGING`，把坐标/尺寸强行改回钉好的值并加
  `SWP_NOMOVE | SWP_NOSIZE` —— 用户拖不动，外部 `MoveWindow` 也会被弹回（实测有效）。

### 10. 时间刷新
`Microsoft.UI.Xaml.DispatcherTimer`，`Interval = 1s`；`Loaded` 时先立刻取一次时间（避免首秒空白）再启动；
每次 Tick 重新读 `DateTime.Now`（而不是自增），格式 `HH:mm:ss`、`InvariantCulture`、24 小时制。
字体 Cascadia Code、字号 17、SemiBold、白色；`IsTextScaleFactorEnabled="False"` 保证不被系统文字缩放撑破。

### 11. 看板布局：两个视图（指标 / 时间工具），高度会变
看板宽 550、内边距 16，**两个视图只切可见性**，但**高度不一样**：

* 指标视图 **350**、时间工具视图 **430**（`IslandOptions.ExpandedHeight / ExpandedToolsHeight`）；
* 窗口画布按最大的那个一次留够（`WindowHeight = 40 + 6 + 430 × 1.12 ≈ 528`），
  切视图时只改**窗口区域**；`IslandShell` 里除了展开弹簧 `_panelSpring` 还有一根
  **高度弹簧 `_heightSpring`**（单位 DIP，350 ↔ 430），所以看板是"长高 80 / 缩矮 80"，不是跳变；
* 看板内容元素本身高度固定 = 430（`Dashboard.Height = MaxExpandedHeight`），切视图**不重排**；
* 右下角那两个圆形按钮是**两个视图共用**的一层 `FooterLayer`，它始终贴着看板底边 ——
  高度变化时由 `Dashboard.UpdatePanelHeight(当前高度)` 改它的 `Translation.Y`
  （偏移 = 当前高度 − 430，指标视图时是 −80），只动合成属性，不触发布局。

**视图 1（默认，指标）**：

```
第 1 行  处理器(21% + 进度条)   内存(9.5/16.0 GB 59% + 进度条)   网络(↓ 9.6 KB/s / ↑ 1.5 KB/s)
第 2 行  ────────────────────────── 一条 1px 半透明白横线 ──────────────（窗口内 y = 134，两个视图共用）
第 3 行  [ 🕐 时间工具 ]（左下）                  右下角：⚙「设置」  ⏻「关闭程序」
```

**视图 2（时间工具，点左下「时间工具」进入，看板长高到 430）**：

```
第 0 行  ←「返回」  时间工具                                       （40 高）
第 1 行  [ 正计时 | 倒计时 | 闹钟 ]  ← 分段控件，selected 段有滑动指示条   （44 高）
第 2 行  ────────────────────────── 同一条横线，位置不变 ────────────────────
第 3 行  (环形进度 96)   00:00.0 / 05:00 / 08:00 + 一行小字说明
第 4 行  [ 记一圈 | 上一圈 00:12.3 ] / [ 1|3|5|10 分钟 ] / [ −5 | −1 | +1 | +5 分 ]
第 5 行  [        开始 / 暂停 / 停止        ]  [ 复位 ]
                                                     右下角：⚙  ⏻
```

* 三张卡的可用宽度 ≈ 164.7（`(550-32-24)/3`），因此 `DashboardViewModel.BarMaxWidth = 136`。
* 按钮都是普通 `Button` + **矢量 `Path` 图标**（不依赖图标字体，也不引入图片资源）；
  指针态主题资源在 `UserControl.Resources` 里就地覆盖成半透明白，避免在纯黑底上出现浅色主题的灰块。
  * **右下角两个圆形图标按钮**（`IconButton` 样式：40 × 40 / `CornerRadius 20` / 半透明白底 + 细描边，间距 12）：
    **设置**（齿轮图标，只有 UI）和 **关闭程序**（`CloseIconButton`，`BasedOn` 上面那个只换红底红边 + 浅红电源图标）。
  * **左下「时间工具」**（`ToolButton` 样式：`Padding 16,9` / `CornerRadius 12` / 半透明白底 + 细描边）：
    时钟矢量图标 + 文字，点击 → 视图 2。
  * **左上「返回」**（`IconButton`）：左箭头矢量图标，点击 → 回视图 1。
  * **分段控件**（`SegmentFrame` + `SegmentButton` + 一条 `SegmentIndicator` 指示条）：
    三个段等宽，指示条用模式色（蓝 / 橙 / 紫）的 25% 淡染，切模式时用 Fast 档的 `Translation` 动画滑过去；
    `SizeChanged` 时按 `宽度 / 3` 重算指示条的宽度和位置。
  * **环形进度**：`Ellipse`（暗色轨道）+ `Path`（进度弧），`Path.Data` 由
    `UpdateRing(progress)` 用 `PathFigure + ArcSegment` 重算 —— 12 点方向起、顺时针扫 `progress × 360°`，
    圆头端点；进度变化小于 0.01 时不重画（正计时 50ms 一跳，没必要每帧重建几何）。
  * **读数**：`Cascadia Code` 52px（`HeroReadout` 样式），宽度固定 248，位数变化时整组不左右跳。
  * **三种模式的配色**（沿用 iOS 深色模式取色，和指标卡那套 token 一致）：

    | 模式 | 主色 | 用在哪 |
    | --- | --- | --- |
    | 正计时 | 蓝 `#0A84FF` | 指示条 / 进度环 / 主按钮 / 分段文字 |
    | 倒计时 | 橙 `#FF9F0A` | 同上 |
    | 闹钟 | 紫 `#BF5AF2` | 同上 |

    响铃（时间到）时环画满一整圈、读数和小字都转成 `Critical` 红，主按钮变「停止」。
* **时间工具是真的能用的**（`ViewModels/TimeToolsViewModel.cs`）：
  * 正计时：`Stopwatch`，50ms tick，读数 `mm:ss.f`；「记一圈」记下与上一次记圈的差值（上一圈读数）；
  * 倒计时：1/3/5/10 分钟预设，走**绝对时间（deadline）**而不是每 tick 减 100ms，UI 线程被挤住也不会走慢；
    读数向上取整到秒，归零进「时间到」；
  * 闹钟：`HH:mm` + ±1/±5 分钟微调，启用后算「下一次」（当天过了就明天），到点进「响铃中」；
  * 计时器只在 `NeedsClock`（在跑 / 闹钟已启用）时开着，闲时零开销；面板收起也继续走，读数不丢。
* **正计时 / 倒计时 / 闹钟之间的切换**：**只切内容、不做整块淡入淡出**（点段位 → `SelectMode()` →
  指示条用 Fast 档滑到目标段，文字 / 环形进度 / 模式专属行 / 按钮文案直接换）。
  * 曾经做过一版「内容淡出 + 位移 → 换内容 → 淡入」的过场，但偶发会停在半透明 / 偏移状态
    （看起来像错位 / 被裁掉），**按需求整段撤掉了**，只保留指示条的滑动；
  * 模式专属那一行（预设 / 分钟加减 / 记一圈）在 `ApplyModeRowVisibility()` 里按 VM 直接摆一遍，
    不依赖 x:Bind 通知，避免两行同时可见叠在一起；
  * 视图之间的 push / pop（指标 ↔ 工具 ↔ 设置 ↔ 提醒）保持不变，见下一条。
* **过场动画（iOS 风格 push / pop）**：`DashboardIslandControl.SwitchTo()` 里做：
  * **push**（进工具视图）：旧视图往**左**淡出滑走、新视图从**右**滑进来；**pop**（返回）方向相反；
  * 旧视图滑走的距离只有新视图的 60%（`ViewExitParallax`），形成 iOS 那种轻微视差；
  * 时长：入场 300ms（`MotionTokens.Normal`）、出场 150ms（`Fast`）；曲线 **cubic-bezier(0.32, 0.72, 0, 1)**；
  * 滑的是 **`Translation`** 而不是 `Offset` —— 后者是布局算出来的位置，动画收尾到 `(0,0)` 会把元素
    拽回父容器原点（先 `ElementCompositionPreview.SetIsTranslationEnabled(el, true)`，再按字符串键
    `"Translation"` 起动画，它在 WinUI 里没有强类型属性）；
  * 全程在**合成线程**上跑、不参与布局，UI 线程不用出帧，所以不掉帧；
  * 旧视图要等出场动画放完才 `Collapsed`（否则看不到淡出），收尾用序号 `_viewSwitchToken` 保护 ——
    连点「时间工具 / 返回」时只有最后一次收尾生效，不会把刚滑进来的视图又收掉；
  * 视图切换时会同时发一个 `PreferredPanelHeightChanged(高度, 是否动画)` 给外壳，
    高度动画和内容过场是两条互不干扰的线（一个改窗口区域、一个改合成属性）。
* 视图切换会写一条 `dashboard view: metrics / time tools (panel 350 / 430 dip)` 诊断日志。
* 切到工具视图时**指标采样照常在跑**（每秒一次），所以返回时数据是新的，不用重采样。
* **每次展开都是指标视图**：看板从收起态展开时（`IslandShell.SetDashboardExpanded(true)` 里）会先调
  `Dashboard.ResetToMetricsView()` —— 只在真的「收起 → 展开」时走到那里，收起宽限期内把鼠标移回来
  （`_expanded` 已经是 true，方法提前 return）不会打断正在操作的工具视图。
  复位时高度也直接落回 350（`animate: false`），展开动画只负责 0 → 350 那一段。
* **复位为什么要「推」而不是「停」动画**：`Translation` 是
  `ElementCompositionPreview.SetIsTranslationEnabled()` 挂上去的动画属性，
  用 `StopAnimation` 停掉**不保证回到 0** —— 上一次出场留在元素上的位移会一直留着，
  表现就是「下次展开时整个看板内容错位」。所以复位时用一段 1ms 的动画
  （`SnapToRest`）把 `Opacity` 推回 1、`Translation` 推回 0，两个视图都推一遍，
  并 `_viewSwitchToken++` 让还没收尾的那次过场失效。

### 11.5 收起后的「监测区」：正在跑的计时显示在顶部
看板收起后，顶部胶囊不再只是时钟，而是**变成计时监测区**（`TimeIslandControl` +
`TimerDotControl` + `TimeToolsViewModel.Shared`）。规则：

| 状态 | 长什么样 | 窗口区域 |
| --- | --- | --- |
| 没有计时 | 一颗胶囊，时钟 `HH:MM:SS` | 胶囊 320 × 40 |
| **一个**计时（正计时或倒计时，跑着或暂停） | 时钟**消失**，胶囊里换成「模式图标 + `mm:ss`」，颜色跟模式走（蓝 / 橙） | 胶囊 172 × 40 |
| **两个**计时 | **两块独立的岛**：新开的在胶囊里（图标 + 读数），原来那个变成**胶囊左边一颗独立的圆形小岛**（40 × 40，模式图标 + 一圈进度环）；点小圆可换位 | 胶囊 172×40 **∪** 圆 40×40（中间 10 缝） |
| 闹钟启用 / 响铃 | **不算占用**：胶囊继续显示时钟（工具页里才有闹钟的响铃态） | 胶囊 320 × 40 |

* 占用顺序由 `TimeToolsViewModel._activities`（`List<TimeToolMode>`）维护：
  开始一个计时就 `Promote` 到最前面，复位就把自己 `Demote` 掉；
  倒计时**到点会自动提到主位**（不然「时间到」缩在小圆里看不见）。
* 这解决了两个真问题：
  1. 面板收起后计时就"看不见了" —— 现在秒表在走、倒计时还剩多少，胶囊上一直有；
  2. 两个计时同时跑时，不能只有最后一个可见 —— 两块岛各自显示，主位看读数、小圆看图标 + 进度环。
* **两块岛是真的两块**：`IslandShapeSnapshot` 把「胶囊左/宽 + 小圆左/直径/出现进度 + 看板」一帧打包，
  `IslandWindowStyler` 用 `CreateRoundRectRgn` 分别裁出胶囊、圆、看板再 `RGN_OR` 并起来 ——
  所以圆是**独立的窗口区域**，不是塞进胶囊里的子元素，两块之间保持 10px 的真实缝隙（桌面透出来）。
* 整组居中：小圆出现时胶囊往右让 `(40 + 10) / 2 = 25`（`PillShiftWithDot`），
  圆和胶囊的中点始终落在屏幕中线上；让位是弹簧（`_dotSpring` 0→1），
  小圆同时淡入 + 从 0.86 放大到 1（只动合成属性，不触发布局）。
* 宽度怎么变：`TimeIslandControl` 只发 `PreferredWidthChanged(目标宽度)`，
  外壳用弹簧 `_pillWidthSpring` 插值，**窗口区域同帧跟着变宽/变窄**；
  位置靠 `Translation` 推（`Clock` / `TimerDot` 都开了 `SetIsTranslationEnabled`），不逐帧布局。
  形态切换本身是 Fast 档的淡入淡出 + 6px 位移，且用序号防止连点错乱。
* 时间工具状态是**单例** `TimeToolsViewModel.Shared`：工具页、胶囊、小圆绑同一个实例，
  所以三边永远一致；tick 由外壳 `IslandShell` 统一驱动（它比这些控件活得久），
  收起 / 展开不影响计时。
* 小圆是 `Button`（不是装饰性 `Border`），键盘和读屏也能操作；`UseSystemFocusVisuals="False"`
  避免点击后留一圈白色焦点环；圆里始终画一条暗色轨道环，进度小时也读得出「这是一个岛」。
* 一个坑：`Shared` 是静态字段，静态字段按**声明顺序**初始化 —— 实例字段初始化器里引用
  后面的静态字段（比如 `PresetSeconds[2]`）会拿到 `null`，构造时直接 `NullReferenceException`，
  表现为启动就崩、XAML 只报 `Cannot create instance of type 'Capsyn.Controls.IslandShell'`。
  修法：默认预设值单独写成 `const int DefaultPresetSeconds = 300`。
  顺带把 `Diagnostics.LogCrash` 改成**连 InnerException 一起写**，否则这种崩法只有外层一句废话。

### 11.6 设置 → 铃声（含自定义），以及到点提醒
**设置视图**（点 ⚙ 进入，看板 360 高）里目前只有一个板块：**铃声**。

| 行 | 内容 |
| --- | --- |
| 闹钟 | 5 个内置音效：经典双响 / 上升三连 / 电子脉冲 / 柔和钟声 / 蜂鸣警报（默认第 1 个） |
| 倒计时 | 5 个内置音效：清脆提示 / 双叮 / 上升琶音 / 水滴 / 柔和和弦（默认第 1 个） |
| 自定义 | 「闹钟：选择文件…」「倒计时：选择文件…」——各自挑一个本地音频文件 |

* 两行**各选各的**（闹钟紫色系、倒计时橙色系高亮）；**点一下 = 选中 + 立刻试听**（单次播放）；
* **自定义**：点按钮开系统文件选择器（`.wav .mp3 .m4a .aac .wma .flac .ogg`），选中的文件会**复制**进
  `%LOCALAPPDATA%\Capsyn\ringtones\custom-{alarm|countdown}{扩展名}`（原文件之后删了也不影响），
  再写进设置并试听；自定义生效时该类的 5 个内置音效都不再高亮；点任意内置音效 = 把这一类切回内置；
* 选择立刻写进 `%LOCALAPPDATA%\Capsyn\settings.json`，重启沿用；文件损坏 / 越界 / 自定义文件不见了
  都会安全退回（越界夹紧、失效的自定义清空、缺文件就是默认第一个）。

**音效怎么来的**：内置音效由 `RingtoneLibrary` **代码合成** 44.1kHz/16-bit 单声道 WAV（正弦 / 方波 /
钟声泛音 + 起音 6ms + 指数衰减包络），首次播放时写到 `%LOCALAPPDATA%\Capsyn\ringtones\{alarm|countdown}-N.wav`。
所以仓库里没有任何音频二进制文件，也没有版权问题；自定义文件则是用户自己的音频。

**播放**：`RingtoneService` 用 `Windows.Media.Playback.MediaPlayer`（支持 mp3 等格式、能循环、能停）；
MediaPlayer 起不来时退回 winmm 的 `PlaySound`（只认 wav）：

| 场景 | 行为 |
| --- | --- |
| 倒计时归零 | 循环播放倒计时铃声，最长 30 秒；胶囊 `00:00` 变红；**岛自动展开成提醒面板** |
| 闹钟到点 | 循环播放闹钟铃声，最长 2 分钟；**岛自动展开成提醒面板** |
| 设置页点音效 / 自定义 | 单次试听（不循环，最多 3 秒） |

**到点提醒 = 岛的第二级面板**（不再单开屏幕中央的窗口）：岛从胶囊**自动展开**，切到「提醒视图」
（180 高），内容按类型换：

```
┌──────────────────────────────────────────────┐
│ [🔔/⏳]  闹钟 / 倒计时                           │
│          08:00 / 00:00                        │
│          到时间了 / 时间到                       │
│  [停止]  [稍后 5 分钟]          ⚙  ⏻            │   ← 稍后 5 分钟只有闹钟有
└──────────────────────────────────────────────┘
```

* 提醒期间**鼠标移开也不收起**（`_alertActive` 挡住悬停收起逻辑），保证响铃能被看见、能点掉；
* 「停止」→ `DismissRinging()` → 静音 + 关提醒；「稍后 5 分钟」→ `SnoozeAlarm(5min)` 重新排期；
* 关掉时：鼠标还在岛上 → 切回指标；已经移开 → 直接收起（下次展开自动复位成指标）；
* 声音在 `App.OnRingingChanged` 里编排，提醒面板在 `IslandShell`（因为它管展开/收起），VM 两样都不碰。

**已知取舍**：
* 音量跟系统音量走，没有 per-app 音量；
* 提醒面板会把岛自动展开（会短暂盖住屏幕顶部的其它窗口），但**不抢键盘焦点**；
* 调试钩子：`CAPSYN_ALARM_TEST=<秒>` / `CAPSYN_COUNTDOWN_TEST=<秒>` 启动即各起一个 N 秒后到点的计时，
  专门用来手动验证提醒面板 + 铃声。

### 12. 关闭程序（看板右下角 → 退出应用）
整个「电源岛」已按需求删除（过程与结论见下面的「电源岛与电源操作」表），退出入口只剩看板右下角这一个图标按钮：

```
DashboardIslandControl.OnCloseClick
  → DashboardIslandControl.ExitRequested
    → IslandShell.ExitRequested（IslandShell 只做转发）
      → MainWindow.OnExitRequested：Close() + Application.Current.Exit()
```

* 用 `Button.Click` 而不是光标轮询：窗口虽然带 `WS_EX_NOACTIVATE`（不抢焦点、不激活），
  但客户区里的控件照样能收到点击（这一点在电源按钮时代就实测过）。
* 因为不再有「悬停长出第三块岛」这件事，`IslandShell` 里跟电源岛有关的代码全部删掉了：
  电源岛命中框、250ms 收回宽限、130ms 级联收回计时器、第二根弹簧、
  以及出场顺序用的 `IslandAnimator.Nudge()`。
  现在只有一条悬停链：**胶囊/看板内 → 展开，移开 → 160ms 宽限后收起**。
* 窗口区域现在只有两块圆角矩形的并集（胶囊 ∪ 看板）：
  `IslandWindowStyler.UpdateIslandShape(panelWidthDip, panelHeightDip, cornerProgress)` 是**三个参数**
  （宽度和高度分开传，因为切视图时看板只长高、不改宽）；
  窗口高按公式 `40 + 6 + 430 × 1.12 ≈ 528`（按最高的那个视图留回弹余量，实测 616 × 528）。
* 这个按钮不写诊断日志，但真的退出。

### 13. 健壮性：单实例 / 异常兜底 / 显示器变化 / 深色主题
这一组是「整程序」级别的处理，不改变任何界面表现：

* **单实例**（`Services/SingleInstance.cs`）：命名 Mutex（`Capsyn.SingleInstance`，默认 `Local\` 作用域 =
  当前登录会话）。拿不到就说明已经有一个在跑 → 记一条日志后 `Environment.Exit(0)`。
  目的：双击两次、或以后「开机自启 + 手动打开」时不会出现两个胶囊叠在一起。
  Mutex 本身创建失败（权限等）时按「允许多实例」放行 —— 保护措施不该把程序挡在门外。
* **未处理异常兜底**（`App.xaml.cs`）：接 `Application.UnhandledException` +
  `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`，
  全部写进 exe 同目录的 `capsyn-crash.log`（**不受 `CAPSYN_DIAG` 开关限制**）。
  UI 线程上的异常记完标记 `Handled = true`：定时器回调出错不该让整只岛静默消失
  （定时器/过场都是 `async void`，否则异常会直接把进程带走）。
* **缩放 / 分辨率 / 主屏变化**（`IslandWindowStyler`）：子类化里处理 `WM_DPICHANGED` 与
  `WM_DISPLAYCHANGE` → 重新 `ApplyIslandLayout()`（重算窗口矩形）并按最近的形状进度重算窗口区域。
  两个细节：① 位置锁会把 `MoveAndResize` 拦回去，所以先临时解锁、算完再锁上；
  ② DPI 是「移完再取」的 —— 窗口刚创建时可能还停在别的显示器上，先取到的是那块屏的比例，
  取到不一样就用新比例再算一遍。
* **固定深色主题**（`App.xaml` 的 `RequestedTheme="Dark"`）：岛永远是黑的，控件如果跟随系统主题，
  浅色主题下按钮/开关/提示条会露出浅色底 —— 看板里那些半透明白的指针态资源就是在手工补这个洞。
  固定深色之后这类洞就不用一个个补了。
* **动画热路径的日志**：`UpdateIslandShape` 里那行「region apply」是逐帧走的，
  字符串插值发生在调用之前，所以先判断 `Diagnostics.IsEnabled` 再拼字符串，避免白造垃圾。

---

### 拖拽解压（v4.1.0 新增：把 zip 拖到岛上）

**交互**：把 `.zip` **拖到岛上**（胶囊，或已经展开的看板区域）→ 岛展开 + 下方出现**虚线落点区**
→ 在落点区松手 → 弹**系统原生文件夹选择器**，默认定位在压缩包所在目录
→ 解压（带进度条）→ 完成后显示落点路径 + 文件数，可以「打开文件夹」或「完成」。

> 不做全局拖拽探测：拖窗口、拖别的文件、在屏幕别处拖来拖去都**不会有任何反应**，
> 只有文件真的被拖到岛窗口上，落点区才会出现。
>
> **拖拽期间（左键按着）不响应悬停**：否则拖着一个 zip 划过胶囊的瞬间，悬停会先把指标看板（350 高）
> 展开，紧接着拖放事件到了又切成解压岛（230 高）—— 看起来就是"先显示看板，再显示解压岛"。
> 现在拖拽时只由拖放逻辑展开，岛会**一次性直接长成解压岛**。
>
> **拖出岛（没落下）就直接消失**：`CancelDrop()` 不再切回指标视图，只通知外壳收起；
> 下次悬停展开时 `ResetToMetricsView()` 会复位成指标。这样"移出"不会先闪一下看板。
> 另外如果拖出时光标还在岛内（晃了一下又回来），会立刻复位成指标，避免残留"松开以解压"。
>
> 坑：拖拽期间 `OnHoverTick` 因为"左键按着"会提前 return —— 早期版本在那里顺手调了
> `_collapseTimer.Stop()`，导致拖拽中每 50ms 都把收起计时器停一次，**移出后岛永远收不起来**。
> 现在那里只 return 不动计时器，收起与否由 `OnCollapseTick` 里的 `_dragActive` / `_keepExpanded` 判断。

**实现**

| 环节 | 位置 | 说明 |
| --- | --- | --- |
| 接收拖放 | `IslandShell.xaml` 的 `ShellRoot`（`AllowDrop` + `DragOver/DragLeave/Drop`） | OLE 拖放只在光标进入岛窗口时通知；`DragLeave` 给 350ms 宽限，光标从胶囊挪到落点区时不会闪 |
| 落点 UI | `DashboardIslandControl.xaml` 的 `DropPanel` | 虚线框 = `Rectangle` + `StrokeDashArray="4 3"` + 圆角；状态：`松开以解压` → `解压中…`（进度条）→ `解压完成` / `不支持这个格式` / `解压失败`；**这一页不显示右下角页脚（设置 / 关闭程序）和中间那条横线** —— 页脚/横线的可见性在 `SwitchTo` 里、**瞬切分支之前**设好（早期放在瞬切之后，导致解压岛上横着一条线） |
| 选择目录 | `Services/FolderPicker.cs` | 手写 COM 互操作走 `IFileOpenDialog` + `FOS_PICKFOLDERS`：WinRT 的 `FolderPicker` **没法指定默认路径**，而「默认压缩包所在目录」正是这个功能的重点，所以用 `SetFolder()` |
| 解压 | `Services/ArchiveExtractor.cs` | **零第三方库、零打包二进制的双内核**：<br>• `.zip` → .NET 自带 `ZipArchive`（逐条解压、按字节精确进度、挡 zip-slip）；<br>• `.7z / .rar / .tar / .tar.gz / .tgz / .xz / .bz2` → **系统自带 `C:\Windows\System32\tar.exe`（bsdtar + libarchive）**，不往包里塞任何东西；进度靠**解压期间轮询目标目录**（已解出文件数 / 包内文件数）算出来，实测 3000 个文件的包能出 6% → 15% → … → 96% 的连续进度（单个超大文件会 0 → 100 跳变）；<br>两种情况都：后台线程、**重名不覆盖**（自动落到 `压缩包名\` 子目录并在结果里注明）、解完只统计本包内的文件 |
| 保持展开 | `IslandShell._keepExpanded` | 拖拽中 / 解压中 / 结果没确认之前都不收起（和到点提醒的 `_alertActive` 同一套路） |

**调试钩子**：`CAPSYN_DROP_TEST=<zip 路径>` 跳过真实拖拽与选目录，直接走「落点区 → 进度 → 结果」
（OLE 拖放没法用脚本模拟，只能这样自动验证）；`CAPSYN_PICK_TEST=1` 只创建对话框并应用默认目录、
不 Show，验证 COM 互操作没写错。

开 `CAPSYN_DIAG=1` 时日志会写 `drag catcher: on/off`、`drag: 拖的是 zip → 显示落点区` /
`drag: 拖的不是 zip → 不显示落点区`，排查"某种来源拖过来没反应"看这几行就够了。

**已知**：只支持 `.zip`。要支持 7z / rar，改走系统自带的 `C:\Windows\System32\tar.exe`（libarchive）
或 shell 的「全部解压缩」动词即可，`ArchiveExtractor` 的接口不用动。

### 音乐控制（分支 `feature/music-control`，未发布）

数据来自 **系统媒体会话（SMTC）**，不是给某个播放器写死的适配器 —— 汽水音乐 / 网易云 / Spotify / 浏览器
注册了媒体会话就都能读能控；UIA 那条路在本机实测对汽水音乐是空的（Chromium 默认不暴露无障碍树），
所以没走。

| 项 | 实现 |
| --- | --- |
| 数据层 | `Services/MediaMonitor.cs`：`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` → 订阅 `SessionsChanged` / `CurrentSessionChanged` / `MediaPropertiesChanged` / `PlaybackInfoChanged` / `TimelinePropertiesChanged`；快照 `MediaSnapshot`（歌名 / 歌手 / 专辑 / 是否播放 / 位置 / 时长 / 能否跳曲 / 来源应用）；事件统一编组到 UI 线程 |
| 控制 | `TryTogglePlayPauseAsync` / `TrySkipNextAsync` / `TrySkipPreviousAsync`（返回 false 就禁用对应按钮） |
| 进度 | SMTC 只给「某一刻的位置」，`EstimatePosition()` 在播放中按经过时间插值；音乐视图可见时用 250ms 定时器刷新进度条与时间文字（隐藏时停表） |
| 封面 | `Thumbnail.OpenReadAsync()` → `byte[]` → UI 线程转 `BitmapImage`；换歌才重新读（按「歌名\|歌手」缓存 key） |
| 收起态 | **音乐播放时，时间岛胶囊变成音乐胶囊**：`♪ 歌名`（12px 状态文字，暂停时显示「已暂停」）；没有媒体会话时恢复时钟 |
| 展开动画 | **先展开看板（居中）→ 看板往左移 + 副岛从看板后面往右滑出**（由 `_musicSlideSpring` 逐帧驱动，`ApplyMusicSlide(slide)` 摆两块岛的位置、`PublishShape` 同步窗口区域）；鼠标移出时**反向**：副岛先往左滑回看板后面、看板回中，然后才收起 |
| 展开态 | 看板在左、**音乐副岛在右**（340×210，`IslandOptions.MusicSideWidth / MusicSideHeight / MusicSideGap`）：<br>• 窗口区域 = 胶囊 ∪ 小圆 ∪ 看板 ∪ 副岛（鼠标在副岛上也算岛内，不会收起）；<br>• 副岛内容**垂直居中铺满**（因为和看板等高，不能留大片空白）：背景专辑封面光晕，140 圆角封面 + 歌名 20px（单行省略）/ 歌手 13px + 进度条 240 宽 + 时间 + 控制（40px 上一首 + **52px 主色圆形播放暂停** + 40px 下一首）；整套内容 290px 高，正好装进与看板等高的 350，不会裁切；<br>• 副岛头部「正在播放」+ 3 根 Composition 均衡器（播放时跳动）；<br>• 看板根 Grid 加了不透明黑底，副岛滑出/滑回时被看板挡住，看起来是「从看板后面滑出来」 |
| 看板清理 | 指标页「♪ 歌名」入口、看板内旧 `Music` 视图（XAML + 绑定 + 处理代码 + `ExpandedMusicHeight`）**已全部删除**，音乐只走副岛 |
| 实测 | 汽水音乐正在播放时：UI 读到 `Shark / 小恒 / 0:04 / 2:25 / 汽水音乐`；点播放暂停 → `ok=True` 且状态真的翻转；下一首 → 歌变成 `清空 - 袁小葳, 阿辰`；上一首 → 回到 `Shark`；无崩溃 |

**调试钩子**：`CAPSYN_MUSIC_TEST=1` 启动后自动展开看板并切到音乐视图（截图 / 自动验证用）。

---

## 四、验收对照（实测结果）
### 里程碑 1：时间岛
| 验收项 | 结果 |
| --- | --- |
| 顶部正中间出现黑色胶囊 | ✅ 胶囊屏幕矩形 `(800,10) 320x40`（1920×1080@100% → 居中、距顶 10px） |
| 里面活跃跳动 `HH:MM:SS` | ✅ 每秒刷新；间隔 1.6s 的两张截图里时间区域像素发生变化 |
| 背景纯黑、无白边、无默认阴影 | ✅ 胶囊内取样 `#000000`；圆角外是桌面像素；四周无阴影渐变 |
| 不出现在任务栏、Alt+Tab 看不到 | ✅ `IsShownInSwitchers=false` + `WS_EX_TOOLWINDOW`（exstyle `0x08000188`），无 owner |
| 切到别的全屏/窗口化应用仍最上层 | ✅ `WS_EX_TOPMOST`；Z 序实测在任务栏与最大化 Edge 之上 |
| 位置固定，拖不动 | ✅ 外部 `MoveWindow` 后矩形仍为钉好的值 |
| 点击不触发动作、不抢焦点 | ✅ 点击前后前台窗口句柄不变 |

### 里程碑 2：看板岛（鼠标悬停展开）
| 验收项 | 结果 |
| --- | --- |
| 鼠标移上去展开、移开收回 | ✅ 收起态区域 `(92,0)-(412,40)`；展开态区域 `(27,0)-(477,396)` 即 450×396（胶囊 ∪ 看板） |
| 看板在**时间岛下方** | ✅ 时间岛固定在窗口顶部（屏幕 `800,10`），看板从它下方 6px 缝隙处向下生长，展开时两者同时可见 |
| 看板尺寸 550 × 350 | ✅ 展开区域 `(33,0)-(583,396)` 即 550×396（胶囊 ∪ 看板）；周围桌面正常透出，胶囊仍在屏幕 `800,10` |
| 第一行三张卡 + 横线 + 电源按钮 | ✅ 处理器 / 内存 / 网络并排一行，下面一条横线，横线下面是带矢量图标的「电源」按钮（**这个按钮与电源岛后来一起删除，见「电源岛与电源操作」表**） |
| 电源按钮只做 UI | ✅ 实测点击会在诊断日志留下 `power button clicked`（说明客户区控件的命中区域正常）—— 同时证明了**不激活的窗口照样能收到按钮点击**，现在右下角的「关闭程序」用的就是这个结论 |
| 弹簧动画（保留） | ✅ 逐帧采样到完整曲线，末尾带一次过冲：`342×79 → … → 568×423（过冲）→ 550×396` |
| 生长动画（已删除） | ✅ 内容只做淡入淡出，不再整体放大、不再逐条上浮入场 |
| CPU / 内存 / 网络上下行数据 | ✅ 实测读数如 `处理器 19%`、`内存 9.4 / 16.0 GB 59%`、`网络 ↓ 4.9 KB/s ↑ 0 B/s`；间隔 1.8s 两帧有 838 像素变化 |
| 空白区域留白 | ✅ 第四格与下半部分留空，等通知 / 音乐 / 计时器 |
| 原有行为不回退 | ✅ 胶囊位置仍是 `(800,10)`、仍置顶 / 不进任务栏 / 点击不抢焦点 / 拖不动 |

### 里程碑 4：设置按钮（只有 UI，功能未接）
| 验收项 | 结果 |
| --- | --- |
| 看板右下角出现「设置」按钮（齿轮矢量图标，只有图标没有文字） | ⏳ 待运行核对（本地构建 0 警告 0 错误） |
| 点击不触发任何动作、不写诊断日志 | ⏳ 待运行核对（XAML 里没有 `Click`，只留了 `TODO(扩展)`） |
| 样式与位置：和「关闭程序」同款的 40 × 40 圆形图标按钮（`IconButton` 样式），排在它左边 | ⏳ 待运行核对 |
| 原有行为不回退 | ⏳ 待运行核对（只改 `DashboardIslandControl.xaml`，没碰悬停判定 / 窗口区域 / 动画） |

### 里程碑 3 / 5 / 6：电源岛与电源操作（**已全部删除**，这里只保留结论）
这一批功能最终一个都没留下代码。时间线和结论：

| 阶段 | 结果 |
| --- | --- |
| 电源岛（悬停「电源」按钮 → 看板下方展开 550 × 84 面板） | ✅ 当时实测通过：窗口区域 `550×396 → 550×486`；级联收回高度轨迹 `486→471→441→431→412→404→48→40`；四个按钮「关机 / 重启 / 睡眠 / 关闭程序」并排一行 |
| 三个按钮去掉后端与诊断日志 | ✅ 只剩 UI（连 `Click` 都不接） |
| 「关机」试接真实 `shutdown.exe` | ✅ 日志实测 22 次全部 `exit code=5`（拒绝访问）—— 进程起得来，但一次都没生效 |
| 改调 `advapi32!InitiateShutdownW` 重试 | ✅ 代码写完并编译通过（显式启用 `SeShutdownPrivilege`），未再上机验证 |
| 关键结论：权限 | ✅ 正常登录令牌**有** `SeShutdownPrivilege`（`whoami /priv` 显示「已禁用」，属正常状态，程序可自行启用）；沙箱化终端里只剩 `SeChangeNotifyPrivilege` 一条 —— 那次 `5` 是受限令牌上下文造成的 |
| 常量取值 | ✅ 查本机 SDK 头文件核对：`winreg.h` 的 `SHUTDOWN_POWEROFF=0x8` / `SHUTDOWN_RESTART=0x4`、`reason.h` 的 `SHTDN_REASON_MAJOR_APPLICATION=0x00040000` / `FLAG_PLANNED=0x80000000` |
| 电源岛缩小到 240 × 68 | ✅ 代码改过一版，随后整块删除 |
| 最终处理 | ✅ 电源岛整块删除：`PowerIslandControl.xaml(.cs)`、`ShutdownService.cs`、`Diagnostics.LogAlways`、`NativeMethods` 里的 P/Invoke，以及 `IslandShell` 的电源岛命中框 / 弹簧 / 级联计时器 / `IslandAnimator.Nudge()`；`UpdateIslandShape` 回到两个参数；构建 0 警告 0 错误 |
| 新的退出入口 | ⏳ 待运行核对（看板**右下角**一个 40 × 40 的红色圆形电源图标按钮，只有图标没有文字，点击后进程退出） |

> 以后要重新做关机 / 重启 / 睡眠：`shutdown.exe` 在受限令牌下必然返回 `5`，优先直接调
> `advapi32!InitiateShutdownW`（关机 `SHUTDOWN_POWEROFF=0x8`，重启 `SHUTDOWN_RESTART=0x4`），
> 调用前用 `AdjustTokenPrivileges` 启用 `SeShutdownPrivilege`；注意这个 API
> **没把特权赋上也会返回 TRUE**，必须复查 `GetLastWin32Error()`（`0` 成功 / `1300` 没分配上）。

### 里程碑 9：时间工具视图（UI 重构 + 接上功能）
| 验收项 | 结果 |
| --- | --- |
| 看板左下出现「时间工具」按钮（时钟矢量图标 + 文字） | ✅ 实跑截图核对（`click: time tools`） |
| 点击后看板从 350 **长高到 430**（弹簧），再点「返回」缩回 350 | ✅ 实跑核对，日志 `dashboard view: time tools (panel 430 dip)` / `… metrics (panel 350 dip)`，区域逐帧 `550×350 → 550×430` |
| 分段控件「正计时 / 倒计时 / 闹钟」，指示条跟着模式滑动、颜色跟着换 | ✅ 实跑截图（蓝 / 橙 / 紫） |
| 环形进度：正计时走 60s、倒计时走剩余比例、响铃画满 | ✅ 实跑截图（倒计时 1 分钟归零后整圈变红 + 读数「时间到」） |
| 正计时读数 `mm:ss.f` 每 50ms 跳；「记一圈 / 上一圈」 | ✅ 实跑截图（`00:02.2`、按「记一圈」后上一圈有值） |
| 倒计时 1/3/5/10 分钟预设 + 开始/暂停/继续/停止 + 复位 | ✅ 实跑截图（1 分钟 → 00:58 → 00:00 → 时间到 → 停止） |
| 闹钟 ±1/±5 分钟 + 启用/取消，启用后显示「N 小时后」 | ✅ 实跑截图（`08:00`、`19.7 小时后`、按钮变「取消」） |
| 主按钮 / 复位 / 预设的禁用态与指针态 | ✅ 按钮走 `PresetChip` / `PrimaryActionButton` 样式，禁用态用 token |
| 右下角「设置」「关闭程序」两个视图里都常驻，且跟着看板底边上下滑 | ✅ 实跑截图（指标 334 / 工具 414 底边对齐） |
| 过场动画：iOS 风格 push / pop（入场 300ms / 出场 150ms，60% 视差，cubic-bezier(0.32,0.72,0,1)） | ✅ 实跑核对（Composition `Translation`，跑在合成线程） |
| 连点「时间工具 / 返回」不会收错视图 | ✅ 收尾用 `_viewSwitchToken` 序号保护 |
| 计时中把鼠标移开：岛收起但计时不停，再展开读数是对的 | ✅ 计时器与面板可见性解耦，只在 `NeedsClock` 时 tick |
| 崩溃 / 未处理异常 | ✅ 全流程跑完 `capsyn-crash.log` 为空 |

> 测试方式：WinUI 3 的 ContentIsland 不接 `mouse_event` 注入的点击，所以验证脚本改用
> **UI Automation**（按 `AutomationId` 找到按钮 → `InvokePattern.Invoke()`）驱动，
> 截图用 `PrintWindow(PW_RENDERFULLCONTENT)` 只拍窗口本身。脚本在 `.tools/`（已 gitignore）。

### 里程碑 9.5：收起后的顶部胶囊「监测区」
| 验收项 | 结果 |
| --- | --- |
| 没有计时时胶囊显示时钟（宽 320） | ✅ 实跑 + 区域日志 `pill 320x40 + dot 0` |
| 只开正计时 / 倒计时 → 收起后胶囊显示「图标 + mm:ss」、时钟消失（宽 172） | ✅ 实跑截图（`00:02` / `00:58`）+ `pill 172x40 + dot 0` |
| 两个计时同时跑 → **两块独立的岛**（胶囊 172 + 圆 40 + 10 缝隙），读数还在走 | ✅ 实跑 + 区域日志 `pill 172x40 + dot 40`；截图里圆与胶囊之间露出桌面 |
| 点小圆换位（圆 ↔ 胶囊内容互换） | ✅ 实跑截图（换位后圆变倒计时、胶囊变正计时） |
| 复位其中一个 → 圆消失、胶囊收窄（172）；两个都复位 → 时钟回来（320） | ✅ 区域日志 `dot 40 → dot 0`、`pill 172 → pill 320` |
| 倒计时在**收起状态**下归零 → 胶囊显示红色 `00:00`，且被提到主位 | ✅ 实跑截图（收起等 63 秒后截） |
| 只启用闹钟 → 胶囊保持时钟（闹钟不占用） | ✅ 实跑截图 + `pill 320x40 + dot 0` |
| 计时跨收起 / 展开不中断、读数连续 | ✅ 正计时在小圆里从 `00:02` 走到 `00:13` |
| 胶囊 / 圆变宽变窄时窗口区域逐帧跟着变（不露边、不裁掉内容） | ✅ 区域日志 `320 → 172 → 172+圆40 → 172 → 320` 连续过渡 |
| 启动 / 全流程无崩溃 | ✅ `capsyn-crash.log` 为空（中途修掉一个静态初始化顺序的 NRE） |

### 里程碑 9.6：设置 → 铃声（含自定义）+ 到点提醒
| 验收项 | 结果 |
| --- | --- |
| 点 ⚙ 打开设置视图（看板 360 高），返回回指标 | ✅ 实跑 + 日志 `dashboard view: settings (panel 360 dip)` |
| 两行各 5 个内置音效，默认都选中第一个 | ✅ 实跑截图（闹钟紫、倒计时橙，第 1 个高亮） |
| 点某个音效 = 选中 + 试听 + 存盘 | ✅ `settings.json` 变成 `{"AlarmRingtone":2,...}`，日志 `ringtone: play Alarm #2 "电子脉冲" (loop=False)` |
| 自定义：选本地音频文件 → 复制进本地铃声目录 + 存盘 + 试听 | ✅ 实跑（预置 `AlarmCustomFile` 后触发闹钟：`ringtone: play Alarm #-1 "custom-alarm.wav" (loop=True)`）；选择器走 `FileOpenPicker + InitializeWithWindow` |
| 切回内置音效会清掉自定义 | ✅ 实跑（点内置后 `settings.json` 里 `AlarmCustomFile: null`） |
| 重启后沿用上次选择 | ✅ 文件持久化（`%LOCALAPPDATA%\Capsyn\settings.json`），失效/越界自动退回默认 |
| 10 个内置音效都是合法 WAV | ✅ 落盘后 `alarm-0.wav` 97,066 B / `alarm-2.wav` 88,244 B，可正常播放 |
| **倒计时归零**：岛自动展开成提醒面板 + 响铃 | ✅ 实跑（`CAPSYN_COUNTDOWN_TEST=6`）：`SetDashboardExpanded(True)` → `dashboard view: alert (panel 180 dip)` → `ringtone: play Countdown #1 "双叮" (loop=True)` |
| **闹钟到点**：同样的提醒面板（紫色铃铛 + 08:00 + 稍后 5 分钟）+ 响铃 | ✅ 实跑（`CAPSYN_ALARM_TEST=8`）：`alert: show Alarm` + `ringtone: play Alarm #0 …` |
| 提醒期间鼠标移开不收起 | ✅ `_alertActive` 挡住悬停收起；实跑时鼠标在屏幕外，岛仍展开 |
| 「停止」：静音 + 关提醒 + 复位 | ✅ 实跑：`alert: stop (Countdown)` → `ringtone: stopped` → 面板收起 |
| 「稍后 5 分钟」：收铃并重新排期 | ✅ 代码路径 `SnoozeAlarm(5min)`（日志 `alarm: snoozed for 5 min -> …`） |
| 全流程无崩溃 | ✅ `capsyn-crash.log` 为空 |

### 里程碑 10：健壮性整改（单实例 / 异常兜底 / 显示器变化 / 深色主题）
| 验收项 | 结果 |
| --- | --- |
| 双击两次 exe 只有一个胶囊（第二个进程直接退出） | ⏳ 待运行核对（命名 Mutex `Capsyn.SingleInstance`；退出时写一条诊断日志） |
| 出现未处理异常时写出 `capsyn-crash.log`（含调用栈） | ✅ 三条钩子都接上了（`Application.UnhandledException` / `AppDomain` / `UnobservedTaskException`），日志不受 `CAPSYN_DIAG` 限制 |
| UI 线程异常后程序继续活着 | ⏳ 待运行核对（`e.Handled = true`） |
| 改显示缩放 / 改分辨率 / 换主屏后胶囊位置与大小自动重算 | ⏳ 待运行核对（`WM_DPICHANGED` / `WM_DISPLAYCHANGE` → `ApplyIslandLayout()`；DPI 采用「移完再取」的两遍算法） |
| 位置锁不会把重算挡回去 | ✅ 处理消息时临时解锁 `_positionLocked`，算完再锁回新矩形 |
| 界面固定深色主题（不再依赖系统主题 + 手工刷资源） | ⏳ 待运行核对（`App.xaml` 的 `RequestedTheme="Dark"`；看板里原有的半透明白指针态资源保持不变） |
| 动画热路径不再白造字符串 | ✅ `UpdateIslandShape` 的逐帧日志先判 `Diagnostics.IsEnabled` |
| 关闭程序 / 展开收起 / 看板两个视图不受影响 | ✅ 只加钩子与重算逻辑，界面代码一行没改；构建 0 警告 0 错误 |

---

## 五、已知限制 / 下一步 TODO

代码里都留了 `TODO(扩展)` 注释，主要接入点：

* **想恢复生长动画**：`IslandAnimator` 的旧实现（内容 0.94→1 + 条目错帧上浮淡入）在 git 历史里，
  当前版本按需求只保留弹簧 + 淡入淡出
* **通知队列**：`DashboardViewModel` 加可绑定属性 + `DashboardIslandControl` 下半部分放列表
* **想重新加电源操作**：关机 / 重启 / 睡眠已按需求删除（结论见「电源岛与电源操作」那张表）。
  要加回来**优先直接调 `advapi32!InitiateShutdownW` 并自行启用 `SeShutdownPrivilege`**，
  别再走 `shutdown.exe`（受限令牌下必然返回 `5`）；建议先弹确认再执行
* **正计时 / 倒计时 / 闹钟接功能**：现在三个按钮只有 UI（不接 `Click`）。
  接的时候：`DashboardIslandControl` 的按钮加 `Click` → 交给 ViewModel/服务跑计时 →
  时间岛上显示剩余 / 已用时间（时间岛目前只显示 `HH:MM:SS`）
* **设置面板**：`DashboardIslandControl` 的 `SettingsButton` 现在只有 UI（不接 `Click`、不写日志），
  要接设置项时给它加 `Click`，然后弹窗或者再展开一块岛
* ~~**音乐卡片 / 计时器**~~：音乐已实现（`MediaMonitor` + `DashboardView.Music`，见第三节）；计时器已在时间工具视图里
* **更多图表**：第四格、以及看板下半部分
* **鼠标穿透开关**：`IslandWindowStyler.ApplyChrome()` 里加 `WS_EX_TRANSPARENT | WS_EX_LAYERED` + 运行时开关
* **点击交互**：`IslandShell` 里订阅指针事件（目前刻意不接，只有悬停判定）
* **托盘图标 + 开机自启**：`MainWindow` 构造函数（退出入口已经做了：看板右下角那个图标按钮）
* **WM_DPICHANGED / WM_DISPLAYCHANGE**：已在 `IslandWindowStyler` 的子类化里处理（缩放/分辨率/主屏变化后重算窗口矩形与形状）；剩下的是**多显示器**策略 —— 目前固定钉在主屏顶部居中，不跟随光标所在屏
* **外部配置文件**：`IslandOptions` 改为读 exe 同目录的 `island.config.json`
* **形状同步帧率**：目前外轮廓约 30Hz（`DispatcherTimer`），如果觉得边缘不够顺，
  可以把它挪到独立线程做更高频率的 `SetWindowRgn`

---

## 六、环境说明

* 依赖：.NET 9 桌面运行时、Windows App Runtime **1.8**（`8000.994.2142.0`，已安装）。
  * 本机实际只装了 .NET **8 / 10 / 11** 运行时，没有 9。`Capsyn.csproj` 里的
    `<RollForward>Major</RollForward>` 让框架依赖的 net9.0 程序落到 10.0.15 上跑，无需装 .NET 9；
    装了 .NET 9 之后这一行留着也无害（会优先用 9.x）。
  * 本机开发（`dotnet build` / VS F5）用框架依赖就行；**要发到没装这些运行时的机器**时用自包含发布
    （命令见下面的「打安装包 / 自包含发布」）。
* `nuget.config`：只保留 `nuget.org` 官方源（方便以后在 VS 里装新包），包统一解到项目内的
  `.tools\nuget-packages`（已 gitignore），不会写到项目目录之外。
  **不要再把 `C:\Users\sqw-j\.nuget\packages` 配成 `fallbackPackageFolders`**：该目录一旦不存在，
  NuGet 还原会直接抛 `NuGetPackaging.Core.PackagingException: Unable to find fallback package folder`，
  VS 里表现为 `MSB4018` + `ResolvePackageAssets 任务意外失败`（错误码下方还有 `NU1301`）。
  第一次还原需要联网下载 Windows App SDK 等包。
* **Visual Studio 报一片 `NU1100`（`Microsoft.NETCore.App.Runtime.win-x86 / win-x64 / win-arm64`、
  `Microsoft.WindowsDesktop.App.Runtime.*`、`Microsoft.AspNetCore.App.Runtime.*`）的原因与修复**：
  * 原因：`RuntimeIdentifiers` 被 WinUI/WinAppSDK 注入成 `win-x86;win-x64;win-arm64`，而 .NET SDK
    默认把这些 **RID 专用运行时包** 当成 restore 的下载依赖；框架依赖（`SelfContained=false`）
    的构建其实完全不需要它们，本机缓存里也没有，于是解析失败。
  * 为什么只有 VS 报错：`dotnet` CLI 是 Core MSBuild（`UsePackageDownload=true`，解析不到只记警告），
    VS 是 Framework MSBuild（`UsePackageDownload=false`，会退化成普通 `PackageReference` → 硬错误 `NU1100`）。
  * 修复（已写进 `Capsyn.csproj`）：显式收窄 RID 并关掉这两类下载 —
    `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`、
    `<EnableRuntimePackDownload>false</EnableRuntimePackDownload>`、
    `<DisableTransitiveFrameworkReferenceDownloads>true</DisableTransitiveFrameworkReferenceDownloads>`。
  * 已实测：`dotnet restore` 与 VS 2022 的 Framework MSBuild `/t:Restore` 都返回 0，
    `project.assets.json` 里不再有运行时包依赖、没有 `NU1100`。
* **这两个下载开关带条件**（`Condition="'$(SelfContained)' != 'true'"`）：
  **自包含发布**（`publish -p:SelfContained=true`）恰恰必须要 RID 运行时包，
  无条件关掉会报 `NETSDK1185`（「运行时包不可用」）。带条件之后 ——
  `dotnet build` 照旧（NU1100 修复保留），`publish -p:SelfContained=true` 自动放开下载。
* **打安装包 / 自包含发布**：

```powershell
# 自包含：目标机器不需要预装 .NET 9 / Windows App Runtime，产物约 200 MB、500 多个文件
dotnet publish Capsyn.csproj -c Release -p:Platform=x64 -r win-x64 `
  --self-contained true -p:WindowsAppSDKSelfContained=true
# 打包原料就是这个目录（不是 dotnet build 的输出目录）：
#   bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\publish\
```

  * WinUI 3 / WinAppSDK **不支持 `PublishSingleFile` 与 `PublishTrimmed`**，别开。
  * **踩过的坑**：`dotnet publish` 默认**不会**把 XAML 编译产物（每个 `.xaml` 的 `.xbf` 和
    MRT Core 的 `Capsyn.pri`）复制进发布目录 —— publish 只收「build 产物清单」里的文件，
    而 `.xbf` 是 XAML 编译器用 target 拷出来的、不在清单里。结果发布出来的程序双击起不来，
    而 `dotnet build` 一切正常，极难发现。
    已在 `Capsyn.csproj` 末尾加 `CopyXamlArtifactsToPublish` target，在 `Publish` 之后把这批文件
    补拷进去（**只补拷，不改 build 行为**）。发布完请确认 `publish\` 里有
    `App.xbf`、`MainWindow.xbf`、`Controls\*.xbf` 和 `Capsyn.pri`。
  * 另一种（官方模板的）做法是把 `<EnableMsixTooling>` 改成 `true`：那样 `.xbf` 会被打进
    `Capsyn.pri`（约 1.3 MB），publish 也会自动带上它 —— 缺点是会改变 build 输出的形态，
    所以本项目保留 `false` + 补拷 target 这条路。
* 如果构建时出现 `MSB6003 / CreatePipe 拒绝访问`，说明当前 shell 有进程/管道沙箱限制：
  XAML 编译器需要 `cmd.exe`/`csc.exe` 子进程，换到普通 PowerShell 或 Visual Studio 里构建即可。

---

## 七、GitHub 仓库（同步与回滚参考）

* 仓库：<https://github.com/SQW-Rool/Capsyn>
  （原来叫 `Capsyn-backup`，改名后 GitHub 会让旧地址自动跳转，但文档和脚本统一用新地址；
  本地 clone 想换过来就执行一次
  `git remote set-url origin https://github.com/SQW-Rool/Capsyn.git`）
* 提交内容：源码与配置。`bin/`、`obj/`、`.vs/`、`.tools/`、`capsyn-diag.log`、`capsyn-crash.log` 都已被 `.gitignore` 忽略。
* 约定：**本地构建通过（0 警告 0 错误）+ 运行验收通过之后**才提交推送；提交信息写清本次改动，
  重要节点同时打 tag，GitHub 的提交历史 + tag 就是回滚锚点。
* 一键备份脚本（`tools/backup-to-github.ps1`）：

```powershell
cd G:\Capsyn
powershell -ExecutionPolicy Bypass -File .\tools\backup-to-github.ps1 -Message "改了什么" -Tag v0.3.1-ProjectChange-U2
# 不需要 tag 时省略 -Tag
```

* 回滚方式：

```powershell
git fetch origin --tags
git log --oneline --decorate      # 找到要回滚到的 commit / tag
git checkout <tag>                # 只看当时的代码（detached HEAD，不动 main）
git checkout main
git revert <commit>               # 或撤销某次提交（保留历史，推荐）
git reset --hard <commit>         # 或彻底回退本地 main（危险，仅本地）
```

* 已备份版本：
  * `v0.1.0-time-island` —— 时间岛最小可用版本（无边框置顶胶囊 + 每秒 `HH:MM:SS`）。
  * `v0.2.0` —— 看板岛（悬停展开，550 宽，CPU / 内存 / 网络上下行三张卡 + 横线 + 电源按钮）
    + 电源岛（悬停电源按钮在看板下方展开 关机 / 重启 / 睡眠 / 关闭程序）+ 删除条目生长动画（只保留弹簧 + 内容淡入淡出）。
    （`v0.1.0` 之后的看板岛、电源岛、去生长动画这三步是**一次性提交**的，所以共用一个 tag。）
  * `v0.2.1` —— 「关闭程序」按钮标红（红底 / 红边 / 红字 + 红色指针态），其它三个按钮不变。
  * `v0.2.2` —— 看板岛第 3 行加「设置」按钮（矢量齿轮图标 + 文字，**只有 UI**，点击不接任何动作）；
    电源岛的「关机 / 重启 / 睡眠」删掉后端（连 `Click` 都不接），四个按钮都不再写诊断日志
    （「关闭程序」仍只保留退出）。
  * `v0.2.3-Power-Modified` —— **电源岛整块删除**（`PowerIslandControl`、`ShutdownService`、相关 P/Invoke、
    `IslandShell` 的电源岛状态机与 `IslandAnimator.Nudge()` 全部移除）；
    「关闭程序」改成看板岛**右下角的图标按钮**（40 × 40 红色圆形，只有图标没有文字），
    「电源」按钮一并删除，看板第 3 行只剩左下的「设置」；`UpdateIslandShape` 回到两个参数，
    窗口高从 538 收到 438。
  * `v0.2.4-Setting-Modified` —— 「设置」按钮**也挪到右下角、也改成只有图标**：
    原来是左下带「设置」文字的圆角矩形（`RowActionButton`），现在和「关闭程序」同款 ——
    40 × 40 圆形图标按钮（新抽出 `IconButton` 管尺寸 / 圆角 / 描边，`CloseIconButton` 改成 `BasedOn` 它），
    两个按钮在底行右对齐排列，「设置」在左、「关闭程序」在右，间距 12。
  * `v0.3.0-TimeToolIslandUI-Update` —— 看板岛加**「时间工具」视图**（按需求只有 UI，不接功能）：
    左下新增「时间工具」入口（时钟矢量图标 + 文字）；点进去显示「正计时 / 倒计时 / 闹钟」三个
    等宽按钮 —— **各有各的色系**（蓝 `#0A84FF` / 橙 `#FF9F0A` / 紫 `#BF5AF2`，各自带指针态），
    左上角「返回」圆形图标按钮；右下角「设置」「关闭程序」两个视图里都常驻。
    两个视图之间走 **iOS 风格 push / pop 过场**（旧视图滑出、新视图从另一侧滑入、60% 视差、
    入场 340ms / 出场 200ms、`cubic-bezier(0.32, 0.72, 0, 1)`，跑在合成线程、不参与布局）。
    两个坑也一并记在 README 里：第 1 行高度必须固定成 104（否则过场时下面两行跳 64px）、
    复位视图时不能用 `StopAnimation` 清位移（要用 `SnapToRest` 显式推回 0）。
    行为：每次从收起态展开都会**复位成指标视图**（不点返回直接移开也一样）。
  * `v0.3.1-ProjectChange` —— **为打包（Inno Setup 等）做的工程改动**，界面这一版没有变化：
    ① `EnableRuntimePackDownload` / `DisableTransitiveFrameworkReferenceDownloads` 两个开关加上
    `Condition="'$(SelfContained)' != 'true'"` —— 自包含发布不再报 `NETSDK1185`，
    平时 `dotnet build` 的 NU1100 修复原样保留；
    ② `Capsyn.csproj` 末尾新增 `CopyXamlArtifactsToPublish` target，修掉
    「`dotnet publish` 出来的目录缺 `.xbf` / `Capsyn.pri`、双击起不来」这个坑（只补拷，不改 build 行为）；
    ③ 删掉重复写了两遍的 `<WindowsAppSDKSelfContained>`；④ README 第六节补自包含发布命令与这两个坑的说明。
    实测：`dotnet build` 0 警告 0 错误；框架依赖与自包含两种 publish 的产物都含
    `Capsyn.exe` + 5 个 `.xbf` + `Capsyn.pri`（自包含 509 文件 / 约 210 MB）。
  * `v0.3.1-ProjectChange-U2` —— **健壮性整改**，界面表现不变（都是「整程序」级别的处理）：
    ① **单实例保护**：命名 Mutex `Capsyn.SingleInstance`，第二个进程记日志后直接退出 ——
    双击两次、或以后「开机自启 + 手动打开」都不会出现两个胶囊叠在一起；
    ② **未处理异常兜底**：`Application.UnhandledException` / `AppDomain` / `UnobservedTaskException`
    全部写 exe 同目录的 `capsyn-crash.log`（**不受 `CAPSYN_DIAG` 限制**），UI 线程异常记完
    标记已处理、程序继续活着（定时器与过场都是 `async void`，否则一出错整只岛就静默消失）；
    ③ **缩放 / 分辨率 / 主屏变化**：子类化里处理 `WM_DPICHANGED` / `WM_DISPLAYCHANGE`，
    重算窗口矩形并按最近的形状进度重算窗口区域；配套两个细节 —— 位置锁会把 `MoveAndResize`
    拦回去（先临时解锁、算完锁回新矩形），DPI 改成「移完再取」的两遍算法（窗口刚创建时可能
    还停在别的显示器上）；
    ④ `App.xaml` 固定 `RequestedTheme="Dark"`，控件不再跟随系统主题（浅色主题下露浅底的问题从根上消掉）；
    ⑤ 动画热路径的逐帧日志先判 `Diagnostics.IsEnabled`，不再白造字符串；
    ⑥ 顺手：`IslandOptions` 注释里不存在的 tag 名改对、删掉无用的 `x:Name`、`.gitignore` 收掉崩溃日志。
    实测：干净构建 Debug + Release 均 0 警告 0 错误。
  * `v0.3.5-TimeToolsUI`（已随 `v4.0.0-beta` 发布）—— 时间工具视图**整页重构 + 接上真功能**：
    ① 看板变成**可变高度**：指标 350 / 时间工具 430，`IslandShell` 加第二根弹簧 `_heightSpring`，
    `UpdateIslandShape` 从「两个缩放比例」改成「宽 + 高 + 圆角进度」；窗口画布按 430 一次留够（616 × 528），
    切视图只改窗口区域，窗口尺寸与胶囊位置一秒都不用重算；
    ② 工具页重新排版：左上「返回」+ 标题 → **分段控件**（正计时 / 倒计时 / 闹钟，带滑动指示条）→
    **96px 环形进度 + 52px 等宽读数** → 模式自己的操作行（记一圈 / 预设 / ±分钟）→ 主按钮 + 复位；
    右下角两个图标按钮改成**两个视图共用的页脚层**，跟着看板底边上下滑（只改 `Translation`，不重排）；
    ③ 三个模式都**真的计时**（新 `TimeToolsViewModel`）：正计时 50ms tick + 记一圈、倒计时走 deadline
    （UI 线程卡住也不会走慢）+ 1/3/5/10 分钟预设、闹钟 HH:mm 微调 + 下一次响铃；到点进「时间到 / 响铃中」
    （环画满、读数与小字转红、主按钮变「停止」）；计时跟面板可见性解耦，岛收起也照走；
    ④ 新增样式 `SegmentFrame` / `SegmentButton` / `PresetChip` / `PrimaryActionButton` /
    `SecondaryActionButton` / `HeroReadout` / `HeroCaption` / `ToolsTitle`，全部走 token。
    期间修掉一个真 bug：`Resources["CardBorder"]` 在 `Loaded` 里抛 `Cannot find a resource`
    （`CardBorder` 在 App.Resources，控件自己的字典里没有），改成两级 `TryGetValue`，卡片阴影才真正挂上。
  * `v0.3.6-TimeMonitorPill`（已随 `v4.0.0-beta` 发布）—— 收起后的**顶部变成计时监测区**：
    ① 有正计时 / 倒计时占用时，时钟**消失**，胶囊换成「模式图标 + mm:ss」（宽 172）；
    ② 两个计时同时跑：**两块独立的岛** —— 新开的在胶囊里，原来那个变成胶囊左边一颗
    40 × 40 的圆形小岛（模式图标 + 进度环），中间留 10px 真实缝隙；整组居中，胶囊让出半个圆；
    点小圆换位；**闹钟不算占用**（时钟照旧、不参与排序）；
    ③ 计时状态从 `DashboardIslandControl` 私有实例改成**单例** `TimeToolsViewModel.Shared`，
    工具页、胶囊、小圆绑同一个实例；tick 移到 `IslandShell`（比这些控件活得久）；
    ④ 轮廓参数打包成 `IslandShapeSnapshot`（胶囊左/宽 + 圆左/径/出现进度 + 看板），
    `IslandWindowStyler` 分别裁圆角矩形再并起来；胶囊宽度 / 小圆出现都是弹簧插值，逐帧变；
    ⑤ 修了单例的静态初始化顺序坑（见 11.5 最后一条），并把 `Diagnostics.LogCrash` 改成连 InnerException 一起写。
  * `v0.3.7-RingtoneSettings`（已随 `v4.0.0-beta` 发布）—— **设置页 + 铃声 + 到点提醒**：
    ① ⚙ 不再是占位按钮：打开**设置视图**（看板 360 高，第四块视图，复用 push/pop 过场与高度弹簧）；
    ② **铃声板块**：闹钟 / 倒计时**各 5 个内置音效**，分开选择、默认第一个，点一下即试听；
    另有「自定义」一行，各自挑一个本地音频文件（`.wav/.mp3/.m4a/…`，复制进本地铃声目录，
    原文件删了也不影响）；选择存 `%LOCALAPPDATA%\Capsyn\settings.json`（容错读取、越界夹紧、失效自定义清空）；
    ③ 内置音效全部**代码合成**（44.1kHz/16-bit WAV，正弦/方波/钟声泛音 + 包络），
    仓库里没有音频文件；首次播放落到 `%LOCALAPPDATA%\Capsyn\ringtones\`；
    ④ `RingtoneService` 改用 `MediaPlayer`（能放 mp3、能循环、能停），`PlaySound` 只留兜底；
    倒计时最长 30 秒、闹钟最长 2 分钟，点「停止」立即静音；
    ⑤ **到点提醒改成"岛的第二级面板"**：倒计时归零 / 闹钟到点会**自动展开岛**并切到「提醒视图」
    （180 高，铃铛/沙漏 + 时间 + 「停止」，闹钟多「稍后 5 分钟」）；提醒期间鼠标移开也不收起；
    ⑥ 声音在 `App.OnRingingChanged` 编排，提醒面板在 `IslandShell`（它管展开/收起），VM 两样都不碰；
    ⑦ 新增调试钩子 `CAPSYN_ALARM_TEST=<秒>` / `CAPSYN_COUNTDOWN_TEST=<秒>`，手动验提醒面板 + 铃声用。
  * `v0.3.8-ModeTransition`（已随 `v4.0.0-beta` 发布）—— 时间工具**三个模式之间的切换**：
    ① 先做过一版「内容淡出 110ms + 位移 → 透明状态下换内容 → 淡入 240ms」的过场
    （指示条照旧用 Fast 滑过去，`_modeSwitchToken` 收尾，连点不跳）；
    ② 实测切回正计时偶发出现"内容错位 / 被裁掉 / 和按钮重叠"的观感——加了
    `SnapToolsContentToRest()`（动画走完显式落位）和 `ApplyModeRowVisibility()`（模式行按 VM 硬设）两个双保险，
    仍无法在这种合成器时序下稳定复现；
    ③ **按需求把内容过场整段删掉**：现在点段位 = 直接 `SelectMode()` + 指示条滑动，
    内容零动画、零残留状态；上面两个双保险里的「模式行硬设」保留（它只是把可见性摆对，不产生动画）；
    ④ 视图之间的 push / pop 过场（指标 ↔ 工具 ↔ 设置 ↔ 提醒）**不受影响**，仍然保留。
  * `v4.0.0-beta`（上一个稳定版）—— 把 `v0.3.5` ~ `v0.3.8` 四批改动合并发布（时间工具大版本）：
    ① **时间工具真能用**：正计时（`mm:ss.f` + 记一圈/上一圈）、倒计时（1/3/5/10 分钟预设、deadline 计时）、
    闹钟（HH:mm ±1/±5 分钟、下一次响铃）；分段控件 + 环形进度读数 + 模式专属操作行；
    ② **看板高度按视图变**：指标 350 / 时间工具 430 / 设置 360 / 到点提醒 180，
    窗口画布一次留够（616 × 528），切视图只改窗口区域、用弹簧长高/缩矮；
    ③ **收起后的顶部变成监测区**：时钟 / 「图标 + mm:ss」/ **两块独立的岛**（胶囊 + 旁边一颗 40×40 圆岛，
    点圆换位）；闹钟不算占用；计时跨收起/展开不中断；
    ④ **设置页 + 铃声**：闹钟 / 倒计时各 5 个**代码合成**的内置音效（默认第一个），可各选一个**自定义音频文件**
    （wav/mp3/m4a/…），存 `%LOCALAPPDATA%\Capsyn\settings.json`；播放用 `MediaPlayer`；
    ⑤ **到点提醒**：倒计时 / 闹钟到点 → 岛自动展开成提醒面板（铃铛/沙漏 + 时间 + 停止，闹钟多「稍后 5 分钟」）
    + 循环响铃；提醒期间鼠标移开不收起；
    ⑥ 期间修掉的问题：NuGet 兜底目录不存在导致 `MSB4018`、本机缺 .NET 9 运行时（`RollForward=Major`）、
    `Resources["CardBorder"]` 抛异常、单例静态初始化顺序 NRE、崩溃日志只写外层异常；
    ⑦ 调试钩子：`CAPSYN_DIAG` / `CAPSYN_ALARM_TEST` / `CAPSYN_COUNTDOWN_TEST` / `CAPSYN_BACKDROP`。
  * `v5.0.0-NewFeatures`（**当前发布版本**：`main` + tag，Release 已标 Latest）—— **拖拽解压**：
    ① 把 `.zip` 拖到胶囊上 → 岛**一次性直接长成解压岛** + 下方出现**虚线落点区**
    （新视图 `DashboardView.Drop`，高 230，收起了右下角页脚和中间横线，只有虚线框 + 图标 + 文字）；
    ①' 只有把文件**拖到岛上**才会显示：做过两版"全局探测"（鼠标钩子 + 透明接收区）都撤掉了 ——
    第一版"拖啥都弹"（窗口 / 快捷方式 / 任意文件都触发），第二版虽然加了内容验证但落点区出现得太晚，
    按需求回到简单可靠的行为：不拖到岛上就不弹；拖出岛立刻收起消失（不闪看板）；
    ①'' 拖拽期间（左键按着）**不响应悬停**：否则悬停会先把指标看板（350 高）展开、再被换成解压岛（230 高），
    看起来像"两个岛"；现在岛只由拖放逻辑展开，一次到位；
    ② 松手后弹**系统原生文件夹选择器**，默认定位在**压缩包所在目录**（`IFileOpenDialog` + `SetFolder`，
    WinRT 的 `FolderPicker` 做不到这点）；
    ③ 解压用 .NET 自带 `ZipArchive`（后台线程、进度条、防 zip-slip）；目标已有同名文件时**不覆盖**，
    自动落到 `压缩包名\` 子目录并在结果里注明；完成后可「打开文件夹」；
    ④ 拖拽 / 解压 / 结果未确认期间岛不收起（`_keepExpanded`）；
    ⑤ 新增调试钩子 `CAPSYN_DROP_TEST=<zip>`、`CAPSYN_PICK_TEST=1`（OLE 拖放没法脚本模拟，用它做自动验证）；
    ⑥ 实测：解压中文文件名正常、同名冲突自动进子目录（`escaped=True`）、点「完成」回到指标视图并收起、
    `capsyn-crash.log` 为空。
  * `v5.1.0-ExtractionKernels`（**当前发布版本**：`main` + tag + Release asset）—— **解压内核扩展：支持 7z / rar / tar**：
    ① `.zip` 继续用 .NET 自带 `ZipArchive`（逐条解压、精确进度、挡 zip-slip）；
    ② 新增 `.7z / .rar / .tar / .tar.gz / .tgz / .tar.xz / .txz / .tar.bz2 / .tbz2 / .gz / .xz / .bz2 / .zst / .cab`
    等格式：走**系统自带 `C:\Windows\System32\tar.exe`（bsdtar + libarchive）**——
    实测 `bsdtar 3.8.9` 能 `tar -tf` / `tar -xf` 真实的 `.7z` 文件；
    **不打包任何二进制、不加 NuGet 依赖**；进度用“解压期间轮询目标目录里新出现的文件数 / 包内文件数”算，
    实测 3000 个文件的包能给出 6% → 15% → 23% → … → 96% 的连续进度（单个超大文件会 0 → 100 跳变，这是 tar.exe 的本质限制）；
    ③ 重名不覆盖 / 解到 `压缩包名\` 子目录的逻辑两种内核共用；tar 的文件数按 `tar -tf` 清单核对，
    不会把目标目录里原有的文件算进去；
    ④ 落点区文案改为 `支持 .zip / .7z / .rar / .tar / .tar.gz / .xz / .bz2`；
    ⑤ 实测（`CAPSYN_DROP_TEST`）：`sample.zip` 走 `unzip(zip)` 1 个文件 16 B、
    `sample.7z` 走 `unzip(tar)` 1 个文件 2844 B、`sample.tar.gz` 冲突时自动进 `sample\` 子目录，
    均显示「解压完成 + 路径 + 文件数」，无崩溃。
  * `v5.2.0-MusicControl`（**分支 `feature/music-control`，未打 tag**）—— **音乐控制**：
    ① 新增 `Services/MediaMonitor.cs`：订阅系统媒体会话（SMTC），拿到歌名 / 歌手 / 专辑 / 封面 / 播放状态 / 时长，
    并把播放暂停 / 上一首 / 下一首发回去（`TryTogglePlayPauseAsync` / `TrySkipNextAsync` / `TrySkipPreviousAsync`）；
    ② **收起态胶囊变成音乐胶囊**（`♪ 歌名` + 播放/暂停状态）；展开态是「看板在左 + **音乐副岛在右**」（副岛 340×210，`MusicSideWidth/MusicSideHeight/MusicSideGap`，出现时看板整体左移让整组居中；窗口区域变成 胶囊∪小圆∪看板∪副岛，改完窗口画布宽约 1012）；
    副岛空状态显示「没有正在播放的媒体」；
    ③ 副岛进度用「某一刻位置 + 经过时间」插值（250ms 刷新），头部有 **Composition 均衡器**（播放时跳动）；
    ④ **展开动画两段式**：先展开看板（居中弹簧），再让看板左移 + 副岛从看板后面滑出（`_musicSlideSpring` + `ApplyMusicSlide` 逐帧算位置与窗口区域）；鼠标移出时反向收回（副岛先滑回、看板再回中，最后才收起），收起前会等滑出量归零。
    ④' 清理：删除看板内旧 `Music` 视图（XAML/绑定/处理代码）与 `ExpandedMusicHeight`；指标页「♪ 歌名」入口也删了。
    ④'' 副岛 UI 适配等高：从「左图右文」改为**垂直居中**（封面 140 居中 → 歌名/歌手居中 → 进度 → 控制），内容总高 290 刚好放进 350，不再留大片空白、也不会把顶部/底部裁掉。
    ⑤ 踩坑记录：WinUI 的 XAML 编译器对「Grid.RowDefinitions 出现在子元素之后」会**静默崩溃**（exit 1、无任何错误信息），
    把属性元素放到最前面即可（这次排查花了不少时间，记下来避免以后再踩）；
    ⑥ 线程：SMTC 事件统一编组到 UI 线程；会话消失 / COM 异常时自动重连；
    ⑦ 实测（对着汽水音乐）：收起态胶囊显示 `♪ Shark`；展开后看板在左、副岛在右（区域 904×396）；副岛播放暂停 / 下一首 / 上一首全部 `ok=True`，
    下一首切到 `清空 - 袁小葳, 阿辰`、上一首回到 `Shark`，无崩溃。UIA 路线实测不可行（汽水音乐是 Chromium 壳，UIA 树只有 2 个元素）。


---

## 八、视觉与交互体系升级（v0.3.2-DesignUpgrade → v0.3.4-Interaction2）

### 已完成
| 项 | 实现 | 依据 |
| --- | --- | --- |
| 设计依据清单 | 8 条官方文档 + 官方样例仓库（WinUI Gallery / WindowsAppSDK-Samples） | 见「视觉体系」章节 |
| 颜色 token | 三主题字典（Dark/Light/HighContrast）各 32 key；4 强调色 × 6 档状态层 | Materials / WinUI 主题资源 |
| 迁移 | XAML 硬编码颜色 62→1（仅注释）、字号 4→0、C# 散落时长 7→0 | — |
| 圆角 / 焦点视觉 | 全局 `ControlCornerRadius=8`；`FocusVisual*` 三主题统一 | Fluent + 无障碍 |
| 三档动效 | `Fast 150 / Normal 300 / Slow 500` + 统一缓动（Composition 与 XAML 各一处定义） | Motion in practice |
| 指针反馈 | hover +4.5% / 按下 −4.5%，Composition **隐式动画**（只动 Scale，不动 Opacity） | XAML/Composition 互操作 |
| 窗口入场 | 淡入 + 上浮 8px，Normal 300ms（不缩放：形状由窗口区域裁出） | 同上 |
| 卡片阴影 | `ThemeShadow` + Z 抬升（代码里挂：ThemeShadow 不能走 Style 资源） | Materials / Fluent 层级 |
| 图标规范化 | 尺寸 18 → `IconSizeStandard`(20)（8px 网格）；描边走 `IconStrokeThickness` | Segoe Fluent Icons 页（网格） |

### 两项「做法层面」的决策（为何没有按原样改）
1. **图标保留自绘矢量 `<Path>`，没有替换成 Segoe Fluent Icons 字形**
   —— 现有 4 个图标（时钟 / 左箭头 / 齿轮 / 电源）是自绘矢量，与岛的黑底细描边风格一致；
   换字体字形要重新对齐基线与字号，且字形缺失时会显示成方框。验收标准是「好看」，
   所以**风险大于收益** → 改为"尺寸对齐 8px 网格 + 描边 token 化"。
2. **场景切换保留现有「平移 + 淡入淡出」，没有改写成 `VisualStateManager`**
   —— 现有过场本来就不是生硬跳变（用 iOS 弹层曲线），改 VSM 属于整段逻辑重写、收益仅是实现风格一致。
   → 改为"过场曲线与时长统一走 token"（`MotionTokens.TransitionControlPoint*` / 三档时长）。

如将来确实需要这两项原样实现，可以直接在此基线上替换：图标节点已全部 token 化，过场时序也已集中。

---

---

## 九、文件路径树与结构整理

> 自动生成（排除 `bin/` `obj/` `.vs/` `.tools/`），括号内为行数。当前源文件共 37 个。

```
Capsyn/
├── Configuration/
│   ├── IslandOptions.cs  (90 行)
│   └── MotionTokens.cs  (47 行)
├── Controls/
│   ├── DashboardIslandControl.xaml  (267 行)
│   ├── DashboardIslandControl.xaml.cs  (227 行)
│   ├── IslandShell.xaml  (34 行)
│   ├── IslandShell.xaml.cs  (251 行)
│   ├── TimeIslandControl.xaml  (26 行)
│   ├── TimeIslandControl.xaml.cs  (38 行)
│   ├── TimerDotControl.xaml  (60 行)      ← 第二个计时的独立小圆岛
│   └── TimerDotControl.xaml.cs  (110 行)
├── Helpers/
│   └── ColorHelper.cs  (27 行)
├── Interop/
│   └── NativeMethods.cs  (170 行)
├── Models/
│   └── SystemMetrics.cs  (15 行)
├── Services/
│   ├── CardElevation.cs  (55 行)
│   ├── Diagnostics.cs  (63 行)
│   ├── InteractiveAnimations.cs  (68 行)
│   ├── IslandAnimator.cs  (39 行)
│   ├── IslandBackdrop.cs  (77 行)
│   ├── IslandWindowStyler.cs  (330 行)
│   ├── RingtoneService.cs  (210 行)      ← MediaPlayer 播放（内置 + 自定义）
│   ├── RingtoneService.cs  (150 行)      ← winmm PlaySound 播放 / 循环 / 停
│   ├── SingleInstance.cs  (33 行)
│   ├── SpringScalar.cs  (62 行)
│   ├── SystemMetricsProvider.cs  (128 行)
│   └── WindowAnimations.cs  (43 行)
├── Themes/
│   ├── DashboardStyles.xaml  (101 行)
│   └── Tokens.xaml  (260 行)
├── tools/
│   └── backup-to-github.ps1  (53 行)
├── ViewModels/
│   ├── DashboardViewModel.cs  (101 行)
│   ├── ObservableObject.cs  (21 行)
│   ├── TimeIslandViewModel.cs  (41 行)
│   └── TimeToolsViewModel.cs  (520 行，正计时 / 倒计时 / 闹钟的状态机)
├── .gitignore
├── app.manifest  (28 行)
├── App.xaml  (27 行)
├── App.xaml.cs  (110 行，含铃声编排)
├── Capsyn.csproj  (77 行)
├── Capsyn.sln  (24 行)
├── Configuration/AppSettings.cs  (100 行，设置持久化)
├── MainWindow.xaml  (13 行)
├── MainWindow.xaml.cs  (61 行)
├── nuget.config  (24 行)
└── README.md  (572 行)
```

### 结构整理进度（越拆越细，每步都构建 0 警告 0 错误）

| 步骤 | 内容 | 状态 |
| --- | --- | --- |
| ① | 把看板样式从页面抽到独立资源字典 `Themes/DashboardStyles.xaml`（101 行）<br>→ `DashboardIslandControl.xaml` **363 → 267 行**，页面只剩布局 | ✅ 已完成 |
| ② | `partial class` 拆分（零行为变化）：<br>`IslandWindowStyler.cs`(330) → `.Chrome` / `.Region` / `.Display` / `.Subclass`<br>`IslandShell.xaml.cs`(251) → `.State` / `.Hover` / `.Shape`<br>`DashboardIslandControl.xaml.cs`(227) → `.Views` / `.Metrics` / `.Animations` / `.Interaction`<br>`IslandOptions.cs`(90) → `.Layout` / `.Animation` / `.Data` | ⏳ 待做 |
| ③ | `Interop/NativeMethods.cs`(170) → `WindowMethods` / `CompositionMethods` / `GdiMethods` / `Structs` | ⏳ 待做 |
| ④ | 视图拆成独立 `UserControl`：指标页 / 时间工具页 / 底部按钮（会改代码引用，风险最高，最后做） | ⏳ 待做 |

**拆分原则**：① ② ③ 三步都是"纯搬移"（同一个类分散到多个文件 / 资源换位置），**零行为变化**，每步构建验证；
第 ④ 步会改到按名字访问元素的代码，单独一轮做。

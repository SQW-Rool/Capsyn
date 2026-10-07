# Capsyn —— Windows 灵动岛（时间岛 + 看板岛）

一个常驻屏幕顶部中央的悬浮小组件，模仿 iOS 14+ Dynamic Island 的视觉：

* **时间岛（收起态）**：一枚 320 × 40 的纯黑超圆角胶囊，里面每秒跳动 `HH:MM:SS`，固定在屏幕顶部居中。
* **看板岛（展开态）**：鼠标移到时间岛上，**它的下方**用弹簧动画长出一块 550 × 350 的看板，
  里面两个视图（只切可见性，尺寸 / 动画都不变）：
  * **指标**（默认）：第一行 **CPU / 内存 / 网络上下行** 三张数据卡，下面一条横线，
    左下是「时间工具」按钮（时钟图标 + 文字），右下角两个圆形图标按钮 ——「设置」（目前只有 UI，
    点击不接任何动作）和红色「关闭程序」（退出应用的唯一入口）；
  * **时间工具**（点左下「时间工具」进入）：左上角「返回」，中间一排「正计时 / 倒计时 / 闹钟」三个按钮
    （按需求**先只做 UI**），右下角两个图标按钮照常在。
  两个视图之间是 **iOS 风格的 push / pop 过场**（旧视图往一侧滑出、新视图从另一侧滑入，带轻微视差）。
  其余空间先留空，给通知 / 音乐 / 计时器。
* 鼠标移开 → 延迟一点点平滑收回胶囊。

* 技术栈：WinUI 3（C#）+ Windows App SDK 1.8，动画用 **Composition API**（弹簧 + 内容淡入淡出；条目错帧的「生长动画」已按需求移除）
* 模板形态：**Unpackaged（未打包）** —— 不需要 MSIX、不需要签名，直接跑 exe
* 目标环境：本机运行（.NET 9 + Windows App Runtime 1.8 已安装）

### 当前版本状态（快照）

* 代码与远端 `main` 完全一致，对应 tag **`v0.3.1-ProjectChange-U2`**（commit `f2381a0`）。
* **已实现**：时间岛胶囊；看板岛（指标 / 时间工具两页 + iOS 风格 push/pop 过场）；弹簧驱动的窗口形状；
  置顶 / 不进任务栏 / 点击不抢焦点 / 位置启动算一次后焊死；单实例保护；未处理异常兜底（`capsyn-crash.log`）；
  显示器与 DPI 变化处理；固定深色主题。
* **刻意只做 UI / 没做**：看板右下角的 ⚙「设置」按钮（当前不接任何动作）、时间工具页的三个计时按钮、
  托盘图标、开机自启、设置面板。
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
| `Controls/TimeIslandControl.xaml(.cs)` | **时间岛**：`HH:MM:SS` 文本 + 每秒刷新的 `DispatcherTimer` |
| `Controls/DashboardIslandControl.xaml(.cs)` | **看板岛**：550 × 350，两个视图（指标 / 时间工具）只切可见性；右下角常驻「设置」「关闭程序」两个圆形图标按钮 |
| `ViewModels/TimeIslandViewModel.cs` | `CurrentTime`（可绑定属性，24 小时制）与字体/配色 |
| `ViewModels/DashboardViewModel.cs` | 指标的可绑定文本与进度条宽度；采样在线程池上跑 |
| `ViewModels/ObservableObject.cs` | 极简 `INotifyPropertyChanged` 基类（不引入第三方 MVVM 库） |
| `Models/SystemMetrics.cs` | 一次采样结果（CPU% / 内存 / 网络上下行） |
| `Services/SystemMetricsProvider.cs` | 指标采集：`GetSystemTimes`、`GlobalMemoryStatusEx`、网卡统计 |
| `Services/IslandAnimator.cs` | 内容淡入淡出（Composition）；条目错帧的「生长动画」已按需求移除 |
| `Services/SpringScalar.cs` | 阻尼弹簧积分器：驱动 Win32 窗口区域的外轮廓 |
| `Services/IslandWindowStyler.cs` | 窗口系统行为：无边框、置顶、不进任务栏、不抢焦点、窗口区域裁剪、锁死位置、缩放/分辨率变化重算 |
| `Services/SingleInstance.cs` | 单实例保护（命名 Mutex），避免两个胶囊叠在一起 |
| `Services/Diagnostics.cs` | 开关式诊断日志（`CAPSYN_DIAG=1`）+ **永远写**的崩溃日志 |
| `Helpers/ColorHelper.cs` | `#RRGGBB` / `#AARRGGBB` 解析 |
| `Configuration/IslandOptions.cs` | 全部可调参数：尺寸、缝隙、回弹余量、弹簧参数、淡入淡出时长、采样间隔 |
| `Interop/NativeMethods.cs` | 用到的 Win32 / DWM / GDI P/Invoke |
| `nuget.config` | 官方源 + 本机缓存兜底（见「六、环境说明」） |
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

### 11. 看板布局：两个视图（指标 / 时间工具）
看板内容是一张 `550 × 350`、内边距 16 的 Grid，里面**两个视图只切可见性**
（面板尺寸、窗口区域、弹簧动画完全不受影响）。

**视图 1（默认，指标）**：

```
第 1 行  处理器(21% + 进度条)   内存(9.5/16.0 GB 59% + 进度条)   网络(↓ 9.6 KB/s / ↑ 1.5 KB/s)
第 2 行  ────────────────────────── 一条 1px 半透明白横线 ──────────────────────────
第 3 行  [ 🕐 时间工具 ]（左下）                  右下角：⚙「设置」  ⏻「关闭程序」
```

**视图 2（时间工具，点左下「时间工具」进入）**：

```
第 1 行  ←「返回」（左上角）
第 2 行  ────────────────────────── 一条 1px 半透明白横线 ──────────────────────────
第 3 行        [ 正计时 ]      [ 倒计时 ]      [ 闹钟 ]   ← 紧贴横线下方 8px
                                                右下角：⚙  ⏻
```

* 三张卡 / 三个工具按钮的可用宽度都 ≈ 164.7（`(550-32-24)/3`），因此 `DashboardViewModel.BarMaxWidth = 136`，
  内存数值字号降到 16、网络两行降到 15 以适配。
* 按钮都是普通 `Button` + **矢量 `Path` 图标**（不依赖 Segoe MDL2 / Fluent 图标字体，也不引入图片资源）；
  指针态主题资源在 `UserControl.Resources` 里就地覆盖成半透明白，避免在纯黑底上出现浅色主题的灰块。
  * **右下角两个圆形图标按钮**（`IconButton` 样式：40 × 40 / `CornerRadius 20` / 半透明白底 + 细描边，间距 12）：
    **设置**（齿轮图标，只有 UI）和 **关闭程序**（`CloseIconButton`，`BasedOn` 上面那个只换红底红边 + 浅红电源图标）。
    两个视图里都常驻，右对齐 + 底对齐。
  * **左下「时间工具」**（`ToolButton` 样式：`Padding 16,9` / `CornerRadius 12` / 半透明白底 + 细描边）：
    时钟矢量图标（圆环 + 两根指针）+ 文字，点击 → 视图 2。
  * **左上「返回」**（`IconButton`）：左箭头矢量图标，点击 → 回视图 1。
  * **正计时 / 倒计时 / 闹钟**（`ToolButton`，三列等宽、高 56、**靠上排** —— `VerticalAlignment="Top"` + 上边距 8px）：
    **三个按钮各一个色系，好区分**（都 `BasedOn` `ToolButton`，只换底色/描边/字色 + 指针态）：

    | 按钮 | 主色（iOS 深色模式） | 底色 / 描边 / 文字 |
    | --- | --- | --- |
    | 正计时 | 蓝 `#0A84FF` | `#2E0A84FF` / `#660A84FF` / `#FF7AB8FF` |
    | 倒计时 | 橙 `#FF9F0A` | `#2EFF9F0A` / `#66FF9F0A` / `#FFFFC46B` |
    | 闹钟 | 紫 `#BF5AF2` | `#2EBF5AF2` / `#66BF5AF2` / `#FFD3A6FF` |

    底色是 18% 的淡染、文字提亮，悬停 / 按下态各自加深（做法和「关闭程序」那套红一样）。
    按需求**先只做 UI**，不接 `Click`、不写日志。
* **过场动画（iOS 风格 push / pop）**：`DashboardIslandControl.SetTimeToolsVisible()` 里做：
  * **push**（进工具视图）：旧视图往**左**淡出滑走、新视图从**右**滑进来；**pop**（返回）方向相反；
  * 旧视图滑走的距离只有新视图的 60%（`ViewExitParallax`），形成 iOS 那种轻微视差；
  * 时长：入场 340ms、出场 200ms；曲线 **cubic-bezier(0.32, 0.72, 0, 1)**（iOS 弹层那条，起手快、收尾柔和）；
  * 滑的是 **`Translation`** 而不是 `Offset` —— 后者是布局算出来的位置，动画收尾到 `(0,0)` 会把元素
    拽回父容器原点（先 `ElementCompositionPreview.SetIsTranslationEnabled(el, true)`，再按字符串键
    `"Translation"` 起动画，它在 WinUI 里没有强类型属性）；
  * 全程在**合成线程**上跑、不参与布局，UI 线程不用出帧，所以不掉帧；
  * 旧视图要等出场动画放完才 `Collapsed`（否则看不到淡出），收尾用序号 `_viewSwitchToken` 保护 ——
    连点「时间工具 / 返回」时只有最后一次收尾生效，不会把刚滑进来的视图又收掉；
  * 两个视图在过场期间**同时可见**（一个在淡入、一个在淡出）；为此**第 1 行高度固定成 `104`**
    （= 指标卡高度）而不是 `Auto` —— 否则旧视图收起时第 1 行会从 104 缩到 40（返回按钮的高度），
    下面两行连同横线、三个按钮会整体往上跳 64px（这是踩过的坑）。
* 视图切换会写一条 `dashboard view: metrics / time tools` 诊断日志。
* 切到工具视图时**指标采样照常在跑**（每秒一次），所以返回时数据是新的，不用重采样。
* **每次展开都是指标视图**：看板从收起态展开时（`IslandShell.SetDashboardExpanded(true)` 里）会先调
  `Dashboard.ResetToMetricsView()` —— 只在真的「收起 → 展开」时走到那里，收起宽限期内把鼠标移回来
  （`_expanded` 已经是 true，方法提前 return）不会打断正在操作的工具视图。
* **复位为什么要「推」而不是「停」动画**：`Translation` 是
  `ElementCompositionPreview.SetIsTranslationEnabled()` 挂上去的动画属性，
  用 `StopAnimation` 停掉**不保证回到 0** —— 上一次出场留在元素上的 -29px 会一直留着，
  表现就是「下次展开时整个看板内容错位」。所以复位时用一段 1ms 的动画
  （`SnapToRest`）把 `Opacity` 推回 1、`Translation` 推回 0，四个元素都推一遍，
  并 `_viewSwitchToken++` 让还没收尾的那次过场失效。

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
  `IslandWindowStyler.UpdateIslandShape(panelScaleX, panelScaleY)` 是**两个参数**；
  窗口高按公式 `40 + 6 + 350 × 1.12 = 438`（旧尺寸实测过 616 × 538，新的这个还没实测）。
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

### 里程碑 9：时间工具视图（只有 UI，功能未接）
| 验收项 | 结果 |
| --- | --- |
| 看板左下出现「时间工具」按钮（时钟矢量图标 + 文字） | ⏳ 待运行核对（本地构建 0 警告 0 错误） |
| 点击后隐藏指标与入口按钮，显示三个工具按钮 + 左上角返回 | ⏳ 待运行核对（只切 4 个元素的 `Visibility`；日志 `dashboard view: time tools`） |
| 三个按钮「正计时 / 倒计时 / 闹钟」并排一行、等宽（高 56），**各有各的色系**（蓝 / 橙 / 紫） | ⏳ 待运行核对 |
| 点左上「返回」回到指标视图 | ⏳ 待运行核对（日志 `dashboard view: metrics`） |
| 不点返回直接移开鼠标：下次展开是**指标视图**、且布局正常 | ⏳ 待运行核对（展开前复位 + `SnapToRest` 归零位移，日志 `dashboard view: metrics (reset)`） |
| 过场动画：iOS 风格 push / pop（入场 340ms / 出场 200ms，60% 视差，cubic-bezier(0.32,0.72,0,1)） | ⏳ 待运行核对（Composition `Translation`，跑在合成线程） |
| 连点「时间工具 / 返回」不会收错视图 | ✅ 收尾用 `_viewSwitchToken` 序号保护 |
| 三个工具按钮不接任何逻辑 | ✅ XAML 里没有 `Click`，只留了 `TODO(扩展)` |
| 右下角「设置」「关闭程序」两个视图里都常驻 | ⏳ 待运行核对 |
| 不影响面板尺寸 / 窗口区域 / 弹簧动画 | ✅ 只改 XAML 元素的可见性 + Composition 动画，`IslandShell` / `IslandWindowStyler` 一行没动 |

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
* **音乐卡片 / 计时器**：同样加 ViewModel + 往看板留白区放控件
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
  本机开发（`dotnet build` / VS F5）用框架依赖就行；**要发到没装这些运行时的机器**时用自包含发布
  （命令见下面的「打安装包 / 自包含发布」）。
* `nuget.config`：保留 `nuget.org` 官方源（方便以后在 VS 里装新包），同时把本机缓存
  `C:\Users\sqw-j\.nuget\packages` 配成只读 `fallbackPackageFolders`，所以没有外网也能还原
  （离线时只会出现 `NU1900` 漏洞库警告，可忽略）。新解出来的包放在项目内的 `.tools\nuget-packages`
  （已 gitignore），不会写到项目目录之外。
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

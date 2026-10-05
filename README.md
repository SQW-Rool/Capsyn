# Capsyn —— Windows 灵动岛（时间岛 + 看板岛）

一个常驻屏幕顶部中央的悬浮小组件，模仿 iOS 14+ Dynamic Island 的视觉：

* **时间岛（收起态）**：一枚 320 × 40 的纯黑超圆角胶囊，里面每秒跳动 `HH:MM:SS`，固定在屏幕顶部居中。
* **看板岛（展开态）**：鼠标移到时间岛上，**它的下方**用弹簧动画长出一块 550 × 350 的看板：
  第一行是 **CPU / 内存 / 网络上下行** 三张数据卡，下面一条横线，横线下面是「电源」按钮；
  其余空间先留空，给通知 / 音乐 / 计时器。
* **电源岛**：鼠标移到看板上那个「电源」按钮上，**看板岛下方**再长出一块 550 × 84 的电源岛，
  里面四个按钮：**关机 / 重启 / 睡眠 / 关闭程序**（按需求只有「关闭程序」真正生效，另外三个只做 UI）。
* 鼠标移开 → 延迟一点点平滑收回胶囊。

* 技术栈：WinUI 3（C#）+ Windows App SDK 1.8，动画用 **Composition API**（弹簧 + 内容淡入淡出；条目错帧的「生长动画」已按需求移除）
* 模板形态：**Unpackaged（未打包）** —— 不需要 MSIX、不需要签名，直接跑 exe
* 目标环境：本机运行（.NET 9 + Windows App Runtime 1.8 已安装）

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
把鼠标移到顶部的胶囊上 → 下方展开看板；鼠标移开 → 自动收回。点击不会触发任何动作、也不会抢焦点。

### 这个程序怎么退出
胶囊不响应点击（按需求设计），当前也还没有托盘图标，所以**没有退出按钮**。
暂时用任务管理器结束 `Capsyn.exe`，或者：
```powershell
taskkill /IM Capsyn.exe /F
```
> 退出入口 / 托盘图标 / 开机自启写在 `MainWindow` 的 TODO 里，属于后续里程碑。

### 调试
设置环境变量 `CAPSYN_DIAG=1` 后启动，会在 exe 同目录写 `capsyn-diag.log`
（悬停判定、展开/收起、形状同步、采样耗时等时序信息），正常运行时不产生任何开销。

---

## 二、文件清单

| 文件 | 职责 |
| --- | --- |
| `Capsyn.csproj` | 工程定义：`net9.0-windows10.0.19041.0`、`UseWinUI`、`WindowsPackageType=None`（未打包）、固定 x64、离线还原开关 |
| `Capsyn.sln` | 解决方案（Debug/Release × x64） |
| `app.manifest` | DPI（PerMonitorV2）、comctl32 v6 依赖 |
| `App.xaml` / `App.xaml.cs` | 应用入口；把窗口宿主的页面背景色改成透明 |
| `MainWindow.xaml(.cs)` | **只有壳**：把窗口、位置、置顶等系统行为与岛壳接起来 |
| `Controls/IslandShell.xaml(.cs)` | **岛的状态机与外轮廓**：悬停判定、展开/收起、弹簧驱动的窗口形状、Composition 动画 |
| `Controls/TimeIslandControl.xaml(.cs)` | **时间岛**：`HH:MM:SS` 文本 + 每秒刷新的 `DispatcherTimer` |
| `Controls/DashboardIslandControl.xaml(.cs)` | **看板岛**：550 × 350，第一行三张数据卡 + 横线 + 电源按钮（电源岛的悬停触发器）+ 留白区 |
| `Controls/PowerIslandControl.xaml(.cs)` | **电源岛**：550 × 84，关机 / 重启 / 睡眠 / 关闭程序（只有「关闭程序」生效） |
| `ViewModels/TimeIslandViewModel.cs` | `CurrentTime`（可绑定属性，24 小时制）与字体/配色 |
| `ViewModels/DashboardViewModel.cs` | 指标的可绑定文本与进度条宽度；采样在线程池上跑 |
| `ViewModels/ObservableObject.cs` | 极简 `INotifyPropertyChanged` 基类（不引入第三方 MVVM 库） |
| `Models/SystemMetrics.cs` | 一次采样结果（CPU% / 内存 / 网络上下行） |
| `Services/SystemMetricsProvider.cs` | 指标采集：`GetSystemTimes`、`GlobalMemoryStatusEx`、网卡统计 |
| `Services/IslandAnimator.cs` | 内容淡入淡出（Composition）；条目错帧的「生长动画」已按需求移除 |
| `Services/SpringScalar.cs` | 阻尼弹簧积分器：驱动 Win32 窗口区域的外轮廓 |
| `Services/IslandWindowStyler.cs` | 窗口系统行为：无边框、置顶、不进任务栏、不抢焦点、窗口区域裁剪、锁死位置 |
| `Services/Diagnostics.cs` | 开关式诊断日志（`CAPSYN_DIAG=1`） |
| `Helpers/ColorHelper.cs` | `#RRGGBB` / `#AARRGGBB` 解析 |
| `Configuration/IslandOptions.cs` | 全部可调参数：尺寸、缝隙、回弹余量、弹簧参数、淡入淡出时长、采样间隔 |
| `Interop/NativeMethods.cs` | 用到的 Win32 / DWM / GDI P/Invoke |
| `nuget.config` | 官方源 + 本机缓存兜底（见「六、环境说明」） |
| `tools/backup-to-github.ps1` | 一键备份脚本（见「七、备份到 GitHub」） |

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
  在 8ms 定时器里逐帧积分，换算成看板 / 电源岛的缩放后交给窗口区域 —— 展开有回弹、收起平滑。

  实测展开轨迹（1920×1080@100%）：`320×40 → 342×79 → 392×155 → 473×278 → 531×368 → 568×423（过冲）→ 550×396`，
  约 300ms 长完并带一次回弹；收起同理平滑。
* **内容层（Composition，只做透明度）**：按需求**生长动画已删除** ——
  内容不再从 0.94 放大到 1，卡片 / 按钮也不再逐个错帧「上浮 + 淡入」，只做透明度过渡
  （`IslandAnimator` 现在只播 Opacity 关键帧）；「电源岛出场后让看板岛让一下」也改成了透明度回一下。

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

### 11. 看板布局与电源按钮（只做 UI）
看板内容是一张 `550 × 350`、内边距 16 的 Grid：

```
第 1 行  处理器(21% + 进度条)   内存(9.5/16.0 GB 59% + 进度条)   网络(↓ 9.6 KB/s / ↑ 1.5 KB/s)
第 2 行  ────────────────────────── 一条 1px 半透明白横线 ──────────────────────────
第 3 行  电源按钮（矢量图标 + 文字）      其余整块留白
```

* 三张卡的可用宽度 ≈ 164.7（`(550-32-24)/3`），因此 `DashboardViewModel.BarMaxWidth = 136`，内存数值字号降到 16、网络两行降到 15 以适配。
* 按钮是普通 `Button` + 一个**矢量 `Path` 图标**（圆环缺口 + 竖杠：
  `Data="M 12,3 L 12,11 M 16,6.07 A 8,8 0 1 1 8,6.07"` + `Stroke`/`StrokeThickness`），
  不依赖 Segoe MDL2 / Fluent 图标字体，也不引入图片资源；按钮的指针态主题资源就地覆盖成半透明白，
  避免在纯黑底上出现浅色主题的灰块。
* 按需求**电源按钮本身不接任何系统动作**（不做关机/重启/睡眠），它真正的用途是**电源岛的悬停触发器**：
  鼠标停上去时电源岛在看板岛下方展开，按钮同时变亮一点作为状态反馈。

### 12. 电源岛（悬停电源按钮展开）
形态上它是第三个岛：在**看板岛下方**再挂一块 550 × 84 的圆角面板，四个按钮平分一行。
窗口区域就是三块圆角矩形的并集（胶囊 ∪ 看板 ∪ 电源岛），仍然由 `UpdateIslandShape(panelX, panelY, power)` 逐帧给出；
窗口高度也按「胶囊 + 看板 + 电源岛」加上回弹余量（实测窗口 616 × 538）。

悬停链（全部用光标轮询 + 屏幕矩形判定，不依赖 XAML 路由事件）：

```
时间岛胶囊  ──悬停──▶  看板岛展开
看板岛的「电源」按钮 ──悬停──▶  电源岛展开（看板岛保持展开，按钮变亮）
看板岛其它区域      ──▶  只收电源岛，看板岛保留   ← 「移到看板岛就只显示看板岛」
所有岛之外          ──▶  级联收回：先电源岛、后看板岛
```

* 「电源」按钮的命中框用 `TransformToVisual(null)` 把按钮矩形换算到窗口客户区，再映射到屏幕像素；
  按钮不大，四周放宽 6 DIP 好悬停；从按钮移到电源岛要跨过中间的 6px 缝隙，所以给电源岛单独留了
  250ms 的收回宽限，避免闪断。
* **入场顺序**：先播电源岛的出场动画，随后 70ms（`DashboardNudgeDelayMs`）让看板岛轻量重播一次入场
  （只是透明度回一下，不做缩放 / 位移），也就是「先电源岛、再看板岛」。
* **出场顺序**：级联收回，实测窗口区域高度轨迹 `486 → 471 → 441 → 431 → 412 → 404 → 48 → 40`
  （先电源岛缩回，再整个看板岛缩回）。
* 四个按钮：只有「关闭程序」生效（`Close()` + `Application.Current.Exit()`）；
  关机 / 重启 / 睡眠按需求只做 UI，点击不执行任何系统操作（只写诊断日志）。

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
| 第一行三张卡 + 横线 + 电源按钮 | ✅ 处理器 / 内存 / 网络并排一行，下面一条横线，横线下面是带矢量图标的「电源」按钮 |
| 电源按钮只做 UI | ✅ 实测点击会在诊断日志留下 `power button clicked`（说明命中区域正常），不触发任何系统动作 |
| 弹簧动画（保留） | ✅ 逐帧采样到完整曲线，末尾带一次过冲：`342×79 → … → 568×423（过冲）→ 550×396` |
| 生长动画（已删除） | ✅ 内容只做淡入淡出，不再整体放大、不再逐条上浮入场 |
| CPU / 内存 / 网络上下行数据 | ✅ 实测读数如 `处理器 19%`、`内存 9.4 / 16.0 GB 59%`、`网络 ↓ 4.9 KB/s ↑ 0 B/s`；间隔 1.8s 两帧有 838 像素变化 |
| 空白区域留白 | ✅ 第四格与下半部分留空，等通知 / 音乐 / 计时器 |
| 原有行为不回退 | ✅ 胶囊位置仍是 `(800,10)`、仍置顶 / 不进任务栏 / 点击不抢焦点 / 拖不动 |

### 里程碑 3：电源岛（悬停「电源」按钮展开）
| 验收项 | 结果 |
| --- | --- |
| 悬停电源按钮 → 看板岛下方长出电源岛 | ✅ 窗口区域从 `550×396` 长到 `550×486`（看板 350 + 缝隙 6 + 电源岛 84 + 胶囊 40） |
| 移到看板岛其它区域 → 只收电源岛 | ✅ 区域回到 `550×396`，看板岛保留（日志 `hover: left power button -> power island closing`） |
| 彻底移开 → 级联收回 | ✅ 先电源岛、后看板岛（区域高度 `486→471→441→431→412→404→48→40`） |
| 入场顺序「先电源岛、再看板岛」 | ✅ 电源岛弹簧先启动，70ms 后看板岛轻量重播一次入场 |
| 四个按钮 | ✅ 关机 / 重启 / 睡眠 / 关闭程序 并排一行 |
| 关机 / 重启 / 睡眠 只做 UI | ✅ 点击后进程仍存活、系统无任何动作（只写诊断日志） |
| 关闭程序真正退出 | ✅ 点击后 Capsyn 进程消失 |

---

## 五、已知限制 / 下一步 TODO

代码里都留了 `TODO(扩展)` 注释，主要接入点：

* **想恢复生长动画**：`IslandAnimator` 的旧实现（内容 0.94→1 + 条目错帧上浮淡入）在 git 历史里，
  当前版本按需求只保留弹簧 + 淡入淡出
* **通知队列**：`DashboardViewModel` 加可绑定属性 + `DashboardIslandControl` 下半部分放列表
* **电源岛三个按钮接功能**：`PowerIslandControl` 里接系统电源操作（关机 / 重启 / 睡眠），
  建议先弹确认再执行（现在按需求只做了 UI，只有「关闭程序」生效）
* **音乐卡片 / 计时器**：同样加 ViewModel + 往看板留白区放控件
* **更多图表**：第四格、以及看板下半部分
* **鼠标穿透开关**：`IslandWindowStyler.ApplyChrome()` 里加 `WS_EX_TRANSPARENT | WS_EX_LAYERED` + 运行时开关
* **点击交互**：`IslandShell` 里订阅指针事件（目前刻意不接，只有悬停判定）
* **托盘图标 + 退出入口 + 开机自启**：`MainWindow` 构造函数
* **WM_DPICHANGED / WM_DISPLAYCHANGE**：显示器缩放或主屏变化后重算尺寸与居中
* **外部配置文件**：`IslandOptions` 改为读 exe 同目录的 `island.config.json`
* **形状同步帧率**：目前外轮廓约 30Hz（`DispatcherTimer`），如果觉得边缘不够顺，
  可以把它挪到独立线程做更高频率的 `SetWindowRgn`

---

## 六、环境说明

* 依赖：.NET 9 桌面运行时、Windows App Runtime **1.8**（`8000.994.2142.0`，已安装）。
  如果换到没装运行时的机器，把 `Capsyn.csproj` 里的
  `<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>` 改成 `true` 再 build 即可。
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
* 如果构建时出现 `MSB6003 / CreatePipe 拒绝访问`，说明当前 shell 有进程/管道沙箱限制：
  XAML 编译器需要 `cmd.exe`/`csc.exe` 子进程，换到普通 PowerShell 或 Visual Studio 里构建即可。

---

## 七、备份到 GitHub（回滚参考）

* 备份仓库：<https://github.com/SQW-Rool/Capsyn-backup>
* 备份内容：源码与配置。`bin/`、`obj/`、`.vs/`、`.tools/`、`capsyn-diag.log` 都已被 `.gitignore` 忽略。
* 约定：**本地构建通过（0 警告 0 错误）+ 运行验收通过之后**才备份；提交信息写清本次改动，
  重要节点同时打 tag，GitHub 的提交历史 + tag 就是回滚锚点。
* 一键备份脚本（`tools/backup-to-github.ps1`）：

```powershell
cd G:\Capsyn
powershell -ExecutionPolicy Bypass -File .\tools\backup-to-github.ps1 -Message "改了什么" -Tag v0.2.0-xxx
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

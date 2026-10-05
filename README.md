# Capsyn —— Windows 灵动岛（时间岛）

一个常驻屏幕顶部中央的悬浮小组件，模仿 iOS 14+ Dynamic Island 的视觉：
默认是一枚 **纯黑超圆角胶囊**，里面每秒跳动一次 `HH:MM:SS`。

本次里程碑只做 **时间岛**，把骨架跑通；通知 / 音乐 / 计时器留给后续扩展（代码里已留好 TODO 接入点）。

* 技术栈：WinUI 3（C#）+ Windows App SDK 1.8
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

### 这个程序怎么退出
胶囊不响应点击（按需求设计），当前也还没有托盘图标，所以**没有退出按钮**。
暂时用任务管理器结束 `Capsyn.exe`，或者：
```powershell
taskkill /IM Capsyn.exe /F
```
> 退出入口 / 托盘图标 / 开机自启已经写在 `MainWindow` 的 TODO 里，属于下一个里程碑。

---

## 二、文件清单

| 文件 | 职责 |
| --- | --- |
| `Capsyn.csproj` | 工程定义：`net9.0-windows10.0.19041.0`、`UseWinUI`、`WindowsPackageType=None`（未打包）、固定 x64 |
| `Capsyn.sln` | 解决方案（Debug/Release × x64） |
| `app.manifest` | DPI（PerMonitorV2）、comctl32 v6 依赖 |
| `App.xaml` / `App.xaml.cs` | 应用入口；把窗口宿主的页面背景色改成透明（否则圆角外会是白角） |
| `MainWindow.xaml` | **只有壳**：一个全透明 Grid + 一个 `TimeIslandControl` |
| `MainWindow.xaml.cs` | 壳的代码：创建窗口、套用系统行为、算一次位置后固定 |
| `Controls/TimeIslandControl.xaml(.cs)` | **时间岛本体**：黑色胶囊 + 时间文本 + 每 1 秒刷新的 `DispatcherTimer` |
| `ViewModels/TimeIslandViewModel.cs` | `CurrentTime`（可绑定属性，`HH:mm:ss` 24 小时制）以及胶囊样式（颜色 / 圆角 / 等宽字体） |
| `ViewModels/ObservableObject.cs` | 极简 `INotifyPropertyChanged` 基类（不引入第三方 MVVM 库） |
| `Configuration/IslandOptions.cs` | 可调配置：宽 320 / 高 40 / 顶距 10 / 格式 / 字号 17 / Cascadia Code / 配色 |
| `Services/IslandWindowStyler.cs` | 所有窗口系统行为：无边框、置顶、不进任务栏、点击不抢焦点、透明、裁成胶囊、锁死位置 |
| `Interop/NativeMethods.cs` | 用到的 Win32 / DWM / GDI P/Invoke（只保留必要的那几个） |
| `nuget.config` | 保留官方源 + 把本机 NuGet 缓存作为离线兜底（详见「六、环境说明」） |

---

## 三、关键实现点

### 1. 无边框、无标题栏、无白边
`OverlappedPresenter.SetBorderAndTitleBar(false, false)` 之后，窗口上仍然留着
`WS_DLGFRAME / WS_SYSMENU / WS_WINDOWEDGE` 这些「非客户区」，浅色主题下就是**一圈可见的白边**，
而且会把客户区缩小约 4px。所以 `IslandWindowStyler.ApplyChrome()` 里又把它们全部清掉：

```csharp
style &= ~(WS_CAPTION | WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_SYSMENU | ...);
exStyle &= ~(WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME | WS_EX_APPWINDOW);
SetWindowPos(..., SWP_FRAMECHANGED);   // 让改动立即生效
```

再加上 `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DONOTROUND)`（不要 Win11 自动圆角）
和 `DWMWA_BORDER_COLOR = NONE`（不要系统描边）。

### 2. 胶囊形状：整窗裁剪 + 圆角外透明
WinUI 3 的窗口客户区是**不透明**的：框架会用「页面背景」主题色把客户区填满
（浅色主题下是纯白）。所以 XAML 里 `Background="Transparent"` 并不足以让圆角外面透出桌面。
这里做了两件事，互为保险：

1. `App.xaml` 里把 `ApplicationPageBackgroundThemeBrush` 覆盖成 `Transparent`；
2. `IslandWindowStyler.ApplyPillShape()` 用 Win32 窗口区域把窗口本身裁成胶囊：
   ```csharp
   var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, height, height); // 椭圆高=窗口高 → 半径=高/2
   SetWindowRgn(hwnd, region, true);
   ```
   圆角以外就不属于这个窗口，**深色/浅色主题都不会露出白角**（实测裁剪后圆角处仍保留抗锯齿灰阶）。

### 3. 始终置顶
`OverlappedPresenter.IsAlwaysOnTop = true`（即 `WS_EX_TOPMOST`），显示后再补一次
`SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)`；实测在 Z 序里位于任务栏、Edge 等所有窗口之上。

### 4. 不出现在任务栏 / Alt+Tab
`AppWindow.IsShownInSwitchers = false` + `WS_EX_TOOLWINDOW` 双保险；窗口没有 owner，也不会有任务栏按钮。

### 5. 点击不抢焦点、不触发任何动作
`WS_EX_NOACTIVATE`，并在子类化窗口过程里对 `WM_MOUSEACTIVATE` 直接返回 `MA_NOACTIVATE`。
胶囊里没有任何命中测试/命令绑定，点击不会有任何行为。

### 6. 位置启动算一次，之后焊死
* 位置在启动时按 **当前 DPI 缩放** 换算：宽高 DIP × (DPI/96)，`x = 主屏中点 - 宽/2`，`y = 主屏顶边 + 10 × 缩放`；
  用 `DisplayArea.Primary`（主显示器，多屏时不会被带到副屏）+ 全屏 `OuterBounds`（贴屏幕顶边而不是工作区）。
* 之后用 `SetWindowSubclass` 拦截 `WM_WINDOWPOSCHANGING`，把坐标/尺寸强行改回钉好的值并加上
  `SWP_NOMOVE | SWP_NOSIZE` —— 用户拖不动，外部 `MoveWindow` 也会被弹回（实测有效）。

### 7. 时间刷新
`Microsoft.UI.Xaml.DispatcherTimer`，`Interval = 1s`；`Loaded` 时先立刻取一次时间（避免首秒空白）再启动；
每次 Tick 都重新读 `DateTime.Now`（而不是自增），格式 `HH:mm:ss`、`InvariantCulture`、24 小时制。
字体 Cascadia Code、字号 17、SemiBold、白色；`IsTextScaleFactorEnabled="False"` 保证尺寸不被系统文字缩放撑破。

---

## 四、验收对照（实测结果）

| 验收项 | 结果 |
| --- | --- |
| 顶部正中间出现黑色胶囊 | ✅ `GetWindowRect` = `(800,10) 320x40`（1920×1080@100% → 居中、距顶 10px） |
| 里面活跃跳动 `HH:MM:SS` | ✅ 每秒刷新；两张间隔 1.7s 的截图中时间区域像素发生变化 |
| 背景纯黑、无白边 | ✅ 胶囊内部取样 `#000000`；圆角以外为桌面像素，无白色/灰色残留 |
| 无默认阴影 | ✅ 窗口四周取样仍是纯桌面色，没有阴影渐变 |
| 不出现在任务栏、Alt+Tab 看不到 | ✅ `IsShownInSwitchers=false` + `WS_EX_TOOLWINDOW`（exstyle `0x08000188`），无 owner |
| 切到别的全屏/窗口化应用仍最上层 | ✅ `WS_EX_TOPMOST`；Z 序实测在任务栏与最大化 Edge 之上 |
| 位置固定，拖不动 | ✅ 外部 `MoveWindow(40,400,700x260)` 后矩形仍是 `(800,10) 320x40` |
| 点击不触发动作、不抢焦点 | ✅ 点击胶囊前后前台窗口句柄不变 |

> 备注：验证时用了一块纯品红色（magenta）的全屏背景做对照，确认圆角外是「真正透出下层像素」而不是白色。

---

## 五、已知限制 / 下一步 TODO

代码里都留了 `TODO(扩展)` 注释，主要接入点：

* **展开动画**：`TimeIslandControl.xaml` 的 `PillRoot`，从胶囊态切到展开态（宽高 + 圆角 + 内容布局）
* **通知队列**：`TimeIslandViewModel` 新增可绑定属性 + `TimeIslandControl.OnLoaded`
* **音乐卡片 / 计时器**：同样挂 `ViewModel` + 换一个 UserControl，`MainWindow` 不用动
* **鼠标穿透**：`IslandWindowStyler.ApplyChrome()` 里加 `WS_EX_TRANSPARENT | WS_EX_LAYERED` + 运行时开关
* **拖拽 / 点击交互**：`TimeIslandControl` 里订阅 `PointerPressed / Tapped`（目前刻意不接）
* **托盘图标 + 退出入口 + 开机自启**：`MainWindow` 构造函数
* **WM_DPICHANGED / WM_DISPLAYCHANGE**：显示器缩放或主屏变化后重新计算尺寸与居中
* **外部配置文件**：`IslandOptions` 改为读 exe 同目录的 `island.config.json`

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
* 备份内容：源码与配置。`bin/`、`obj/`、`.vs/`、`.tools/` 都已被 `.gitignore` 忽略，不备份编译产物。
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

* 当前已备份版本：`v0.1.0-time-island` —— 时间岛最小可用版本（无边框置顶胶囊 + 每秒 `HH:MM:SS`）。

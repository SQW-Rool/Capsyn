using System.Diagnostics;
using System.Globalization;
using Capsyn.Services;
using Microsoft.UI.Xaml;

namespace Capsyn.ViewModels;

/// <summary>时间工具的三种模式。</summary>
public enum TimeToolMode
{
    /// <summary>正计时（秒表）。</summary>
    Stopwatch = 0,

    /// <summary>倒计时。</summary>
    Countdown = 1,

    /// <summary>闹钟。</summary>
    Alarm = 2,
}

/// <summary>
/// 「时间工具」的视图模型：正计时 / 倒计时 / 闹钟，同时也是**顶部胶囊「监测区」的状态源**。
///
/// 为什么是单例（<see cref="Shared"/>）：这个状态有两处消费者 ——
/// 展开后的工具页（<see cref="Controls.DashboardIslandControl"/>）和收起态的顶部胶囊
/// （<see cref="Controls.TimeIslandControl"/>）。两者是各自独立的 UserControl，
/// 靠一个共享实例保持一致，跟项目里 <see cref="Configuration.IslandOptions.Default"/> 是同一个路子。
/// tick 由外壳 <see cref="Controls.IslandShell"/> 统一驱动（它比两个控件都活得久）。
///
/// 计时本身：
///   * 正计时用 <see cref="Stopwatch"/>，倒计时用**绝对 deadline**（不用每 tick 减 100ms，UI 卡住也不会走慢）；
///   * 正计时 / 倒计时可以**同时跑**，各自一个 running 标志；
///   * 闹钟到点只在这一页显示，**不参与**顶部胶囊的占用（产品要求：闹钟不算「占用时间岛」）。
///
/// 占用顺序（<see cref="_activities"/>）：第一个 = 胶囊里的主计时（大字 + 图标），
/// 第二个 = 胶囊里的小圆（只显示模式图标）；新开始的计时插到最前面，
/// 原来的主计时降级成小圆 —— 即「正计时在显示时开启倒计时，正计时缩成一个圆」。
/// </summary>
public sealed class TimeToolsViewModel : ObservableObject
{
    /// <summary>全局共享实例：工具页和顶部胶囊都绑它。</summary>
    public static TimeToolsViewModel Shared { get; } = new();

    /// <summary>倒计时的四个预设（秒）。</summary>
    public static readonly int[] PresetSeconds = { 60, 180, 300, 600 };

    /// <summary>倒计时预设的按钮文案，与 <see cref="PresetSeconds"/> 一一对应。</summary>
    public static readonly string[] PresetLabels = { "1 分钟", "3 分钟", "5 分钟", "10 分钟" };

    /// <summary>默认选中「5 分钟」。</summary>
    private const int DefaultPresetIndex = 2;

    /// <summary>
    /// 默认预设的秒数（= <see cref="PresetSeconds"/>[2]）。
    /// 单独写一个常量是**必须**的：<see cref="Shared"/> 是静态字段，它的构造会先于
    /// 后面那些静态字段的初始化 —— 实例字段初始化器里引用 <see cref="PresetSeconds"/> 会拿到 null。
    /// </summary>
    private const int DefaultPresetSeconds = 300;

    private readonly Stopwatch _stopwatch = new();

    /// <summary>占用顶部胶囊的计时，按「主 → 次」排序（最多两个：正计时 / 倒计时）。</summary>
    private readonly List<TimeToolMode> _activities = new();

    private TimeToolMode _mode = TimeToolMode.Stopwatch;

    private bool _stopwatchRunning;
    private bool _countdownRunning;
    private bool _countdownRinging;
    private bool _alarmRinging;

    private int _presetIndex = DefaultPresetIndex;
    private TimeSpan _countdownTotal = TimeSpan.FromSeconds(DefaultPresetSeconds);
    private TimeSpan _countdownRemaining = TimeSpan.FromSeconds(DefaultPresetSeconds);
    private DateTime _countdownDeadline;

    /// <summary>
    /// 闹钟默认时间：08:00。
    /// 必须写字面量、不能引用静态字段 —— <see cref="Shared"/> 这个静态字段会先初始化，
    /// 那时后面声明的静态字段（包括这类默认值）还都是 default(T)，实例字段初始化器会拿到 0（00:00）。
    /// </summary>
    private TimeSpan _alarmTime = new(8, 0, 0);
    private DateTime? _alarmAt;

    private string _displayText = "00:00.0";
    private string _captionText = "就绪";
    private string _primaryText = "开始";
    private double _progress;

    /// <summary>正计时「记一圈」：上一圈用时 + 上一次记圈的读数。</summary>
    private TimeSpan _lastLap;
    private TimeSpan _lapMark;
    private string _lastLapText = "--";

    /// <summary>胶囊监测区的两个读数（缓存起来做变化判断，避免每次 tick 都通知界面）。</summary>
    private string _monitorPrimaryText = string.Empty;
    private string _monitorSecondaryText = string.Empty;

    /// <summary>小圆进度环的值（缓存 + 变化判断）。</summary>
    private double _monitorSecondaryProgress;

    private TimeToolsViewModel()
    {
        RefreshDisplay();

        // 调试钩子：CAPSYN_ALARM_TEST=<秒> 时，启动就把闹钟设成 N 秒后响。
        // 手动验证"到点弹窗 + 响铃"用（和 CAPSYN_DIAG / CAPSYN_BACKDROP 一样，只认环境变量）。
        var alarmTest = Environment.GetEnvironmentVariable("CAPSYN_ALARM_TEST");
        if (int.TryParse(alarmTest, out var seconds) && seconds > 0)
        {
            _alarmAt = DateTime.Now.AddSeconds(seconds);
            Diagnostics.Log($"alarm test hook: 将在 {_alarmAt:HH:mm:ss} 响（{seconds}s 后）");
        }

        // 同上，倒计时版：CAPSYN_COUNTDOWN_TEST=<秒>，启动就开一个 N 秒的倒计时。
        var countdownTest = Environment.GetEnvironmentVariable("CAPSYN_COUNTDOWN_TEST");
        if (int.TryParse(countdownTest, out var countdownSeconds) && countdownSeconds > 0)
        {
            _countdownTotal = TimeSpan.FromSeconds(countdownSeconds);
            _countdownRemaining = _countdownTotal;
            _countdownDeadline = DateTime.Now + _countdownTotal;
            _countdownRunning = true;
            Promote(TimeToolMode.Countdown);
            Diagnostics.Log($"countdown test hook: {countdownSeconds}s 倒计时已开始");
        }
    }

    // ---------------- 绑定给 XAML 的属性 ----------------

    /// <summary>当前模式（工具页正在看哪一页）。</summary>
    public TimeToolMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            OnPropertyChanged();

            OnPropertyChanged(nameof(ModeIndex));
            OnPropertyChanged(nameof(IsStopwatch));
            OnPropertyChanged(nameof(IsCountdown));
            OnPropertyChanged(nameof(IsAlarm));
            OnPropertyChanged(nameof(PresetsVisibility));
            OnPropertyChanged(nameof(StepperVisibility));
            OnPropertyChanged(nameof(StopwatchToolsVisibility));
            OnPropertyChanged(nameof(StopwatchIconVisibility));
            OnPropertyChanged(nameof(CountdownIconVisibility));
            OnPropertyChanged(nameof(AlarmIconVisibility));
        }
    }

    /// <summary>模式序号（0/1/2）：分段控件用它算指示条位置。</summary>
    public int ModeIndex => (int)Mode;

    public bool IsStopwatch => Mode == TimeToolMode.Stopwatch;

    public bool IsCountdown => Mode == TimeToolMode.Countdown;

    public bool IsAlarm => Mode == TimeToolMode.Alarm;

    /// <summary>倒计时预设那一行是否显示。</summary>
    public Visibility PresetsVisibility => IsCountdown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>闹钟加减那一行是否显示。</summary>
    public Visibility StepperVisibility => IsAlarm ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>正计时那行（记一圈 + 上一圈读数）是否显示。</summary>
    public Visibility StopwatchToolsVisibility => IsStopwatch ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StopwatchIconVisibility => IsStopwatch ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CountdownIconVisibility => IsCountdown ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AlarmIconVisibility => IsAlarm ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>大字读数。</summary>
    public string DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    /// <summary>读数下面的小字说明。</summary>
    public string CaptionText
    {
        get => _captionText;
        private set => SetProperty(ref _captionText, value);
    }

    /// <summary>主按钮文案（开始 / 暂停 / 继续 / 停止 / 启用 / 取消）。</summary>
    public string PrimaryText
    {
        get => _primaryText;
        private set => SetProperty(ref _primaryText, value);
    }

    /// <summary>环形进度 0~1（正计时 = 当前分钟走过多少，倒计时 = 剩余比例，闹钟 = 距响铃的进度）。</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>当前这一页是否在响铃（正计时没有响铃，倒计时/闹钟各自独立）。</summary>
    public bool IsRinging => Mode switch
    {
        TimeToolMode.Countdown => _countdownRinging,
        TimeToolMode.Alarm => _alarmRinging,
        _ => false,
    };

    /// <summary>闹钟读数：<c>HH:mm</c>（弹窗也用这个文本）。</summary>
    public string AlarmTimeText => FormatAlarm(_alarmTime);

    /// <summary>当前选中的倒计时预设序号（按钮高亮用）。</summary>
    public int PresetIndex => _presetIndex;

    /// <summary>上一圈用时（"00:12.3"，还没记过是 "--"）。</summary>
    public string LastLapText => _lastLapText;

    /// <summary>只有跑着的时候才能记圈。</summary>
    public bool CanLap => _stopwatchRunning && IsStopwatch;

    /// <summary>还需要每帧刷新的时间？外壳据此决定是否开着高频定时器（响铃是静态画面，不需要 tick）。</summary>
    public bool NeedsClock => _stopwatchRunning || _countdownRunning || _alarmAt.HasValue;

    // ---------------- 顶部胶囊「监测区」 ----------------

    /// <summary>胶囊里是否已经有计时占用（= 时钟要消失）。闹钟不算。</summary>
    public bool HasMonitorActivity => _activities.Count > 0;

    /// <summary>胶囊主计时（大字那个）；没有就是 null。</summary>
    public TimeToolMode? PrimaryActivity => _activities.Count > 0 ? _activities[0] : null;

    /// <summary>胶囊里缩成小圆的那个；没有就是 null。</summary>
    public TimeToolMode? SecondaryActivity => _activities.Count > 1 ? _activities[1] : null;

    /// <summary>主计时读数（<c>mm:ss</c>）。</summary>
    public string MonitorPrimaryText => _monitorPrimaryText;

    /// <summary>小圆的读数（暂时只用于无障碍/诊断，圆本身只画图标）。</summary>
    public string MonitorSecondaryText => _monitorSecondaryText;

    /// <summary>小圆画的进度环 0~1（正计时 = 当前分钟、倒计时 = 剩余比例、时间到 = 满圈）。</summary>
    public double MonitorSecondaryProgress => _monitorSecondaryProgress;

    /// <summary>主计时是不是「时间到」（胶囊读数转红）。</summary>
    public bool MonitorPrimaryRinging => PrimaryActivity == TimeToolMode.Countdown && _countdownRinging;

    /// <summary>小圆那个是不是在响（圆的描边转红）。</summary>
    public bool MonitorSecondaryRinging => SecondaryActivity == TimeToolMode.Countdown && _countdownRinging;

    // ---------------- 交互 ----------------

    /// <summary>合并后的「响铃开始 / 结束」通知：<c>(类型, 是否开始)</c>。声音和闹钟弹窗都靠它。</summary>
    public event Action<RingtoneKind, bool>? RingingChanged;

    /// <summary>切模式。正在响铃时切模式 = 先停掉这一页的铃。</summary>
    public void SelectMode(TimeToolMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        if (IsRinging)
        {
            StopRinging();
        }

        Mode = mode;
        RefreshDisplay();
    }

    /// <summary>主按钮：开始 / 暂停 / 继续 / 停止 / 启用 / 取消。</summary>
    public void TogglePrimary()
    {
        if (IsRinging)
        {
            StopRinging();
            RefreshDisplay();
            return;
        }

        switch (Mode)
        {
            case TimeToolMode.Stopwatch:
                if (_stopwatchRunning)
                {
                    _stopwatch.Stop();
                    _stopwatchRunning = false;
                }
                else
                {
                    _stopwatch.Start();
                    _stopwatchRunning = true;
                    Promote(TimeToolMode.Stopwatch);
                }

                break;

            case TimeToolMode.Countdown:
                if (_countdownRunning)
                {
                    PauseCountdown();
                }
                else
                {
                    StartCountdown();
                }

                break;

            case TimeToolMode.Alarm:
                _alarmAt = _alarmAt.HasValue ? null : NextOccurrence(_alarmTime);
                break;
        }

        RefreshDisplay();
    }

    /// <summary>次按钮：复位（把这一页的计时清掉，并从胶囊监测区里撤下）。</summary>
    public void ResetCurrent()
    {
        switch (Mode)
        {
            case TimeToolMode.Stopwatch:
                _stopwatch.Reset();
                _stopwatchRunning = false;
                _lastLap = TimeSpan.Zero;
                _lapMark = TimeSpan.Zero;
                _lastLapText = "--";
                OnPropertyChanged(nameof(LastLapText));
                Demote(TimeToolMode.Stopwatch);
                break;

            case TimeToolMode.Countdown:
                _countdownRunning = false;
                _countdownRemaining = _countdownTotal;
                SetCountdownRinging(false);
                Demote(TimeToolMode.Countdown);
                break;

            case TimeToolMode.Alarm:
                _alarmAt = null;
                SetAlarmRinging(false);
                break;
        }

        RefreshDisplay();
    }

    /// <summary>正计时：记一圈（只在跑着的时候生效）。</summary>
    public void Lap()
    {
        if (!CanLap)
        {
            return;
        }

        var now = _stopwatch.Elapsed;
        _lastLap = now - _lapMark;
        _lapMark = now;
        _lastLapText = FormatStopwatch(_lastLap);

        OnPropertyChanged(nameof(LastLapText));
    }

    /// <summary>选倒计时预设（秒表/闹钟模式下无副作用）。</summary>
    public void SelectPreset(int index)
    {
        if (index < 0 || index >= PresetSeconds.Length)
        {
            return;
        }

        _presetIndex = index;
        _countdownTotal = TimeSpan.FromSeconds(PresetSeconds[index]);
        _countdownRemaining = _countdownTotal;
        _countdownRunning = false;
        SetCountdownRinging(false);
        Demote(TimeToolMode.Countdown);

        OnPropertyChanged(nameof(PresetIndex));
        RefreshDisplay();
    }

    /// <summary>闹钟时间加减（分钟，可负）。启用状态下改时间会重算「下一次响铃」。</summary>
    public void AdjustAlarmMinutes(int minutes)
    {
        var total = (int)Math.Round(_alarmTime.TotalMinutes) + minutes;
        total = ((total % 1440) + 1440) % 1440;
        _alarmTime = TimeSpan.FromMinutes(total);

        if (_alarmAt.HasValue)
        {
            _alarmAt = NextOccurrence(_alarmTime);
        }

        RefreshDisplay();
    }

    /// <summary>点胶囊里的小圆：把小圆那个换到主位（两个计时互换）。</summary>
    public void SwapActivities()
    {
        if (_activities.Count < 2)
        {
            return;
        }

        (_activities[0], _activities[1]) = (_activities[1], _activities[0]);
        NotifyMonitor();
    }

    /// <summary>由外壳的定时器驱动；只把「在跑的东西」推进，和当前看的是哪一页无关。</summary>
    public void Tick()
    {
        var now = DateTime.Now;

        if (_countdownRunning)
        {
            var remaining = _countdownDeadline - now;

            if (remaining <= TimeSpan.Zero)
            {
                _countdownRemaining = TimeSpan.Zero;
                _countdownRunning = false;
                SetCountdownRinging(true);
                // 到点了要让人看见：把这个倒计时提到主位（原来那个正计时缩成小圆）。
                Promote(TimeToolMode.Countdown);
            }
            else
            {
                _countdownRemaining = remaining;
            }
        }

        if (_alarmAt is { } at && now >= at)
        {
            _alarmAt = null;
            SetAlarmRinging(true);
        }

        RefreshDisplay();
    }

    // ---------------- 内部：计时 ----------------

    private void StartCountdown()
    {
        if (_countdownRemaining <= TimeSpan.Zero)
        {
            _countdownRemaining = _countdownTotal;
        }

        _countdownDeadline = DateTime.Now + _countdownRemaining;
        _countdownRunning = true;
        SetCountdownRinging(false);
        Promote(TimeToolMode.Countdown);
    }

    private void PauseCountdown()
    {
        var remaining = _countdownDeadline - DateTime.Now;
        _countdownRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        _countdownRunning = false;
    }

    private void StopRinging()
    {
        switch (Mode)
        {
            case TimeToolMode.Countdown:
                SetCountdownRinging(false);
                _countdownRunning = false;
                _countdownRemaining = _countdownTotal;
                Demote(TimeToolMode.Countdown);
                break;

            case TimeToolMode.Alarm:
                SetAlarmRinging(false);
                _alarmAt = null;
                break;
        }
    }

    /// <summary>
    /// 供外部（闹钟弹窗的「停止」/ 计时页按钮）统一收铃：把当前在响的全部停掉并复位。
    /// 声音与弹窗都监听 <see cref="RingingChanged"/>，不需要调用方自己管。
    /// </summary>
    public void DismissRinging()
    {
        if (_alarmRinging)
        {
            SetAlarmRinging(false);
            _alarmAt = null;
        }

        if (_countdownRinging)
        {
            SetCountdownRinging(false);
            _countdownRunning = false;
            _countdownRemaining = _countdownTotal;
            Demote(TimeToolMode.Countdown);
        }

        RefreshDisplay();
    }

    /// <summary>闹钟弹窗上的「稍后 5 分钟」：先收铃，再把下一次响铃推到 <paramref name="delay"/> 之后。</summary>
    public void SnoozeAlarm(TimeSpan delay)
    {
        SetAlarmRinging(false);
        _alarmAt = DateTime.Now + delay;
        RefreshDisplay();

        Diagnostics.Log($"alarm: snoozed for {delay.TotalMinutes:0} min -> {_alarmAt:HH:mm:ss}");
    }

    private void SetCountdownRinging(bool value)
    {
        if (_countdownRinging == value)
        {
            return;
        }

        _countdownRinging = value;
        OnPropertyChanged(nameof(IsRinging));
        OnPropertyChanged(nameof(NeedsClock));
        OnPropertyChanged(nameof(MonitorPrimaryRinging));
        OnPropertyChanged(nameof(MonitorSecondaryRinging));

        RingingChanged?.Invoke(RingtoneKind.Countdown, value);
    }

    private void SetAlarmRinging(bool value)
    {
        if (_alarmRinging == value)
        {
            return;
        }

        _alarmRinging = value;
        OnPropertyChanged(nameof(IsRinging));
        OnPropertyChanged(nameof(NeedsClock));

        RingingChanged?.Invoke(RingtoneKind.Alarm, value);
    }

    private static DateTime NextOccurrence(TimeSpan timeOfDay)
    {
        var today = DateTime.Today + timeOfDay;
        return today > DateTime.Now ? today : today.AddDays(1);
    }

    // ---------------- 内部：胶囊占用顺序 ----------------

    private void Promote(TimeToolMode mode)
    {
        _activities.Remove(mode);
        _activities.Insert(0, mode);
        NotifyMonitor();
    }

    private void Demote(TimeToolMode mode)
    {
        if (_activities.Remove(mode))
        {
            NotifyMonitor();
        }
    }

    /// <summary>重算胶囊里的读数，并按需通知界面（每次 tick 都会走一遍）。</summary>
    private void NotifyMonitor()
    {
        OnPropertyChanged(nameof(HasMonitorActivity));
        OnPropertyChanged(nameof(PrimaryActivity));
        OnPropertyChanged(nameof(SecondaryActivity));
        OnPropertyChanged(nameof(MonitorPrimaryRinging));
        OnPropertyChanged(nameof(MonitorSecondaryRinging));

        var primary = PrimaryActivity is { } p ? FormatMonitor(p) : string.Empty;
        var secondary = SecondaryActivity is { } s ? FormatMonitor(s) : string.Empty;

        if (primary != _monitorPrimaryText)
        {
            _monitorPrimaryText = primary;
            OnPropertyChanged(nameof(MonitorPrimaryText));
        }

        if (secondary != _monitorSecondaryText)
        {
            _monitorSecondaryText = secondary;
            OnPropertyChanged(nameof(MonitorSecondaryText));
        }

        var secondaryProgress = SecondaryActivity is { } second ? ProgressFor(second) : 0.0;
        if (Math.Abs(secondaryProgress - _monitorSecondaryProgress) > 0.002)
        {
            _monitorSecondaryProgress = secondaryProgress;
            OnPropertyChanged(nameof(MonitorSecondaryProgress));
        }
    }

    /// <summary>某个计时当前的进度环值（0~1）。</summary>
    private double ProgressFor(TimeToolMode mode) => mode switch
    {
        TimeToolMode.Stopwatch => _stopwatch.Elapsed.TotalSeconds % 60.0 / 60.0,
        TimeToolMode.Countdown => _countdownRinging
            ? 1.0
            : _countdownTotal > TimeSpan.Zero
                ? Math.Clamp(_countdownRemaining / _countdownTotal, 0.0, 1.0)
                : 0.0,
        _ => 0.0,
    };

    private string FormatMonitor(TimeToolMode mode) => mode switch
    {
        TimeToolMode.Stopwatch => FormatCompact(_stopwatch.Elapsed),
        TimeToolMode.Countdown => FormatClock(_countdownRemaining),
        _ => string.Empty,
    };

    // ---------------- 内部：文本 ----------------

    /// <summary>把当前状态翻译成三个文本 + 进度，并且只在值真的变了的时候通知界面。</summary>
    private void RefreshDisplay()
    {
        OnPropertyChanged(nameof(NeedsClock));
        OnPropertyChanged(nameof(CanLap));

        switch (Mode)
        {
            case TimeToolMode.Stopwatch:
                DisplayText = FormatStopwatch(_stopwatch.Elapsed);
                CaptionText = _stopwatchRunning
                    ? "计时中"
                    : _stopwatch.Elapsed > TimeSpan.Zero ? "已暂停" : "就绪";
                PrimaryText = _stopwatchRunning
                    ? "暂停"
                    : _stopwatch.Elapsed > TimeSpan.Zero ? "继续" : "开始";
                Progress = _stopwatch.Elapsed.TotalSeconds % 60.0 / 60.0;
                break;

            case TimeToolMode.Countdown:
                DisplayText = FormatClock(_countdownRemaining);
                CaptionText = _countdownRinging
                    ? "时间到"
                    : _countdownRunning
                        ? "计时中"
                        : _countdownRemaining < _countdownTotal
                            ? "已暂停"
                            : string.Format(CultureInfo.InvariantCulture, "{0:0} 分钟", _countdownTotal.TotalMinutes);
                PrimaryText = _countdownRinging
                    ? "停止"
                    : _countdownRunning
                        ? "暂停"
                        : _countdownRemaining < _countdownTotal ? "继续" : "开始";
                Progress = _countdownTotal > TimeSpan.Zero
                    ? Math.Clamp(_countdownRemaining / _countdownTotal, 0.0, 1.0)
                    : 0.0;
                break;

            case TimeToolMode.Alarm:
                DisplayText = FormatAlarm(_alarmTime);
                if (_alarmRinging)
                {
                    CaptionText = "响铃中";
                }
                else if (_alarmAt is { } at)
                {
                    var left = at - DateTime.Now;
                    CaptionText = left.TotalHours >= 1
                        ? string.Format(CultureInfo.InvariantCulture, "{0:0.#} 小时后", left.TotalHours)
                        : string.Format(CultureInfo.InvariantCulture, "{0:0} 分钟后", Math.Max(1, left.TotalMinutes));
                }
                else
                {
                    CaptionText = "未启用";
                }

                PrimaryText = _alarmRinging ? "停止" : _alarmAt.HasValue ? "取消" : "启用";
                Progress = _alarmAt is { } target
                    ? 1.0 - Math.Clamp((target - DateTime.Now).TotalSeconds / 86400.0, 0.0, 1.0)
                    : _alarmRinging ? 1.0 : 0.0;
                break;
        }

        NotifyMonitor();
    }

    /// <summary>正计时读数：<c>mm:ss.f</c>，超过一小时变成 <c>h:mm:ss</c>。</summary>
    private static string FormatStopwatch(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2}", elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds / 100);

    /// <summary>胶囊里的正计时（更紧凑：不要十分之一秒）。</summary>
    private static string FormatCompact(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", elapsed.Minutes, elapsed.Seconds);

    /// <summary>倒计时读数：向上取整到秒（所以最后一秒显示 00:01，归零才是 00:00）。</summary>
    private static string FormatClock(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        var span = TimeSpan.FromSeconds(totalSeconds);

        return span.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", span.Minutes, span.Seconds);
    }

    /// <summary>闹钟读数：<c>HH:mm</c>。</summary>
    private static string FormatAlarm(TimeSpan timeOfDay)
        => string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", timeOfDay.Hours, timeOfDay.Minutes);
}

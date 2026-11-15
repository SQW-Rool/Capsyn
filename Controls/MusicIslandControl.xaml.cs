using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Capsyn.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;

namespace Capsyn.Controls;

/// <summary>
/// 「音乐副岛」：看板右侧的独立小岛（与看板等高）。
///
/// 两个视图：
///   * 播放器（有音乐时）：封面 / 歌名 / 歌手 / 进度 / 控制；头部右侧「音源名 ▾」可以切到音源选择；
///   * 音源选择（没有音乐时默认、或点音源按钮进入）：内置播放器列表，本机没装的置灰。
///
/// 位置和窗口区域由 <see cref="IslandShell"/> 统一摆；这里只画内容 + 把选择结果抛给外壳。
/// </summary>
public sealed partial class MusicIslandControl : UserControl
{
    private MediaMonitor? _media;
    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private DispatcherTimer? _timer;
    private int _coverGeneration;
    private bool _equalizerRunning;

    private bool _pickerOpen;
    private IReadOnlyList<MediaSourceStatus> _statuses = Array.Empty<MediaSourceStatus>();
    private int _sourceRefreshToken;

    /// <summary>「仅提醒」模式下待处理的冲突 AUMID。</summary>
    private string? _conflictAumid;

    /// <summary>临时提示（例如「已暂停 汽水音乐」）的恢复定时器。</summary>
    private DispatcherTimer? _noticeTimer;

    public MusicIslandControl()
    {
        InitializeComponent();

        Loaded += (_, _) => StartTimer();
        Unloaded += (_, _) => _timer?.Stop();
    }

    /// <summary>接上媒体服务（由 <see cref="IslandShell"/> 调用）。</summary>
    public void AttachMedia(MediaMonitor media)
    {
        _media = media;
        media.Changed += OnMediaChanged;
        media.ArtworkChanged += OnArtworkChanged;
        media.SourceChanged += OnMediaSourceChanged;
        OnMediaChanged(media.Current);
    }

    /// <summary>快照变化（也可能来自播放器自己切歌）。</summary>
    public event Action<MediaSnapshot>? SnapshotChanged;

    /// <summary>用户在音源面板里选了某个音源（参数是音源 id）。</summary>
    public event Action<string>? SourceSelected;

    /// <summary>用户在音源面板点了「返回」（没有音乐时外壳可以借此收起副岛）。</summary>
    public event Action? PickerClosed;

    /// <summary>「仅提醒」模式下用户点了「暂停它」：参数是冲突的 AUMID。</summary>
    public event Action<string>? ConflictPauseRequested;

    /// <summary>音源面板显示 / 隐藏了（面板也可能由副岛内部的按钮打开，外壳要跟着同步状态）。</summary>
    public event Action<bool>? PickerVisibilityChanged;

    /// <summary>当前是不是音源选择视图。</summary>
    public bool IsPickerOpen => _pickerOpen;

    /// <summary>从应用资源里安全取 Brush / Style（缺失时返回 null，不抛异常）。</summary>
    private static T? Res<T>(string key) where T : class
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;

    /// <summary>
    /// 切到音源选择视图 / 回到播放器。
    /// <paramref name="show"/> = true 时立刻异步刷新一次本机音源状态。
    /// </summary>
    public void ShowSourcePicker(bool show)
    {
        _pickerOpen = show;

        PlayerLayer.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PickerLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SourceButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PickerBackButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (show)
        {
            StateText.Text = "音源选择";
            BackdropLayer.Opacity = 0.55;
            Equalizer.Visibility = Visibility.Collapsed;
            _noticeTimer?.Stop();
            ClearConflictPrompt();
            _ = RefreshSourcesAsync();
        }
        else
        {
            BackdropLayer.Opacity = 1;
            Equalizer.Visibility = _equalizerRunning ? Visibility.Visible : Visibility.Collapsed;
            ApplySnapshotToPlayer();
        }

        PickerVisibilityChanged?.Invoke(show);
    }

    /// <summary>外壳在选中音源后调用：同步高亮（面板可能还开着）。</summary>
    public void UpdateSelection()
    {
        if (_pickerOpen)
        {
            RenderSourceList();
        }
    }

    // ---------------- 播放冲突提示 ----------------

    /// <summary>「仅提醒」模式：在头部显示「暂停 <播放器>」按钮。</summary>
    public void ShowConflictPrompt(string loserName, string aumid)
    {
        if (_pickerOpen)
        {
            return;
        }

        _conflictAumid = aumid;
        ConflictText.Text = $"暂停 {loserName}";
        ConflictButton.Visibility = Visibility.Visible;
    }

    public void ClearConflictPrompt()
    {
        _conflictAumid = null;
        ConflictButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>独占模式暂停了别的播放器后，短暂提示一下（例如「已暂停 汽水音乐」）。</summary>
    public void FlashNotice(string text, int durationMs = 3200)
    {
        if (_pickerOpen)
        {
            return;
        }

        StateText.Text = text;

        _noticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _noticeTimer.Stop();

        if (_noticeTimer.Interval != TimeSpan.FromMilliseconds(durationMs))
        {
            _noticeTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        }

        _noticeTimer.Tick -= OnNoticeTimerTick;
        _noticeTimer.Tick += OnNoticeTimerTick;
        _noticeTimer.Start();
    }

    private void OnNoticeTimerTick(object? sender, object e)
    {
        _noticeTimer?.Stop();
        ApplySnapshotToPlayer();
    }

    private void OnConflictPauseClick(object sender, RoutedEventArgs e)
    {
        var aumid = _conflictAumid;
        ClearConflictPrompt();

        if (aumid is not null)
        {
            ConflictPauseRequested?.Invoke(aumid);
        }
    }

    private void OnMediaSourceChanged()
    {
        if (_pickerOpen)
        {
            RenderSourceList();
        }

        ApplySnapshotToPlayer();
    }

    private void StartTimer()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, object e) => UpdateProgress();

    private void OnMediaChanged(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;

        if (_pickerOpen)
        {
            return;
        }

        ApplySnapshotToPlayer();
        SnapshotChanged?.Invoke(snapshot);
    }

    private void ApplySnapshotToPlayer()
    {
        var snapshot = _snapshot;

        TitleText.Text = snapshot.IsAvailable ? snapshot.Title : "没有正在播放的媒体";
        ArtistText.Text = snapshot.IsAvailable
            ? (string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.Album : snapshot.Artist)
            : "点右上角选音源";

        SourceText.Text = snapshot.IsAvailable
            ? FriendlySourceName(snapshot.SourceApp)
            : "自动";

        StateText.Text = snapshot.IsAvailable
            ? (snapshot.IsPlaying ? "正在播放" : "已暂停")
            : "没有正在播放";

        PlayPauseButton.IsEnabled = snapshot.IsAvailable;
        PreviousButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.IsAvailable && snapshot.CanSkipNext;

        PlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        if (snapshot.IsAvailable && snapshot.IsPlaying)
        {
            StartEqualizer();
        }
        else
        {
            StopEqualizer();
        }

        UpdateProgress();
    }

    /// <summary>把 SMTC 的 SourceAppUserModelId 翻成好读的音源名。</summary>
    private static string FriendlySourceName(string sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return "自动";
        }

        foreach (var definition in MediaSourceCatalog.All)
        {
            if (MediaSourceCatalog.Matches(definition, sourceApp))
            {
                return definition.DisplayName;
            }
        }

        return sourceApp;
    }

    private void UpdateProgress()
    {
        var position = _media?.EstimatePosition() ?? _snapshot.Position;
        var duration = _snapshot.Duration;

        Progress.Value = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
            : 0;

        PositionText.Text = _snapshot.IsAvailable ? FormatTime(position) : "--:--";
        DurationText.Text = duration > TimeSpan.Zero ? FormatTime(duration) : "--:--";
    }

    private static string FormatTime(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    // ---------------- 音源选择 ----------------

    private async Task RefreshSourcesAsync()
    {
        var token = ++_sourceRefreshToken;

        IReadOnlyList<MediaSourceStatus> statuses;
        try
        {
            statuses = await Task.Run(() => MediaSourceCatalog.GetStatuses(forceRefresh: true));
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"music island: 探测音源失败：{ex.Message}");
            return;
        }

        if (token != _sourceRefreshToken || !_pickerOpen)
        {
            return;
        }

        _statuses = statuses;
        RenderSourceList();
    }

    private void RenderSourceList()
    {
        SourceList.Children.Clear();

        // 最上面一行「自动跟随」：清掉手动选择，回到系统当前会话。
        SourceList.Children.Add(BuildAutoRow());

        foreach (var status in _statuses)
        {
            SourceList.Children.Add(BuildSourceRow(status));
        }

        var anyInstalled = _statuses.Any(s => s.IsInstalled);
        PickerHint.Text = anyInstalled
            ? "选中后会切换；没在运行会尝试帮你启动"
            : "本机没探测到这些播放器：先安装一个再回来选";
    }

    /// <summary>当前正在播放的会话是不是这个音源（没手动选音源时用它高亮当前项）。</summary>
    private bool snapshotMatchesSource(MediaSourceDefinition definition)
        => _snapshot.IsAvailable && MediaSourceCatalog.Matches(definition, _snapshot.SourceApp);

    private Button BuildAutoRow()
    {
        var selected = string.IsNullOrEmpty(_media?.PreferredMediaSourceId);

        var name = new TextBlock
        {
            Text = "自动跟随",
            FontSize = 12.5,
            Foreground = Res<Brush>(selected ? "AccentTextBrush" : "TextPrimaryBrush")
                         ?? new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var state = new TextBlock
        {
            Text = "系统当前会话",
            Style = Res<Style>("HeroCaption"),
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Res<Brush>("SubtleHoverBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = "自",
                FontSize = 10.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res<Brush>("TextSecondaryBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray),
            },
        };

        var check = new TextBlock
        {
            Text = "✓",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res<Brush>("AccentTextBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(badge, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(state, 2);
        Grid.SetColumn(check, 3);

        grid.Children.Add(badge);
        grid.Children.Add(name);
        grid.Children.Add(state);
        grid.Children.Add(check);

        var button = new Button
        {
            Content = grid,
            Tag = "auto",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(9, 2, 9, 2),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Res<Brush>(selected ? "AccentTintMediumBrush" : "CardBorderStrongBrush")
                          ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Background = Res<Brush>(selected ? "AccentTintWeakBrush" : "CardBackgroundBrush")
                         ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "SourceItem-auto");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "自动跟随");
        button.Click += OnSourceItemClick;
        return button;
    }

    private Button BuildSourceRow(MediaSourceStatus status)
    {
        var preferred = _media?.PreferredMediaSourceId;
        var playingThis = snapshotMatchesSource(status.Definition);
        var selected = string.Equals(preferred, status.Id, StringComparison.OrdinalIgnoreCase)
                       || (string.IsNullOrEmpty(preferred) && playingThis);
        var installed = status.IsInstalled;

        var name = new TextBlock
        {
            Text = status.DisplayName,
            FontSize = 12.5,
            Foreground = Res<Brush>("TextPrimaryBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var state = new TextBlock
        {
            Text = !installed ? "未安装" : status.IsRunning ? "运行中" : "已安装",
            Style = Res<Style>("HeroCaption"),
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Res<Brush>("SubtleHoverBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = status.DisplayName[..1],
                FontSize = 10.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res<Brush>("TextSecondaryBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray),
            },
        };

        var check = new TextBlock
        {
            Text = "✓",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res<Brush>("AccentTextBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
            Visibility = selected && installed ? Visibility.Visible : Visibility.Collapsed,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(badge, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(state, 2);
        Grid.SetColumn(check, 3);

        grid.Children.Add(badge);
        grid.Children.Add(name);
        grid.Children.Add(state);
        grid.Children.Add(check);

        var button = new Button
        {
            Content = grid,
            Tag = status.Id,
            IsEnabled = installed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(9, 2, 9, 2),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Res<Brush>(selected ? "AccentTintMediumBrush" : "CardBorderStrongBrush")
                          ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Background = Res<Brush>(selected ? "AccentTintWeakBrush" : "CardBackgroundBrush")
                         ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Opacity = installed ? 1.0 : 0.45,
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"SourceItem-{status.Id}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, status.DisplayName);
        button.Click += OnSourceItemClick;
        return button;
    }
    private void OnSourceButtonClick(object sender, RoutedEventArgs e) => ShowSourcePicker(true);

    private void OnPickerBackClick(object sender, RoutedEventArgs e) => PickerClosed?.Invoke();

    private void OnSourceItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        if (id != "auto" && _statuses.FirstOrDefault(s => s.Id == id) is not { IsInstalled: true })
        {
            return;
        }

        SourceSelected?.Invoke(id);
    }

    // ---------------- 封面 ----------------

    private static string ArtworkKeyOf(MediaSnapshot snapshot) => $"{snapshot.Title}|{snapshot.Artist}";

    private void OnArtworkChanged(byte[]? artwork, string key)
    {
        if (!string.Equals(key, ArtworkKeyOf(_snapshot), StringComparison.Ordinal))
        {
            return;
        }

        if (artwork is null || artwork.Length == 0)
        {
            CoverImage.Source = null;
            CoverImage.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
            BackdropImage.Source = null;
            BackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        _ = ShowCoverAsync(artwork, ++_coverGeneration);
    }

    private async Task ShowCoverAsync(byte[] artwork, int generation)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();

            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(artwork);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            if (generation != _coverGeneration)
            {
                return;
            }

            CoverImage.Source = bitmap;
            CoverImage.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;

            BackdropImage.Source = bitmap;
            BackdropLayer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"music island: 封面显示失败：{ex.Message}");
        }
    }

    // ---------------- 均衡器 ----------------

    private void StartEqualizer()
    {
        if (_equalizerRunning)
        {
            return;
        }

        _equalizerRunning = true;
        Equalizer.Visibility = Visibility.Visible;

        var bars = new[] { Eq1, Eq2, Eq3 };

        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3((float)bar.Width / 2f, (float)bar.Height, 0f);

            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0.25f);
            animation.InsertKeyFrame(0.5f, 1f);
            animation.InsertKeyFrame(1f, 0.45f);
            animation.Duration = TimeSpan.FromMilliseconds(560 + (i * 150));
            animation.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Scale.Y", animation);
        }
    }

    private void StopEqualizer()
    {
        if (!_equalizerRunning)
        {
            Equalizer.Visibility = Visibility.Collapsed;
            return;
        }

        _equalizerRunning = false;

        foreach (var bar in new[] { Eq1, Eq2, Eq3 })
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }

        Equalizer.Visibility = Visibility.Collapsed;
    }

    // ---------------- 控制 ----------------

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.TogglePlayPauseAsync();
        Diagnostics.Log($"music island: play/pause ok={ok}");
        UpdateProgress();
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipPreviousAsync();
        Diagnostics.Log($"music island: previous ok={ok}");
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        var ok = await _media.SkipNextAsync();
        Diagnostics.Log($"music island: next ok={ok}");
    }
}

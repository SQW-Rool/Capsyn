using System.IO;
using System.Text.Json;
using Capsyn.Services;

namespace Capsyn.Configuration;

/// <summary>
/// 用户设置（目前只有铃声）。存在 <c>%LOCALAPPDATA%\Capsyn\settings.json</c>，
/// 不放在 exe 旁边 —— 装到 Program Files 时那里是不可写的。
///
/// 每一类铃声有两种来源：
///   * 内置音效：序号 0..4（默认 0 = 第一个）；
///   * 自定义文件：<see cref="AlarmCustomFile"/> / <see cref="CountdownCustomFile"/> 有值就优先用文件，
///     点内置音效会把对应的自定义清掉。
///
/// 读写都容错：文件缺失 / 坏掉 / 没有权限一律退回默认值；自定义文件不见了就退回内置音效，
/// **绝不因为设置读不出来就崩**。
/// </summary>
public sealed class AppSettings
{
    private const string FolderName = "Capsyn";
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>全局唯一实例（和 <see cref="IslandOptions.Default"/> 一个路子）。</summary>
    public static AppSettings Current { get; } = Load();

    /// <summary>闹钟铃声序号（0..4）。</summary>
    public int AlarmRingtone { get; set; }

    /// <summary>倒计时铃声序号（0..4）。</summary>
    public int CountdownRingtone { get; set; }

    /// <summary>闹钟自定义音频文件（绝对路径；null = 用内置音效）。</summary>
    public string? AlarmCustomFile { get; set; }

    /// <summary>倒计时自定义音频文件（绝对路径；null = 用内置音效）。</summary>
    public string? CountdownCustomFile { get; set; }

    /// <summary>设置文件路径（诊断日志里会打出来，方便用户自己看/删）。</summary>
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        FileName);

    /// <summary>自定义铃声的存放目录（选择文件后会复制进来，原文件删了也不影响）。</summary>
    public static string RingtoneFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        "ringtones");

    /// <summary>按 kind 取/存对应内置音效序号。</summary>
    public int GetRingtone(RingtoneKind kind)
        => kind == RingtoneKind.Alarm ? AlarmRingtone : CountdownRingtone;

    /// <summary>按 kind 改对应内置音效序号（只改内存，调用方需要时再 <see cref="Save"/>）。</summary>
    public void SetRingtone(RingtoneKind kind, int index)
    {
        if (kind == RingtoneKind.Alarm)
        {
            AlarmRingtone = index;
        }
        else
        {
            CountdownRingtone = index;
        }
    }

    /// <summary>按 kind 取自定义文件（没有就是 null）。</summary>
    public string? GetCustomFile(RingtoneKind kind)
        => kind == RingtoneKind.Alarm ? AlarmCustomFile : CountdownCustomFile;

    /// <summary>按 kind 设置/清除自定义文件。</summary>
    public void SetCustomFile(RingtoneKind kind, string? path)
    {
        if (kind == RingtoneKind.Alarm)
        {
            AlarmCustomFile = path;
        }
        else
        {
            CountdownCustomFile = path;
        }
    }

    /// <summary>这一类的当前铃声是不是自定义文件。</summary>
    public bool HasCustomFile(RingtoneKind kind) => !string.IsNullOrWhiteSpace(GetCustomFile(kind));

    /// <summary>写回磁盘；失败只记诊断日志，不影响功能。</summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            Diagnostics.Log($"settings: saved to {FilePath}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"settings: save failed: {ex.Message}");
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    loaded.Normalize();
                    Diagnostics.Log(
                        $"settings: loaded {FilePath} (alarm={loaded.AlarmRingtone}{(loaded.AlarmCustomFile is null ? string.Empty : " custom")}, "
                        + $"countdown={loaded.CountdownRingtone}{(loaded.CountdownCustomFile is null ? string.Empty : " custom")})");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"settings: load failed, fallback to defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>把越界值夹回合法范围、把已经不存在/空的自定义文件清掉。</summary>
    private void Normalize()
    {
        AlarmRingtone = Math.Clamp(AlarmRingtone, 0, RingtoneLibrary.Count - 1);
        CountdownRingtone = Math.Clamp(CountdownRingtone, 0, RingtoneLibrary.Count - 1);

        if (!IsUsableFile(AlarmCustomFile))
        {
            AlarmCustomFile = null;
        }

        if (!IsUsableFile(CountdownCustomFile))
        {
            CountdownCustomFile = null;
        }
    }

    private static bool IsUsableFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;
}

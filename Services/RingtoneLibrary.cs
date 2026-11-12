using System.Text;

namespace Capsyn.Services;

/// <summary>两类铃声：闹钟（要吵醒人的循环）和倒计时（一次性提示）。</summary>
public enum RingtoneKind
{
    Alarm = 0,
    Countdown = 1,
}

/// <summary>一个内置音效：序号 + 设置页里显示的名字。</summary>
public sealed record RingtoneDefinition(int Index, string Name);

/// <summary>
/// 内置铃声库：**代码合成**的 WAV，不带任何音频文件（跟项目里"图标用矢量路径、不依赖图标字体"一个思路）。
///
/// 为什么合成而不是塞 mp3/wav 资源：
///   * 仓库里不出现二进制资源，十种音效全部可读、可改、可 review；
///   * 没有版权问题（不是从系统音效/素材站里抠的）；
///   * 体积为零：按需生成一次，缓存成 byte[]（每个约 100~150 KB，十种全用上也就 1 MB 出头）。
///
/// 合成方式：44.1kHz / 16-bit / 单声道，正弦（或方波）音 + 快速起音 + 指数衰减包络，
/// 避免咔哒声（起音 6ms、收音 25ms）。
/// </summary>
public static class RingtoneLibrary
{
    /// <summary>每类内置音效数量（闹钟 5 个、倒计时 5 个）。</summary>
    public const int Count = 5;

    private const int SampleRate = 44100;

    private static readonly RingtoneDefinition[] AlarmTones =
    {
        new(0, "经典双响"),
        new(1, "上升三连"),
        new(2, "电子脉冲"),
        new(3, "柔和钟声"),
        new(4, "蜂鸣警报"),
    };

    private static readonly RingtoneDefinition[] CountdownTones =
    {
        new(0, "清脆提示"),
        new(1, "双叮"),
        new(2, "上升琶音"),
        new(3, "水滴"),
        new(4, "柔和和弦"),
    };

    private static readonly Dictionary<(RingtoneKind Kind, int Index), byte[]> Cache = new();

    /// <summary>取某一类的全部内置音效。</summary>
    public static IReadOnlyList<RingtoneDefinition> For(RingtoneKind kind)
        => kind == RingtoneKind.Alarm ? AlarmTones : CountdownTones;

    /// <summary>取某个音效的 WAV 字节（首次生成后缓存）。序号越界会夹回合法范围。</summary>
    public static byte[] GetWave(RingtoneKind kind, int index)
    {
        var safeIndex = Math.Clamp(index, 0, Count - 1);

        lock (Cache)
        {
            if (!Cache.TryGetValue((kind, safeIndex), out var wave))
            {
                wave = kind == RingtoneKind.Alarm
                    ? BuildAlarm(safeIndex)
                    : BuildCountdown(safeIndex);
                Cache[(kind, safeIndex)] = wave;
            }

            return wave;
        }
    }

    // ---------------- 十种音效的谱子 ----------------

    private static byte[] BuildAlarm(int index) => index switch
    {
        // 经典双响：两声 880Hz 短促"叮咚"，留一段空拍，循环起来有节奏感
        0 => BuildWave(1.10,
            new Note(0.00, 0.13, 880, Waveform.Sine),
            new Note(0.20, 0.13, 880, Waveform.Sine)),

        // 上升三连：660 → 880 → 1175，像老式电子闹钟的"叫你起床"
        1 => BuildWave(1.20,
            new Note(0.00, 0.15, 660, Waveform.Sine),
            new Note(0.18, 0.15, 880, Waveform.Sine),
            new Note(0.36, 0.22, 1175, Waveform.Sine)),

        // 电子脉冲：1568Hz 方波快速六连，比较"刺"
        2 => BuildWave(1.00,
            new Note(0.00, 0.07, 1568, Waveform.Square),
            new Note(0.09, 0.07, 1568, Waveform.Square),
            new Note(0.18, 0.07, 1568, Waveform.Square),
            new Note(0.27, 0.07, 1568, Waveform.Square),
            new Note(0.36, 0.07, 1568, Waveform.Square),
            new Note(0.45, 0.07, 1568, Waveform.Square)),

        // 柔和钟声：523Hz 基音 + 高八度泛音，长衰减
        3 => BuildWave(1.60,
            new Note(0.00, 0.55, 523, Waveform.Bell),
            new Note(0.70, 0.55, 523, Waveform.Bell, Amplitude: 0.28)),

        // 蜂鸣警报：1000/750 交替，三组（警笛感）
        4 => BuildWave(1.50,
            new Note(0.00, 0.18, 1000, Waveform.Sine),
            new Note(0.18, 0.18, 750, Waveform.Sine),
            new Note(0.36, 0.18, 1000, Waveform.Sine),
            new Note(0.54, 0.18, 750, Waveform.Sine),
            new Note(0.72, 0.18, 1000, Waveform.Sine),
            new Note(0.90, 0.18, 750, Waveform.Sine)),

        _ => BuildAlarm(0),
    };

    private static byte[] BuildCountdown(int index) => index switch
    {
        // 清脆提示：单个 1046Hz 短"叮"
        0 => BuildWave(0.90,
            new Note(0.00, 0.28, 1046, Waveform.Bell)),

        // 双叮：1046 → 1318
        1 => BuildWave(1.10,
            new Note(0.00, 0.22, 1046, Waveform.Bell),
            new Note(0.20, 0.30, 1318, Waveform.Bell)),

        // 上升琶音：C5 E5 G5
        2 => BuildWave(1.20,
            new Note(0.00, 0.18, 523, Waveform.Sine),
            new Note(0.12, 0.18, 659, Waveform.Sine),
            new Note(0.24, 0.32, 784, Waveform.Sine)),

        // 水滴：高音快速下滑
        3 => BuildWave(0.90,
            new Note(0.00, 0.08, 1568, Waveform.Sine),
            new Note(0.10, 0.26, 1046, Waveform.Sine)),

        // 柔和和弦：C 大三和弦一起响，慢慢收
        4 => BuildWave(1.20,
            new Note(0.00, 0.70, 523, Waveform.Sine, Amplitude: 0.22),
            new Note(0.00, 0.70, 659, Waveform.Sine, Amplitude: 0.20),
            new Note(0.00, 0.70, 784, Waveform.Sine, Amplitude: 0.18)),

        _ => BuildCountdown(0),
    };

    // ---------------- 合成器 ----------------

    private enum Waveform
    {
        Sine,
        Square,
        Bell,
    }

    private readonly record struct Note(
        double StartSeconds,
        double DurationSeconds,
        double Frequency,
        Waveform Waveform,
        double Amplitude = 0.35);

    /// <summary>把若干音符混成一段 16-bit 单声道 PCM WAV（<paramref name="totalSeconds"/> 是最短总长）。</summary>
    private static byte[] BuildWave(double totalSeconds, params Note[] notes)
    {
        var length = Math.Max(totalSeconds, notes.Max(n => n.StartSeconds + n.DurationSeconds) + 0.06);
        var sampleCount = (int)Math.Ceiling(length * SampleRate);
        var buffer = new double[sampleCount];

        foreach (var note in notes)
        {
            var start = (int)(note.StartSeconds * SampleRate);
            var count = (int)(note.DurationSeconds * SampleRate);

            for (var i = 0; i < count && start + i < sampleCount; i++)
            {
                var t = (double)i / SampleRate;
                var phase = 2 * Math.PI * note.Frequency * t;

                var sample = note.Waveform switch
                {
                    // 方波软化成 tanh，别太刺耳
                    Waveform.Square => Math.Tanh(3.0 * Math.Sin(phase)),
                    // 钟声：基音 + 2.76 倍泛音（金属感），泛音衰减更快
                    Waveform.Bell => (Math.Sin(phase) * 0.85)
                        + (Math.Sin(phase * 2.76) * 0.15 * Math.Exp(-5.0 * t)),
                    _ => Math.Sin(phase),
                };

                buffer[start + i] += sample * note.Amplitude * Envelope(t, note.DurationSeconds);
            }
        }

        return ToWav(buffer);
    }

    /// <summary>起音 6ms + 指数衰减 + 收音 25ms，避免爆音。</summary>
    private static double Envelope(double t, double duration)
    {
        const double attack = 0.006;
        const double release = 0.025;

        var attackGain = Math.Min(1.0, t / attack);
        var releaseGain = Math.Min(1.0, Math.Max(0.0, (duration - t) / release));
        var decay = 0.25 + (0.75 * Math.Exp(-3.5 * t / Math.Max(0.05, duration)));

        return attackGain * releaseGain * decay;
    }

    /// <summary>double 采样 → 16-bit PCM WAV（带 44 字节头）。</summary>
    private static byte[] ToWav(double[] samples)
    {
        const int bitsPerSample = 16;
        const int channels = 1;
        var dataSize = samples.Length * channels * (bitsPerSample / 8);

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                                  // PCM 子块大小
        writer.Write((short)1);                            // PCM
        writer.Write((short)channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * (bitsPerSample / 8));  // 字节率
        writer.Write((short)(channels * (bitsPerSample / 8)));      // 块对齐
        writer.Write((short)bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1.0, 1.0);
            writer.Write((short)(clamped * short.MaxValue * 0.9));
        }

        writer.Flush();
        return stream.ToArray();
    }
}

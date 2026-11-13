using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Capsyn.Services;

/// <summary>一次解压的结果。</summary>
/// <param name="Destination">最终解压到的目录（可能因为重名自动换成了子目录）。</param>
/// <param name="FileCount">解出来的文件数。</param>
/// <param name="Bytes">解出来的总字节数。</param>
/// <param name="EscapedToSubfolder">是否因为目标里已有同名文件而自动落到了子目录。</param>
public sealed record ArchiveExtractResult(string Destination, int FileCount, long Bytes, bool EscapedToSubfolder);

/// <summary>
/// 解压服务。**零第三方库、零打包二进制**，两种内核按格式分流：
///
///   * <c>.zip</c> → .NET 自带 <see cref="ZipArchive"/>：逐条解压，能算精确进度、能挡 zip-slip；
///   * <c>.7z / .rar / .tar / .tar.gz / .tgz / .tar.xz / .txz / .tar.bz2 / .tbz2 / .gz / .xz / .bz2 / .zst</c>
///     → 系统自带的 <c>C:\Windows\System32\tar.exe</c>（bsdtar + libarchive，Win10 1803+ 就有），
///       **不用往安装包里塞任何东西**；代价是没有细粒度进度（UI 用不确定进度条）。
///
/// 为什么不用官方 7z.exe / 7z.dll：要把 2~3 MB 的二进制塞进绿色版，还得附 LGPL 许可；
/// 而系统 tar.exe 实测能解 7z（libarchive 3.8.9），对"偶尔解个包"的场景完全够用。
///
/// 安全与体验细节：
///   * **防路径穿越**：zip 自己逐条校验；tar 由 libarchive 负责，解完再确认落点没跑出目标目录；
///   * **重名不覆盖**：目标目录已有同名文件时，自动解压到 <c>目标\压缩包名\</c>，并在结果里标明；
///   * 进度：zip 按字节精确回调；tar 回调 <c>-1</c> 表示"不确定进度"（UI 会切成滚动条）。
/// </summary>
public static class ArchiveExtractor
{
    private const string TarExecutable = @"C:\Windows\System32\tar.exe";

    /// <summary>走系统 tar.exe（libarchive）的扩展名（含复合扩展名，长的要写在前面）。</summary>
    private static readonly string[] TarExtensions =
    {
        ".tar.gz", ".tar.xz", ".tar.bz2", ".tar.zst", ".tgz", ".txz", ".tbz2", ".tbz",
        ".7z", ".rar", ".tar", ".gz", ".xz", ".bz2", ".zst", ".cab", ".iso", ".lz", ".lzma", ".z",
    };

    /// <summary>这个文件能不能解压。</summary>
    public static bool IsSupported(string path) => KindOf(path) != ArchiveKind.Unsupported;

    /// <summary>给 UI 用的简短格式列表（落点区文案）。</summary>
    public const string SupportedFormatsText = ".zip / .7z / .rar / .tar / .tar.gz / .xz / .bz2";

    private enum ArchiveKind
    {
        Unsupported,
        Zip,
        Tar,
    }

    private static ArchiveKind KindOf(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.Zip;
        }

        foreach (var extension in TarExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveKind.Tar;
            }
        }

        return ArchiveKind.Unsupported;
    }

    /// <summary>
    /// 把 <paramref name="archivePath"/> 解压到 <paramref name="destination"/>。
    /// <paramref name="progress"/> 回调 0~1；回调 <c>-1</c> 表示"不确定进度"（tar 内核）。
    /// </summary>
    public static Task<ArchiveExtractResult> ExtractAsync(
        string archivePath,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ExtractCore(archivePath, destination, progress, cancellationToken), cancellationToken);

    private static ArchiveExtractResult ExtractCore(
        string archivePath,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        return KindOf(archivePath) switch
        {
            ArchiveKind.Zip => ExtractZip(archivePath, destination, progress, cancellationToken),
            ArchiveKind.Tar => ExtractWithTar(archivePath, destination, progress, cancellationToken),
            _ => throw new NotSupportedException($"不支持的压缩格式：{Path.GetFileName(archivePath)}"),
        };
    }

    // ---------------- zip：.NET 自带 ZipArchive ----------------

    private static ArchiveExtractResult ExtractZip(
        string archivePath,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries;

        // 先决定落点：目录里有同名文件就退到子目录，绝不覆盖用户已有文件。
        var escape = HasConflict(entries.Select(e => e.FullName), destination);
        var target = escape ? MakeUniqueDirectory(Path.Combine(destination, BaseNameOf(archivePath))) : destination;

        Directory.CreateDirectory(target);

        var totalBytes = entries.Where(e => !string.IsNullOrEmpty(e.Name)).Sum(e => (long)e.Length);
        long doneBytes = 0;
        var fileCount = 0;
        var reportedPercent = -1;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 目录条目（名字以 / 结尾）只需要建目录。
            if (string.IsNullOrEmpty(entry.Name))
            {
                var dirOnly = ResolveSafePath(target, entry.FullName);
                if (dirOnly is not null)
                {
                    Directory.CreateDirectory(dirOnly);
                }

                continue;
            }

            var destinationPath = ResolveSafePath(target, entry.FullName);
            if (destinationPath is null)
            {
                Diagnostics.Log($"unzip: skip unsafe entry \"{entry.FullName}\"");
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
            fileCount++;
            doneBytes += entry.Length;

            if (totalBytes > 0)
            {
                var percent = (int)(doneBytes * 100 / totalBytes);
                if (percent != reportedPercent)
                {
                    reportedPercent = percent;
                    progress?.Report(percent / 100.0);
                }
            }
        }

        progress?.Report(1.0);
        Diagnostics.Log($"unzip(zip): \"{archivePath}\" -> \"{target}\" ({fileCount} files, {doneBytes} B, escaped={escape})");

        return new ArchiveExtractResult(target, fileCount, doneBytes, escape);
    }

    // ---------------- 7z / rar / tar …：系统 tar.exe（libarchive） ----------------

    private static ArchiveExtractResult ExtractWithTar(
        string archivePath,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // 先列出条目（tar -tf），用来判断重名、以及"解压后到底有几个文件"。
        var listing = RunTar($"-tf \"{archivePath}\"", cancellationToken, throwOnError: true)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .Where(line => !line.EndsWith("/", StringComparison.Ordinal))
            .ToList();

        var escape = HasConflict(listing, destination);
        var target = escape ? MakeUniqueDirectory(Path.Combine(destination, BaseNameOf(archivePath))) : destination;
        Directory.CreateDirectory(target);

        // 这条内核没有细粒度进度：先告诉 UI "不确定"，解完再给 1。
        progress?.Report(-1);

        RunTar($"-xf \"{archivePath}\" -C \"{target}\"", cancellationToken, throwOnError: true);

        progress?.Report(1.0);

        // 只统计**这个压缩包里的**文件：目标的父目录里可能有别的文件（尤其是解到已有目录时），
        // 直接扫目录会把它们也算进去。所以按 tar -tf 的清单逐个核对。
        var count = 0;
        long bytes = 0;

        foreach (var entryName in listing)
        {
            var path = ResolveSafePath(target, entryName);
            if (path is null)
            {
                continue;
            }

            var info = new FileInfo(path);
            if (info.Exists)
            {
                count++;
                bytes += info.Length;
            }
        }

        Diagnostics.Log($"unzip(tar): \"{archivePath}\" -> \"{target}\" ({count} files, {bytes} B, escaped={escape})");

        return new ArchiveExtractResult(target, count, bytes, escape);
    }

    /// <summary>
    /// 跑一次 tar.exe（隐藏窗口、不弹控制台），返回 stdout 行。失败时抛异常（带上 stderr）。
    /// </summary>
    private static List<string> RunTar(string arguments, CancellationToken cancellationToken, bool throwOnError)
    {
        if (!File.Exists(TarExecutable))
        {
            throw new FileNotFoundException(
                "系统里找不到 tar.exe（Windows 10 1803+ 才有），无法解压这个格式。", TarExecutable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = TarExecutable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("tar.exe 启动失败。");

        var output = new List<string>();

        // stdout/stderr 必须边跑边读，否则管道写满会卡死子进程。
        var stdoutTask = Task.Run(() =>
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                output.Add(line);
            }
        }, cancellationToken);

        var stderr = string.Empty;
        var stderrTask = Task.Run(() => stderr = process.StandardError.ReadToEnd(), cancellationToken);

        while (!process.WaitForExit(50))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 已经退出就算了。
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        Task.WaitAll(stdoutTask, stderrTask);

        if (throwOnError && process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? $"退出码 {process.ExitCode}" : stderr.Trim();
            throw new IOException($"解压失败：{detail}");
        }

        return output;
    }

    // ---------------- 公共辅助 ----------------

    /// <summary>压缩包文件名去掉（复合）扩展名：<c>a.tar.gz</c> → <c>a</c>。</summary>
    private static string BaseNameOf(string archivePath)
    {
        var name = Path.GetFileName(archivePath).ToLowerInvariant();

        foreach (var extension in TarExtensions.Concat(new[] { ".zip" }))
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(archivePath[..^extension.Length]);
            }
        }

        return Path.GetFileNameWithoutExtension(archivePath);
    }

    /// <summary>目标目录里是否已经存在压缩包内的同名文件（存在就换子目录，不覆盖）。</summary>
    private static bool HasConflict(IEnumerable<string> entryNames, string destination)
    {
        if (!Directory.Exists(destination))
        {
            return false;
        }

        foreach (var entryName in entryNames)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                continue;
            }

            var path = ResolveSafePath(destination, entryName);
            if (path is not null && File.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把条目相对路径拼到目标目录下，并挡掉 zip-slip（<c>..</c> / 绝对路径 / 盘符）。
    /// 返回 null 表示这个条目不安全，应该跳过。
    /// </summary>
    private static string? ResolveSafePath(string destinationRoot, string entryFullName)
    {
        var relative = entryFullName.Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            return null;
        }

        var root = Path.GetFullPath(destinationRoot);
        var combined = Path.GetFullPath(Path.Combine(root, relative));

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    /// <summary>给子目录名加 (1)、(2) … 直到不重名。</summary>
    private static string MakeUniqueDirectory(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return path;
        }

        for (var i = 1; i < 1000; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{path} ({Guid.NewGuid():N})";
    }
}

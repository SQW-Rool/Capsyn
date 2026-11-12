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
/// 解压服务：只做 <c>.zip</c>，用 .NET 自带的 <see cref="ZipArchive"/>（不引入任何第三方库）。
///
/// 为什么先只做 zip：这是 Windows 资源管理器原生就能处理、也是最常见的分发格式；
/// 以后要支持 7z / rar / tar，可以改走系统自带的 <c>C:\Windows\System32\tar.exe</c>（libarchive），
/// 接口保持不变，只换实现。
///
/// 安全与体验细节：
///   * **防 zip-slip**：条目路径里出现 <c>..</c> 或绝对路径时，跳过该条目；
///   * **重名不覆盖**：目标目录已有同名文件时，自动解压到 <c>解压文件夹\压缩包名\</c>，
///     并在结果里标明（<see cref="ArchiveExtractResult.EscapedToSubfolder"/>）；
///   * 进度按「已解压字节 / 总字节」算，1% 粒度回调，不刷爆 UI。
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>这个文件能不能解压。</summary>
    public static bool IsSupported(string path)
        => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把 <paramref name="archivePath"/> 解压到 <paramref name="destination"/>。
    /// <paramref name="progress"/> 回调 0~1；<paramref name="cancellationToken"/> 可中途取消。
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
        using var archive = ZipFile.OpenRead(archivePath);

        // 先决定落点：目录里有同名文件就退到子目录，绝不覆盖用户已有文件。
        var escape = HasConflict(archive, destination);
        var target = destination;

        if (escape)
        {
            target = Path.Combine(destination, Path.GetFileNameWithoutExtension(archivePath));
            target = MakeUniqueDirectory(target);
        }

        Directory.CreateDirectory(target);

        var totalBytes = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Sum(e => (long)e.Length);
        long doneBytes = 0;
        var fileCount = 0;
        var reportedPercent = -1;

        foreach (var entry in archive.Entries)
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
        Diagnostics.Log($"unzip: \"{archivePath}\" -> \"{target}\" ({fileCount} files, {doneBytes} B, escaped={escape})");

        return new ArchiveExtractResult(target, fileCount, doneBytes, escape);
    }

    /// <summary>目标目录里是否已经存在压缩包内的同名文件（存在就换子目录，不覆盖）。</summary>
    private static bool HasConflict(ZipArchive archive, string destination)
    {
        if (!Directory.Exists(destination))
        {
            return false;
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var path = ResolveSafePath(destination, entry.FullName);
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

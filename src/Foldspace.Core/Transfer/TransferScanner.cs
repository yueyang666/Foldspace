using Foldspace.Core.Protocol;

namespace Foldspace.Core.Transfer;

/// <summary>任務在開始前就不能進行（例如拖入磁碟根目錄）。<see cref="Note"/> 說明原因。</summary>
public sealed class TransferRejectedException(JobNote note) : Exception(note.ToString())
{
    public JobNote Note { get; } = note;
}

public sealed record ScanEntry(string SourcePath, string RelativePath, bool IsDirectory, long Size, DateTime LastWriteUtc);

public sealed class ScanResult
{
    public required IReadOnlyList<OfferItem> Items { get; init; }
    /// <summary>前序排列：資料夾一定排在它的內容之前。</summary>
    public required IReadOnlyList<ScanEntry> Entries { get; init; }
    /// <summary>掃描時就略過的項目（符號連結、無權限）。</summary>
    public required IReadOnlyList<FileResult> Skipped { get; init; }
    public long TotalBytes => Items.Sum(i => i.Bytes);
    public int FileCount => Items.Sum(i => i.FileCount);
    public int DirectoryCount => Entries.Count(e => e.IsDirectory);
}

public static class TransferScanner
{
    public static ScanResult Scan(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (paths.Count == 0)
            throw new TransferRejectedException(new JobNote(JobIssue.NothingToSend));

        var items = new List<OfferItem>();
        var entries = new List<ScanEntry>();
        var skipped = new List<FileResult>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(rawPath);
            var trimmed = Path.TrimEndingDirectorySeparator(fullPath);

            if (string.Equals(Path.GetPathRoot(fullPath), fullPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetPathRoot(fullPath), trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new TransferRejectedException(new JobNote(JobIssue.DriveRoot));

            FileSystemInfo info = Directory.Exists(trimmed) ? new DirectoryInfo(trimmed) : new FileInfo(trimmed);
            var originalName = info.Name;
            if (!info.Exists)
            {
                skipped.Add(new FileResult(originalName, FileOutcome.Skipped, Issue: FileIssue.NotFound));
                continue;
            }
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                skipped.Add(new FileResult(originalName, FileOutcome.Skipped, Issue: FileIssue.ReparsePoint));
                continue;
            }

            // 不同來源的同名項目都會放到對方接收資料夾第一層，先在這裡錯開名稱。
            var topName = UniqueName(originalName, usedNames);
            usedNames.Add(topName);

            if (info is FileInfo file)
            {
                entries.Add(new ScanEntry(file.FullName, topName, false, file.Length, file.LastWriteTimeUtc));
                items.Add(new OfferItem { Name = topName, IsDirectory = false, Bytes = file.Length, FileCount = 1 });
            }
            else
            {
                var dir = (DirectoryInfo)info;
                var (bytes, count) = ScanDirectory(dir, topName, entries, skipped, ct);
                items.Add(new OfferItem { Name = topName, IsDirectory = true, Bytes = bytes, FileCount = count });
            }
        }

        if (items.Count == 0)
            throw new TransferRejectedException(new JobNote(JobIssue.NothingToSend)
            {
                FileIssue = skipped.FirstOrDefault()?.Issue,
                Path = skipped.FirstOrDefault()?.Path,
            });

        return new ScanResult { Items = items, Entries = entries, Skipped = skipped };
    }

    private static (long Bytes, int Count) ScanDirectory(
        DirectoryInfo root, string topName, List<ScanEntry> entries, List<FileResult> skipped, CancellationToken ct)
    {
        long bytes = 0;
        var count = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        // 用堆疊而不是遞迴，並維持前序：先放資料夾本身，再放它的內容。
        var stack = new Stack<(DirectoryInfo Dir, string Relative)>();
        stack.Push((root, topName));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, relative) = stack.Pop();
            entries.Add(new ScanEntry(dir.FullName, relative, true, 0, dir.LastWriteTimeUtc));

            List<FileSystemInfo> children;
            try
            {
                children = dir.EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skipped.Add(new FileResult(relative, FileOutcome.Skipped, Issue: FileIssue.FolderUnreadable));
                continue;
            }

            var subdirs = new List<(DirectoryInfo, string)>();
            foreach (var child in children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var childRelative = relative + "/" + child.Name;
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skipped.Add(new FileResult(childRelative, FileOutcome.Skipped, Issue: FileIssue.ReparsePoint));
                    continue;
                }

                if (child is DirectoryInfo childDir)
                {
                    subdirs.Add((childDir, childRelative));
                }
                else if (child is FileInfo childFile && !PathSafety.IsIgnoredFileName(childFile.Name))
                {
                    entries.Add(new ScanEntry(childFile.FullName, childRelative, false, childFile.Length, childFile.LastWriteTimeUtc));
                    bytes += childFile.Length;
                    count++;
                }
            }

            // 反向推入，讓子資料夾依名稱順序被處理。
            for (var i = subdirs.Count - 1; i >= 0; i--)
                stack.Push(subdirs[i]);
        }

        return (bytes, count);
    }

    internal static string UniqueName(string name, IReadOnlySet<string> used)
    {
        if (!used.Contains(name))
            return name;
        for (var n = 1; ; n++)
        {
            var candidate = NameWithCounter(name, n, isDirectory: false);
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    /// <summary><c>report.pdf</c> → <c>report (1).pdf</c>；資料夾 <c>Photos</c> → <c>Photos (1)</c>。</summary>
    public static string NameWithCounter(string name, int n, bool isDirectory)
    {
        if (isDirectory)
            return $"{name} ({n})";
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length == 0)
        {
            // ".gitignore" 這類只有副檔名的檔案
            stem = name;
            ext = "";
        }
        return $"{stem} ({n}){ext}";
    }
}

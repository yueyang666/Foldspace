using Serilog;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;

namespace Foldspace.Core.Transfer;

public sealed class FinalizeResult
{
    public List<FileResult> TopLevel { get; } = [];
    public List<FileResult> Problems { get; } = [];
    public int MovedFileCount { get; set; }
    /// <summary>第一個成功落地的頂層項目完整路徑。</summary>
    public string? FirstFinalPath { get; set; }
}

/// <summary>
/// 把暫存區內已驗證的檔案依同名策略移到接收資料夾。只以「拖入的頂層項目」判斷衝突。
/// </summary>
internal static class TransferFinalizer
{
    public static FinalizeResult Finalize(
        string tempJobDir,
        string receiveFolder,
        IReadOnlyList<OfferItem> items,
        ConflictPolicy policy,
        IReadOnlyDictionary<string, DateTime> directoryTimes,
        ILogger log)
    {
        var result = new FinalizeResult();

        foreach (var item in items)
        {
            var source = Path.Combine(tempJobDir, item.Name);
            var sourceExists = item.IsDirectory ? Directory.Exists(source) : File.Exists(source);
            if (!sourceExists)
                continue; // 這個項目沒有任何檔案通過驗證

            var destination = Path.Combine(receiveFolder, item.Name);
            try
            {
                var top = item.IsDirectory
                    ? FinalizeDirectory(source, destination, item, policy, result, log)
                    : FinalizeFile(source, destination, item, policy, result);
                result.TopLevel.Add(top);

                if (top.Outcome is not (FileOutcome.Skipped or FileOutcome.Failed))
                {
                    var finalPath = Path.Combine(receiveFolder, top.FinalName ?? item.Name);
                    result.FirstFinalPath ??= finalPath;
                    if (item.IsDirectory)
                        ApplyDirectoryTimes(item.Name, finalPath, directoryTimes);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning(ex, "Could not move {Name} into the receive folder", item.Name);
                var failed = new FileResult(item.Name, FileOutcome.Failed, Issue: FileIssue.MoveFailed);
                result.TopLevel.Add(failed);
                result.Problems.Add(failed);
            }
        }

        return result;
    }

    private static FileResult FinalizeFile(string source, string destination, OfferItem item, ConflictPolicy policy, FinalizeResult result)
    {
        var exists = File.Exists(destination) || Directory.Exists(destination);
        if (!exists)
        {
            File.Move(source, destination);
            result.MovedFileCount++;
            return new FileResult(item.Name, FileOutcome.Succeeded, item.Name);
        }

        switch (policy)
        {
            case ConflictPolicy.Skip:
            {
                var skipped = new FileResult(item.Name, FileOutcome.Skipped, Issue: FileIssue.SkippedExisting);
                result.Problems.Add(skipped);
                return skipped;
            }

            case ConflictPolicy.Overwrite when File.Exists(destination):
                try
                {
                    File.Move(source, destination, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var failed = new FileResult(item.Name, FileOutcome.Failed, Issue: FileIssue.OverwriteFailed);
                    result.Problems.Add(failed);
                    return failed;
                }
                result.MovedFileCount++;
                return new FileResult(item.Name, FileOutcome.Overwritten, item.Name);

            default:
            {
                // 自動改名；或「覆蓋」但同名的是資料夾（不能用檔案取代資料夾）。
                var renamed = UniqueDestination(destination, isDirectory: false);
                File.Move(source, renamed);
                result.MovedFileCount++;
                return new FileResult(item.Name, FileOutcome.Renamed, Path.GetFileName(renamed));
            }
        }
    }

    private static FileResult FinalizeDirectory(
        string source, string destination, OfferItem item, ConflictPolicy policy, FinalizeResult result, ILogger log)
    {
        var fileCount = CountFiles(source);
        if (!Directory.Exists(destination) && !File.Exists(destination))
        {
            Directory.Move(source, destination);
            result.MovedFileCount += fileCount;
            return new FileResult(item.Name, FileOutcome.Succeeded, item.Name);
        }

        if (policy == ConflictPolicy.Rename || File.Exists(destination))
        {
            var renamed = UniqueDestination(destination, isDirectory: true);
            Directory.Move(source, renamed);
            result.MovedFileCount += fileCount;
            return new FileResult(item.Name, FileOutcome.Renamed, Path.GetFileName(renamed));
        }

        // 覆蓋 / 略過：合併進既有資料夾，同名檔案依策略處理。
        Merge(source, destination, item.Name, policy, result, log);
        return new FileResult(item.Name, FileOutcome.Merged, item.Name);
    }

    private static void Merge(string sourceDir, string destDir, string relative, ConflictPolicy policy, FinalizeResult result, ILogger log)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(destDir, name);
            var rel = relative + "/" + name;

            if (Directory.Exists(target))
            {
                result.Problems.Add(new FileResult(rel, FileOutcome.Failed, Issue: FileIssue.FolderExists));
                continue;
            }

            if (File.Exists(target) && policy == ConflictPolicy.Skip)
            {
                result.Problems.Add(new FileResult(rel, FileOutcome.Skipped, Issue: FileIssue.SkippedExisting));
                continue;
            }

            try
            {
                File.Move(file, target, overwrite: true);
                result.MovedFileCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Information("Could not overwrite {Path} while merging: {Error}", rel, ex.Message);
                result.Problems.Add(new FileResult(rel, FileOutcome.Failed, Issue: FileIssue.OverwriteFailed));
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            var target = Path.Combine(destDir, name);
            var rel = relative + "/" + name;

            if (File.Exists(target))
            {
                result.Problems.Add(new FileResult(rel, FileOutcome.Failed, Issue: FileIssue.FileExists));
                continue;
            }

            if (!Directory.Exists(target))
            {
                Directory.Move(dir, target);
                result.MovedFileCount += CountFiles(target);
                continue;
            }

            Merge(dir, target, rel, policy, result, log);
        }
    }

    /// <summary>從最深的資料夾開始套用修改時間（之後再寫入子項目會改掉父資料夾的時間）。</summary>
    private static void ApplyDirectoryTimes(string topName, string finalTopPath, IReadOnlyDictionary<string, DateTime> times)
    {
        var prefix = topName + "/";
        var relevant = times
            .Where(kv => kv.Key == topName || kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(kv => kv.Key.Count(c => c == '/'));

        foreach (var (relative, time) in relevant)
        {
            var rest = relative.Length == topName.Length ? "" : relative[prefix.Length..];
            var path = rest.Length == 0 ? finalTopPath : Path.Combine(rest.Split('/').Prepend(finalTopPath).ToArray());
            try { Directory.SetLastWriteTimeUtc(path, time); }
            catch { /* 資料夾時間不影響結果 */ }
        }
    }

    internal static string UniqueDestination(string destination, bool isDirectory)
    {
        var folder = Path.GetDirectoryName(destination)!;
        var name = Path.GetFileName(destination);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(folder, TransferScanner.NameWithCounter(name, n, isDirectory));
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static int CountFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).Count();
}

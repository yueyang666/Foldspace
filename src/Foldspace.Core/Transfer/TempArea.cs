using Serilog;

namespace Foldspace.Core.Transfer;

/// <summary><c>&lt;接收資料夾&gt;\.foldspace-tmp\</c>：與正式位置在同一顆磁碟，移動只是改名。</summary>
public static class TempArea
{
    public const string FolderName = ".foldspace-tmp";

    public static string Root(string receiveFolder) => Path.Combine(receiveFolder, FolderName);

    public static string CreateJobDir(string receiveFolder, Guid jobId)
    {
        var root = Root(receiveFolder);
        var info = Directory.CreateDirectory(root);
        if (OperatingSystem.IsWindows() && !info.Attributes.HasFlag(FileAttributes.Hidden))
            info.Attributes |= FileAttributes.Hidden;
        return Directory.CreateDirectory(Path.Combine(root, jobId.ToString("N"))).FullName;
    }

    public static void DeleteJobDir(string path, ILogger log)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not delete temp folder {Path}", path);
        }
    }

    /// <summary>
    /// 刪除暫存區中沒有通過驗證的檔案。整理到接收資料夾前一定要先做：
    /// 接收資料夾裡永遠不能出現不完整的檔案，即使中斷時的清理沒有完成。
    /// </summary>
    public static int RemoveUnverified(string jobDir, IReadOnlySet<string> verifiedRelativePaths, ILogger log)
    {
        if (!Directory.Exists(jobDir))
            return 0;

        var removed = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var file in Directory.EnumerateFiles(jobDir, "*", options).ToList())
        {
            var relative = Path.GetRelativePath(jobDir, file).Replace(Path.DirectorySeparatorChar, '/');
            if (verifiedRelativePaths.Contains(relative))
                continue;
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 刪不掉（例如仍被開著）也不能讓它被移到接收資料夾：交給呼叫端中止整理。
                log.Error(ex, "Could not delete unverified temp file {Path}", relative);
                throw;
            }
        }
        return removed;
    }

    /// <summary>程式當掉或關機留下的暫存：啟動時清除超過 <paramref name="maxAge"/> 的子資料夾。</summary>
    public static int CleanStale(string receiveFolder, TimeSpan maxAge, ILogger log)
    {
        var root = Root(receiveFolder);
        if (!Directory.Exists(root))
            return 0;

        var removed = 0;
        foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (DateTime.UtcNow - dir.LastWriteTimeUtc < maxAge)
                continue;
            DeleteJobDir(dir.FullName, log);
            removed++;
        }
        if (removed > 0)
            log.Information("Removed {Count} stale temp folder(s)", removed);
        return removed;
    }
}

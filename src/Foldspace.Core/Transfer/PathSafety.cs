namespace Foldspace.Core.Transfer;

/// <summary>對方送來的路徑試圖寫到接收資料夾之外。整個任務必須立即中斷。</summary>
public sealed class SecurityViolationException(string message) : Exception(message);

public enum NameCheck
{
    Ok,
    /// <summary>Windows 保留名稱或結尾為空白/句點：略過該檔並回報。</summary>
    Unsupported,
}

/// <summary>
/// 協定中的相對路徑一律用 <c>/</c> 分隔，第一段必須是 OFFER 裡宣告的頂層名稱。
/// </summary>
public static class PathSafety
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    private static readonly char[] InvalidNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*', .. Enumerable.Range(0, 32).Select(i => (char)i)];

    /// <summary>
    /// 驗證並切分相對路徑。任何可能逃出接收資料夾的寫法都丟 <see cref="SecurityViolationException"/>。
    /// </summary>
    public static string[] SplitRelative(string relativePath, IReadOnlySet<string> allowedTopLevelNames)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath.Length > 32_000)
            throw new SecurityViolationException("Path is empty or too long");
        if (relativePath.Contains('\\') || relativePath.Contains(':') || relativePath.Contains('\0'))
            throw new SecurityViolationException($"Path contains forbidden characters: {relativePath}");
        if (relativePath.StartsWith('/'))
            throw new SecurityViolationException($"Absolute paths are not allowed: {relativePath}");

        var segments = relativePath.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                throw new SecurityViolationException($"Path contains an invalid segment: {relativePath}");
            if (segment.IndexOfAny(InvalidNameChars) >= 0)
                throw new SecurityViolationException($"Path contains forbidden characters: {relativePath}");
        }

        if (!allowedTopLevelNames.Contains(segments[0]))
            throw new SecurityViolationException($"Path is not part of this transfer: {relativePath}");

        return segments;
    }

    /// <summary>檔名能否在 Windows 上建立。不合法字元在 <see cref="SplitRelative"/> 已經擋掉。</summary>
    public static NameCheck CheckName(string name)
    {
        if (name.EndsWith(' ') || name.EndsWith('.'))
            return NameCheck.Unsupported;
        var stem = name.Split('.')[0].TrimEnd(' ');
        return ReservedNames.Contains(stem) ? NameCheck.Unsupported : NameCheck.Ok;
    }

    /// <summary>OFFER 中的頂層名稱：必須是單一合法檔名。</summary>
    public static bool IsValidTopLevelName(string name) =>
        !string.IsNullOrEmpty(name) && name != "." && name != ".." && name.Length <= 255 &&
        name.IndexOfAny(InvalidNameChars) < 0 && CheckName(name) == NameCheck.Ok;

    /// <summary>
    /// 把已驗證的片段組合到 <paramref name="baseDir"/> 下，最後再用完整路徑比對一次，
    /// 確保結果一定在 baseDir 裡面。
    /// </summary>
    public static string Combine(string baseDir, IReadOnlyList<string> segments)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(segments.Prepend(root).ToArray()));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison) || full.Length == root.Length)
            throw new SecurityViolationException($"Path resolves outside the receive folder: {string.Join('/', segments)}");
        return full;
    }

    public static string ToRelative(params IEnumerable<string> segments) => string.Join('/', segments);

    /// <summary>傳送端略過的系統產生檔。</summary>
    public static bool IsIgnoredFileName(string name) =>
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
}
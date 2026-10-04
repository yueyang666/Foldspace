namespace Foldspace.Core;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>340 MB、1.2 GB 這類顯示格式（1024 進位）。</summary>
    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : value >= 100 ? $"{value:0} {Units[unit]}" : $"{value:0.#} {Units[unit]}";
    }
}

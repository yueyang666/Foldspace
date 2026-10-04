namespace Foldspace.App.Platform;

/// <summary>Windows 內建圖示：桌面捷徑用的資料夾（imageres.dll 第 3 個）與系統的警告、資訊等圖示。</summary>
public static class ShellIcons
{
    public const int FolderIconIndex = 3;

    public static string FolderIconFile => Path.Combine(Environment.SystemDirectory, "imageres.dll");

    /// <summary>Windows 的系統圖示，例如 <see cref="StockIconId.Warning"/>。</summary>
    public static Bitmap Stock(StockIconId id, int size)
    {
        using var icon = SystemIcons.GetStockIcon(id, size);
        return icon.ToBitmap();
    }
}

using System.Drawing;

namespace Foldspace.App.Platform;

/// <summary>Foldspace 自己的圖示（Assets/Foldspace.ico，與 exe 檔案圖示相同），給系統匣與視窗用。</summary>
public static class AppIcon
{
    private static readonly byte[] Data = Load();

    /// <summary>含所有尺寸，給視窗用：標題列、Alt+Tab 會各自挑合適的尺寸。</summary>
    public static Icon Create() => new(new MemoryStream(Data));

    /// <summary>ico 裡最接近 <paramref name="size"/> 的那一張。</summary>
    public static Icon Create(int size) => new(new MemoryStream(Data), size, size);

    private static byte[] Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Foldspace.ico")
            ?? throw new InvalidOperationException("Embedded resource Foldspace.ico is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

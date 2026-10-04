using Foldspace.Core.Protocol;

namespace Foldspace.App;

/// <summary>版本資訊：產品版本給使用者看，建置版本給除錯用。</summary>
public static class AppInfo
{
    /// <summary>產品版本（例如 1.0.0）。</summary>
    public static string ProductVersion => ProtocolConstants.AppVersion;

    /// <summary>建置版本（例如 1.0.0+202610041530），可以分辨是哪一次建置。</summary>
    public static string BuildVersion => ProtocolConstants.BuildVersion;
}

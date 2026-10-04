using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Foldspace.App.Platform;

/// <summary>建立 .lnk 捷徑（桌面、開始功能表）。</summary>
public static class ShellLinkFile
{
    /// <param name="iconFile">沒有指定時用目標程式本身的圖示。</param>
    /// <param name="appUserModelId">
    /// 捷徑的 AppUserModelID。和程式設定的相同時，工作列與通知會用這個捷徑的名稱和圖示代表程式。
    /// </param>
    public static void Save(string path, string target, string arguments, string description,
        string? iconFile = null, int iconIndex = 0, string? appUserModelId = null)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            if (iconFile is not null)
                link.SetIconLocation(iconFile, iconIndex);
            link.SetDescription(description);
            if (arguments.Length > 0)
                link.SetArguments(arguments);
            if (appUserModelId is not null)
                SetAppUserModelId((IPropertyStore)link, appUserModelId);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void SetAppUserModelId(IPropertyStore store, string appUserModelId)
    {
        var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 }; // PKEY_AppUserModel_ID
        var value = new PropVariant { VarType = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(appUserModelId) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    /// <summary>PROPVARIANT（x64 為 24 bytes），這裡只用到字串。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

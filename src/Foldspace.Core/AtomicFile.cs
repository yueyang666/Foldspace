namespace Foldspace.Core;

/// <summary>先寫暫存檔再取代，避免斷電時留下寫一半的設定檔。</summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data);
            fs.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}

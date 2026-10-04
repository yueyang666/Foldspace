using System.Net.Sockets;

namespace Foldspace.Core.Net;

/// <summary>網路例外的一行英文說明。SocketException 的訊息來自作業系統、會是系統語言，日誌改用錯誤代碼。</summary>
public static class NetworkError
{
    public static string Describe(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException socket)
                return $"{ex.GetType().Name}: socket error {socket.SocketErrorCode} ({(int)socket.SocketErrorCode})";
        }
        return $"{ex.GetType().Name}: {ex.Message}";
    }
}

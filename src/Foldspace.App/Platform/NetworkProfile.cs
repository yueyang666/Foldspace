using System.Runtime.InteropServices;
using Serilog;

namespace Foldspace.App.Platform;

/// <summary>
/// 偵測目前連線的網路是否為「公用」設定檔（防火牆可能阻擋入站連線）。
/// 用 Network List Manager 的 COM 介面。介面明確宣告，不用 dynamic：dynamic 走 IDispatch，
/// 列舉網路時在部分環境會失敗。
/// </summary>
public static class NetworkProfile
{
    private const int NlmEnumNetworkConnected = 1;
    private const int NlmCategoryPublic = 0;

    public static bool IsAnyConnectedNetworkPublic()
    {
        try
        {
            var manager = (INetworkListManager)new NetworkListManager();
            try
            {
                var networks = manager.GetNetworks(NlmEnumNetworkConnected);
                while (networks.Next(1, out var network, out var fetched) == 0 && fetched == 1)
                {
                    var category = network.GetCategory();
                    Marshal.ReleaseComObject(network);
                    if (category == NlmCategoryPublic)
                        return true;
                }
                Marshal.ReleaseComObject(networks);
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read the network profile: {Error}", ex.Message);
        }
        return false;
    }

    // netlistmgr.h：三個介面都是 dual，.NET 會在宣告的方法前自動保留 IDispatch 的 4 個槽位。
    // 只宣告用得到的方法，但前面的方法數量必須和 vtable 一致。

    [ComImport]
    [Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
    private class NetworkListManager;

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        IEnumNetworks GetNetworks(int flags);
    }

    [ComImport]
    [Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworks
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNewEnum();

        [PreserveSig]
        int Next(int count, out INetwork network, out int fetched);
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        void GetName();
        void SetName();
        void GetDescription();
        void SetDescription();
        void GetNetworkId();
        void GetDomainType();
        void GetNetworkConnections();
        void GetTimeCreatedAndConnected();
        void IsConnectedToInternet();
        void IsConnected();
        void GetConnectivity();
        int GetCategory();
    }
}

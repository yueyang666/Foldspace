using Foldspace.Core.Net;
using Foldspace.Core.Settings;

namespace Foldspace.App.Platform;

/// <summary>配對結果存進 settings.json。</summary>
public sealed class SettingsPairingStore(Func<AppSettings> get, Action<Func<AppSettings, AppSettings>> update) : IPairingStore
{
    public string? PeerFingerprint => get().PeerFingerprint;
    public string? PeerHostname => get().PeerHostname;

    public void SavePairing(string fingerprint, string hostname) =>
        update(s => s with { PeerFingerprint = fingerprint, PeerHostname = hostname });

    public void ClearPairing() =>
        update(s => s with { PeerFingerprint = null, PeerHostname = null });

    public void UpdatePeerHostname(string hostname) =>
        update(s => s with { PeerHostname = hostname });
}

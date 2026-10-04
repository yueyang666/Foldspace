# Foldspace

[繁體中文](README.md)

Send files between two Windows PCs on the same local network: drag a file or folder onto "Foldspace" on your desktop, and it shows up in the receive folder on the other PC.

- **Works like a folder**: "Foldspace" on the desktop looks like an ordinary folder. Drop something on it to send it; folder structure is kept.
- **Pair once**: click "Pair…" to find the PCs on your network that are running Foldspace. Pick one, confirm that both show the same 6-digit code, and from then on each PC only trusts the other's device certificate. If the other PC's IP address changes, Foldspace finds it again automatically.
- **Secure**: the control connection always uses TLS with mutual certificate authentication. File data is encrypted by default (you can turn this off for speed).
- **No broken files**: every file is checked with XxHash3 and resent if it doesn't match. If a transfer stops, no partial files are left in the receive folder.
- **Lightweight**: a single exe with no installer. It sits in the notification area and shows the connection state by color. It doesn't need administrator rights, except for a UAC prompt when it adds or removes its firewall rule.
- **Three languages**: English, Traditional Chinese and Simplified Chinese, following the Windows display language.

## Requirements

- Windows 10 21H2 or later, or Windows 11, x64
- Both PCs on the same subnet and able to reach each other over TCP (port 52500 by default); discovery uses UDP 52500

## Getting started

1. Download `Foldspace-<version>-win-x64.exe` from [Releases](../../releases) and put a copy on each PC. You can check the file against the `.sha256` on the same page.
2. Run the exe.
   - The exe isn't code-signed, so Windows may show "Windows protected your PC". Choose "More info → Run anyway".
   - The first time, a UAC prompt appears. If you allow it, Foldspace adds a Windows Firewall rule that only lets computers on the same subnet connect, on both private and public networks.
3. In the settings window, click "Pair…", choose the other PC from the list, and confirm that both PCs show the same 6-digit code. If the other PC isn't found (for example, a guest Wi-Fi that blocks broadcasts), enter its IP address on the Connection tab instead.
4. Drag files or folders onto "Foldspace" on the desktop. Received files go to `Downloads\Foldspace` by default.

In the settings window you can choose how to handle files with the same name (rename, overwrite or skip), whether to ask before receiving, whether to encrypt, and whether to start with Windows.

## Uninstalling

Use Settings > Apps, or right-click "Foldspace" in the Start menu and choose "Uninstall". This removes the program, its settings, pairing, logs and shortcuts. Files in the receive folder are kept. Removing the firewall rules needs administrator rights, so a UAC prompt appears.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet test tests/Foldspace.Core.Tests
dotnet publish src/Foldspace.App -c Release
```

The single-file exe is written to `src/Foldspace.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/Foldspace.exe`. macOS and Linux can also build the Windows exe and run the tests, but can't run the app.

Architecture, protocol, file locations and design decisions are described in [docs/design.md](docs/design.md) (Traditional Chinese). Code comments are also in Traditional Chinese.

## License

Licensed under the [Apache License 2.0](LICENSE). © 2026 yueyang

Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

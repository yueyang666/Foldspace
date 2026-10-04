# Third-party notices

The published Foldspace exe is self-contained and includes the following components.
Each is distributed under its own license, linked below.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| .NET runtime and Windows Forms | 10.0 | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/winforms |
| System.IO.Hashing | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.ProtectedData | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| Serilog | 4.3.0 | Apache-2.0 | https://github.com/serilog/serilog |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | MIT | https://github.com/CommunityToolkit/WindowsCommunityToolkit |
| C#/WinRT runtime (WinRT.Runtime) | — | MIT | https://github.com/microsoft/CsWinRT |
| Windows SDK projection (Microsoft.Windows.SDK.NET) | 10.0.19041 | Microsoft Windows SDK license | https://aka.ms/WinSDKLicenseURL |

The .NET runtime itself includes further third-party code; see its
[THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

Development-only dependencies (xUnit and the test SDK) are not included in the published exe.

## Apache License 2.0 (Serilog, Serilog.Sinks.File)

Copyright © Serilog Contributors.
Licensed under the Apache License, Version 2.0: https://www.apache.org/licenses/LICENSE-2.0

## MIT License (.NET, Windows Community Toolkit, C#/WinRT)

Copyright © .NET Foundation and Contributors; Copyright © Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
THE USE OR OTHER DEALINGS IN THE SOFTWARE.

# Channel Ripper Setup Guide

## Tooling

For development and packaging on Windows:

- Visual Studio Community 2022/2026 with:
  - `.NET desktop development`
  - `Desktop development with C++`
  - `WinUI application development`
- `.NET 10 SDK`
- Windows 11 SDK

The repo currently builds successfully in the maintained no-calls path with:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\path\to\repo\Telegram\Telegram.csproj" /p:Configuration=Debug /p:Platform=x64 /p:EnableCalls=false
```

## Telegram API Credentials

Create `Telegram\Constants.Secret.cs` with your own Telegram API ID and hash:

```csharp
namespace Telegram
{
    public static partial class Constants
    {
        static Constants()
        {
            ApiId = your_api_id;
            ApiHash = "your_api_hash";
            AppChannel = "optional_update_channel";
        }
    }
}
```

## Current Supported Build Path

This fork is currently maintained in a no-calls configuration.

- Calls/WebRTC are not required for Channel Ripper.
- The app is built and packaged with `EnableCalls=false`.

## Recommended Build Commands

App build:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ".\Telegram\Telegram.csproj" /p:Configuration=Debug /p:Platform=x64 /p:EnableCalls=false
```

Package build:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ".\Telegram.Msix\Telegram.Msix.wapproj" /t:Build /p:Configuration=Debug /p:Platform=x64 /p:EnableCalls=false /p:SolutionDir="$PWD\\"
```

## Helper Script

You can also use:

```powershell
.\Scripts\Build-ChannelRipper-Package.ps1
```

That script builds the app, builds the package, and stages a transfer folder under:

`dist\ChannelRipper-Installer`

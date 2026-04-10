# Channel Ripper Install Guide

## Fastest Path For A Second Machine Tonight

Use the staged package folder:

`dist\ChannelRipper-Installer`

Copy that folder to the second machine, then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install-ChannelRipper-Package.ps1
```

That wrapper script:

- imports the signing certificate into `CurrentUser\TrustedPeople`
- removes an older installed package with the same identity
- installs the `msixbundle` and x64 dependencies

## If You Want To Use The Generated Visual Studio Package Folder Directly

You can also copy one of these folders:

- `Telegram.Msix\AppPackages\Telegram.Msix_12.3.5.0_Debug_Test`
- `Telegram.Msix\AppPackages\Telegram.Msix_12.3.5.0_Test`

Then run the included:

```powershell
.\Install.ps1
```

## Notes

- The currently validated install flow is based on the packaged app, not launching `Telegram.exe` directly.
- Reinstalling the same package identity may require removing the old package first.
- For tonight, the simplest safe approach is copying the staged installer folder and running the wrapper installer script.

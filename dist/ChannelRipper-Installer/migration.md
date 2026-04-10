# Migration Notes

## Immediate next steps

### Validate the freshly packaged build

The current package was freshly rebuilt, packaged, signed, and reinstalled from the latest source.

Validation artifacts:

- `codex-app-build.log`
- `codex-package.log`

Fresh package folder:

- `Telegram.Msix\AppPackages\Telegram.Msix_12.3.5.0_Debug_Test`

### Move to a second machine tonight

Copy this folder to the second machine:

- `dist\ChannelRipper-Installer`

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install-ChannelRipper-Package.ps1
```

That staged installer folder includes:

- the packaged `msixbundle`
- dependency packages
- the exported certificate
- the wrapper installer script

### Confirm Channel Ripper behavior

On the fresh install, verify:

1. login works
2. `Channel Ripper` opens
3. `Folder` can be set
4. non-forum targets queue and download
5. forum targets can:
   - refresh topics
   - add/remove topics
   - filter topics
6. `Start / Pause` works
7. output layout and dedupe mode selectors work

## Documentation map

The current Channel Ripper documentation is split into:

- `Documentation\Channel-Ripper.md`
- `Documentation\Channel-Ripper-User-Guide.md`
- `Documentation\Channel-Ripper-Setup.md`
- `Documentation\Channel-Ripper-Install.md`
- `Documentation\Channel-Ripper-Branding.md`

## Branding guidance

Low-risk customization path:

1. change visible app name
2. change logos and tiles
3. keep package identity unchanged for now

Relevant files:

- `Telegram.Msix\Package.appxmanifest`
- `Telegram\Assets\Logos`

Package identity changes are possible later, but they are more invasive because they affect install/update identity and app data location.

## Recommended follow-up work

After confirming the second-machine install:

1. switch to a cleaner release-oriented package workflow
2. evaluate whether to rename the visible app branding
3. optionally add more UI polish:
   - compact mode
   - sticky controls
   - safer splitter/resizer behavior if needed

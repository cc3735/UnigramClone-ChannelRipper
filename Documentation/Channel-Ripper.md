# Channel Ripper

Channel Ripper is the archive-focused part of this fork.

It continuously downloads media from Telegram chats, channels, and forum topics using TDLib's normal media download pipeline instead of chat export.

## Documentation Map

- User guide: `Documentation/Channel-Ripper-User-Guide.md`
- Setup guide: `Documentation/Channel-Ripper-Setup.md`
- Install guide: `Documentation/Channel-Ripper-Install.md`
- Branding notes: `Documentation/Channel-Ripper-Branding.md`

## Current Feature Summary

- forum topic targeting inside Channel Ripper
- add/remove topics directly from the ripper UI
- topic filtering by name or ID
- output layout modes
- dedupe modes
- per-target queue/progress/status
- packaged install workflow for another machine

## Fast/Premium Download Path

Channel Ripper uses TDLib `DownloadFile` / `DownloadFileAsync`, the same general path used by Telegram auto-download.

- It is not chat export.
- It is not the slow export pipeline.
- Premium speed behavior is still controlled by Telegram's account/server policy.

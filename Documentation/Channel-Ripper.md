# Channel Ripper

## What It Does

Channel Ripper continuously archives photos and videos from chats/channels you add.

- It performs a full reconciliation scan for enabled targets on app start, then keeps running for new posts while the app stays open.
- It stores files under a custom root folder with chat/topic/date organization.
- It deduplicates globally by Telegram `file.remote.unique_id`.
- It does not re-download media that is already present in the persistent on-disk ledger, even if files were moved to other drives later.

## Why It Is Fast

Channel Ripper uses TDLib `DownloadFile` / `DownloadFileAsync`, which is the same pipeline used by Telegram auto-download.

- It does not use chat history export.
- Premium speed behavior is still controlled by Telegram account/server policy.

## Setup

1. Open `Channel Ripper` from the main page button.
2. Click `Folder` and choose the rip root folder.
3. Optional: click `Backup` to choose a backup folder for ledger snapshots.
4. Click `Add target` to add a chat/channel.
5. For forum channels, enter topic IDs (comma-separated) when prompted.
6. Click `Start / Pause` to run workers.

## Progress and Queue Visibility

The `Channel Ripper` popup shows both global and per-target status:

- Global line: queue size, active workers, downloaded, skipped, failed.
- Per target:
  - backfill state (`running` or `idle`)
  - progress bar (processed vs remaining in current known queue)
  - queue/active/downloaded/skipped/failed counters for that chat/channel.

## Per-Chat Quick Toggle

Inside a chat/channel menu:

- `Start Ripping` adds/enables ripper for this chat.
- `Stop Ripping` disables ripper for this chat.

For forums, the first start asks for topic IDs.

## Folder and Naming Scheme

Saved path:

`<root>/<chat-title>/<topic-or-General>/<yyyy-MM-dd>/<yyyyMMdd_HHmmss>_<messageId>_<fileId>.<ext>`

## Deduplication and Moved Storage

- Ledger key: Telegram `file.remote.unique_id`.
- If a file is in ledger, it is treated as already downloaded forever.
- Path existence is not used for duplicate decisions.
- Moving media files to another drive does not trigger re-download.

## Reset Behavior

`Reset download history` on a target clears only that target's scan history.

- It allows intentional re-scan of older messages for that target.
- Global unique-id ledger still prevents duplicate file fetches for media already seen elsewhere.

## Notes and Limits

- v1 scope: photos, videos, animations, video notes, and video-like documents.
- Default workers: 4.
- Default retry attempts: 5 (exponential backoff).
- Targets must be chats/channels accessible from the logged-in account.

## Troubleshooting

- If status reports root folder errors, set `Folder` again.
- If the ledger file is corrupted, backup snapshot recovery is attempted on startup.
- If downloads do not speed up, verify account premium state and network constraints.

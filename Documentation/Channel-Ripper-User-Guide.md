# Channel Ripper User Guide

## What Channel Ripper Does

Channel Ripper continuously archives media from Telegram chats, channels, and forum topics using TDLib's normal `DownloadFile` pipeline.

- It is not chat export.
- It follows the same general download path as normal Telegram media downloads.
- If your Premium account gets faster media downloads in-app, Channel Ripper is using that same path.

## Core Workflow

1. Open `Channel Ripper`.
2. Click `Folder` and choose the archive root.
3. Click `Add channel target`.
4. Pick a channel or group.
5. For forum chats:
   - expand the target with `Show topics`
   - click `Refresh topics`
   - use `Add topic` and `Remove topic` to define the exact topic set
6. Click `Start / Pause` to start or pause the worker pool.

## Topic Workflow

Forum targets can be managed directly in Channel Ripper:

- `Show topics` / `Hide topics`
- `Refresh topics`
- `Add topic`
- `Remove topic`
- topic filter box for finding topics by name or ID

You can also start a topic target directly from inside a forum topic thread:

- open the topic
- use chat menu -> `Start ripping this topic`

## Status and Counters

The top status line shows:

- `Queue`
- `Active`
- `Downloaded`
- `Skipped`
- `Failed`

Each target also shows:

- whether backfill is running
- per-target queue and activity counts
- last error text if something failed

## Output Layout Modes

You can choose one of three output layouts:

- `Channel -> Topic -> Date`
- `Channel -> Topic`
- `Channel only`

## Dedupe Modes

You can choose one of three dedupe modes:

- `Global dedupe`
- `Per-chat dedupe`
- `Per-topic dedupe`

## Good Operating Pattern

For large archive sessions:

1. set the archive `Folder` first
2. add a few targets
3. use `Activity first` sorting
4. enable `Show active targets only` when monitoring live progress
5. expand only the forum target you are actively editing

## Troubleshooting

- If a forum target has no topics yet, click `Refresh topics`.
- If a target is present but idle, confirm it is `Enabled` and the global ripper is started.
- If `Root: (not set)` appears, pick the archive folder again.
- If you want to rescan older messages for one target, use `Reset download history`.

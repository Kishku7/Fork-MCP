# Fork v1.3.0 — Re-attach State Sync & Periodic Polling

**Status:** Approved — implementation in progress  
**Approved:** 2026-05-19

---

## Overview

When Fork re-attaches to a Minecraft server that was left running under ForkGuard, it currently has no reliable way to populate its in-memory player/role lists. This release adds:

1. A full state sync on re-attach (commands + file reads)
2. Log backfill so the console shows recent history
3. Re-enabled per-line event detection (new lines only, not backfilled history)
4. A configurable periodic poll cycle (save-all + re-sync)

---

## Step 1 — Re-attach State Sync (new service)

**Trigger:** Called from `ServerLifecycleManager.TryReattach()` after the named pipe connects and the log tailer starts.

**New class:** `logic/service/ReattachSyncService.cs`  
**Method:** `public static async Task SyncAsync(ServerViewModel viewModel)`

### Actions

**Via named pipe (commands sent, responses parsed from log tailer):**
- `list` → parse `"There are X of a max of Y players online: name1, name2, ..."` → populate `PlayerList`
- `whitelist list` → parse `"There are X whitelisted players: ..."` or `"There are no whitelisted players"` → populate `WhiteList`
- `banlist` → parse multi-line ban list output → populate `BanList`
- `banlist ips` → parse multi-line IP ban list output (stored separately or merged)

**Via file read (direct, no command needed):**
- Read `ops.json` from `{ServerPath}/{ServerName}/ops.json`
- Deserialize array of `{ uuid, name, level, bypassesPlayerLimit }` → populate `OPList`
- Cross-reference online players from `list` against `OPList` to set `ServerPlayer.IsOP`

**Notes:**
- Commands are sent via `ConsoleReader.Read(command, viewModel)` — existing pipe relay path
- Log response parsing needs a short await window (e.g., 2s timeout per command) for the response to appear in the tailer
- `op list` does NOT work on Fabric — confirmed live: it tries to op a player named "list". Use `ops.json` only.

---

## Step 2 — Log Backfill (LogTailConsoleWriter)

**Current behavior:** `fs.Seek(0, SeekOrigin.End)` — seeks to end immediately, no history shown.

**New behavior:**
1. Before entering the tail loop, read backwards from end of file
2. Collect up to `AppSettings.MaxConsoleLines` lines (the configured scrollback buffer — currently 100,000)
3. Add collected lines to the console view (oldest first)
4. Scroll the UI to the bottom so the most recent lines are visible
5. Set `_catchupComplete = true`
6. Continue tailing forward from current position

**Implementation notes:**
- Reading backwards in a StreamReader efficiently requires seeking to blocks from the end and scanning for newlines
- Alternative: read entire file into a list, take `TakeLast(MaxConsoleLines)`, then display — simpler and acceptable for log sizes
- Must not call `RoleInputHandler` on any backfilled lines (see Step 3)

---

## Step 3 — RoleInputHandler (new lines only)

**Current state:** `RoleInputHandler` call commented out in `LogTailConsoleWriter` (disabled for testing, 2026-05-19).

**New behavior:**
- `LogTailConsoleWriter` gets a `bool _catchupComplete` field, initialized to `false`
- Set to `true` after Step 2 backfill is complete
- In the tail loop, only call `viewModel.RoleInputHandler(line)` when `_catchupComplete == true`
- Backfilled lines are added to console view only — no event processing

**Why:** Avoids re-triggering whitelist/op/ban parse events on historical log entries that are already loaded via Step 1.

---

## Step 4 — Periodic Poll (configurable timer)

**New setting:** `AppSettings.ReattachPollIntervalMinutes` (default: 15, 0 = disabled)

**New class:** `logic/service/PeriodicSyncService.cs`  
(or inner timer on `ServerLifecycleManager` / `ServerViewModel`)

### Cycle

1. Skip if `viewModel.CurrentStatus != ServerStatus.RUNNING`
2. Send `save-all` via `ConsoleReader`
3. Await `"Saved the game"` log confirmation from the tailer (with timeout, e.g., 30s)
4. Call `ReattachSyncService.SyncAsync(viewModel)` — same sync as Step 1

**Notes:**
- Timer starts after successful re-attach, stopped when server stops
- Interval is per-server (stored in server settings, not global)
- `save-all` confirmation uses a `TaskCompletionSource<bool>` set by the log tailer when it sees the "Saved the game" line — no fixed sleep
- If `save-all` confirmation times out, proceed with sync anyway (don't block indefinitely)

---

## Files to Create / Modify

| File | Action |
|------|--------|
| `logic/service/ReattachSyncService.cs` | **New** — state sync logic |
| `logic/service/PeriodicSyncService.cs` | **New** — timer + poll cycle |
| `logic/CustomConsole/LogTailConsoleWriter.cs` | **Modify** — add backfill + `_catchupComplete` flag + re-enable `RoleInputHandler` for new lines |
| `logic/manager/ServerLifecycleManager.cs` | **Modify** — call `ReattachSyncService.SyncAsync` after `TryReattach` succeeds; start `PeriodicSyncService` |
| `logic/model/AppSettings.cs` | **Modify** — add `ReattachPollIntervalMinutes` |
| `Fork.csproj` | **Modify** — version bump to 1.3.0 |
| `Properties/AssemblyInfo.cs` | **Modify** — version bump to 1.3.0 |

---

## Version

`1.2.5` → `1.3.0` (minor bump — new feature set, no breaking changes)

---

## What Does NOT Change

- `ops.json` is never written by Fork — read-only
- `RoleUpdater.InitializeList` still runs at startup (non-reattach path) as before
- The named pipe relay in `GuardedConsoleReader` is unchanged
- ForkGuard itself is unchanged

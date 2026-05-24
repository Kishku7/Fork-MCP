# Fork-MCP

**A heavily enhanced fork of [Fork by ForkGG](https://github.com/ForkGG/Fork) — the Minecraft server manager for Windows.**

Fork-MCP takes the solid foundation of Fork and adds a suite of major features focused on automation, resilience, and AI integration. If you've used Fork before, you'll feel right at home — and then notice everything that's better.

---

## What's New in Fork-MCP

### 🛡️ ForkGuard — Re-attachable Servers

The biggest change. In stock Fork, if the Fork application crashes or is restarted, any running Minecraft servers are orphaned — Fork has no way to reconnect to them, and you're left with a blind server you can't manage.

Fork-MCP introduces **ForkGuard**, a lightweight NativeAOT guardian process that runs alongside Java. ForkGuard:

- Holds the Java process inside a **Windows Job Object** so it can't outlive its guardian
- Writes a **marker file** (`fork-guard.marker`) to the server directory containing the pipe name and Java PID
- Exposes a **named pipe** that forwards stdin commands directly into Java
- Logs all Java stdout/stderr to `fork-guard.log` for diagnostics

When Fork restarts, it detects the marker file, confirms Java is still alive, and **re-attaches automatically** — no server restart required, no lost sessions, no blind running processes.

### 🔄 Re-attach State Sync

When Fork re-attaches to a running server, it doesn't just show you a blank console. It:

- **Backfills the console** with up to the configured scrollback limit (default: 100,000 lines) from `logs/latest.log` so you see recent history immediately
- **Queries the live server** with `list`, `whitelist list`, `banlist`, and `banlist ips` to populate the player and role lists in real time
- **Reads `ops.json`** directly for operator data (the only reliable source on Fabric/Vanilla — there is no `op list` command)
- **Re-enables per-line event detection** (join/leave, whitelist, ban, op events) from the point of re-attach forward — historical lines are never re-processed

### ⏱️ Periodic State Sync

Fork-MCP adds a configurable background polling cycle (default: every 15 minutes). Each cycle:

1. Sends `save-all` to the server and waits for the `"Saved the game"` confirmation
2. Re-syncs all player and role lists

This keeps Fork's UI accurate even after extended uptime without manual intervention. The interval is configurable in app settings (`ReattachPollIntervalMinutes`), and the cycle is skipped if the server is not in a RUNNING state.

### 🤖 Built-in MCP Server

Fork-MCP embeds a full **Model Context Protocol (MCP) server** over Streamable HTTP, making it possible to control your Minecraft servers directly from AI tools like Claude.

| Tool | Description |
|------|-------------|
| `list_servers` | List all servers and their current status |
| `get_status` | Status, CPU, memory, disk, player count |
| `get_console` | Recent console output (up to 500 lines) |
| `send_command` | Send a console command |
| `start_server` | Start a stopped server |
| `stop_server` | Stop a running server |
| `restart_server` | Restart (starts if stopped) |
| `list_players` | All known players (online and offline) with OP status |
| `get_setting` | Read a `server.properties` key |
| `set_setting` | Write a `server.properties` key |
| `list_plugins` | List plugins with enabled/disabled state |
| `install_plugin` | Install from Spiget resource ID |
| `enable_plugin` | Enable a disabled plugin |
| `disable_plugin` | Disable a plugin |

The MCP server binds to `0.0.0.0:19475` by default and supports optional Bearer token authentication. Configure the IP, port, and token in Fork's app settings.

### ⚡ Console Performance Overhaul

The original Fork rendered every console line as a live WPF element — on modded servers with spammy output this caused severe GUI lag and effectively made the console unusable above a few thousand lines.

Fork-MCP rewrites the console pipeline:

- **VirtualizingStackPanel with recycling** — only ~25 rows are rendered at any time, regardless of total line count
- **`InvokeAsync` instead of `Invoke`** — the background reader thread never blocks on a UI layout pass
- **O(1) duplicate detection** — a mirrored `HashSet<ConsoleMessage>` replaces the O(n) `Contains` check on the backing list
- **100,000 line scrollback buffer** — up from the original 1,000 line cap
- **Configurable throttle** — `MaxConsoleLinesPerSecond` caps throughput to protect UI responsiveness during log floods

The console can now keep up with even the most verbose modpacks without frame drops.

### 🎨 Redesigned Server List

The server list sidebar has been redesigned from the ground up:

- **Compact row height** — more servers visible at a glance without scrolling
- **Cleaner typography** — server name, type, and version shown in a tighter, more readable layout
- **Named action buttons** — buttons are clearly labelled (Start, Stop, Restart, Kill) instead of unlabelled icon-only controls, so the interface is immediately understandable without hover tooltips
- **Consistent hover states** — monochrome icon variants and state-aware button colours make the active server and available actions obvious at a glance
- **Network list redesigned separately** — network entries get their own layout distinct from single servers

### 🔧 Settings Reorganised

The settings page has been broken into focused sections rather than one long scrolling form:

- **General** — server path, Java path, system tray behaviour
- **Java & Networking** — RAM, startup parameters, port, MOTD, resource pack
- **Server Appearance** — server icon selection
- **Advanced** — MCP server, Discord bot, telemetry, rich presence

### 🛠️ Reliability Fixes

A number of crashes and race conditions present in the upstream codebase have been resolved:

- **Thread pool starvation** — `Thread.Sleep` inside `Task.Run` in the role input handler was blocking pool threads for every log line. Replaced with `Task.Delay` so threads are released while waiting.
- **Per-player task explosion** — player list initialisation previously spawned a `Task.Run` call per player during startup, creating hundreds of tasks on servers with large player histories. Collapsed to a single sort after the full list loads.
- **GDI+ startup crash** — servers configured in Fork but with no server directory on disk caused `Image.FromFile` to throw an unhandled `ExternalException` inside `MainWindow..ctor()`, preventing the WPF window from ever opening. Fork-MCP guards all icon I/O against missing directories.
- **Singleton initialisation deadlock** — the original re-attach code called `TryReattach` synchronously during the `ServerManager` constructor, which set `CurrentStatus` (firing `PropertyChanged`), which triggered a lambda referencing `ApplicationManager.Instance` before it existed, causing infinite recursion and a silent crash. Fixed with `Dispatcher.BeginInvoke` to defer re-attach until after the full initialisation chain completes.

### 📦 Cleaner Codebase

- **`EntityViewModel` split into partials** — console I/O, icon handling, and performance tracking are each in their own file
- **`ServerLifecycleManager` extracted** — process start/stop/restart/kill logic separated from the entity collection coordinator (`ServerManager`)
- **`WorldManager` extracted** — world import/create/delete separated from the coordinator
- **`IConsoleReader` interface** — `ConsoleReader` (direct stdin) and `GuardedConsoleReader` (named pipe) share a common interface; the rest of the app doesn't need to know which one is in use
- **`Directory.Build.props`** — `AnalysisMode=All`, `TreatWarningsAsErrors=true`, Meziantou.Analyzer enforced across all projects

---

### 🩺 Health Endpoint

Fork-MCP now exposes a `GET /health` endpoint on the MCP server port. Returns current application status, server count, and uptime. Useful for monitoring and automated health checks.

### 🔒 Server Survival on Fork Exit (ForkGuard Fix A)

Previously, closing the Fork application would terminate the Job Object and kill all managed Minecraft servers. Fork-MCP now detaches from the Job Object on clean exit — servers keep running, guarded by ForkGuard, ready to re-attach next time Fork starts.

### 🔁 TryReattach Hardening (ForkGuard Fix B)

Re-attach is now more resilient: better handling of edge cases where ForkGuard's named pipe is slow to appear, improved timeout behaviour, and cleaner error reporting when re-attach fails.

### ☕ Per-Server Java Version Selection

Fork-MCP adds a Java version discovery service and per-server version selector. On startup it scans for installed Java runtimes in priority order: Modrinth app data, Eclipse Adoptium, `C:\Program Files\Java`, and an optional extra directory configured in app settings. Each server can then select a specific major version (Auto, 8, 11, 17, 21, 25) from a dropdown — Fork picks the highest available patch release for that major at launch time. The extra directory and a manual reload button are available in App Settings → Java Installations.

### 👥 Accurate Player Tracking

Player list accuracy has been significantly improved across multiple areas:

- **Race condition fix** — `SyncOnlinePlayersAsync` previously ran before `InitializeLists` finished resolving player UUIDs (which involves Mojang API calls). The result was an empty `PlayerList` at sync time, so online players were silently skipped. The sync now waits for `Initialized` before querying the server.
- **Unknown player recovery** — When a player appears in the `list` response but isn't yet in `PlayerList` (e.g. first join, or connected before Fork attached), they are now fetched from `PlayerManager` and added automatically rather than ignored.
- **Real-time join/leave tracking** — `RoleInputHandler` now parses `joined the game` and `left the game` console events and updates `IsOnline` immediately. Players are added to `PlayerList` on first join even if they weren't present at attach time.
- **Startup player list — Minecraft 26.x compatibility** — Minecraft 26.x moved player data from `world/playerdata/` to `world/players/data/`, and moved region data from the world root into `dimensions/`. The old scanner checked `playerdata/` only and relied on world directory validation (which requires a `region/` folder) — both assumptions broke silently on modern servers, leaving the player list empty until someone logged in. Fork-MCP now scans the server directory directly up to two levels deep, checks both path patterns, and no longer depends on world validation to discover player files.
- **usercache.json as name resolver** — Player names are now resolved from the server's `usercache.json` (written by Minecraft on every connect) instead of the Mojang session API, eliminating cold-start network calls and making player list population instantaneous.
- **`list_players` shows all known players** — Previously only showed currently online players. Now groups all known players as Online / Known offline, with OP status shown for both.

## Version History

| Version | Highlights |
|---------|------------|
| 1.5.3 | Player list Online/Offline sections in UI; 8 new 26.x server.properties typed; Custom Properties section for unknown keys; Automation sidebar rename |
| 1.5.2 | Startup player list fix for Minecraft 26.x — new path scan, usercache resolver, list_players shows all known players |
| 1.5.1 | Per-server Java version selection — discovery service, major version dropdown, bump-version.ps1 corrected |
| 1.5.0 | Accurate online player tracking — race condition fix, unknown player recovery, real-time join/leave |
| 1.4.0 | Health endpoint, server survival on Fork exit (ForkGuard Fix A), TryReattach hardening (Fix B), WindowStyle cleanup |
| 1.3.0 | Re-attach state sync, log backfill, periodic polling, RoleInputHandler gated post-backfill |
| 1.2.5 | ForkGuard integration, thread explosion fix, GDI+ crash fix, startup deadlock fix, log tailer |
| 1.2.0 | Console virtualisation, 100k line buffer, async dispatcher pipeline |
| 1.1.0 | Built-in MCP server (14 tools, Streamable HTTP, optional auth) |
| 1.0.0 | Fork-MCP fork baseline — .NET 8 upgrade, namespace cleanup, settings redesign, server list redesign, named buttons |

---

## Requirements

- Windows 10 / Windows Server 2019 or later
- .NET 8 Desktop Runtime
- Java 17 or later (for Minecraft 1.17+)

---

## Based On

Fork-MCP is a fork of **[Fork by ForkGG](https://github.com/ForkGG/Fork)**, originally created by Christian Kerner and contributors, licensed under the MIT License. All upstream changes are made independently; this project is not affiliated with or endorsed by the original Fork project.

---

## License

MIT — see [LICENSE](LICENSE)

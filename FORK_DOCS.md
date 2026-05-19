# Fork Legacy — Codebase Documentation
**Version:** 0.8.10 | **Stack:** C# / WPF / .NET 8 | **Pattern:** MVVM

---

## Overview

Fork is a Windows WPF desktop application that acts as a GUI wrapper and manager for Minecraft servers. It handles server creation, downloading server JARs, starting/stopping processes, player management, plugin management, automated scheduling, and settings editing — all without the user needing to touch the command line.

The application is structured in three clean layers: **logic** (model + business logic), **ViewModel** (binding layer), and **View** (XAML UI). All managers are singletons.

---

## Data Storage Locations

| What | Path |
|------|------|
| App data root | `%AppData%\Fork\` |
| Server list (JSON) | `%AppData%\Fork\persistence\entities.json` |
| App settings (JSON) | `%AppData%\Fork\persistence\appsettings.json` |
| Player cache | `%AppData%\Fork\players\players.json` |
| Vanilla version cache | `%AppData%\Fork\persistence\vanilla-release.json`, `vanilla-snapshot.json` |
| Error log | `%AppData%\Fork\logs\` |
| Servers folder (default) | `%AppData%\Fork\servers\` (configurable) |
| Temp files | `%AppData%\Fork\tmp\` |

Servers are stored under `{ServerPath}\{ServerName}\` — the server path is configurable in app settings.

---

## Solution Structure

```
Fork.sln
├── App.xaml / App.xaml.cs          ← Entry point
├── Settings.cs                     ← .NET settings stub (mostly empty hooks)
├── logic/                          ← All business logic
│   ├── manager/                    ← Singleton managers (core of the app)
│   ├── model/                      ← Data models
│   ├── persistence/                ← File I/O
│   ├── WebRequesters/              ← HTTP/API clients
│   ├── Controller/                 ← API + Network controllers
│   ├── BackgroundWorker/           ← Performance trackers + query worker
│   ├── CustomConsole/              ← Console I/O
│   ├── ImportLogic/                ← File copy/import helpers
│   ├── Logging/                    ← Error logger
│   ├── Query/                      ← Minecraft server query protocol
│   ├── RoleManagement/             ← OP/whitelist role types
│   └── Utils/                      ← Misc utilities
├── ViewModel/                      ← WPF ViewModels
└── View/                           ← XAML pages, controls, styles, converters
```

---

## Entry Point

### `App.xaml.cs`
The WPF `Application` class. Key responsibilities:
- Defines `App.ApplicationPath` → resolves to `%AppData%\Fork\` (creates if missing)
- Defines `App.ServerPath` → delegates to `AppSettingsSerializer` (configurable by user)
- `OnStartup()` → sets UI culture to `en-us`, initializes `ConsoleWriter`, creates `ErrorLogger`
- `ExitApplication()` → calls `ApplicationManager.Instance.ExitApplication()` (graceful shutdown)
- In `DEBUG` builds only: allocates a Win32 console window for stdout

---

## Managers (`logic/manager/`)

All managers are thread-safe singletons using double-checked locking.

### `ApplicationManager.cs`
**The top-level application singleton.**

- Holds `ActiveEntities` — a `Dictionary<Entity, Process>` mapping every running server/network to its OS process
- Holds `SettingsReaders` — file watchers for each open server's config files
- Owns `MainViewModel` — the root WPF ViewModel
- Reads version numbers from embedded resources (`VersionMajor`, `VersionMinor`, `VersionPatch`, `VersionBeta`) to build `CurrentForkVersion`
- Manages Discord Rich Presence: `StartDiscordWebSocket()` / `StopDiscordWebSocket()`
- On exit: sends `stop` command to every active server process, waits up to 5 seconds, kills if needed, disposes all `SettingsReaders`
- Fires `PlayerEvent` and `ServerListEvent` — consumed by the UI

**When to touch it:** Adding a new globally accessible piece of state, changing shutdown behavior, modifying Discord RPC logic.

---

### `ServerManager.cs`
**The main server lifecycle manager.** The most important class in the codebase.

Owns the `ObservableCollection<EntityViewModel> Entities` — the list of all known servers and networks. On first access, loads from `EntitySerializer` and triggers `server.jar` downloads for any uninitialized servers.

**Key methods:**

| Method | What it does |
|--------|-------------|
| `StartServerAsync(viewModel)` | Saves settings → validates Java → optionally updates resource pack SHA1 → spawns Java process → starts performance tracking, console registration, query stats, automation |
| `StopServer(viewModel)` | Writes `stop` to process stdin; marks all players offline |
| `RestartServer(viewModel)` | Stop → wait for STOPPED → `StartServerAsync` |
| `CreateServerAsync(...)` | Creates directory, downloads JAR, writes eula.txt and server.properties |
| `ImportServerAsync(...)` | Copies an existing server directory into Fork's management |
| `DeleteServerAsync(...)` | Stops server, cancels download if in progress, deletes directory, updates entities.json |
| `CloneServerAsync(viewModel)` | JSON-serializes the Server model, deep-copies files, new UID |
| `RenameServerAsync(...)` | Stops server, renames directory, updates model |
| `ChangeServerVersionAsync(...)` | Stops server, deletes server.jar, downloads new jar |
| `ImportWorldAsync(...)` | Copies a world folder into the server directory |
| `DeleteDimensionAsync(...)` | Zips the dimension folder to `DimensionBackups/`, then deletes it |
| `CreateNetworkAsync(...)` / `StartNetworkAsync(...)` etc. | Proxy network management, delegates to `NetworkController` |

**Server start process in detail:**
1. Await `viewModel.SettingsSavingTask` (ensures settings are flushed to disk first)
2. Validate Java installation (path, 64-bit, version ≥ 16 for 1.17+)
3. If resource pack URL set and `AutoSetSha1` is true, download pack and compute SHA1 if outdated
4. Spawn `Process` with `java -Xmx{MaxRam}m {StartupParameters} -jar server.jar nogui`
5. Register process with `ApplicationManager.ActiveEntities`
6. Start `ConsoleWriter.RegisterApplication()` — pipes stdout/stderr to the in-app console
7. Create `ConsoleReader` for stdin (player commands from the UI)
8. Start `QueryStatsWorker` for live player count
9. Call `ServerAutomationManager.UpdateAutomation()` to arm scheduled timers
10. After process exits: remove from `ActiveEntities`, status → STOPPED, re-arm automation

**When to touch it:** Any server lifecycle change — how it starts, how it stops, what files get created, how cloning/importing works.

---

### `VersionManager.cs`
**Loads and exposes available server versions.**

On construction, fires parallel async tasks to populate these `ObservableRangeCollection<ServerVersion>` properties:
- `VanillaVersions` — from Mojang launcher manifest API (cached 12h)
- `SnapshotVersions` — same API, snapshot filter
- `PaperVersions` — from PaperMC API
- `PurpurVersions` — from Purpur API
- `SpigotVersions` — from Spigot
- `FabricVersions` — from Fabric meta API
- `BungeeCordVersion` / `WaterfallVersion` — fixed/latest builds

`GetLatestBuild(version)` — fetches the latest build number for Paper versions (used at server creation time).

**When to touch it:** Adding a new server type (e.g., Quilt, NeoForge), or changing how versions are fetched/cached.

---

### `WebRequestManager.cs`
**Coordinates all web requests for version data.**

- Vanilla versions are cached both in-memory and to `persistence/vanilla-{type}.json` with a 12-hour TTL
- Fetches Mojang's `version_manifest.json` to get version list and per-version detail JSON URLs
- Delegates to type-specific `WebRequester` classes for Paper/Purpur/Spigot/Fabric

**When to touch it:** Changing how version data is fetched, cached, or refreshed.

---

### `PlayerManager.cs`
**Manages the player database.**

- On startup: loads `players.json` from `%AppData%\Fork\players\`; refreshes any player data older than 7 days via Mojang API
- `GetInitialPlayerList(viewModel)` — scans world `playerdata/*.dat` files to find all UUIDs who have played, then resolves each to a name via Mojang API
- `GetPlayer(name)` — resolves a username to a `Player` (Mojang API lookup, cached)
- Player actions (`WhitelistPlayer`, `KickPlayer`, `BanPlayer`, `OpPlayer`, etc.) — write commands directly to the server process's stdin. **Requires server to be RUNNING.**
- Players are cached to disk after every change

**When to touch it:** Changing player lookup behavior, adding new player management commands, changing the player cache format.

---

### `PluginManager.cs`
**Manages server plugins (Paper/Spigot/Purpur only).**

- Plugins sourced from **Spiget** (spiget.org — the SpigotMC API)
- `InstallPlugin` — looks up the plugin, creates an `InstalledPlugin` object, downloads the JAR to `{ServerPath}/{serverName}/plugins/`
- `DeletePlugin` — removes the JAR, updates the tracked list
- `DisablePlugin` — moves JAR from `plugins/` to `plugins_disabled/`
- `EnablePlugin` — moves JAR back from `plugins_disabled/` to `plugins/`
- `LoadInstalledPlugins` — scans the plugins folder for JARs not yet tracked (handles manually-dropped plugins)

**When to touch it:** Adding support for a different plugin source (Modrinth, Hangar), changing how plugins are stored/tracked.

---

### `ServerAutomationManager.cs`
**Scheduled start/stop/restart using `System.Timers.Timer`.**

Each `ServerViewModel` has up to 8 configurable `AutomationTime` slots:
- 4x `RestartTime` (active when server is RUNNING)
- 2x `StopTime` (active when server is RUNNING)
- 2x `StartTime` (active when server is STOPPED)

`UpdateAutomation(viewModel)` — disposes existing timers, finds the next upcoming automation event, arms a one-shot timer for it. Called every time server status changes or settings are saved.

**When to touch it:** Adding cyclic (AutoReset) timers, adding new automation types, changing how timers are calculated.

---

## Models (`logic/model/`)

### `Entity` (interface)
Base interface for everything Fork manages. Properties: `UID`, `Name`, `Version`, `JavaSettings`, `Initialized`, `StartWithFork`, `ServerIconId`.

### `Server : Entity`
The main server model. Serialized to/from `entities.json`.

Key fields:
- `Name` — server directory name
- `Version` — `ServerVersion` (type + version string + JAR URL + build number)
- `JavaSettings` — `MaxRam`, `JavaPath`, `StartupParameters`
- `ServerSettings` — lazy-loaded from `server.properties` if not already set; `[JsonIgnore]`
- `Restart1-4`, `AutoStop1-2`, `AutoStart1-2` — automation schedule
- `AutoSetSha1` — whether Fork auto-updates the resource pack SHA1 on server start
- `Initialized` — false until server.jar download completes
- `StartWithFork` — auto-start on Fork launch
- `UID` — GUID, assigned on creation, used as stable identifier

### `ServerVersion`
Version descriptor. `VersionType` enum: `Vanilla`, `Paper`, `Spigot`, `Waterfall`, `BungeeCord`, `Snapshot`, `Purpur`, `Fabric`.

Key derived properties:
- `IsProxy` → true for Waterfall
- `SupportBuilds` → true for Paper (has build numbers)
- `HasPlugins` → true for Paper, Purpur, Spigot

Implements `IComparable` for semantic version comparison (parses `major.minor.patch`).

### `ServerSettings`
Wraps an `ObservableDictionary<string, string>` matching `server.properties` key names. Provides strongly-typed C# properties (e.g., `MaxPlayers`, `Pvp`, `LevelName`) that read/write from the dictionary. Setting any property sets `HasChanged = true`. Initialized with sensible Minecraft defaults in `InitializeValues()`.

**Adding a new server.properties field:** Add the key to `InitializeValues()` with a default value, then add a typed property.

### `JavaSettings`
`MaxRam` (MB, default 2048), `JavaPath` (default from `AppSettings.DefaultJavaPath`), `StartupParameters` (extra JVM flags).

### `AppSettings`
Global app settings: `ServerPath`, `MaxConsoleLines` (1000), `MaxConsoleLinesPerSecond` (10), `DefaultJavaPath`, `EnableDiscordBot`, `DiscordBotToken`, `UseBetaVersions`, `ConsoleThrottling`, `RichPresence`, `SystemTrayOptions`.

### Automation models (`logic/model/Automation/`)
- `AutomationTime` — base class with `Enabled` bool and `SimpleTime` (Hours, Minutes)
- `RestartTime : AutomationTime` — triggers restart
- `StopTime : AutomationTime` — triggers stop
- `StartTime : AutomationTime` — triggers start

---

## ViewModels (`ViewModel/`)

### `MainViewModel`
The root ViewModel bound to `MainWindow`. Holds:
- `Entities` — the full server list
- `SelectedEntity` — currently selected server/network in the UI
- `AppSettingsViewModel` — settings panel VM
- `CurrentForkVersion` / `LatestForkVersion` / `NewerVersionExists` — version check (runs every 12h via Timer)
- `InstalledJavaVersion` / `ShowJavaWarning` / `JavaWarningMessage` — Java detection status
- `Boi` / `BoiHover` — the mascot image (swaps to Christmas version in December)

### `EntityViewModel` (abstract)
Base ViewModel for both servers and networks. Everything the UI needs to display and interact with a single entity.

Key responsibilities:
- **Console management**: `ConsoleOutList` (ObservableCollection bound to UI), throttling via Damerau-Levenshtein distance to suppress spam, search/filter via `ApplySearchQueryToConsole()`
- **Performance tracking**: CPU/Mem/Disk trackers update `CPUValueRaw`, `MemValueRaw`, `DiskValueRaw` which are displayed as percentages
- **Download/import progress**: `DownloadProgress`, `CopyProgress`, `DownloadCompleted`, `ImportCompleted`
- **Status colors**: `IconColor` / `IconColorHovered` — green=RUNNING, gray=STOPPED, yellow=STARTING
- **Availability indicator**: `LastAvailabilityCheckResult` (OK/PENDING/FAILED) — checks external reachability
- **Settings saving**: `SaveSettings()` triggers `SettingsViewModel.SaveChanges()` and re-arms automation
- **Server icon**: loads icons from embedded resources + `custom-icon.png`; writes selected icon to `server-icon.png`
- **Console input**: `ReadConsoleIn` command sends typed text to `ConsoleReader`

**Console throttling logic:** When `ConsoleThrottling` is enabled, consecutive INFO messages that are very similar (within 10% Levenshtein distance relative to message length) are collapsed into a `SubContents` counter rather than shown individually. Also enforces `MaxConsoleLinesPerSecond`.

### `ServerViewModel : EntityViewModel`
Adds:
- `PlayerList` — online players, updated from server query
- `OPList`, `Whitelist`, `BannedList` — loaded from server JSON files
- `Worlds` — list of world directories
- `Server` property — typed cast of `Entity`
- `ServerTitle` — formatted display name
- `InitializeLists()` — loads player lists and worlds from disk

### `NetworkViewModel : EntityViewModel`
Adds network/proxy-specific state: `Servers` list, network settings, `SyncServers` flag.

### `SettingsViewModel`
Manages the settings page. Reads `.yml`, `.properties`, and other config files into `SettingsFile` objects, exposes them for the UI, and writes changes back to disk.

### `PluginViewModel`
Wraps a server's plugin state: `InstalledPlugins` collection, browseable plugin catalog from Spiget, install/uninstall commands.

### `AddServerViewModel` / `ImportViewModel`
Handle the Create Server and Import Server flows — selecting version, name, world path, Java settings.

### `AppSettingsViewModel`
Binds to `AppSettings`, handles save/validation for the global settings panel.

---

## Persistence (`logic/persistence/`)

### `EntitySerializer`
Serializes/deserializes the full entity list to `entities.json`. Uses Newtonsoft.Json with a custom type discriminator to handle `Server` vs `Network` polymorphism. `StoreEntities()` is called after every mutation.

### `AppSettingsSerializer`
Singleton. Reads/writes `appsettings.json`. Accessed via `AppSettingsSerializer.Instance.AppSettings`.

### `FileReader`
- `ReadServerSettings(directory)` — parses `server.properties` into `Dictionary<string, string>`
- `ReadBannedPlayers()`, `ReadOPList()`, `ReadWhitelist()` — reads the server JSON player files

### `FileWriter`
- `WriteEula(path)` — writes `eula.txt` with `eula=true`
- `WriteServerSettings(path, settings)` — writes `server.properties` from the settings dictionary

### `SettingsReader`
A `FileSystemWatcher` that monitors a server's config files. When an external program edits a config file (e.g., a player editor tool), `SettingsReader` detects the change and reloads the relevant settings into the ViewModel. One instance per open entity, stored in `ApplicationManager.SettingsReaders`.

### `InstalledPluginSerializer`
Reads/writes the tracked plugin list for a server. Scans the `plugins/` folder for JARs and tracks metadata.

---

## Web Requesters (`logic/WebRequesters/`)

### `Downloader`
Downloads `server.jar` files with progress reporting. Used by `ServerManager` when creating or changing server version. Also has `DownloadFileAsync()` used for resource pack SHA1 calculation.

### `FabricWebRequester`
Queries `meta.fabricmc.net` to fetch available Fabric loader + game version combinations and constructs download URLs.

### `PaperWebRequester`
Queries `api.papermc.io` to list Paper versions and get the latest build's download URL.

### `PurpurWebRequester`
Queries `api.purpurmc.org` for available Purpur versions.

### `SpigotWebRequester`
Fetches Spigot versions. Spigot doesn't have a clean API — this handles their build system.

### `WaterfallWebRequester`
Queries PaperMC's Waterfall API for the latest build.

### `PluginWebRequester`
Queries `spiget.org` API for plugin search, details, and version info.

### `WebSocketHandler`
Manages the Discord RPC WebSocket connection for Rich Presence. Initialized/disposed via `ApplicationManager`.

### `ResponseCache`
Simple in-memory cache for web responses to reduce redundant API calls.

---

## Controllers (`logic/Controller/`)

### `APIController`
- `GetLatestForkVersion()` — calls Fork's update server to check for new versions
- `GetExternalIPAddress()` — fetches the machine's external IP (used for the address display in the server list)
- `DownloadPluginAsync()` — handles plugin JAR download via Spiget

### `NetworkController`
Handles all Waterfall/BungeeCord proxy network operations: create, start, stop, restart, rename, clone, delete. Mirrors `ServerManager`'s server methods but for network entities.

---

## Console (`logic/CustomConsole/` and `logic/ApplicationConsole/`)

### `logic/CustomConsole/ConsoleWriter`
Reads stdout/stderr streams from the Minecraft server process and pipes each line into `EntityViewModel.AddToConsole()`. Runs on a background thread. Detects server status transitions (`Done (` → RUNNING) by parsing log output.

### `logic/CustomConsole/ConsoleReader`
Wraps the server process's stdin `StreamWriter`. `Read(command, viewModel)` writes a command to the server (e.g., `/op`, `/stop`, or any Minecraft command).

### `logic/ApplicationConsole/ConsoleWriter`
Redirects `Console.Out` to Fork's internal console window (used for Fork's own debug output, not the Minecraft server's output).

---

## Background Workers (`logic/BackgroundWorker/`)

### `QueryStatsWorker`
Polls the Minecraft server via the **Query protocol** (UDP, port from `server.properties`) to get live player count and player names while the server is running. Updates `ServerViewModel.PlayerList`.

### `Performance/CPUTracker`, `MemTracker`, `DiskTracker`
Each polls the target `Process` at regular intervals and calls `EntityViewModel.CPUValueUpdate()`, `MemValueUpdate()`, `DiskValueUpdate()`. Values are smoothed by a 3-sample rolling average.

---

## View Layer (`View/`)

The UI uses WPF XAML throughout. The main areas are:

| Path | Purpose |
|------|---------|
| `View/Xaml2/MainWindow.xaml` | Main window shell with server list sidebar and content area |
| `View/Xaml2/Pages/Server/ConsolePage.xaml` | Server console tab |
| `View/Xaml2/Pages/Server/ServerPage.xaml` | Server overview (players, status, actions) |
| `View/Xaml2/Pages/Server/PluginsPage.xaml` | Plugin browser/manager |
| `View/Xaml2/Pages/Server/WorldsPage.xaml` | World list and import |
| `View/Xaml2/Pages/Settings/` | Settings pages (Vanilla, Fork-specific, network) |
| `View/Xaml2/Pages/CreatePage.xaml` | New server creation wizard |
| `View/Xaml2/Pages/ImportPage.xaml` | Import existing server |
| `View/Xaml2/Pages/AppSettingsPage.xaml` | Global app settings |
| `View/Resources/dictionaries/` | All visual styles (buttons, tabs, text boxes, colors) |
| `View/Xaml/Converter/` | WPF value converters (bool↔visibility, percentage→color, etc.) |
| `View/Xaml/ValidationRules/` | Input validation (port numbers, times, positive longs) |

**Theme:** All colors and styles come from `DefaultTheme.xaml` and the individual `*Styles.xaml` dictionaries. To change the visual appearance of any control type, edit the relevant style dictionary.

**Custom controls** (`View/Xaml2/Controls/`):
- `IconButton` / `IconRadioButton` — buttons with SVG/PNG icons
- `ServerTypeRadioButton` — radio button styled for server type selection
- `RatingsControl` / `StarControl` — star rating display for plugins
- `StretchyWrapPanel` — WrapPanel that stretches items to fill width

---

## Import/File Utilities (`logic/ImportLogic/`, `logic/Utils/`)

### `FileImporter`
Recursive directory copy/move with progress events. Used for server import, world import, and server moves.

### `DirectoryValidator`
Checks whether a directory looks like a valid Minecraft server (has server.jar, world folder, etc.) before import.

### `JavaVersionUtils`
Detects installed Java installations. Parses `java -version` output to extract version number and 32/64-bit status.

### `ForkUtils`
Misc utilities used throughout.

### `StringUtils`
- `DamerauLevenshteinDistance()` — used for console throttling
- `PluginNameToJarName()` / `BeautifyPluginName()` — plugin name normalization

### `ObservableDictionary`
A `Dictionary<TKey, TValue>` that fires `CollectionChanged` events — used for `ServerSettings` so the UI can react to property changes.

### `ObservableRangeCollection`
Extension of `ObservableCollection` with `AddRange()` — used for version lists.

---

## Build and Run

**Requirements:** .NET 8 SDK, Visual Studio 2022 or JetBrains Rider.

```bash
# Build
dotnet build Fork.sln

# Run (debug)
dotnet run --project Fork.csproj
```

**Publish** (self-contained for distribution):
```bash
dotnet publish Fork.csproj -c Release -r win-x64 --self-contained true -o publish/
```
The `.run/Publish Fork to folder.run.xml` file has the JetBrains Rider run configuration for this.

**Version numbers** live in `Properties/Resources.resx` as `VersionMajor`, `VersionMinor`, `VersionPatch`, `VersionBeta`. The `.csproj` `<Version>` tag should match.

**Auto-update** is handled by the Fork Launcher (`ForkLauncher.exe`) — the app itself does not self-update. When `CheckForkVersion()` finds a newer version, it shows a notification in the UI but the launcher handles the actual download and replacement.

---

## Common Edit Scenarios

### Add a new server type (e.g., Quilt)

1. Add `Quilt` to `ServerVersion.VersionType` enum in `logic/model/ServerVersion.cs`
2. Create `logic/WebRequesters/QuiltWebRequester.cs` following the pattern of `FabricWebRequester`
3. Add `QuiltVersions` property to `VersionManager.cs` and populate it in the constructor
4. Add `GetQuiltVersions()` to `WebRequestManager.cs`
5. Add the `HasPlugins` / `SupportBuilds` / `IsProxy` logic for Quilt in `ServerVersion.cs`
6. Add dimension folder handling to `ServerManager.GetDimensionFolder()` if needed
7. Add icon assets to `View/Resources/images/Icons/` (Quilt.png, QuiltW.png)
8. Add the icon cases to `EntityViewModel.Icon` and `EntityViewModel.IconW`
9. Add the version type to the Create page UI in `View/Xaml2/Pages/CreatePage.xaml`

### Change Java startup arguments

Edit `ServerManager.StartServerAsync()`, specifically the `ProcessStartInfo.Arguments` line:
```csharp
Arguments = "-Xmx" + viewModel.Server.JavaSettings.MaxRam + "m " +
            viewModel.Server.JavaSettings.StartupParameters + " -jar server.jar nogui"
```

### Add a new server.properties field

1. Add the key and default value to `ServerSettings.InitializeValues()` in `logic/model/ServerSettings.cs`
2. Add a typed property in the `#region Properties` section
3. The settings UI auto-generates from the dictionary — for a custom control, add it to the relevant settings XAML page in `View/Xaml2/Pages/Settings/`

### Add a new global app setting

1. Add property to `AppSettings.cs`
2. Add UI binding in `View/Xaml2/Pages/AppSettingsPage.xaml`
3. Access anywhere via `AppSettingsSerializer.Instance.AppSettings.YourNewProperty`

### Change how the console looks or behaves

- Throttling logic: `EntityViewModel.AddToConsole()` in `ViewModel/EntityViewModel.cs`
- Max lines / lines-per-second: `AppSettings.MaxConsoleLines` / `AppSettings.MaxConsoleLinesPerSecond`
- Visual styling: `View/Resources/dictionaries/` — most relevant is `DefaultTheme.xaml` and `TextBoxStyles.xaml`
- Message parsing/coloring: `logic/model/ServerConsole/ConsoleMessage.cs` and `ConsoleWriter`

### Add a new automation type

1. Create a new class in `logic/model/Automation/` extending `AutomationTime`
2. Add it to `Server.cs` as properties (following the pattern of `Restart1-4`)
3. Handle it in `ServerAutomationManager.TimerElapsed()` and `GetRelevantTimes()`
4. Add settings UI in the server settings page

---

## Key Patterns to Know

**Singleton managers:** All managers use `private static T instance` with `lock(myLock)` double-checked initialization. Never instantiate them directly — always use `Manager.Instance`.

**Dispatcher.Invoke:** All UI collection mutations must run on the WPF dispatcher. The pattern throughout the codebase is `Application.Current.Dispatcher?.Invoke(() => collection.Add(item))`.

**Async wrapping:** Many methods follow this pattern — a synchronous private implementation wrapped in a Task for the public async API:
```csharp
public async Task<bool> DoThingAsync(...)
{
    Task<bool> t = new(() => DoThing(...));
    t.Start();
    return await t;
}
```

**PropertyChanged (MVVM):** ViewModels call `raisePropertyChanged(nameof(Property))` to notify the UI of changes. The `[NotifyPropertyChangedInvocator]` attribute on the method enforces the pattern. Uses `PropertyChanged.Fody` for automatic property change notification on simple properties.

**entities.json:** The server list is the source of truth. Any time a server is added, renamed, or modified, `EntitySerializer.Instance.StoreEntities()` should be called. The file is in `%AppData%\Fork\persistence\entities.json`.

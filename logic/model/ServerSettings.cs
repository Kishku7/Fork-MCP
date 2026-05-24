using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Fork.Logic.Utils;

namespace Fork.Logic.Model;

public class ServerSettings
{
    public enum Difficulty
    {
        Peaceful,
        Easy,
        Normal,
        Hard
    }

    public enum Gamemode
    {
        Survival,
        Creative,
        Adventure,
        Spectator
    }

    public enum LevelType
    {
        Default,
        Flat,
        Large_biomes,
        Amplified,
        Buffet
    }

    public ServerSettings(string levelname)
    {
        SettingsDictionary = new ObservableDictionary<string, string>();
        SettingsDictionary.CollectionChanged += (_, _) => { HasChanged = true; };

        InitializeValues(levelname);
    }

    public ServerSettings(Dictionary<string, string> settingsDictionary)
    {
        SettingsDictionary = new ObservableDictionary<string, string>();

        if (settingsDictionary != null && settingsDictionary.ContainsKey("LevelName"))
        {
            InitializeValues(settingsDictionary["LevelName"]);
        }
        else
        {
            InitializeValues("world");
        }

        foreach (KeyValuePair<string, string> keyValuePair in settingsDictionary)
            SettingsDictionary[keyValuePair.Key] = keyValuePair.Value;

        SettingsDictionary.CollectionChanged += (_, _) => { HasChanged = true; };

        // Populate unknown properties now that all keys are loaded from file.
        RefreshUnknownProperties();
    }

    private void InitializeValues(string levelname)
    {
        SpawnProtection = 16;
        OpPermissionLevel = 4;
        MaxPlayers = 20;
        NetworkCompressionThreshold = 256;
        RconPort = 25575;
        ServerPort = 25565;
        QueryPort = 25565;
        ViewDistance = 10;
        MaxBuildHeight = 256;
        RateLimit = 0;

        MaxTickTime = 60000L;
        PlayerIdleTimeout = 0L;
        MaxWorldSize = 29999984;

        GeneratorSettings = "";
        ResourcePackSha1 = "";
        ServerIp = "";
        LevelName = levelname;
        ResourcePack = "";
        RconPassword = "";
        LevelSeed = "";
        Motd = @"\u00A7aPowered by Fork" + "\n" + @"\u00A77A Minecraft Server Manager";

        ForceGamemode = false;
        AllowNether = true;
        EnforceWhitelist = false;
        BcastConsoleToOps = true;
        EnableQuery = true;
        SpawnMonsters = true;
        BcastRconToOps = true;
        Pvp = true;
        SnooperEnabled = true;
        Hardcore = false;
        EnableCommandBlock = false;
        SpawnNpcs = true;
        AllowFlight = false;
        SpawnAnimals = true;
        Whitelist = false;
        GenerateStructures = true;
        OnlineMode = true;
        UseNativeTransport = true;
        PreventProxyConnections = false;
        EnableRcon = false;

        SyncChunkWrites = false;
        EnableJmxMonitoring = false;
        EnableStatus = true;
        RequireResourcePack = false;
        EntityBroadcastRangePercentage = 100;

        // 1.16.5+
        HideOnlinePlayers = false;
        // 1.18+
        SimulationDistance = 10;
        // 1.19.1+
        BugReportLink = "";
        // 1.19.4+
        ResourcePackId = "";
        ResourcePackPrompt = "";
        // 1.20.2+
        LogIps = true;
        // 1.20.5+
        AcceptsTransfers = false;
        // 1.21.4+
        PauseWhenEmptySeconds = 0;

        CurrGamemode = Gamemode.Survival;
        CurrDifficulty = Difficulty.Easy;
        CurrLevelType = LevelType.Default;
    }

    #region Properties

    public List<Difficulty> Difficulties { get; } = new(Enum.GetValues(typeof(Difficulty)).Cast<Difficulty>());
    public List<Gamemode> Gamemodes { get; } = new(Enum.GetValues(typeof(Gamemode)).Cast<Gamemode>());
    public List<LevelType> LevelTypes { get; } = new(Enum.GetValues(typeof(LevelType)).Cast<LevelType>());

    public ObservableDictionary<string, string> SettingsDictionary { get; }
    public bool HasChanged { get; set; }

    public int SpawnProtection
    {
        get => int.Parse(SettingsDictionary["spawn-protection"]);
        set => SettingsDictionary["spawn-protection"] = value.ToString();
    }

    public long MaxTickTime
    {
        get => long.Parse(SettingsDictionary["max-tick-time"]);
        set => SettingsDictionary["max-tick-time"] = value.ToString();
    }

    public int QueryPort
    {
        get => int.Parse(SettingsDictionary["query.port"]);
        set => SettingsDictionary["query.port"] = value.ToString();
    }

    public string GeneratorSettings
    {
        get => SettingsDictionary["generator-settings"];
        //TODO Builder https://minecraft.gamepedia.com/Superflat
        set => SettingsDictionary["generator-settings"] = value;
    }

    public bool ForceGamemode
    {
        get => bool.Parse(SettingsDictionary["force-gamemode"]);
        set => SettingsDictionary["force-gamemode"] = value.ToString().ToLower();
    }

    public bool AllowNether
    {
        get => bool.Parse(SettingsDictionary["allow-nether"]);
        set => SettingsDictionary["allow-nether"] = value.ToString().ToLower();
    }

    public bool EnforceWhitelist
    {
        get => bool.Parse(SettingsDictionary["enforce-whitelist"]);
        set => SettingsDictionary["enforce-whitelist"] = value.ToString().ToLower();
    }

    public Gamemode CurrGamemode
    {
        get => (Gamemode)Enum.Parse(typeof(Gamemode),
            SettingsDictionary["gamemode"].First().ToString().ToUpper() + SettingsDictionary["gamemode"].Substring(1));
        set => SettingsDictionary["gamemode"] = value.ToString().ToLower();
    }

    public bool BcastConsoleToOps
    {
        get => bool.Parse(SettingsDictionary["broadcast-console-to-ops"]);
        set => SettingsDictionary["broadcast-console-to-ops"] = value.ToString().ToLower();
    }

    public bool EnableQuery
    {
        get => bool.Parse(SettingsDictionary["enable-query"]);
        set => SettingsDictionary["enable-query"] = value.ToString().ToLower();
    }

    public long PlayerIdleTimeout
    {
        get => long.Parse(SettingsDictionary["player-idle-timeout"]);
        set => SettingsDictionary["player-idle-timeout"] = value.ToString();
    }

    public Difficulty CurrDifficulty
    {
        get => (Difficulty)Enum.Parse(typeof(Difficulty),
            SettingsDictionary["difficulty"].First().ToString().ToUpper() +
            SettingsDictionary["difficulty"].Substring(1));
        set => SettingsDictionary["difficulty"] = value.ToString().ToLower();
    }

    public bool SpawnMonsters
    {
        get => bool.Parse(SettingsDictionary["spawn-monsters"]);
        set => SettingsDictionary["spawn-monsters"] = value.ToString().ToLower();
    }

    public bool BcastRconToOps
    {
        get => bool.Parse(SettingsDictionary["broadcast-rcon-to-ops"]);
        set => SettingsDictionary["broadcast-rcon-to-ops"] = value.ToString().ToLower();
    }

    public int OpPermissionLevel
    {
        get => int.Parse(SettingsDictionary["op-permission-level"]);
        set => SettingsDictionary["op-permission-level"] = value.ToString();
    }

    public bool Pvp
    {
        get => bool.Parse(SettingsDictionary["pvp"]);
        set => SettingsDictionary["pvp"] = value.ToString().ToLower();
    }

    public bool SnooperEnabled
    {
        get => bool.Parse(SettingsDictionary["snooper-enabled"]);
        set => SettingsDictionary["snooper-enabled"] = value.ToString().ToLower();
    }

    public LevelType CurrLevelType
    {
        get => (LevelType)Enum.Parse(typeof(LevelType),
            SettingsDictionary["level-type"].First().ToString().ToUpper() +
            SettingsDictionary["level-type"].Substring(1));
        set => SettingsDictionary["level-type"] = value.ToString().ToLower();
    }

    public bool Hardcore
    {
        get => bool.Parse(SettingsDictionary["hardcore"]);
        set => SettingsDictionary["hardcore"] = value.ToString().ToLower();
    }

    public bool EnableCommandBlock
    {
        get => bool.Parse(SettingsDictionary["enable-command-block"]);
        set => SettingsDictionary["enable-command-block"] = value.ToString().ToLower();
    }

    public int MaxPlayers
    {
        get => int.Parse(SettingsDictionary["max-players"]);
        set => SettingsDictionary["max-players"] = value.ToString();
    }

    public int NetworkCompressionThreshold
    {
        get => int.Parse(SettingsDictionary["network-compression-threshold"]);
        set => SettingsDictionary["network-compression-threshold"] = value.ToString();
    }

    public string ResourcePackSha1
    {
        get => SettingsDictionary["resource-pack-sha1"];
        set => SettingsDictionary["resource-pack-sha1"] = value;
    }

    public long MaxWorldSize
    {
        get => long.Parse(SettingsDictionary["max-world-size"]);
        set => SettingsDictionary["max-world-size"] = value.ToString();
    }

    public int RconPort
    {
        get => int.Parse(SettingsDictionary["rcon.port"]);
        set => SettingsDictionary["rcon.port"] = value.ToString();
    }

    public int ServerPort
    {
        get => int.Parse(SettingsDictionary["server-port"]);
        set => SettingsDictionary["server-port"] = value.ToString();
    }

    public string ServerIp
    {
        get => SettingsDictionary["server-ip"];
        set => SettingsDictionary["server-ip"] = value;
    }

    public bool SpawnNpcs
    {
        get => bool.Parse(SettingsDictionary["spawn-npcs"]);
        set => SettingsDictionary["spawn-npcs"] = value.ToString().ToLower();
    }

    public bool AllowFlight
    {
        get => bool.Parse(SettingsDictionary["allow-flight"]);
        set => SettingsDictionary["allow-flight"] = value.ToString().ToLower();
    }

    public string LevelName
    {
        get => SettingsDictionary["level-name"];
        set => SettingsDictionary["level-name"] = value;
    }

    public int ViewDistance
    {
        get => int.Parse(SettingsDictionary["view-distance"]);
        set => SettingsDictionary["view-distance"] = value.ToString();
    }

    public string ResourcePack
    {
        get => SettingsDictionary["resource-pack"];
        set => SettingsDictionary["resource-pack"] = value;
    }

    public bool SpawnAnimals
    {
        get => bool.Parse(SettingsDictionary["spawn-animals"]);
        set => SettingsDictionary["spawn-animals"] = value.ToString().ToLower();
    }

    public bool Whitelist
    {
        get => bool.Parse(SettingsDictionary["white-list"]);
        set => SettingsDictionary["white-list"] = value.ToString().ToLower();
    }

    public string RconPassword
    {
        get => SettingsDictionary["rcon.password"];
        set => SettingsDictionary["rcon.password"] = value;
    }

    public bool GenerateStructures
    {
        get => bool.Parse(SettingsDictionary["generate-structures"]);
        set => SettingsDictionary["generate-structures"] = value.ToString().ToLower();
    }

    public int MaxBuildHeight
    {
        get => int.Parse(SettingsDictionary["max-build-height"]);
        set => SettingsDictionary["max-build-height"] = value.ToString();
    }

    public int RateLimit
    {
        get => int.Parse(SettingsDictionary["rate-limit"]);
        set => SettingsDictionary["rate-limit"] = value.ToString();
    }

    public bool OnlineMode
    {
        get => bool.Parse(SettingsDictionary["online-mode"]);
        set => SettingsDictionary["online-mode"] = value.ToString().ToLower();
    }

    public string LevelSeed
    {
        get => SettingsDictionary["level-seed"];
        set => SettingsDictionary["level-seed"] = value;
    }

    public bool UseNativeTransport
    {
        get => bool.Parse(SettingsDictionary["use-native-transport"]);
        set => SettingsDictionary["use-native-transport"] = value.ToString().ToLower();
    }

    public bool PreventProxyConnections
    {
        get => bool.Parse(SettingsDictionary["prevent-proxy-connections"]);
        set => SettingsDictionary["prevent-proxy-connections"] = value.ToString().ToLower();
    }

    public bool EnableRcon
    {
        get => bool.Parse(SettingsDictionary["enable-rcon"]);
        set => SettingsDictionary["enable-rcon"] = value.ToString().ToLower();
    }

    public string Motd
    {
        get => SettingsDictionary["motd"];
        set => SettingsDictionary["motd"] = value;
    }

    public bool SyncChunkWrites
    {
        get => bool.Parse(SettingsDictionary["sync-chunk-writes"]);
        set => SettingsDictionary["sync-chunk-writes"] = value.ToString().ToLower();
    }

    public bool EnableJmxMonitoring
    {
        get => bool.Parse(SettingsDictionary["enable-jmx-monitoring"]);
        set => SettingsDictionary["enable-jmx-monitoring"] = value.ToString().ToLower();
    }

    public bool EnableStatus
    {
        get => bool.Parse(SettingsDictionary["enable-status"]);
        set => SettingsDictionary["enable-status"] = value.ToString().ToLower();
    }

    public bool RequireResourcePack
    {
        get => bool.Parse(SettingsDictionary["require-resource-pack"]);
        set => SettingsDictionary["require-resource-pack"] = value.ToString().ToLower();
    }

    public int EntityBroadcastRangePercentage
    {
        get => int.Parse(SettingsDictionary["entity-broadcast-range-percentage"]);
        set => SettingsDictionary["entity-broadcast-range-percentage"] = value.ToString();
    }

    // ── Minecraft 26.x / modern additions ────────────────────────────────────

    /// <summary>1.16.5+ — Hide online players from the server list player count.</summary>
    public bool HideOnlinePlayers
    {
        get => bool.Parse(SettingsDictionary["hide-online-players"]);
        set => SettingsDictionary["hide-online-players"] = value.ToString().ToLower();
    }

    /// <summary>1.18+ — Separate simulation distance from render distance.</summary>
    public int SimulationDistance
    {
        get => int.Parse(SettingsDictionary["simulation-distance"]);
        set => SettingsDictionary["simulation-distance"] = value.ToString();
    }

    /// <summary>1.19.1+ — URL shown to players in the bug report screen.</summary>
    public string BugReportLink
    {
        get => SettingsDictionary["bug-report-link"];
        set => SettingsDictionary["bug-report-link"] = value;
    }

    /// <summary>1.19.4+ — UUID of the resource pack (required for RequireResourcePack enforcement).</summary>
    public string ResourcePackId
    {
        get => SettingsDictionary["resource-pack-id"];
        set => SettingsDictionary["resource-pack-id"] = value;
    }

    /// <summary>1.19.4+ — JSON text shown to the player when prompted to accept the resource pack.</summary>
    public string ResourcePackPrompt
    {
        get => SettingsDictionary["resource-pack-prompt"];
        set => SettingsDictionary["resource-pack-prompt"] = value;
    }

    /// <summary>1.20.2+ — Whether to log player IPs in the server log.</summary>
    public bool LogIps
    {
        get => bool.Parse(SettingsDictionary["log-ips"]);
        set => SettingsDictionary["log-ips"] = value.ToString().ToLower();
    }

    /// <summary>1.20.5+ — Allow incoming player transfers from other servers via /transfer.</summary>
    public bool AcceptsTransfers
    {
        get => bool.Parse(SettingsDictionary["accepts-transfers"]);
        set => SettingsDictionary["accepts-transfers"] = value.ToString().ToLower();
    }

    /// <summary>1.21.4+ — Pause simulation when no players are online (0 = disabled).</summary>
    public int PauseWhenEmptySeconds
    {
        get => int.Parse(SettingsDictionary["pause-when-empty-seconds"]);
        set => SettingsDictionary["pause-when-empty-seconds"] = value.ToString();
    }

    // ── Unknown / custom properties ────────────────────────────────────────────

    /// <summary>
    ///     All known server.properties keys that Fork-MCP maps to typed properties.
    ///     Any key present in <see cref="SettingsDictionary"/> but NOT in this set
    ///     is exposed via <see cref="UnknownSettings"/>.
    /// </summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "spawn-protection", "max-tick-time", "query.port", "generator-settings", "force-gamemode",
        "allow-nether", "enforce-whitelist", "gamemode", "broadcast-console-to-ops", "enable-query",
        "player-idle-timeout", "difficulty", "spawn-monsters", "broadcast-rcon-to-ops",
        "op-permission-level", "pvp", "snooper-enabled", "level-type", "hardcore",
        "enable-command-block", "max-players", "network-compression-threshold", "resource-pack-sha1",
        "max-world-size", "rcon.port", "server-port", "query.port", "server-ip", "spawn-npcs",
        "allow-flight", "level-name", "view-distance", "resource-pack", "spawn-animals",
        "white-list", "rcon.password", "generate-structures", "max-build-height", "rate-limit",
        "online-mode", "level-seed", "use-native-transport", "prevent-proxy-connections",
        "enable-rcon", "motd", "sync-chunk-writes", "enable-jmx-monitoring", "enable-status",
        "require-resource-pack", "entity-broadcast-range-percentage",
        // 26.x additions
        "hide-online-players", "simulation-distance", "bug-report-link",
        "resource-pack-id", "resource-pack-prompt", "log-ips",
        "accepts-transfers", "pause-when-empty-seconds",
    };

    /// <summary>
    ///     Key/value pairs from server.properties that Fork-MCP does not have a
    ///     typed property for. Editable from the UI; changes go directly into
    ///     <see cref="SettingsDictionary"/> and are saved on the next write cycle.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> UnknownSettings =>
        SettingsDictionary
            .Where(kv => !KnownKeys.Contains(kv.Key))
            .OrderBy(kv => kv.Key);

    // ── Unknown properties — observable, two-way bindable ────────────────────

    private ObservableCollection<SettingKeyValuePair> _unknownProperties;

    /// <summary>
    ///     Observable collection of unknown properties for UI binding.
    ///     Each item wraps a dictionary entry and allows two-way editing.
    /// </summary>
    public ObservableCollection<SettingKeyValuePair> UnknownProperties =>
        _unknownProperties ??= BuildUnknownProperties();

    /// <summary>Rebuild the observable collection from current dictionary state.</summary>
    public void RefreshUnknownProperties() =>
        _unknownProperties = BuildUnknownProperties();

    private ObservableCollection<SettingKeyValuePair> BuildUnknownProperties()
        => new(SettingsDictionary
            .Where(kv => !KnownKeys.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .Select(kv => new SettingKeyValuePair(kv.Key, SettingsDictionary)));

    #endregion

    // ── Nested helper ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Wraps a single entry in <see cref="ServerSettings.SettingsDictionary"/>
    ///     so it can be two-way bound from the Unknown Properties UI section.
    /// </summary>
    public sealed class SettingKeyValuePair : INotifyPropertyChanged
    {
        private readonly ObservableDictionary<string, string> _dict;

        public SettingKeyValuePair(string key, ObservableDictionary<string, string> dict)
        {
            Key = key;
            _dict = dict;
        }

        public string Key { get; }

        public string Value
        {
            get => _dict.TryGetValue(Key, out string v) ? v : string.Empty;
            set
            {
                _dict[Key] = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
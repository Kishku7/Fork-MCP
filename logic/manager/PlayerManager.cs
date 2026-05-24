using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fork.Logic.Model;
using Fork.ViewModel;
using Newtonsoft.Json;

namespace Fork.Logic.Manager;

public sealed class PlayerManager
{
    private readonly Dictionary<string, Task<Player>> playerGenerators = new();
    private string PlayerJsonPath;

    private readonly HashSet<Player> PlayerSet;

    public void WhitelistPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("whitelist add " + name, viewModel);
    }

    public void UnWhitelistPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("whitelist remove " + name, viewModel);
    }

    public void KickPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("kick " + name + " You were kicked by an Operator using the Fork Server Manager.", viewModel);
    }

    public void BanPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("ban " + name + " You were banned by an Operator using the Fork Server Manager.", viewModel);
    }

    public void UnBanPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("pardon " + name, viewModel);
    }

    public void OpPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("op " + name, viewModel);
    }

    public void DeopPlayer(ServerViewModel viewModel, string name)
    {
        if (viewModel.CurrentStatus != ServerStatus.RUNNING)
        {
            Console.WriteLine("Can only change roles while server is running!");
            return;
        }

        viewModel.ConsoleReader?.Read("deop " + name, viewModel);
    }

    public async Task<Player> GetPlayer(string name)
    {
        if (!CheckPlayerName(name))
        {
            Console.WriteLine("Tried to retrieve invalid player (" + name + ") from database. Skipping...");
            return null;
        }

        Player[] existingPlayers = PlayerSet.Where(player => player.Name.Equals(name)).ToArray();
        if (existingPlayers.Any())
        {
            return existingPlayers.First();
        }

        Player p = await Task.Run(() => CreatePlayer(name));
        PlayerSet.Add(p);
        SafePlayersToFile();
        return p;
    }

    public async IAsyncEnumerable<ServerPlayer> GetInitialPlayerList(ServerViewModel viewModel)
    {
        // Load usercache.json for fast UUID→name resolution — no Mojang API needed.
        // Minecraft writes this file itself whenever a player connects.
        Dictionary<string, string> uuidNameCache = LoadUserCache(viewModel.Server);

        HashSet<string> playerIDsToAdd = new();

        // Scan the server directory directly for player .dat files.
        // We do NOT rely on viewModel.Worlds because WorldValidationInfo.IsValid requires
        // a "region/" subdirectory that Minecraft 26.x+ no longer places at the world root
        // (region data moved to dimensions/), causing Worlds to be empty on modern servers.
        // Instead we walk up to 2 levels from the server root looking for both path patterns:
        //   - playerdata/*.dat       (pre-26.x, stored directly under the world dir)
        //   - players/data/*.dat     (26.x+, stored under world/players/data/)
        string serverDir = Path.Combine(App.ServerPath, viewModel.Server.Name);
        if (Directory.Exists(serverDir))
            CollectPlayerUuids(new DirectoryInfo(serverDir), playerIDsToAdd, depth: 0, maxDepth: 2);

        bool anyNew = false;
        foreach (string uuid in playerIDsToAdd)
        {
            Player p;

            // 1. Already in PlayerSet (fastest path — no I/O)
            Player[] existing = PlayerSet.Where(pl => pl.Uid.Equals(uuid, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Any())
            {
                p = existing.First();
            }
            // 2. In usercache — build Player directly, no Mojang API call
            else if (uuidNameCache.TryGetValue(uuid, out string cachedName) && CheckPlayerName(cachedName))
            {
                p = new Player { Uid = uuid, Name = cachedName, OfflineChar = false, LastUpdated = DateTime.Now };
                PlayerSet.Add(p);
                anyNew = true;
            }
            // 3. Fall back to Mojang API (original path for servers without usercache)
            else
            {
                lock (Instance)
                {
                    if (!playerGenerators.ContainsKey(uuid))
                        playerGenerators.Add(uuid, GetPlayerFromUuid(uuid));
                }
                p = await playerGenerators[uuid];
            }

            if (p != null)
            {
                bool isOp = viewModel.OPList.Any(op =>
                    string.Equals(op.Uid, p.Uid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(op.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                yield return new ServerPlayer(p, viewModel, isOp, false);
            }
        }

        if (anyNew)
            SafePlayersToFile();
    }

    /// <summary>
    ///     Recursively scan <paramref name="dir"/> for player .dat files.
    ///     Checks both "playerdata/" (pre-26.x) and "players/data/" (26.x+) at each level.
    /// </summary>
    private void CollectPlayerUuids(DirectoryInfo dir, HashSet<string> results, int depth, int maxDepth)
    {
        if (!dir.Exists || depth > maxDepth) return;

        // Pattern 1: {dir}/playerdata/*.dat  (legacy)
        DirectoryInfo legacy = new(Path.Combine(dir.FullName, "playerdata"));
        if (legacy.Exists)
            foreach (string f in Directory.GetFiles(legacy.FullName, "*.dat", SearchOption.TopDirectoryOnly))
            {
                string uuid = Path.GetFileNameWithoutExtension(f).Replace("-", "");
                if (ValidateUuid(uuid)) results.Add(uuid);
            }

        // Pattern 2: {dir}/players/data/*.dat  (Minecraft 26.x+)
        DirectoryInfo modern = new(Path.Combine(dir.FullName, "players", "data"));
        if (modern.Exists)
            foreach (string f in Directory.GetFiles(modern.FullName, "*.dat", SearchOption.TopDirectoryOnly))
            {
                string uuid = Path.GetFileNameWithoutExtension(f).Replace("-", "");
                if (ValidateUuid(uuid)) results.Add(uuid);
            }

        // Recurse into subdirectories — skip non-world directories to keep the scan fast
        if (depth < maxDepth)
            foreach (DirectoryInfo sub in dir.EnumerateDirectories())
            {
                string name = sub.Name;
                if (name.StartsWith(".") || name == "bin" || name == "logs" || name == "mods" ||
                    name == "plugins" || name == "config" || name == "cache" || name == "libraries" ||
                    name == "versions" || name == "backups" || name == "players" || name == "playerdata")
                    continue;
                CollectPlayerUuids(sub, results, depth + 1, maxDepth);
            }
    }

    /// <summary>
    ///     Read usercache.json from the server directory.
    ///     Returns a dict of UUID (dashes stripped, lowercase) → player name.
    ///     This file is written by Minecraft itself — no external API needed.
    /// </summary>
    private Dictionary<string, string> LoadUserCache(Server server)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = Path.Combine(App.ServerPath, server.Name, "usercache.json");
            if (!File.Exists(path)) return result;

            var entries = JsonConvert.DeserializeObject<List<UserCacheEntry>>(File.ReadAllText(path));
            if (entries == null) return result;

            foreach (UserCacheEntry entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.uuid) && !string.IsNullOrEmpty(entry.name))
                {
                    string stripped = entry.uuid.Replace("-", "").ToLowerInvariant();
                    if (!result.ContainsKey(stripped))
                        result[stripped] = entry.name;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerManager] Failed to read usercache.json for {server.Name}: {ex.Message}");
        }
        return result;
    }

    private class UserCacheEntry
    {
        public string name { get; set; }
        public string uuid { get; set; }
        public string expiresOn { get; set; }
    }

    private async Task<Player> CreatePlayer(string name)
    {
        Player player = new();
        await player.InitWithName(name);
        return player;
    }

    private async Task<Player> GetPlayerFromUuid(string uuid)
    {
        Player[] existingPlayers = PlayerSet.Where(player => player.Uid.Equals(uuid)).ToArray();
        if (existingPlayers.Any())
        {
            return existingPlayers.First();
        }

        Player p = await CreatePlayerFromUuid(uuid);
        if (p != null)
        {
            PlayerSet.Add(p);
            SafePlayersToFile();
        }
        else
        {
            DirectoryInfo playerDir = new(Path.Combine(App.ApplicationPath, "players", uuid));
            if (playerDir.Exists)
            {
                playerDir.Delete(true);
            }
        }

        return p;
    }

    private async Task<Player> CreatePlayerFromUuid(string uuid)
    {
        Player p = new();
        await p.InitWithUid(uuid);

        return CheckPlayerName(p.Name) ? p : null;
    }

    private void SafePlayersToFile()
    {
        string json = JsonConvert.SerializeObject(PlayerSet, Formatting.Indented);
        File.WriteAllText(PlayerJsonPath, json);
    }

    private HashSet<Player> InitializePlayerSet()
    {
        DirectoryInfo directoryInfo = Directory.CreateDirectory(Path.Combine(App.ApplicationPath, "players"));
        PlayerJsonPath = Path.Combine(directoryInfo.FullName, "players.json");
        if (!File.Exists(PlayerJsonPath))
        {
            return new HashSet<Player>();
        }

        string json = File.ReadAllText(PlayerJsonPath);
        return JsonConvert.DeserializeObject<HashSet<Player>>(json);
    }

    /// <summary>
    ///     Check if a given Player has a valid name
    ///     This is used to remove players with UUID name (This happens if UUID is not in mojang database)
    /// </summary>
    /// <param name="playerName">The player name to check</param>
    /// <returns>Validity of Player.Name</returns>
    private bool CheckPlayerName(string playerName)
    {
        Regex regex = new(@"^[A-Za-z0-9_]{3,16}$");
        return regex.Matches(playerName).Count != 0;
    }

    private bool ValidateUuid(string uuid)
    {
        try
        {
            byte[] ba = Enumerable.Range(0, uuid.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(uuid.Substring(x, 2), 16))
                .ToArray();

            return ba.Length == 16;
        }
        catch (Exception)
        {
            return false;
        }
    }

    #region Singleton

    //Lock to ensure Singleton pattern
    private static readonly object myLock = new();
    private static PlayerManager instance;

    public static PlayerManager Instance
    {
        get
        {
            if (instance == null)
            {
                lock (myLock)
                {
                    if (instance == null)
                    {
                        instance = new PlayerManager();
                    }
                }
            }

            return instance;
        }
    }

    private PlayerManager()
    {
        PlayerSet = InitializePlayerSet();

        Task.Run(async () =>
        {
            //Check for old player data and update
            bool anyUpdate = false;
            foreach (Player player in PlayerSet.Where(player =>
                         !player.OfflineChar && (DateTime.Now - player.LastUpdated).TotalDays > 7d))
            {
                await player.Update();
                Console.WriteLine("Updated data for player " + player);
                anyUpdate = true;
            }

            if (anyUpdate)
            {
                SafePlayersToFile();
            }
        });
    }

    #endregion
}
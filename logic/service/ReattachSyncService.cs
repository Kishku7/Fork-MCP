using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Fork.Logic.CustomConsole;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
using Fork.Logic.RoleManagement;
using Fork.ViewModel;

namespace Fork.Logic.Service;

/// <summary>
/// Synchronises Fork's in-memory player and role lists after re-attaching to a
/// running server via ForkGuard.  Called once from TryReattach after the pipe
/// and log tailer are both active.
/// </summary>
public static class ReattachSyncService
{
    /// <summary>
    /// Re-loads whitelist, banlist, and op list from disk (authoritative JSON
    /// files), then queries the live server for its current online player list.
    /// </summary>
    public static async Task SyncAsync(ServerViewModel viewModel)
    {
        ConsoleWriter.Write("[ForkGuard] Syncing server state (roles + online players)...", viewModel);

        // ── Clear existing role lists (avoid duplicates on repeated sync) ──────
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            viewModel.WhiteList.Clear();
            viewModel.BanList.Clear();
            viewModel.OPList.Clear();
        });

        // ── Re-load role lists from disk ──────────────────────────────────────
        // InitializeList is async void internally but blocks synchronously on
        // PlayerManager.GetPlayer().Result — run on a background thread so the
        // UI thread is never blocked while waiting for Mojang API responses.
        await Task.Run(() =>
        {
            RoleUpdater.InitializeList(RoleType.WHITELIST, viewModel.WhiteList, viewModel.Server);
            RoleUpdater.InitializeList(RoleType.BAN_LIST,  viewModel.BanList,   viewModel.Server);
            RoleUpdater.InitializeList(RoleType.OP_LIST,   viewModel.OPList,    viewModel.Server);
        });

        // ── Online player list via `list` command ─────────────────────────────
        await SyncOnlinePlayersAsync(viewModel);

        // ── Update IsOP on all online players ─────────────────────────────────
        // OPListChanged fires EvaluateOPs each time an op is added above, but
        // PlayerList may have been empty at that point. Re-evaluate now that
        // online players are populated.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (ServerPlayer sp in viewModel.PlayerList)
            {
                sp.IsOP = viewModel.OPList.Any(p =>
                    string.Equals(p.Name, sp.Player?.Name, StringComparison.OrdinalIgnoreCase));
            }
            viewModel.RefreshPlayerList();
        });

        ConsoleWriter.Write("[ForkGuard] State sync complete.", viewModel);
    }

    // ── Online player sync ────────────────────────────────────────────────────

    private static async Task SyncOnlinePlayersAsync(ServerViewModel viewModel)
    {
        if (viewModel.ConsoleReader == null) return;

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (ConsoleMessage msg in e.NewItems)
            {
                // Matches: "There are 2 of a max of 20 players online: Dave, Jeff"
                // and the singular: "There is 1 of a max of 20 player online: Dave"
                if (msg.Content.Contains("players online:", StringComparison.OrdinalIgnoreCase) ||
                    msg.Content.Contains("player online:",  StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult(msg.Content);
                    return;
                }
            }
        };

        // CollectionChanged fires on the UI thread — subscribe there.
        await Application.Current.Dispatcher.InvokeAsync(() =>
            viewModel.ConsoleOutList.CollectionChanged += handler);

        viewModel.ConsoleReader.Read("list", viewModel);

        // Wait up to 4 s for the server's response to appear in the console.
        await Task.WhenAny(tcs.Task, Task.Delay(4_000));

        await Application.Current.Dispatcher.InvokeAsync(() =>
            viewModel.ConsoleOutList.CollectionChanged -= handler);

        if (tcs.Task.IsCompletedSuccessfully)
            await ParseOnlinePlayersAsync(tcs.Task.Result, viewModel);
    }

    private static async Task ParseOnlinePlayersAsync(string line, ServerViewModel viewModel)
    {
        // Find the player name list after "players online:" or "player online:"
        int idx = line.IndexOf("players online:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            idx = line.IndexOf("player online:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;

        int colonIdx = line.IndexOf(':', idx);
        if (colonIdx < 0 || colonIdx + 1 >= line.Length) return;

        string namePart = line[(colonIdx + 1)..].Trim();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Mark all existing tracked players offline.
            foreach (ServerPlayer sp in viewModel.PlayerList)
                sp.IsOnline = false;

            if (!string.IsNullOrWhiteSpace(namePart))
            {
                string[] names = namePart.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (string name in names)
                {
                    ServerPlayer? existing = viewModel.PlayerList.FirstOrDefault(p =>
                        string.Equals(p.Player?.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        existing.IsOnline = true;
                    // Players not in PlayerList yet (connected after Fork last ran)
                    // will be picked up by RoleInputHandler on their next join event.
                }
            }

            viewModel.RefreshPlayerList();
        });
    }
}

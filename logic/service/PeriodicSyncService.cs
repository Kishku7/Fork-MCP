using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Fork.Logic.CustomConsole;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
using Fork.Logic.Persistence;
using Fork.ViewModel;

namespace Fork.Logic.Service;

/// <summary>
/// Runs a recurring save-all + state-sync cycle for re-attached servers.
/// One instance per server; started after a successful TryReattach and stopped
/// when the server stops.
/// </summary>
public static class PeriodicSyncService
{
    // Keyed by server UID so multiple servers can poll independently.
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActivePollers = new();

    /// <summary>
    /// Starts the periodic poll loop for <paramref name="viewModel"/>.
    /// If a loop is already running for this server it is cancelled first.
    /// Does nothing if <see cref="AppSettings.ReattachPollIntervalMinutes"/> is 0.
    /// </summary>
    public static void Start(ServerViewModel viewModel)
    {
        Stop(viewModel); // cancel any existing poller for this server

        int intervalMinutes = AppSettingsSerializer.Instance.AppSettings.ReattachPollIntervalMinutes;
        if (intervalMinutes <= 0) return;

        var cts = new CancellationTokenSource();
        ActivePollers[viewModel.Server.UID] = cts;

        Task.Run(() => PollLoop(viewModel, intervalMinutes, cts.Token));
    }

    /// <summary>
    /// Cancels the poll loop for <paramref name="viewModel"/> if one is running.
    /// </summary>
    public static void Stop(ServerViewModel viewModel)
    {
        if (ActivePollers.TryRemove(viewModel.Server.UID, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static async Task PollLoop(ServerViewModel viewModel, int intervalMinutes, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait the configured interval before the first (and each subsequent) cycle.
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), ct);

                if (viewModel.CurrentStatus != ServerStatus.RUNNING)
                    continue; // server paused, stopped, or still starting — skip this tick

                await RunSaveSyncCycleAsync(viewModel, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — loop was stopped via Stop().
        }
        catch (Exception ex)
        {
            ConsoleWriter.Write($"[ForkGuard] Periodic sync error: {ex.Message}", viewModel);
        }
    }

    private static async Task RunSaveSyncCycleAsync(ServerViewModel viewModel, CancellationToken ct)
    {
        if (viewModel.ConsoleReader == null) return;

        // ── 1. Send save-all and wait for confirmation ────────────────────────
        var saveTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        NotifyCollectionChangedEventHandler saveHandler = (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (ConsoleMessage msg in e.NewItems)
            {
                if (msg.Content.Contains("Saved the game", StringComparison.OrdinalIgnoreCase))
                {
                    saveTcs.TrySetResult(true);
                    return;
                }
            }
        };

        await Application.Current.Dispatcher.InvokeAsync(() =>
            viewModel.ConsoleOutList.CollectionChanged += saveHandler);

        viewModel.ConsoleReader.Read("save-all", viewModel);

        // Wait up to 30 s for the "Saved the game" confirmation.
        // If it times out we proceed anyway — don't block the sync indefinitely.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await saveTcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout or outer cancellation — proceed with sync regardless.
            if (ct.IsCancellationRequested) return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
            viewModel.ConsoleOutList.CollectionChanged -= saveHandler);

        // ── 2. Re-sync roles and online players ───────────────────────────────
        await ReattachSyncService.SyncAsync(viewModel);
    }
}

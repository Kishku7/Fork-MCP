using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
using Fork.Logic.Persistence;
using Fork.ViewModel;

namespace Fork.Logic.CustomConsole;

/// <summary>
/// Tails a log file and feeds new lines into a server's console view.
/// Used when Fork re-attaches to a server via ForkGuard after a restart —
/// the original stdout pipe is gone, so we tail logs/latest.log instead.
/// </summary>
public static class LogTailConsoleWriter
{
    /// <summary>
    /// Starts tailing <paramref name="logFile"/> in the background.
    /// <para>
    /// On start-up, backfills up to <see cref="AppSettings.MaxConsoleLines"/> historical
    /// lines from the file so the console shows recent history.  After the backfill,
    /// per-line event processing (RoleInputHandler) is enabled for all new lines.
    /// </para>
    /// <para>Stops when the viewModel's status becomes STOPPED.</para>
    /// </summary>
    public static void StartTailing(EntityViewModel viewModel, string logFile)
    {
        new Thread(() =>
        {
            // Wait up to 5 s for the log file to appear (e.g., server still starting).
            int waited = 0;
            while (!File.Exists(logFile) && waited < 5000 &&
                   viewModel.CurrentStatus != ServerStatus.STOPPED)
            {
                Thread.Sleep(200);
                waited += 200;
            }

            if (!File.Exists(logFile)) return;

            try
            {
                int maxLines = AppSettingsSerializer.Instance.AppSettings.MaxConsoleLines;

                // ── Step 1: Backfill historical lines ─────────────────────────────
                // Read the whole file and display the last maxLines entries so the
                // user can see recent output without replaying the full log through
                // the event pipeline.
                var history = new List<string>();
                try
                {
                    using var backfillFs = new FileStream(logFile, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var backfillReader = new StreamReader(backfillFs);
                    string? bl;
                    while ((bl = backfillReader.ReadLine()) != null)
                        history.Add(bl);
                }
                catch
                {
                    // File vanished or locked mid-read — skip backfill, start from end.
                }

                int startIdx = Math.Max(0, history.Count - maxLines);
                for (int i = startIdx; i < history.Count; i++)
                    viewModel.AddToConsole(new ConsoleMessage(history[i]));

                history.Clear(); // release memory before entering the tail loop

                // ── Step 2: Tail — open at EOF, process only new lines ────────────
                // Open with shared read/write so Minecraft can still write while we read.
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);

                // Seek to end — backfill already displayed history.
                fs.Seek(0, SeekOrigin.End);

                // Backfill is complete. All lines from this point forward are new and
                // eligible for per-line event processing via RoleInputHandler.
                bool catchupComplete = true;

                while (viewModel.CurrentStatus != ServerStatus.STOPPED)
                {
                    string? line = reader.ReadLine();
                    if (line is null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    bool isSuccess = false;
                    if (line.Contains("For help, type \"help\""))
                    {
                        viewModel.CurrentStatus = ServerStatus.RUNNING;
                        isSuccess = true;
                    }

                    // RoleInputHandler fires for new lines only (post-backfill).
                    // This detects join/leave, whitelist, ban, and op events in real time.
                    // catchupComplete is always true here; the flag is kept for clarity.
                    if (catchupComplete && viewModel is ServerViewModel sv)
                        sv.RoleInputHandler(line);

                    viewModel.AddToConsole(isSuccess
                        ? new ConsoleMessage(line, ConsoleMessage.MessageLevel.SUCCESS)
                        : new ConsoleMessage(line));
                }
            }
            catch (Exception ex)
            {
                ConsoleWriter.Write($"[ForkGuard] Log tailer error: {ex.Message}", viewModel);
            }
        }) { IsBackground = true, Name = "log-tailer" }.Start();
    }
}

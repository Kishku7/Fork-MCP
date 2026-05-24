using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
///
/// <para>
/// <b>Rotation-safe.</b> The file is re-opened <i>by path</i> on every poll
/// cycle and the consumed byte offset is tracked manually. When log4j2 rolls
/// latest.log over (e.g. at the midnight date boundary it renames the live file
/// to a dated <c>.log.gz</c> and creates a fresh empty <c>latest.log</c>), a
/// long-held file handle would keep reading the now-frozen old file forever.
/// By re-resolving the path each cycle and detecting the rollover via a
/// length-shrink, the tailer transparently re-attaches to the new file and
/// replays everything written since the rollover — which keeps console output
/// flowing and re-syncs player join/leave state instead of silently freezing.
/// </para>
/// </summary>
public static class LogTailConsoleWriter
{
    /// <summary>Poll interval (ms) while waiting for new data / checking for rotation.</summary>
    private const int PollMs = 200;

    /// <summary>Max bytes read per cycle, so a large backlog can't blow up memory.</summary>
    private const int MaxChunkBytes = 1 << 20; // 1 MiB

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
                // the event pipeline.  Record the byte offset we have consumed up to
                // so the tail loop resumes from exactly there.
                long position = 0;
                var history = new List<string>();
                try
                {
                    using var backfillFs = new FileStream(logFile, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var backfillReader = new StreamReader(backfillFs);
                    string? bl;
                    while ((bl = backfillReader.ReadLine()) != null)
                        history.Add(bl);
                    position = backfillFs.Length; // resume tailing from current EOF
                }
                catch
                {
                    // File vanished or locked mid-read — skip backfill, start from 0.
                    position = 0;
                }

                int startIdx = Math.Max(0, history.Count - maxLines);
                for (int i = startIdx; i < history.Count; i++)
                    viewModel.AddToConsole(new ConsoleMessage(history[i]));

                history.Clear(); // release memory before entering the tail loop

                // ── Step 2: Tail — rotation-aware, byte-offset based ──────────────
                // Each cycle we re-open the path (so we always read whatever file
                // currently lives at logFile, including a freshly rolled latest.log)
                // and read only complete lines beyond `position`.
                while (viewModel.CurrentStatus != ServerStatus.STOPPED)
                {
                    long length;
                    try
                    {
                        var fi = new FileInfo(logFile);
                        if (!fi.Exists)
                        {
                            // Brief gap during rotation (old renamed, new not yet created).
                            Thread.Sleep(PollMs);
                            continue;
                        }
                        length = fi.Length;
                    }
                    catch
                    {
                        Thread.Sleep(PollMs);
                        continue;
                    }

                    // Rotation / truncation: the file now at this path is shorter than
                    // what we have already consumed → it was replaced. Re-attach to the
                    // new file from its start so nothing written since the rollover is
                    // missed (this also replays join/leave events so player state
                    // re-syncs after the gap).
                    if (length < position)
                    {
                        ConsoleWriter.Write(
                            "[ForkGuard] Detected log rotation — re-attaching tail to new latest.log.",
                            viewModel);
                        position = 0;
                    }

                    if (length == position)
                    {
                        Thread.Sleep(PollMs);
                        continue;
                    }

                    // Read everything new, but only up to the last complete line — the
                    // file may end mid-line while Minecraft is still writing it.
                    string chunk;
                    long consumed;
                    try
                    {
                        using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        fs.Seek(position, SeekOrigin.Begin);

                        int toRead = (int)Math.Min(length - position, MaxChunkBytes);
                        var buffer = new byte[toRead];
                        int read = fs.Read(buffer, 0, toRead);
                        if (read <= 0)
                        {
                            // Seek landed past EOF (e.g. file replaced between the
                            // length check and the open) — let the next cycle detect it.
                            Thread.Sleep(PollMs);
                            continue;
                        }

                        int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                        if (lastNewline < 0)
                        {
                            // No complete line available yet — wait for more bytes.
                            Thread.Sleep(PollMs);
                            continue;
                        }

                        chunk = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
                        consumed = lastNewline + 1; // bytes up to and including last '\n'
                    }
                    catch
                    {
                        Thread.Sleep(PollMs);
                        continue;
                    }

                    position += consumed;

                    foreach (string raw in chunk.Split('\n'))
                    {
                        // Split on '\n' yields a trailing empty element after the final
                        // newline; TrimEnd removes the '\r' on CRLF files.
                        string line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;

                        bool isSuccess = false;
                        if (line.Contains("For help, type \"help\""))
                        {
                            viewModel.CurrentStatus = ServerStatus.RUNNING;
                            isSuccess = true;
                        }

                        // RoleInputHandler fires for new lines only (post-backfill).
                        // This detects join/leave, whitelist, ban, and op events in real time.
                        if (viewModel is ServerViewModel sv)
                            sv.RoleInputHandler(line);

                        viewModel.AddToConsole(isSuccess
                            ? new ConsoleMessage(line, ConsoleMessage.MessageLevel.SUCCESS)
                            : new ConsoleMessage(line));
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleWriter.Write($"[ForkGuard] Log tailer error: {ex.Message}", viewModel);
            }
        }) { IsBackground = true, Name = "log-tailer" }.Start();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Fork.Logic.CustomConsole;
using Fork.Logic.Logging;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
using Fork.Logic.Persistence;
using Fork.Logic.Utils;

namespace Fork.ViewModel;

/// <summary>
/// Console I/O, throttling, query filtering and on-demand scrollback for EntityViewModel.
/// While following live output only a small window of lines is held in memory; older lines
/// stay in the server's log file and are paged in when the user scrolls back to the top.
/// </summary>
public abstract partial class EntityViewModel
{
    private int consoleMessagesLastSecond;
    private readonly List<ConsoleMessage> consoleOutListNoQuery = new();
    private string currentQuery = "";
    private ConsoleMessage lastConsoleMessage = new("");
    private ICommand readConsoleIn;
    private readonly Stopwatch timeSinceLastConsoleMessage = new();

    // ---- scrollback paging state (guarded by lock(this)) ----
    private bool consolePinnedToBottom = true;
    private long? historyStartOffset; // file offset of the oldest line held in memory; null = unknown
    private bool historyExhausted;
    private int historyLoading; // 1 while a LoadOlderConsoleLines call is running

    public IConsoleReader? ConsoleReader;

    public ObservableCollection<ConsoleMessage> ConsoleOutList { get; set; }

    public AppSettings AppSettings => AppSettingsSerializer.Instance.AppSettings;

    /// <summary>Log file older console lines are paged in from. Null = this console has no history source.</summary>
    protected virtual string? ConsoleHistoryFile => null;

    /// <summary>
    /// Lines kept in memory while following live output. Consoles with a history file keep
    /// only a small window; the rest is read from disk on demand.
    /// </summary>
    public int ConsoleWindowLines =>
        ConsoleHistoryFile != null
            ? Math.Max(200, Math.Min(AppSettings.ConsoleMemoryLines, AppSettings.MaxConsoleLines))
            : AppSettings.MaxConsoleLines;

    public string ConsoleIn { get; set; } = "";

    public ICommand ReadConsoleIn
    {
        get
        {
            return readConsoleIn ??= new ActionCommand(() =>
            {
                ConsoleReader?.Read(ConsoleIn, this);
                ConsoleIn = "";
            });
        }
    }

    private void InitializeConsole()
    {
        // Background thread resets the throttle counter once per second
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(1000);
                consoleMessagesLastSecond = 0;
            }
        }) { IsBackground = true }.Start();

        ConsoleOutList = new ObservableCollection<ConsoleMessage>();
    }

    /// <summary>
    /// The console page tells the view model whether it is following the newest line.
    /// Old lines are only discarded from memory while following; a user who has scrolled
    /// back keeps what they are reading.
    /// </summary>
    public void SetConsolePinnedToBottom(bool pinned)
    {
        lock (this)
        {
            consolePinnedToBottom = pinned;
        }
    }

    public void AddToConsole(ConsoleMessage message)
    {
        lock (this)
        {
            if (AppSettings.ConsoleThrottling && message.Level == ConsoleMessage.MessageLevel.INFO)
            {
                int threshold =
                    (int)Math.Round(Math.Min(lastConsoleMessage.Content.Length, message.Content.Length) * 0.10);
                int dist = StringUtils.DamerauLevenshteinDistance(lastConsoleMessage.Content,
                    message.Content, threshold);
                if (dist < threshold * 5 / Math.Max(timeSinceLastConsoleMessage.Elapsed.TotalSeconds, 1))
                {
                    lastConsoleMessage.SubContents++;
                    return;
                }

                if (consoleMessagesLastSecond > AppSettings.MaxConsoleLinesPerSecond)
                {
                    return;
                }
            }

            try
            {
                if (CurrentStatus == ServerStatus.RUNNING)
                {
                    consoleMessagesLastSecond++;
                }

                lastConsoleMessage = message;
                consoleOutListNoQuery.Add(message);
                if (MatchesQuery(message))
                {
                    // InvokeAsync (fire-and-forget) so the background reader thread is never
                    // blocked waiting for a UI layout pass to complete.
                    Application.Current?.Dispatcher?.InvokeAsync(() => ConsoleOutList.Add(message),
                        DispatcherPriority.Background);
                    timeSinceLastConsoleMessage.Restart();
                }

                TrimConsoleIfNeeded();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while adding line to console: " + message);
                ErrorLogger.Append(e);
            }
        }
    }

    private bool MatchesQuery(ConsoleMessage message)
    {
        return currentQuery.Length == 0 ||
               message.Content.Contains(currentQuery, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Drops the oldest lines once the buffer is a quarter-window over its limit, in one
    /// batch (amortised O(1) per line). Caller holds lock(this).
    /// </summary>
    private void TrimConsoleIfNeeded()
    {
        int limit = consolePinnedToBottom ? ConsoleWindowLines : AppSettings.MaxConsoleLines;
        int slack = Math.Max(50, limit / 4);
        if (consoleOutListNoQuery.Count <= limit + slack) return;

        int drop = consoleOutListNoQuery.Count - limit;
        HashSet<ConsoleMessage> dropped = new(consoleOutListNoQuery.GetRange(0, drop));
        consoleOutListNoQuery.RemoveRange(0, drop);

        // The oldest line in memory changed, so the remembered file position is stale.
        historyStartOffset = null;

        // Messages reach the screen in the same order they are stored, so the dropped
        // ones are always the front of the UI list. Removed in one dispatcher job.
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            while (ConsoleOutList.Count > 0 && dropped.Contains(ConsoleOutList[0]))
                ConsoleOutList.RemoveAt(0);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Bulk-loads historical lines into the in-memory buffer (newest <see cref="ConsoleWindowLines"/>)
    /// while rendering only the last <paramref name="renderLines"/> lines to the console UI.
    /// Used by the re-attach log backfill. <paramref name="firstLineOffset"/> is the file offset
    /// of messages[0] when known, so older history can be paged in without a search.
    /// </summary>
    public void BackfillConsole(IReadOnlyList<ConsoleMessage> messages, int renderLines,
        long firstLineOffset = -1)
    {
        if (messages == null || messages.Count == 0) return;

        lock (this)
        {
            try
            {
                foreach (ConsoleMessage message in messages)
                    consoleOutListNoQuery.Add(message);

                historyStartOffset = firstLineOffset >= 0 && consoleOutListNoQuery.Count == messages.Count
                    ? firstLineOffset
                    : null;

                int overflow = consoleOutListNoQuery.Count - ConsoleWindowLines;
                if (overflow > 0)
                {
                    consoleOutListNoQuery.RemoveRange(0, overflow);
                    historyStartOffset = null;
                }

                lastConsoleMessage = messages[messages.Count - 1];

                // UI: render only the newest renderLines lines, in one dispatcher batch.
                int renderStart = Math.Max(0, messages.Count - renderLines);
                var toRender = new List<ConsoleMessage>(messages.Count - renderStart);
                for (int i = renderStart; i < messages.Count; i++)
                {
                    if (MatchesQuery(messages[i]))
                        toRender.Add(messages[i]);
                }

                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    foreach (ConsoleMessage message in toRender)
                        ConsoleOutList.Add(message);
                }, DispatcherPriority.Background);

                timeSinceLastConsoleMessage.Restart();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while backfilling console (" + messages.Count + " lines)");
                ErrorLogger.Append(e);
            }
        }
    }

    private readonly record struct HistoryPage(List<string> Lines, long Start, bool Reached);

    /// <summary>
    /// Pages up to <paramref name="count"/> older lines from the log file in front of what the
    /// console holds. Returns how many lines were added (0 = nothing older / already loading).
    /// Call from the UI thread; the disk read runs on a worker thread.
    /// </summary>
    public async Task<int> LoadOlderConsoleLines(int count)
    {
        string? file = ConsoleHistoryFile;
        if (file == null || count <= 0) return 0;
        if (Interlocked.CompareExchange(ref historyLoading, 1, 0) != 0) return 0;

        try
        {
            List<string> oldestInMemory;
            long? knownStart;
            lock (this)
            {
                if (historyExhausted) return 0;
                knownStart = historyStartOffset;
                oldestInMemory = new List<string>();
                for (int i = 0; i < consoleOutListNoQuery.Count && i < 21; i++)
                    oldestInMemory.Add(consoleOutListNoQuery[i].Content);
            }

            HistoryPage? page = await Task.Run(() => ReadHistoryPage(file, knownStart, oldestInMemory, count));

            if (page == null || page.Value.Lines.Count == 0)
            {
                lock (this) historyExhausted = true;
                return 0;
            }

            List<ConsoleMessage> older = new(page.Value.Lines.Count);
            foreach (string line in page.Value.Lines) older.Add(new ConsoleMessage(line));

            lock (this)
            {
                consoleOutListNoQuery.InsertRange(0, older);
                historyStartOffset = page.Value.Start;
                if (page.Value.Reached) historyExhausted = true;

                List<ConsoleMessage> visible = currentQuery.Length == 0 ? older : older.FindAll(MatchesQuery);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    for (int i = visible.Count - 1; i >= 0; i--)
                        ConsoleOutList.Insert(0, visible[i]);
                }, DispatcherPriority.Send);
            }

            return older.Count;
        }
        finally
        {
            Interlocked.Exchange(ref historyLoading, 0);
        }
    }

    private static HistoryPage? ReadHistoryPage(string file, long? knownStart, List<string> oldestInMemory,
        int count)
    {
        try
        {
            if (!File.Exists(file)) return null;

            long start;
            if (knownStart.HasValue)
            {
                start = knownStart.Value;
            }
            else if (oldestInMemory.Count == 0)
            {
                start = new FileInfo(file).Length;
            }
            else if (!LogHistoryReader.TryLocate(file, oldestInMemory, 16L << 20, out start))
            {
                return null; // the log was rotated since these lines were written
            }

            if (start <= 0) return new HistoryPage(new List<string>(), 0, true);

            List<string> lines = LogHistoryReader.ReadLinesBefore(file, start, count, out long newStart);
            return new HistoryPage(lines, newStart, newStart <= 0);
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            return null;
        }
    }

    public void ApplySearchQueryToConsole(string query)
    {
        lock (this)
        {
            currentQuery = query ?? "";
            List<ConsoleMessage> visible = currentQuery.Length == 0
                ? new List<ConsoleMessage>(consoleOutListNoQuery)
                : consoleOutListNoQuery.FindAll(MatchesQuery);

            // Queued inside the lock so it lands after every earlier per-line Add and before
            // every later one, whatever order the dispatcher runs them in.
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                ConsoleOutList.Clear();
                foreach (ConsoleMessage m in visible) ConsoleOutList.Add(m);
            }, DispatcherPriority.Background);
        }
    }

    public void ClearConsole()
    {
        lock (this)
        {
            consoleOutListNoQuery.Clear();
            historyStartOffset = null;
            historyExhausted = true; // cleared means cleared: do not page the old lines back in
        }

        Application.Current.Dispatcher.Invoke(() => ConsoleOutList.Clear());
    }
}

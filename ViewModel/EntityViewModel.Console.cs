using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
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
/// Console I/O, throttling, and query filtering for EntityViewModel.
/// </summary>
public abstract partial class EntityViewModel
{
    private int consoleMessagesLastSecond;
    private readonly List<ConsoleMessage> consoleOutListNoQuery = new();
    private readonly HashSet<ConsoleMessage> consoleOutListSet    = new(); // O(1) membership mirror of ConsoleOutList
    private string currentQuery = "";
    private ConsoleMessage lastConsoleMessage = new("");
    private ICommand readConsoleIn;
    private readonly Stopwatch timeSinceLastConsoleMessage = new();

    public IConsoleReader? ConsoleReader;

    public ObservableCollection<ConsoleMessage> ConsoleOutList { get; set; }

    public AppSettings AppSettings => AppSettingsSerializer.Instance.AppSettings;

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
        ConsoleOutList.CollectionChanged += ConsoleOutChanged;
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
                if (message.Content.Contains(currentQuery))
                {
                    // InvokeAsync (fire-and-forget) so the background reader thread is never
                    // blocked waiting for a UI layout pass to complete.
                    Application.Current?.Dispatcher?.InvokeAsync(() => ConsoleOutList.Add(message),
                        DispatcherPriority.Background);
                    timeSinceLastConsoleMessage.Restart();
                }

                while (consoleOutListNoQuery.Count > AppSettings.MaxConsoleLines)
                {
                    ConsoleMessage messageToDelete = consoleOutListNoQuery[0];
                    if (consoleOutListSet.Contains(messageToDelete)) // O(1) — was O(n) ConsoleOutList.Contains
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() => ConsoleOutList.RemoveAt(0),
                            DispatcherPriority.Background);
                    }

                    consoleOutListNoQuery.RemoveAt(0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while adding line to console: " + message);
                ErrorLogger.Append(e);
            }
        }
    }

    /// <summary>
    /// Bulk-loads historical lines into the in-memory scrollback buffer
    /// (<see cref="consoleOutListNoQuery"/>, capped at MaxConsoleLines) while rendering
    /// only the last <paramref name="renderLines"/> lines to the console UI.
    /// Used by the re-attach log backfill — history is for scrollback and search,
    /// not for replaying onto the screen. Bypasses throttling and per-line dispatch.
    /// </summary>
    public void BackfillConsole(IReadOnlyList<ConsoleMessage> messages, int renderLines)
    {
        if (messages == null || messages.Count == 0) return;

        lock (this)
        {
            try
            {
                // Memory buffer: append everything, then trim from the front to the cap.
                foreach (ConsoleMessage message in messages)
                    consoleOutListNoQuery.Add(message);

                int overflow = consoleOutListNoQuery.Count - AppSettings.MaxConsoleLines;
                if (overflow > 0)
                    consoleOutListNoQuery.RemoveRange(0, overflow);

                lastConsoleMessage = messages[messages.Count - 1];

                // UI: render only the newest renderLines lines, in one dispatcher batch.
                int renderStart = Math.Max(0, messages.Count - renderLines);
                var toRender = new List<ConsoleMessage>(messages.Count - renderStart);
                for (int i = renderStart; i < messages.Count; i++)
                {
                    if (messages[i].Content.Contains(currentQuery))
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

    public void ApplySearchQueryToConsole(string query)
    {
        if (query.Equals(""))
        {
            ResetConsoleOutList();
        }
        else if (query.StartsWith(currentQuery))
        {
            RemoveNotMatchingMessages(query);
        }
        else
        {
            ResetConsoleOutList();
            RemoveNotMatchingMessages(query);
        }

        currentQuery = query;
    }

    public void ClearConsole()
    {
        Application.Current.Dispatcher.Invoke(() => ConsoleOutList.Clear());
    }

    private void ConsoleOutChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep consoleOutListSet in sync so trim-loop Contains checks are O(1).
        // raisePropertyChanged(ConsoleOutList) is NOT needed here — WPF data binding
        // responds directly to INotifyCollectionChanged without a redundant property notification.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                    foreach (ConsoleMessage m in e.NewItems) consoleOutListSet.Add(m);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                    foreach (ConsoleMessage m in e.OldItems) consoleOutListSet.Remove(m);
                break;
            case NotifyCollectionChangedAction.Reset:
                consoleOutListSet.Clear();
                break;
        }
    }

    private void RemoveNotMatchingMessages(string query)
    {
        List<ConsoleMessage> original = new(ConsoleOutList);
        foreach (ConsoleMessage consoleMessage in original)
            if (!consoleMessage.Content.ToLower().Contains(query.ToLower()))
            {
                Application.Current.Dispatcher?.Invoke(() => ConsoleOutList.Remove(consoleMessage),
                    DispatcherPriority.Send);
            }
    }

    private void ResetConsoleOutList()
    {
        for (int i = 0; i < consoleOutListNoQuery.Count; i++)
        {
            if (ConsoleOutList.Count <= i || consoleOutListNoQuery[i] != ConsoleOutList[i])
            {
                int i1 = i;
                Application.Current.Dispatcher?.Invoke(() => ConsoleOutList.Insert(i1, consoleOutListNoQuery[i1]));
            }
        }
    }
}

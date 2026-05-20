using System;
using System.IO;
using System.Threading;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
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
    /// Seeks to the end of the file immediately (doesn't replay old output).
    /// Stops when the viewModel's status becomes STOPPED.
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
                // Open with shared read/write so Minecraft can still write while we read.
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);

                // Seek to end — we only want new output from this point forward.
                fs.Seek(0, SeekOrigin.End);

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

                    // RoleInputHandler omitted — role state is loaded from files at startup.
                    // Re-enabling it here would spawn a Task.Run per log line during re-attach.

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

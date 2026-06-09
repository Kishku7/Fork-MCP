using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fork.Logic.BackgroundWorker;
using Fork.Logic.Controller;
using Fork.Logic.CustomConsole;
using Fork.Logic.Logging;
using Fork.Logic.Model;
using Fork.Logic.Model.ServerConsole;
using Fork.Logic.Service;
using Fork.Logic.Utils;
using Fork.Logic.WebRequesters;
using Fork.ViewModel;
using JavaDiscovery = Fork.Logic.Service.JavaDiscoveryService;

namespace Fork.Logic.Manager;

/// <summary>
/// Handles server process lifecycle: start, stop, restart, kill, and version change.
/// Extracted from ServerManager. Resource pack hashing delegated to ResourcePackService.
/// </summary>
public sealed class ServerLifecycleManager
{
    private static ServerLifecycleManager instance;

    private ServerLifecycleManager() { }

    public static ServerLifecycleManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new ServerLifecycleManager();
            }
            return instance;
        }
    }

    public async Task<bool> StartServerAsync(ServerViewModel viewModel)
    {
        viewModel.StopRequested = false;
        viewModel.CrashedLastExit = false;
        viewModel.LastExitInfo = null;
        ConsoleWriter.Write("\n", viewModel);
        ConsoleWriter.Write("Saving settings files before starting server ...", viewModel);
        await Task.Run(async () => await viewModel.SettingsSavingTask);

        ConsoleWriter.Write(
            "Starting server " + viewModel.Server + " on world: " + viewModel.Server.ServerSettings.LevelName,
            viewModel);
        Console.WriteLine("Starting server " + viewModel.Server.Name + " on world: " +
                          viewModel.Server.ServerSettings.LevelName);

        DirectoryInfo directoryInfo = new(Path.Combine(App.ServerPath, viewModel.Server.Name));
        if (!directoryInfo.Exists)
        {
            return false;
        }

        // Resolve the Java binary through the discovery service so the correct
        // version is always used regardless of system PATH order.
        string resolvedJavaPath = JavaDiscovery.Instance
            .GetBestForMajor(viewModel.Server.JavaSettings.PreferredMajorVersion)
            ?.BinaryPath ?? "java.exe";

        JavaVersion javaVersion = JavaVersionUtils.GetInstalledJavaVersion(resolvedJavaPath);
        if (javaVersion == null)
        {
            ConsoleWriter.Write("ERROR: Java is not installed! Minecraft servers require Java!", viewModel);
            return false;
        }

        if (!javaVersion.Is64Bit)
        {
            ConsoleWriter.Write(
                "WARN: The Java installation selected for this server is a 32-bit version, which can cause errors.",
                viewModel);
        }

        if (javaVersion.VersionComputed < 16)
        {
            if (new ServerVersion { Version = "1.17" }.CompareTo(viewModel.Entity.Version) <= 0)
            {
                ConsoleWriter.Write(
                    "ERROR: The Java installation selected for this server is outdated. Please update Java to version 16 or higher.",
                    viewModel);
                return false;
            }

            ConsoleWriter.Write(
                "WARN: The Java installation selected for this server is outdated. Please update Java to version 16 or higher.",
                viewModel);
        }

        if (!viewModel.Server.ServerSettings.ResourcePack.Equals("") && viewModel.Server.AutoSetSha1)
        {
            ConsoleWriter.Write(new ConsoleMessage("Generating Resource Pack hash...",
                ConsoleMessage.MessageLevel.INFO), viewModel);
            string resourcePackUrl = viewModel.Server.ServerSettings.ResourcePack.Replace("\\", "");
            bool isHashUpToDate = await ResourcePackService.IsHashUpToDate(
                viewModel.Server.ResourcePackHashAge, resourcePackUrl);

            if (!string.IsNullOrEmpty(viewModel.Server.ServerSettings.ResourcePackSha1) && isHashUpToDate)
            {
                ConsoleWriter.Write(new ConsoleMessage("Resource Pack hash is still up to date. Staring server...",
                    ConsoleMessage.MessageLevel.SUCCESS), viewModel);
            }
            else
            {
                ConsoleWriter.Write(new ConsoleMessage("Resource Pack hash is outdated. Updating it...",
                    ConsoleMessage.MessageLevel.WARN), viewModel);
                DateTime hashAge = DateTime.Now;
                IProgress<double> downloadProgress = new Progress<double>();
                string hash = await ResourcePackService.HashResourcePack(resourcePackUrl, downloadProgress);
                if (!string.IsNullOrEmpty(hash))
                {
                    viewModel.Server.ServerSettings.ResourcePackSha1 = hash;
                    viewModel.Server.ResourcePackHashAge = hashAge;
                    await viewModel.SaveProperties();
                    ConsoleWriter.Write(new ConsoleMessage("Successfully updated Resource Pack hash to: " + hash,
                        ConsoleMessage.MessageLevel.SUCCESS), viewModel);
                    ConsoleWriter.Write(new ConsoleMessage("Starting the server...",
                        ConsoleMessage.MessageLevel.INFO), viewModel);
                }
                else
                {
                    ConsoleWriter.Write(new ConsoleMessage(
                        "Error updating the Resource Pack hash! Continuing with no hash...",
                        ConsoleMessage.MessageLevel.ERROR), viewModel);
                }
            }
        }

        // ── Determine whether ForkGuard is available ──────────────────────────
        string forkGuardPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ForkGuard.exe");
        bool   useGuard      = File.Exists(forkGuardPath);

        string javaArguments =
            $"-Xmx{viewModel.Server.JavaSettings.MaxRam}m " +
            $"{viewModel.Server.JavaSettings.StartupParameters} " +
            $"-jar server.jar nogui";

        Process process = new();
        Process perfProcess = null; // process to track for CPU/mem/disk — Java itself, not the guard

        if (useGuard)
        {
            // Generate a unique pipe name from the current epoch (seconds).
            string pipeName = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // ForkGuard receives: <pipeName> <workingDir> <javaPath> <javaArgs...>
            string guardArgs =
                $"{pipeName} " +
                $"\"{directoryInfo.FullName}\" " +
                $"\"{resolvedJavaPath}\" " +
                $"{javaArguments}";

            process.StartInfo = new ProcessStartInfo
            {
                FileName               = forkGuardPath,
                Arguments              = guardArgs,
                WorkingDirectory       = directoryInfo.FullName,
                UseShellExecute        = false,
                RedirectStandardInput  = true,   // ForkGuard forwards to Java stdin
                RedirectStandardOutput = true,   // ForkGuard echoes Java stdout
                RedirectStandardError  = true,   // ForkGuard echoes Java stderr
                CreateNoWindow         = true,
                WindowStyle            = ProcessWindowStyle.Hidden,
            };
            process.Start();

            // Wait for ForkGuard to write the marker file (Java has started).
            string markerFile = Path.Combine(directoryInfo.FullName, "fork-guard.marker");
            int    waited     = 0;
            while (!File.Exists(markerFile) && waited < 10_000 && !process.HasExited)
            {
                await Task.Delay(100);
                waited += 100;
            }
            if (!File.Exists(markerFile))
            {
                ConsoleWriter.Write("WARN: ForkGuard did not initialise within 10 s — proceeding anyway.", viewModel);
            }
            else
            {
                // Performance tracking must target the Java child (marker line 2), not the
                // guard — the guard is a tiny stub, so tracking it shows 0% CPU/memory.
                try
                {
                    string[] markerLines = File.ReadAllLines(markerFile);
                    if (markerLines.Length >= 2 && int.TryParse(markerLines[1].Trim(), out int javaPid))
                    {
                        Process javaProcess = Process.GetProcessById(javaPid);
                        if (!javaProcess.HasExited &&
                            javaProcess.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
                        {
                            perfProcess = javaProcess;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWriter.Write(
                        $"WARN: Could not resolve Java PID for performance tracking — metrics will track the guard process ({ex.Message}).",
                        viewModel);
                }
            }

            // ConsoleReader routes commands to ForkGuard's stdin (it forwards to Java).
            viewModel.ConsoleReader = new ConsoleReader(process.StandardInput);

            ConsoleWriter.Write("[ForkGuard] Guardian active — server is re-attachable.", viewModel);
        }
        else
        {
            // Fallback: spawn Java directly (no re-attach capability).
            // Note: WindowStyle is intentionally omitted — it has no effect when
            // UseShellExecute = false. CreateNoWindow = true suppresses the window.
            process.StartInfo = new ProcessStartInfo
            {
                UseShellExecute        = false,
                RedirectStandardError  = true,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                FileName               = resolvedJavaPath,
                WorkingDirectory       = directoryInfo.FullName,
                Arguments              = javaArguments,
                CreateNoWindow         = true,
            };
            process.Start();
            viewModel.ConsoleReader = new ConsoleReader(process.StandardInput);
        }

        // ── Common startup wiring ─────────────────────────────────────────────
        Task.Run(() => { viewModel.TrackPerformance(perfProcess ?? process); });
        viewModel.CurrentStatus = ServerStatus.STARTING;
        ConsoleWriter.RegisterApplication(viewModel, process.StandardOutput, process.StandardError);
        ServerAutomationManager.Instance.UpdateAutomation(viewModel);

        Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fork] WaitForExitAsync exception for '{viewModel.Name}': {ex.Message}");
            }
            finally
            {
                // Always reconcile state — covers normal exits, force-kills, and crashes.
                ApplicationManager.Instance.ActiveEntities.Remove(viewModel.Server);
                EvaluateExit(viewModel, process, System.IO.Path.Combine(App.ServerPath, viewModel.Server.Name));
                viewModel.CurrentStatus = ServerStatus.STOPPED;
                viewModel.ConsoleReader = null;
                ServerAutomationManager.Instance.UpdateAutomation(viewModel);
            }
        });

        ApplicationManager.Instance.ActiveEntities[viewModel.Server] = process;
        Task.Run(async () =>
        {
            QueryStatsWorker worker = new(viewModel);
            await process.WaitForExitAsync();
            worker.Dispose();
        });
        Console.WriteLine("Started server " + viewModel.Server);

        // Register new world if created during first start
        Task.Run(async () =>
        {
            while (!viewModel.ServerRunning) await Task.Delay(500);
            viewModel.InitializeWorldsList();
        });

        return true;
    }

    /// <summary>
    /// Attempts to re-attach to a server that was started under ForkGuard in a
    /// previous Fork session.  Reads fork-guard.marker from the server directory;
    /// if Java is still alive sets the VM to RUNNING and wires up a log tailer +
    /// named-pipe command reader.
    /// </summary>
    public void TryReattach(ServerViewModel viewModel)
    {
        string serverPath = Path.Combine(App.ServerPath, viewModel.Server.Name);
        string markerFile = Path.Combine(serverPath, "fork-guard.marker");

        if (!File.Exists(markerFile))
        {
            // No marker — server was not running under ForkGuard (or was stopped cleanly).
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(markerFile);
            if (lines.Length < 2)
            {
                ConsoleWriter.Write("[ForkGuard] Re-attach failed — marker file is malformed.", viewModel);
                return;
            }

            string pipeName = lines[0].Trim();
            if (!int.TryParse(lines[1].Trim(), out int javaPid))
            {
                ConsoleWriter.Write("[ForkGuard] Re-attach failed — could not parse Java PID from marker file.", viewModel);
                return;
            }

            // Check whether Java is still alive.
            Process javaProcess;
            try   { javaProcess = Process.GetProcessById(javaPid); }
            catch
            {
                ConsoleWriter.Write($"[ForkGuard] Re-attach failed — Java process (PID {javaPid}) is no longer running.", viewModel);
                return;
            }

            if (javaProcess.HasExited)
            {
                ConsoleWriter.Write($"[ForkGuard] Re-attach failed — Java process (PID {javaPid}) has already exited.", viewModel);
                return;
            }

            // Guard against PID recycling: verify the process is actually Java.
            if (!javaProcess.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleWriter.Write(
                    $"[ForkGuard] Re-attach aborted — PID {javaPid} is now '{javaProcess.ProcessName}', not Java. PID was recycled.",
                    viewModel);
                return;
            }

            // ── Re-attach ──────────────────────────────────────────────────────
            viewModel.CurrentStatus = ServerStatus.RUNNING;

            // Commands via named pipe to ForkGuard.
            viewModel.ConsoleReader = new GuardedConsoleReader(pipeName);

            // Console display: tail logs/latest.log (stdout pipe is gone).
            string logFile = Path.Combine(serverPath, "logs", "latest.log");
            LogTailConsoleWriter.StartTailing(viewModel, logFile);

            // Also tail fork-guard.log for stderr output.
            string guardLog = Path.Combine(serverPath, "fork-guard.log");
            LogTailConsoleWriter.StartTailing(viewModel, guardLog);

            // Wire up performance tracking so CPU/memory metrics are live after re-attach.
            Task.Run(() => viewModel.TrackPerformance(javaProcess));

            // Register in ActiveEntities so KillEntity still works (kills Java directly).
            ApplicationManager.Instance.ActiveEntities[viewModel.Server] = javaProcess;

            // Monitor for Java exit.
            Task.Run(async () =>
            {
                await javaProcess.WaitForExitAsync();
                PeriodicSyncService.Stop(viewModel);
                ApplicationManager.Instance.ActiveEntities.Remove(viewModel.Server);
                EvaluateExit(viewModel, javaProcess, serverPath);
                viewModel.CurrentStatus = ServerStatus.STOPPED;
                viewModel.ConsoleReader = null;
                ServerAutomationManager.Instance.UpdateAutomation(viewModel);
            });

            // Sync state and start periodic polling on a background thread.
            // Delay briefly so the log tailer has time to warm up and the console
            // has flushed backfill before we send `list` and wait for its response.
            Task.Run(async () =>
            {
                await Task.Delay(1_500);
                await ReattachSyncService.SyncAsync(viewModel);
                PeriodicSyncService.Start(viewModel);
            });

            ConsoleWriter.Write(
                $"[ForkGuard] Re-attached to running server (Java PID {javaPid}).", viewModel);
        }
        catch (Exception ex)
        {
            ErrorLogger.Append(ex);
        }
    }

    public void StopServer(ServerViewModel serverViewModel)
    {
        serverViewModel.StopRequested = true;
        if (serverViewModel.ConsoleReader != null)
        {
            // Routes to ForkGuard stdin (normal) or named pipe (re-attached).
            serverViewModel.ConsoleReader.Read("stop", serverViewModel);
        }
        else if (ApplicationManager.Instance.ActiveEntities.ContainsKey(serverViewModel.Server))
        {
            // Fallback for processes started without ForkGuard.
            Console.WriteLine("Can't stop server that has no active process");
            ApplicationManager.Instance.ActiveEntities[serverViewModel.Server]
                .StandardInput.WriteLine("stop");
        }
        else
        {
            Console.WriteLine("Can't stop server that has no active process");
            return;
        }

        foreach (ServerPlayer serverPlayer in serverViewModel.PlayerList)
            serverPlayer.IsOnline = false;
        serverViewModel.RefreshPlayerList();
    }

    public bool RestartServer(ServerViewModel serverViewModel)
    {
        StopServer(serverViewModel);
        while (serverViewModel.CurrentStatus != ServerStatus.STOPPED) Thread.Sleep(500);
        Task.Run(async () => await StartServerAsync(serverViewModel));
        return true;
    }

    public async Task<bool> RestartServerAsync(ServerViewModel serverViewModel)
    {
        Task<bool> t = new(() => RestartServer(serverViewModel));
        t.Start();
        bool result = await t;
        return result;
    }

    public bool KillEntity(EntityViewModel entityViewModel)
    {
        entityViewModel.StopRequested = true;
        Process process = ApplicationManager.Instance.ActiveEntities[entityViewModel.Entity];
        try
        {
            process.Kill(true);
            ConsoleWriter.Write("Killed server " + entityViewModel.Entity, entityViewModel);
            Console.WriteLine("Killed server " + entityViewModel.Entity);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.StackTrace);
            return false;
        }
        return true;
    }

    public async Task<bool> ChangeServerVersionAsync(ServerVersion newVersion, ServerViewModel serverViewModel)
    {
        Task<bool> t = new(() => ChangeServerVersion(newVersion, serverViewModel));
        t.Start();
        bool result = await t;
        return result;
    }

    private bool ChangeServerVersion(ServerVersion newVersion, ServerViewModel serverViewModel)
    {
        try
        {
            if (serverViewModel.CurrentStatus != ServerStatus.STOPPED)
            {
                StopServer(serverViewModel);
                while (serverViewModel.CurrentStatus != ServerStatus.STOPPED) Thread.Sleep(500);
            }

            serverViewModel.DownloadCompleted = false;

            // Delete old server.jar
            File.Delete(Path.Combine(App.ServerPath, serverViewModel.Server.Name, "server.jar"));

            // Download new server.jar
            DirectoryInfo directoryInfo = new(Path.Combine(App.ServerPath, serverViewModel.Name));
            Downloader.DownloadJarAsync(serverViewModel, directoryInfo);

            serverViewModel.Server.Version = newVersion;
            ApplicationManager.Instance.TriggerServerListEvent(this, new EventArgs());
            serverViewModel.ServerNameChanged();

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    // --- Crash detection ---
    // Classifies a process exit as a clean stop or an unexpected crash and records it on the
    // view model. Crash = exit was NOT user-requested AND the JVM returned a non-zero exit code
    // (Minecraft watchdog halts non-zero) or left a fresh crash report / watchdog log line.
    // Raises a loud console alert so a crash is never silently shown as a plain "Stopped".
    private static void EvaluateExit(EntityViewModel viewModel, Process process, string serverPath)
    {
        try
        {
            if (viewModel.StopRequested) return; // intentional stop / kill / restart

            int? exitCode = null;
            try { if (process != null && process.HasExited) exitCode = process.ExitCode; } catch { }

            string crashReport = FindRecentCrashReport(serverPath);
            bool watchdog = LogTailHasWatchdog(serverPath);
            bool crashed = (exitCode.HasValue && exitCode.Value != 0) || crashReport != null || watchdog;
            if (!crashed) return;

            string reason = "exit code " + (exitCode.HasValue ? exitCode.Value.ToString() : "unknown");
            if (watchdog) reason += ", server watchdog (tick stall)";
            if (crashReport != null) reason += ", crash report: " + Path.GetFileName(crashReport);

            viewModel.CrashedLastExit = true;
            viewModel.LastExitInfo = reason;
            ConsoleWriter.Write(new ConsoleMessage(
                "[CRASH] Server crashed: " + reason + ". This was NOT a clean stop.",
                ConsoleMessage.MessageLevel.ERROR), viewModel);
            Console.WriteLine($"[Fork] CRASH detected for '{viewModel.Name}': {reason}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Fork] EvaluateExit error for '{viewModel.Name}': {ex.Message}");
        }
    }

    private static string FindRecentCrashReport(string serverPath)
    {
        try
        {
            string dir = Path.Combine(serverPath, "crash-reports");
            if (!Directory.Exists(dir)) return null;
            FileInfo newest = null;
            foreach (var f in new DirectoryInfo(dir).GetFiles("crash-*.txt"))
                if (newest == null || f.LastWriteTimeUtc > newest.LastWriteTimeUtc) newest = f;
            if (newest != null && (DateTime.UtcNow - newest.LastWriteTimeUtc).TotalMinutes <= 3)
                return newest.FullName;
        }
        catch { }
        return null;
    }

    private static bool LogTailHasWatchdog(string serverPath)
    {
        try
        {
            string log = Path.Combine(serverPath, "logs", "latest.log");
            if (!File.Exists(log)) return false;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, fs.Length - 16384);
            fs.Seek(start, SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            string tail = sr.ReadToEnd();
            return tail.Contains("forcibly shutdown") || tail.Contains("Considering it to be crashed")
                   || tail.Contains("This crash report has been saved");
        }
        catch { return false; }
    }
}

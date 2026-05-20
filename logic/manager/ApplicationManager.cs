using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Fork.Logic.ApplicationConsole;
using Fork.Logic.Model;
using Fork.Logic.Model.APIModels;
using Fork.Logic.Model.EventArgs;
using Fork.Logic.Persistence;
using Fork.Logic.Utils;
using Fork.Logic.WebRequesters;
using Fork.ViewModel;
using System.Reflection;

namespace Fork.Logic.Manager;

public sealed class ApplicationManager
{
    public delegate void OnApplicationInitialized();

    public delegate void PlayerEventHandler(object sender, PlayerEventArgs e);

    public delegate void ServerListEventHandler(object sender, EventArgs e);

    // Canonical version — update here when releasing a new build.
    // We don't read this from AssemblyInformationalVersion because the WPF designer
    // temp project auto-generates that attribute and conflicts with AssemblyInfo.cs.
    private const string ForkVersionString = "1.2.5";

    private static string userAgent;

    public static ConsoleWriter ConsoleWriter;
    private static ApplicationManager instance;
    private static WebSocketHandler webSocketHandler;

    //Lock to ensure Singleton pattern
    private static readonly object myLock = new();

    private ApplicationManager()
    {
        var vParts = ForkVersionString.Split('.');
        CurrentForkVersion = new ForkVersion
        {
            Major = vParts.Length > 0 && int.TryParse(vParts[0], out var maj) ? maj : 1,
            Minor = vParts.Length > 1 && int.TryParse(vParts[1], out var min) ? min : 0,
            Patch = vParts.Length > 2 && int.TryParse(vParts[2], out var pat) ? pat : 0
        };
        DiscordRichPresenceUtils.SetupRichPresence();
    }

    public static string UserAgent
    {
        get
        {
            if (userAgent == null)
            {
                userAgent = "Fork Client - fork.gg - contact@fork.gg - v" + ForkVersionString;
            }

            return userAgent;
        }
    }

    public static bool Initialized { get; private set; }

    public static ApplicationManager Instance
    {
        get
        {
            if (instance == null)
            {
                lock (myLock)
                {
                    if (instance == null)
                    {
                        instance = new ApplicationManager();
                        ConsoleWriter.AppStarted();
                        Initialized = true;
                        ApplicationInitialized?.Invoke();
                    }
                }
            }

            return instance;
        }
    }

    public MainViewModel MainViewModel { get; } = new();

    public Dictionary<Entity, Process> ActiveEntities { get; } = new();
    public List<SettingsReader> SettingsReaders { get; } = new();
    public bool HasExited { get; set; }
    public ForkVersion CurrentForkVersion { get; }
    public static event OnApplicationInitialized ApplicationInitialized;

    public static void StartDiscordWebSocket()
    {
        webSocketHandler?.Dispose();
        webSocketHandler = new WebSocketHandler();
        Task.Run(() => webSocketHandler.SetupDiscordWebSocket());
    }

    public static void StopDiscordWebSocket()
    {
        webSocketHandler?.Dispose();
        webSocketHandler = null;
    }

    public event PlayerEventHandler PlayerEvent;
    public event ServerListEventHandler ServerListEvent;

    public void TriggerPlayerEvent(object sender, PlayerEventArgs e)
    {
        PlayerEvent?.Invoke(sender, e);
    }

    public void TriggerServerListEvent(object sender, EventArgs e)
    {
        ServerListEvent?.Invoke(sender, e);
    }

    public void ExitApplication()
    {
        // Stop all running servers gracefully via their ConsoleReader
        // (works for both direct and ForkGuard-managed processes).
        foreach (ServerViewModel vm in ServerManager.Instance.Entities.OfType<ServerViewModel>())
        {
            if (vm.CurrentStatus != ServerStatus.STOPPED)
                ServerLifecycleManager.Instance.StopServer(vm);
        }

        // Wait for and force-kill any surviving processes.
        List<Process> serversToEnd = new(ActiveEntities.Values);
        foreach (Process process in serversToEnd)
            if (process != null)
            {
                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }

        StopDiscordWebSocket();

        foreach (SettingsReader settingsReader in SettingsReaders) settingsReader.Dispose();

        HasExited = true;
    }
}

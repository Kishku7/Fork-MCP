using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fork.Annotations;
using Fork.Logic.Controller;
using Fork.Logic.ImportLogic;
using Fork.Logic.Logging;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.Logic.Model.ProxyModels;
using Fork.Logic.Model.Settings;
using Fork.Logic.Persistence;
using Fork.Logic.WebRequesters;
using Brushes = System.Windows.Media.Brushes;
using Server = Fork.Logic.Model.Server;

namespace Fork.ViewModel;

/// <summary>
/// Abstract base ViewModel for all Fork entities (servers and networks).
/// Responsibilities are split across partial files:
///   EntityViewModel.cs          — core: entity state, lifecycle hooks, settings, download/import progress
///   EntityViewModel.Console.cs  — console I/O management and throttling
///   EntityViewModel.Icons.cs    — server icon loading and management
///   EntityViewModel.Performance.cs — CPU / memory / disk tracking
/// </summary>
public abstract partial class EntityViewModel : BaseViewModel
{
    public delegate void HandleEntityPathChangedEvent(object sender, EntityPathChangedEventArgs e);

    private bool isDeleted;

    public Task SettingsSavingTask = Task.CompletedTask;

    protected EntityViewModel(Entity entity)
    {
        Entity = entity;

        // Error: weird crash (should not happen unless entities.json is corrupted)
        if (Entity.Version == null)
        {
            Console.WriteLine(
                "Persistence file storing servers probably is corrupted (entities.json). Can not start Fork!");
        }

        CurrentStatus = ServerStatus.STOPPED;

        InitializeConsole();
        InitializeIcons();

        UpdateAddressInfo();

        if (Entity.StartWithFork)
        {
            Task.Run(async () =>
            {
                while (!ServerManager.Initialized) await Task.Delay(500);

                switch (this)
                {
                    case ServerViewModel serverViewModel:
                        await ServerManager.Instance.StartServerAsync(serverViewModel);
                        break;
                    case NetworkViewModel networkViewModel:
                        await ServerManager.Instance.StartNetworkAsync(networkViewModel,
                            networkViewModel.Network.SyncServers);
                        break;
                }
            });
        }

        PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != null && e.PropertyName.Equals(nameof(CurrentStatus)))
            {
                ApplicationManager.Instance.TriggerServerListEvent(sender, e);
            }
        };
    }

    public Entity Entity { get; set; }

    public string Name
    {
        get => Entity.Name;
        set
        {
            Entity.Name = value;
            EntityPathChangedEvent?.Invoke(this,
                new EntityPathChangedEventArgs(System.IO.Path.Combine(App.ServerPath, value)));
            new Thread(() =>
            {
                if (this is ServerViewModel s)
                {
                    raisePropertyChanged(nameof(s.ServerTitle));
                }
                else if (this is NetworkViewModel n)
                {
                    raisePropertyChanged(nameof(n.NetworkTitle));
                }

                EntitySerializer.Instance.StoreEntities();
            }) { IsBackground = true }.Start();
        }
    }

    public ServerStatus CurrentStatus { get; set; }
    public bool ServerRunning => CurrentStatus == ServerStatus.RUNNING;
    public string AddressInfo { get; set; }
    public string PrivateAddressInfo { get; set; }

    public AvailabilityCheckResult? LastAvailabilityCheckResult { get; set; } = null;

    public bool IsAvailabilityCheckEnabled =>
        ServerRunning && LastAvailabilityCheckResult != AvailabilityCheckResult.PENDING;

    public Brush AvailabilityColor
    {
        get
        {
            return LastAvailabilityCheckResult switch
            {
                AvailabilityCheckResult.OK => (Brush)new BrushConverter().ConvertFromString("#5EED80"),
                AvailabilityCheckResult.FAILED => (Brush)new BrushConverter().ConvertFromString("#ED5E5E"),
                AvailabilityCheckResult.PENDING => (Brush)new BrushConverter().ConvertFromString("#EBED78"),
                _ => (Brush)new BrushConverter().ConvertFromString("#565B7A")
            };
        }
    }

    [CanBeNull]
    public string AvailabilityTooltip
    {
        get
        {
            if (!IsAvailabilityCheckEnabled)
            {
                return !ServerRunning
                    ? "Start the server to enable availability checks"
                    : "Checking...";
            }

            return LastAvailabilityCheckResult switch
            {
                AvailabilityCheckResult.OK => "Server is available to users outside your network",
                AvailabilityCheckResult.FAILED =>
                    "Server is not reachable outside your network! Check your firewall and port forwarding rules",
                _ => null
            };
        }
    }

    public Brush IconColor
    {
        get
        {
            return CurrentStatus switch
            {
                ServerStatus.RUNNING => (Brush)new BrushConverter().ConvertFromString("#5EED80"),
                ServerStatus.STOPPED => (Brush)new BrushConverter().ConvertFromString("#565B7A"),
                ServerStatus.STARTING => (Brush)new BrushConverter().ConvertFromString("#EBED78"),
                _ => Brushes.White
            };
        }
    }

    public Brush IconColorHovered
    {
        get
        {
            return CurrentStatus switch
            {
                ServerStatus.RUNNING => (Brush)new BrushConverter().ConvertFromString("#5EED80"),
                ServerStatus.STOPPED => (Brush)new BrushConverter().ConvertFromString("#1F2234"),
                ServerStatus.STARTING => (Brush)new BrushConverter().ConvertFromString("#EBED78"),
                _ => Brushes.White
            };
        }
    }

    public double DownloadProgress { get; set; }
    public bool DownloadCompleted { get; set; }
    public double CopyProgress { get; set; }
    public bool ImportCompleted { get; set; } = true;
    public bool ReadyToUse => Entity.Initialized && ImportCompleted;

    public Page EntityPage { get; set; }
    public Page ConsolePage { get; set; }
    public Page PluginsPage { get; set; }
    public SettingsViewModel SettingsViewModel { get; set; }

    public event HandleEntityPathChangedEvent EntityPathChangedEvent;

    public void SaveSettings()
    {
        if (isDeleted) return;

        SettingsSavingTask = SettingsViewModel.SaveChanges();
        Task.Run(() => SettingsSavingTask);
        if (this is ServerViewModel serverViewModel)
        {
            ServerAutomationManager.Instance.UpdateAutomation(serverViewModel);
        }

        Task.Run(async () =>
        {
            WriteServerIcon();
            UpdateAddressInfo();
            if (this is ServerViewModel sv)
            {
                foreach (EntityViewModel entityViewModel in ServerManager.Instance.Entities)
                    if (entityViewModel is NetworkViewModel networkViewModel)
                    {
                        foreach (NetworkServer networkServer in networkViewModel.Servers)
                            if (networkServer is NetworkForkServer networkForkServer)
                            {
                                if (networkForkServer.ServerViewModel == this)
                                {
                                    networkViewModel.UpdateServer(networkServer, sv);
                                }
                            }
                    }
            }

            EntitySerializer.Instance.StoreEntities();
        });
    }


    public void InitializeSettingsFiles(List<SettingsFile> files)
    {
        SettingsViewModel.InitializeSettings(files);
    }

    public async Task UpdateSettingsFiles(List<string> fileNames)
    {
        await SettingsViewModel.UpdateSettings(fileNames);
    }

    public void DeleteEntity()
    {
        isDeleted = true;
    }

    public void StartDownload()
    {
        DownloadCompleted = false;
        Entity.Initialized = false;
        EntitySerializer.Instance.StoreEntities();
        Console.WriteLine($"Starting server.jar download for {Entity}");
        raisePropertyChanged(nameof(Server));
        raisePropertyChanged(nameof(ReadyToUse));
    }

    public void DownloadProgressChanged(object sender, Downloader.DownloadProgressChangedEventArgs e)
    {
        double bytesIn = double.Parse(e.BytesReceived.ToString());
        double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
        DownloadProgress = bytesIn / totalBytes * 100;
    }

    public void DownloadCompletedHandler(object sender, AsyncCompletedEventArgs e)
    {
        DownloadCompleted = true;
        Entity.Initialized = true;
        EntitySerializer.Instance.StoreEntities();
        Console.WriteLine("Finished downloading server.jar for server " + Entity);
        raisePropertyChanged(nameof(Server));
        raisePropertyChanged(nameof(ReadyToUse));
    }

    public void StartImport()
    {
        ImportCompleted = false;
        raisePropertyChanged(nameof(ImportCompleted));
        raisePropertyChanged(nameof(ReadyToUse));
    }

    public void FinishedCopying()
    {
        ImportCompleted = true;
        if (this is ServerViewModel serverViewModel)
        {
            serverViewModel.InitializeLists(serverViewModel.Server);
        }

        raisePropertyChanged(nameof(ImportCompleted));
        raisePropertyChanged(nameof(ReadyToUse));
    }

    public void CopyProgressChanged(object sender, FileImporter.CopyProgressChangedEventArgs e)
    {
        CopyProgress = e.FilesCopied / (double)e.FilesToCopy * 100;
    }

    protected void StartSettingsReader()
    {
        SettingsReader settingsReader = new(this);
        ApplicationManager.Instance.SettingsReaders.Add(settingsReader);
    }

    private void UpdateAddressInfo()
    {
        if (Entity is Server server)
        {
            AddressInfo = new APIController().GetExternalIPAddress() + ":" + server.ServerSettings.ServerPort;
            PrivateAddressInfo = GetLocalIPAddress() + ":" + server.ServerSettings.ServerPort;
        }
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            using System.Net.Sockets.Socket socket = new(System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            System.Net.IPEndPoint endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
            return endPoint?.Address.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    [NotifyPropertyChangedInvocator]
    protected void raisePropertyChanged([CallerMemberName] string propertyName = null)
    {
        RaisePropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    public class EntityPathChangedEventArgs
    {
        public EntityPathChangedEventArgs(string newPath)
        {
            NewPath = newPath;
        }

        public string NewPath { get; }
    }

    private class ActionCommand : ICommand
    {
        private readonly Action _action;

        public ActionCommand(Action action)
        {
            _action = action;
        }

        public void Execute(object parameter) => _action();

        public bool CanExecute(object parameter) => true;

        public event EventHandler CanExecuteChanged;
    }

    public enum AvailabilityCheckResult
    {
        OK, PENDING, FAILED
    }
}

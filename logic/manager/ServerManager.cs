using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Fork.Logic.Controller;
using Fork.Logic.ImportLogic;
using Fork.Logic.Logging;
using Fork.Logic.Model;
using Fork.Logic.Model.ProxyModels;
using Fork.Logic.Persistence;
using Fork.Logic.WebRequesters;
using Fork.ViewModel;
using Newtonsoft.Json;

namespace Fork.Logic.Manager;

/// <summary>
/// Singleton coordinator for all entity (server/network) management.
/// Process lifecycle → ServerLifecycleManager
/// World operations  → WorldManager
/// Resource pack hashing → ResourcePackService (via ServerLifecycleManager)
/// </summary>
public sealed class ServerManager
{
    private static ServerManager instance;

    private ObservableCollection<EntityViewModel> entities;
    private readonly NetworkController networkController = new();
    private readonly List<string> serverNames;

    private ServerManager()
    {
        entities = new ObservableCollection<EntityViewModel>();
        LoadEntityList().Wait();

        foreach (EntityViewModel viewModel in Entities)
            if (!viewModel.Entity.Initialized)
            {
                Downloader.DownloadJarAsync(viewModel,
                    new DirectoryInfo(Path.Combine(App.ServerPath, viewModel.Name)));
            }

        Entities.CollectionChanged += ServerListChanged;

        serverNames = new List<string>();
        foreach (EntityViewModel server in Entities) serverNames.Add(server.Entity.Name);

        Initialized = true;
    }

    public static ServerManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new ServerManager();
            }
            return instance;
        }
    }

    public static bool Initialized { get; private set; }

    public ObservableCollection<EntityViewModel> Entities => entities;

    #region Entity Collection

    public void AddEntity(EntityViewModel entityViewModel)
    {
        serverNames.Add(entityViewModel.Entity.Name);
        Application.Current.Dispatcher.Invoke(() => Entities.Add(entityViewModel));
    }

    public void RemoveEntity(EntityViewModel entityViewModel)
    {
        serverNames.Remove(entityViewModel.Entity.Name);
        Application.Current.Dispatcher.Invoke(() => Entities.Remove(entityViewModel));
    }

    public EntityViewModel GetEntityViewModelByUid(string uid)
    {
        return Entities.FirstOrDefault(entityViewModel => entityViewModel.Entity.UID.Equals(uid));
    }

    public async Task<bool> MoveEntitiesAsync(string newPath)
    {
        Task<bool> t = new(() => MoveEntities(newPath));
        t.Start();
        bool r = await t;
        return r;
    }

    private void ServerListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        ApplicationManager.Instance.TriggerServerListEvent(this, new EventArgs());
        ApplicationManager.Instance.MainViewModel.SetServerList(ref entities);
    }

    private bool MoveEntities(string newPath)
    {
        DirectoryInfo newDir = new(newPath);
        if (!newDir.Exists)
        {
            ErrorLogger.Append(new Exception("Can't move Servers/Networks to not existing dir: " + newPath));
            return false;
        }

        DirectoryInfo test = new(Path.Combine(newDir.FullName, "test"));
        test.Create();
        test.Delete();

        foreach (EntityViewModel entityViewModel in Entities)
        {
            string currEntityPath = Path.Combine(App.ServerPath, entityViewModel.Entity.Name);
            string newEntityPath = Path.Combine(newPath, entityViewModel.Entity.Name);

            entityViewModel.StartImport();
            new Thread(() =>
            {
                FileImporter fileImporter = new();
                fileImporter.CopyProgressChanged += entityViewModel.CopyProgressChanged;
                fileImporter.DirectoryMove(currEntityPath, newEntityPath, true);
                Console.WriteLine("Finished moving entity files for entity " + entityViewModel.Name);
                entityViewModel.FinishedCopying();
            }).Start();
        }

        AppSettingsSerializer.Instance.AppSettings.ServerPath = newPath;
        AppSettingsSerializer.Instance.SaveSettings();
        return true;
    }

    private async Task LoadEntityList()
    {
        foreach (Entity entity in EntitySerializer.Instance.LoadEntities())
            switch (entity)
            {
                case Server server:
                    ServerViewModel? serverViewModel = null;
                    Application.Current.Dispatcher?.Invoke(() =>
                    {
                        serverViewModel = new(server);
                        entities.Add(serverViewModel);
                    });
                    // Re-attach to any server that was left running under ForkGuard.
                    // Deferred via BeginInvoke — TryReattach calls ApplicationManager.Instance and
                    // sets CurrentStatus (firing PropertyChanged), both of which recurse back into
                    // the singleton constructors if called during the startup chain. BeginInvoke
                    // queues the call to run after the WPF dispatcher is up and all singletons exist.
                    if (serverViewModel != null)
                    {
                        var vm = serverViewModel;
                        Application.Current.Dispatcher?.BeginInvoke(() =>
                            ServerLifecycleManager.Instance.TryReattach(vm));
                    }
                    break;
                case Network network:
                    Application.Current.Dispatcher?.Invoke(() =>
                    {
                        NetworkViewModel networkViewModel = new(network);
                        entities.Add(networkViewModel);
                    });
                    break;
            }
    }

    #endregion

    #region Server CRUD

    public async Task<bool> CreateServerAsync(string serverName, ServerVersion serverVersion,
        ServerSettings serverSettings, JavaSettings javaSettings, string worldPath = null)
    {
        serverName = RefineName(serverName);
        string serverPath = Path.Combine(App.ServerPath, serverName);
        serverNames.Add(serverName);
        if (string.IsNullOrEmpty(serverSettings.LevelName))
        {
            serverSettings.LevelName = "world";
        }

        DirectoryInfo directoryInfo = Directory.CreateDirectory(serverPath);
        serverVersion.Build = await VersionManager.Instance.GetLatestBuild(serverVersion);
        Server server = new(serverName, serverVersion, serverSettings, javaSettings);
        ServerViewModel viewModel = new(server);
        Application.Current.Dispatcher.Invoke(() => Entities.Add(viewModel));
        ApplicationManager.Instance.MainViewModel.SelectedEntity = viewModel;

        Downloader.DownloadJarAsync(viewModel, directoryInfo);

        if (worldPath != null)
        {
            new FileImporter().DirectoryCopy(worldPath,
                Path.Combine(directoryInfo.FullName, server.ServerSettings.LevelName), true);
        }

        new FileWriter().WriteEula(Path.Combine(App.ServerPath, directoryInfo.Name));
        await new FileWriter().WriteServerSettings(Path.Combine(App.ServerPath, directoryInfo.Name),
            serverSettings.SettingsDictionary);

        return true;
    }

    public async Task<bool> ImportServerAsync(ServerVersion version, ServerValidationInfo validationInfo,
        string originalServerDirectory, string serverName)
    {
        Task<bool> t = new(() =>
            ImportServer(version, validationInfo, originalServerDirectory, serverName));
        t.Start();
        return await t;
    }

    private bool ImportServer(ServerVersion version, ServerValidationInfo validationInfo,
        string originalServerDirectory, string serverName)
    {
        string serverPath = Path.Combine(App.ServerPath, serverName);
        while (Directory.Exists(serverPath))
        {
            serverPath += "-Copy";
            serverName += "-Copy";
        }

        ServerSettings settings;
        if (new FileInfo(Path.Combine(originalServerDirectory, "server.properties")).Exists)
        {
            Dictionary<string, string> settingsDict = new FileReader().ReadServerSettings(originalServerDirectory);
            settings = new ServerSettings(settingsDict);
        }
        else
        {
            string worldName = validationInfo.Worlds.First().Name;
            settings = new ServerSettings(worldName);
        }

        Server server = new(serverName, version, settings, new JavaSettings());
        serverNames.Add(serverName);

        DirectoryInfo serverDirectory = Directory.CreateDirectory(serverPath);

        ServerViewModel viewModel = new(server);
        viewModel.StartImport();
        Application.Current.Dispatcher.Invoke(() => Entities.Add(viewModel));
        ApplicationManager.Instance.MainViewModel.SelectedEntity = viewModel;

        Thread copyThread = new(() =>
        {
            FileImporter fileImporter = new();
            fileImporter.CopyProgressChanged += viewModel.CopyProgressChanged;
            fileImporter.DirectoryCopy(originalServerDirectory, serverPath, true, new List<string> { "server.jar" });
            Console.WriteLine("Finished copying server files for server " + serverName);
            viewModel.FinishedCopying();
        });
        copyThread.Start();

        Downloader.DownloadJarAsync(viewModel, serverDirectory);

        if (!validationInfo.EulaTxt)
        {
            new FileWriter().WriteEula(serverPath);
        }

        return new DirectoryInfo(serverPath).Exists;
    }

    public async Task<bool> RenameServerAsync(ServerViewModel viewModel, string newName)
    {
        Task<bool> t = new(() => RenameServer(viewModel, newName));
        t.Start();
        bool r = await t;
        return r;
    }

    private bool RenameServer(ServerViewModel viewModel, string newName)
    {
        if (viewModel.CurrentStatus != ServerStatus.STOPPED)
        {
            ServerLifecycleManager.Instance.StopServer(viewModel);
            while (viewModel.CurrentStatus != ServerStatus.STOPPED) Thread.Sleep(500);
        }

        try
        {
            DirectoryInfo directoryInfo = new(Path.Combine(App.ServerPath, viewModel.Name));
            if (!directoryInfo.Exists)
            {
                ErrorLogger.Append(
                    new DirectoryNotFoundException("Could not find Directory " + directoryInfo.FullName));
                return false;
            }

            directoryInfo.MoveTo(Path.Combine(App.ServerPath, newName));
            viewModel.Name = newName;
            ApplicationManager.Instance.TriggerServerListEvent(this, new EventArgs());
            return true;
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            return false;
        }
    }

    public async Task<bool> CloneServerAsync(ServerViewModel viewModel)
    {
        Task<bool> t = new(() => CloneServer(viewModel));
        t.Start();
        bool r = await t;
        return r;
    }

    private bool CloneServer(ServerViewModel viewModel)
    {
        if (viewModel.CurrentStatus != ServerStatus.STOPPED)
        {
            ServerLifecycleManager.Instance.StopServer(viewModel);
            while (viewModel.CurrentStatus != ServerStatus.STOPPED) Thread.Sleep(500);
        }

        try
        {
            DirectoryInfo directoryInfo = new(Path.Combine(App.ServerPath, viewModel.Name));
            if (!directoryInfo.Exists)
            {
                ErrorLogger.Append(
                    new DirectoryNotFoundException("Could not find Directory " + directoryInfo.FullName));
                return false;
            }

            string newName = RefineName(viewModel.Name + "-Clone");

            string oldServerJson = JsonConvert.SerializeObject(viewModel.Server);
            Server newServer = JsonConvert.DeserializeObject<Server>(oldServerJson);
            newServer.Name = newName;
            newServer.UID = Guid.NewGuid().ToString();
            ServerViewModel newServerViewModel = new(newServer);

            string newServerPath = Path.Combine(App.ServerPath, newName);
            newServerViewModel.StartImport();
            Application.Current.Dispatcher?.Invoke(() => Entities.Add(newServerViewModel));
            ApplicationManager.Instance.MainViewModel.SelectedEntity = newServerViewModel;

            Directory.CreateDirectory(newServerPath);

            Thread copyThread = new(() =>
            {
                FileImporter fileImporter = new();
                fileImporter.CopyProgressChanged += newServerViewModel.CopyProgressChanged;
                fileImporter.DirectoryCopy(directoryInfo.FullName, newServerPath, true,
                    new List<string> { "server.jar" });
                Console.WriteLine("Finished copying server files for server " + newServerPath);
                newServerViewModel.FinishedCopying();
            });
            copyThread.Start();

            return true;
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            return false;
        }
    }

    public async Task<bool> DeleteServerAsync(ServerViewModel serverViewModel)
    {
        try
        {
            if (serverViewModel.CurrentStatus != ServerStatus.STOPPED)
            {
                ServerLifecycleManager.Instance.StopServer(serverViewModel);
                while (serverViewModel.CurrentStatus != ServerStatus.STOPPED) await Task.Delay(500);
            }

            if (!serverViewModel.DownloadCompleted)
            {
                await Downloader.CancelJarDownloadAsync(serverViewModel);
            }

            serverViewModel.DeleteEntity();
            DirectoryInfo serverDirectory = new(Path.Combine(App.ServerPath, serverViewModel.Name));
            serverDirectory.Delete(true);
            Application.Current.Dispatcher?.Invoke(() => Entities.Remove(serverViewModel));
            serverNames.Remove(serverViewModel.Server.Name);
            EntitySerializer.Instance.StoreEntities();
            return true;
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            return false;
        }
    }

    private string RefineName(string rawName)
    {
        string result = rawName.Trim();
        if (serverNames.Contains(result))
        {
            int i = 1;
            string resultRaw = result;
            while (serverNames.Contains(result))
            {
                result = resultRaw + "(" + i + ")";
                i++;
            }
        }
        return result;
    }

    #endregion

    #region Server Lifecycle — delegates to ServerLifecycleManager

    public async Task<bool> StartServerAsync(ServerViewModel viewModel) =>
        await ServerLifecycleManager.Instance.StartServerAsync(viewModel);

    public void StopServer(ServerViewModel serverViewModel) =>
        ServerLifecycleManager.Instance.StopServer(serverViewModel);

    public bool RestartServer(ServerViewModel serverViewModel) =>
        ServerLifecycleManager.Instance.RestartServer(serverViewModel);

    public async Task<bool> RestartServerAsync(ServerViewModel serverViewModel) =>
        await ServerLifecycleManager.Instance.RestartServerAsync(serverViewModel);

    public bool KillEntity(EntityViewModel entityViewModel) =>
        ServerLifecycleManager.Instance.KillEntity(entityViewModel);

    public async Task<bool> ChangeServerVersionAsync(ServerVersion newVersion, ServerViewModel serverViewModel) =>
        await ServerLifecycleManager.Instance.ChangeServerVersionAsync(newVersion, serverViewModel);

    #endregion

    #region World Management — delegates to WorldManager

    public async Task<bool> ImportWorldAsync(ServerViewModel viewModel, string worldSource) =>
        await WorldManager.Instance.ImportWorldAsync(viewModel, worldSource);

    public async Task<bool> CreateWorldAsync(string name, ServerViewModel viewModel) =>
        await WorldManager.Instance.CreateWorldAsync(name, viewModel);

    public async Task<bool> DeleteDimensionAsync(MinecraftDimension dimension, Server server) =>
        await WorldManager.Instance.DeleteDimensionAsync(dimension, server);

    #endregion

    #region Network Management — delegates to NetworkController

    public async Task<bool> CreateNetworkAsync(string networkName, ServerVersion.VersionType networkType,
        JavaSettings javaSettings)
    {
        Task<bool> t = new(() =>
            networkController.CreateNetwork(networkName, networkType, javaSettings, serverNames));
        t.Start();
        bool r = await t;
        return r;
    }

    public async Task<bool> StartNetworkAsync(NetworkViewModel viewModel, bool startServers = false) =>
        await networkController.StartNetworkAsync(viewModel, startServers);

    public async Task<bool> StopNetworkAsync(NetworkViewModel viewModel, bool stopServers = false)
    {
        Task<bool> t = new(() => networkController.StopNetwork(viewModel, stopServers));
        t.Start();
        bool r = await t;
        return r;
    }

    public async Task<bool> RestartNetworkAsync(NetworkViewModel viewModel, bool restartServers = false)
    {
        bool stopSuccess = await StopNetworkAsync(viewModel, restartServers);
        if (!stopSuccess)
        {
            return false;
        }
        return await StartNetworkAsync(viewModel, restartServers);
    }

    public async Task<bool> RenameNetworkAsync(NetworkViewModel viewModel, string newName)
    {
        Task<bool> t = new(() => networkController.RenameNetwork(viewModel, newName));
        t.Start();
        bool r = await t;
        return r;
    }

    public async Task<bool> CloneNetworkAsync(NetworkViewModel viewModel)
    {
        Task<bool> t = new(() => networkController.CloneNetwork(viewModel, serverNames));
        t.Start();
        bool r = await t;
        return r;
    }

    public async Task<bool> DeleteNetworkAsync(NetworkViewModel viewModel) =>
        await networkController.DeleteNetworkAsync(viewModel);

    public async Task<bool> KillNetworkAsync(NetworkViewModel viewModel, bool killServers = false)
    {
        Task<bool> t = new(() => networkController.KillNetwork(viewModel, killServers));
        t.Start();
        bool r = await t;
        return r;
    }

    #endregion
}

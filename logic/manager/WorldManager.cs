using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Fork.Logic.ImportLogic;
using Fork.Logic.Model;
using Fork.ViewModel;

namespace Fork.Logic.Manager;

/// <summary>
/// Handles world-level operations: import, create, and dimension deletion.
/// Extracted from ServerManager.
/// </summary>
public sealed class WorldManager
{
    private static WorldManager instance;

    private WorldManager() { }

    public static WorldManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new WorldManager();
            }
            return instance;
        }
    }

    public async Task<bool> ImportWorldAsync(ServerViewModel viewModel, string worldSource)
    {
        Task<bool> t = new(() => ImportWorld(viewModel, worldSource));
        t.Start();
        return await t;
    }

    public async Task<bool> CreateWorldAsync(string name, ServerViewModel viewModel)
    {
        Task<bool> t = new(() => CreateWorld(name, viewModel));
        t.Start();
        return await t;
    }

    public async Task<bool> DeleteDimensionAsync(MinecraftDimension dimension, Server server)
    {
        Task<bool> t = new(() => DeleteDimension(dimension, server));
        t.Start();
        bool result = await t;
        return result;
    }

    private bool ImportWorld(ServerViewModel viewModel, string worldSource)
    {
        try
        {
            DirectoryInfo serverDir = new(Path.Combine(App.ServerPath, viewModel.Server.Name));
            DirectoryInfo importWorldDir = new(worldSource);
            string worldName = importWorldDir.Name;
            List<string> worlds = new();
            foreach (World world in viewModel.Worlds) worlds.Add(world.Name);

            while (worlds.Contains(worldName)) worldName += "1";

            if (!serverDir.Exists || !importWorldDir.Exists)
            {
                Console.WriteLine("Error during world import! Server or World directory don't exist");
                return false;
            }

            new FileImporter().DirectoryCopy(importWorldDir.FullName,
                Path.Combine(serverDir.FullName, worldName), true);
            viewModel.InitializeWorldsList();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.StackTrace);
            return false;
        }
    }

    private bool CreateWorld(string worldName, ServerViewModel viewModel)
    {
        try
        {
            DirectoryInfo serverDir = new(Path.Combine(App.ServerPath, viewModel.Server.Name));
            List<string> worlds = new();
            foreach (World world in viewModel.Worlds) worlds.Add(world.Name);

            while (worlds.Contains(worldName)) worldName += "1";

            if (!serverDir.Exists)
            {
                Console.WriteLine("Error during world creation! Server directory doesn't exist");
                return false;
            }

            DirectoryInfo worldDir = Directory.CreateDirectory(Path.Combine(serverDir.FullName, worldName));
            Directory.CreateDirectory(Path.Combine(worldDir.FullName, "region"));
            Directory.CreateDirectory(Path.Combine(worldDir.FullName, "data"));
            viewModel.InitializeWorldsList();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.StackTrace);
            return false;
        }
    }

    private bool DeleteDimension(MinecraftDimension dimension, Server server)
    {
        DirectoryInfo dimensionDir = GetDimensionFolder(dimension, server);
        if (!dimensionDir.Exists)
        {
            return true;
        }

        DirectoryInfo dimBackups =
            Directory.CreateDirectory(Path.Combine(App.ServerPath, server.Name, "DimensionBackups"));
        DateTime now = DateTime.Now;
        string timeStamp = now.Day + "-" + now.Month + "-" + now.Year + "_" +
                           now.Hour + "-" + now.Minute + "-" + now.Second;
        ZipFile.CreateFromDirectory(dimensionDir.FullName,
            Path.Combine(dimBackups.FullName, dimension + "_" + timeStamp + ".zip"));
        dimensionDir.Delete(true);
        return !new DirectoryInfo(dimensionDir.FullName).Exists;
    }

    private DirectoryInfo GetDimensionFolder(MinecraftDimension dimension, Server server)
    {
        string worldFolder = Path.Combine(App.ServerPath, server.Name, server.ServerSettings.LevelName);
        switch (server.Version.Type)
        {
            case ServerVersion.VersionType.Vanilla:
            case ServerVersion.VersionType.Snapshot:
                switch (dimension)
                {
                    case MinecraftDimension.Nether:
                        return new DirectoryInfo(Path.Combine(worldFolder, "DIM-1"));
                    case MinecraftDimension.End:
                        return new DirectoryInfo(Path.Combine(worldFolder, "DIM1"));
                    default:
                        throw new ArgumentException("No implementation for deletion of dimension " + dimension +
                                                    " on Vanilla servers");
                }
            case ServerVersion.VersionType.Spigot:
            case ServerVersion.VersionType.Paper:
            case ServerVersion.VersionType.Purpur:
            case ServerVersion.VersionType.Fabric:
                switch (dimension)
                {
                    case MinecraftDimension.Nether:
                        return new DirectoryInfo(worldFolder + "_nether");
                    case MinecraftDimension.End:
                        return new DirectoryInfo(worldFolder + "_the_end");
                    default:
                        throw new ArgumentException(
                            $"No implementation for deletion of dimension {dimension} on {server.Version.Type} servers");
                }
            default:
                throw new ArgumentException("No implementation for deletion of " + server.Version.Type +
                                            " dimensions");
        }
    }
}

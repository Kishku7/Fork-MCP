using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using Fork.Logic.Logging;
using Fork.Logic.Model;
using Fork.Logic.Utils;
using Image = System.Drawing.Image;

namespace Fork.ViewModel;

/// <summary>
/// Server icon loading, selection, and persistence for EntityViewModel.
/// Covers both the type icon (Vanilla/Paper/etc.) and the custom server icon (PNG).
/// </summary>
public abstract partial class EntityViewModel
{
    public ObservableCollection<ImageSource> ServerIcons { get; set; }
    public ImageSource SelectedServerIcon { get; set; }

    /// <summary>Version-type icon (coloured). Used in the server list sidebar.</summary>
    public ImageSource Icon
    {
        get
        {
            BitmapImage bi3 = new();
            bi3.BeginInit();
            switch (Entity.Version.Type)
            {
                case ServerVersion.VersionType.Vanilla:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Vanilla.png");
                    break;
                case ServerVersion.VersionType.Snapshot:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Snapshot.png");
                    break;
                case ServerVersion.VersionType.Paper:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Paper.png");
                    break;
                case ServerVersion.VersionType.Purpur:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Purpur.png");
                    break;
                case ServerVersion.VersionType.Spigot:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Spigot.png");
                    break;
                case ServerVersion.VersionType.Fabric:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Fabric.png");
                    break;
                case ServerVersion.VersionType.Waterfall:
                case ServerVersion.VersionType.BungeeCord:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Waterfall.png");
                    break;
                default:
                    return null;
            }
            bi3.EndInit();
            return bi3;
        }
    }

    /// <summary>Version-type icon (white/monochrome). Used in hover states.</summary>
    public ImageSource IconW
    {
        get
        {
            BitmapImage bi3 = new();
            bi3.BeginInit();
            switch (Entity.Version.Type)
            {
                case ServerVersion.VersionType.Vanilla:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/VanillaW.png");
                    break;
                case ServerVersion.VersionType.Snapshot:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/SnapshotW.png");
                    break;
                case ServerVersion.VersionType.Paper:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/PaperW.png");
                    break;
                case ServerVersion.VersionType.Purpur:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/PurpurW.png");
                    break;
                case ServerVersion.VersionType.Spigot:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/SpigotW.png");
                    break;
                case ServerVersion.VersionType.Fabric:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/FabricW.png");
                    break;
                case ServerVersion.VersionType.Waterfall:
                case ServerVersion.VersionType.BungeeCord:
                    bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/WaterfallW.png");
                    break;
                default:
                    return null;
            }
            bi3.EndInit();
            return bi3;
        }
    }

    public void UpdateCustomImage(string filePath)
    {
        if (isDeleted) return;
        try
        {
            ImageSource toRemove = ServerIcons[^1];
            bool newIsSelected = SelectedServerIcon == toRemove;
            Application.Current.Dispatcher?.Invoke(() => ServerIcons.Remove(toRemove));
            Bitmap bitmap;
            using (Image image = Image.FromFile(filePath)) bitmap = ImageUtils.ResizeImage(image, 64, 64);

            ImageSource img = ImageUtils.BitmapToImageSource(bitmap);
            img.Freeze();
            Application.Current.Dispatcher?.Invoke(() => ServerIcons.Add(img));
            if (newIsSelected)
            {
                SelectedServerIcon = img;
            }

            bitmap.Save(Path.Combine(App.ServerPath, Entity.Name, "custom-icon.png"));
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
        }
    }

    private void InitializeIcons()
    {
        ServerIcons = new ObservableCollection<ImageSource>();

        // Only attempt custom icon I/O if the server directory actually exists.
        // Servers configured in Fork but not yet deployed have no server directory — without
        // this guard the FileStream throws DirectoryNotFoundException / GDI+ and kills the WPF window.
        DirectoryInfo serverDir = new(Path.Combine(App.ServerPath, Entity.Name));
        bool serverDirExists = serverDir.Exists;

        FileInfo customIcon = new(Path.Combine(App.ServerPath, Entity.Name, "custom-icon.png"));
        if (serverDirExists && !customIcon.Exists)
        {
            try
            {
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(
                    new Uri("pack://application:,,,/View/Resources/images/Server-Icons/default.png")));
                using FileStream fileStream = new(customIcon.FullName, FileMode.Create);
                encoder.Save(fileStream);
            }
            catch (Exception e)
            {
                ErrorLogger.Append(e);
                serverDirExists = false; // treat as missing — skip custom icon below
            }
        }

        // Built-in icons are always loaded; custom icon is only added when its file is available.
        var iconUriList = new System.Collections.Generic.List<string>
        {
            "pack://application:,,,/View/Resources/images/Server-Icons/default.png",
            "pack://application:,,,/View/Resources/images/Server-Icons/forkboi.png",
            "pack://application:,,,/View/Resources/images/Server-Icons/forkchristmas.png",
            "pack://application:,,,/View/Resources/images/Server-Icons/icon1.png",
        };
        if (serverDirExists && customIcon.Exists)
            iconUriList.Add(customIcon.FullName);

        foreach (string iconUri in iconUriList)
        {
            try
            {
                Image image;
                if (iconUri.StartsWith("pack://application:,,,/"))
                {
                    StreamResourceInfo info = Application.GetResourceStream(new Uri(iconUri));
                    image = Image.FromStream(info?.Stream);
                }
                else
                {
                    image = Image.FromFile(iconUri);
                }

                Bitmap bitmap = ImageUtils.ResizeImage(image, 64, 64);
                ImageSource img = ImageUtils.BitmapToImageSource(bitmap);
                img.Freeze();
                Application.Current.Dispatcher?.Invoke(() => ServerIcons.Add(img));
            }
            catch (Exception e)
            {
                ErrorLogger.Append(e);
            }
        }

        // Guard against all icons failing — prevents a crash on SelectedServerIcon access.
        if (ServerIcons.Count == 0) return;

        if (Entity.ServerIconId >= 0 && Entity.ServerIconId < ServerIcons.Count)
        {
            SelectedServerIcon = ServerIcons[Entity.ServerIconId];
        }
        else
        {
            SelectedServerIcon = ServerIcons[0];
            Entity.ServerIconId = 0;
        }

        WriteServerIcon();
    }

    private void WriteServerIcon()
    {
        try
        {
            Entity.ServerIconId = ServerIcons.IndexOf(SelectedServerIcon);
            FileInfo customIcon = new(Path.Combine(App.ServerPath, Entity.Name, "server-icon.png"));

            // Remove server-icon.png and return if the default icon is selected
            if (Entity.ServerIconId == 0 && customIcon.Exists)
            {
                customIcon.Delete();
                return;
            }

            if (Entity.ServerIconId == 0) return;

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)SelectedServerIcon));
            using (FileStream fileStream = new(customIcon.FullName, FileMode.Create)) encoder.Save(fileStream);
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            Console.WriteLine("Saving server icon failed! See error log");
        }
    }
}

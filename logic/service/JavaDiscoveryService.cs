using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Fork.Logic.Model;
using Fork.Logic.Persistence;
using Fork.Logic.Utils;

namespace Fork.Logic.Service;

/// <summary>
/// Discovers Java installations on this machine and exposes them as an
/// <see cref="ObservableCollection{T}"/> of <see cref="KnownJavaMajor"/> entries.
///
/// Priority order for scanning:
///   1. Modrinth AppData  — %APPDATA%\ModrinthApp\meta\java_versions  (if present)
///   2. Eclipse Adoptium  — C:\Program Files\Eclipse Adoptium
///   3. Generic Java root — C:\Program Files\Java
///   4. User-configured   — AppSettings.JavaBaseDirectory              (if set)
///
/// Major versions tracked are the ones Minecraft has actually required:
///   8, 11, 17, 21, 25
/// Plus a synthetic "Auto" entry (Major = 0) that always picks the highest installed.
/// </summary>
public sealed class JavaDiscoveryService
{
    // ── Singleton ────────────────────────────────────────────────────────────
    private static JavaDiscoveryService? _instance;
    public static JavaDiscoveryService Instance => _instance ??= new JavaDiscoveryService();

    // ── Known Minecraft Java majors (fixed set) ───────────────────────────
    public static readonly int[] KnownMajors = { 8, 11, 17, 21, 25 };

    // ── State ────────────────────────────────────────────────────────────────
    private List<DiscoveredJavaInstallation> _discovered = new();

    /// <summary>
    /// Fixed-size observable list:  [Auto, Java 8, Java 11, Java 17, Java 21, Java 25].
    /// Bind the per-server ComboBox to this collection.
    /// </summary>
    public ObservableCollection<KnownJavaMajor> KnownJavaVersions { get; } = new(
        new[] { new KnownJavaMajor(0) }                   // "Auto"
            .Concat(KnownMajors.Select(m => new KnownJavaMajor(m)))
    );

    // ── Construction ─────────────────────────────────────────────────────────
    private JavaDiscoveryService()
    {
        // Kick off discovery in the background immediately.
        Task.Run(Reload);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Rescans all search roots and refreshes <see cref="KnownJavaVersions"/>.
    /// Thread-safe; may be called from any thread.
    /// </summary>
    public void Reload()
    {
        var found = new List<DiscoveredJavaInstallation>();

        // 1. Modrinth managed runtimes
        string modrinthRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModrinthApp", "meta", "java_versions");
        TryScanRoot(modrinthRoot, found);

        // 2. Eclipse Adoptium
        TryScanRoot(@"C:\Program Files\Eclipse Adoptium", found);

        // 3. Generic Java directory
        TryScanRoot(@"C:\Program Files\Java", found);

        // 4. User-configured extra root
        string? userRoot = AppSettingsSerializer.Instance.AppSettings.JavaBaseDirectory;
        if (!string.IsNullOrWhiteSpace(userRoot))
            TryScanRoot(userRoot, found);

        _discovered = found;
        RefreshKnownVersions();
    }

    /// <summary>
    /// Returns the highest-patch binary for the requested major version.
    /// If <paramref name="major"/> is 0 (Auto), returns the highest available overall.
    /// Returns null if nothing was found.
    /// </summary>
    public DiscoveredJavaInstallation? GetBestForMajor(int major)
    {
        IEnumerable<DiscoveredJavaInstallation> candidates = major == 0
            ? _discovered
            : _discovered.Where(d => d.MajorVersion == major);

        return candidates
            .OrderByDescending(d => d.MajorVersion)
            .ThenByDescending(d => d.ParsedVersion ?? new Version(0, 0))
            .FirstOrDefault();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void TryScanRoot(string root, List<DiscoveredJavaInstallation> results)
    {
        if (!Directory.Exists(root)) return;
        try { ScanDirectory(root, results, depth: 0, maxDepth: 4); }
        catch { /* ignore inaccessible roots */ }
    }

    private void ScanDirectory(string dir, List<DiscoveredJavaInstallation> results, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        // Check if THIS directory contains bin\java.exe
        string candidate = Path.Combine(dir, "bin", "java.exe");
        if (File.Exists(candidate) &&
            !results.Any(r => r.BinaryPath.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            JavaVersion? ver = JavaVersionUtils.GetInstalledJavaVersion(candidate);
            if (ver != null)
            {
                results.Add(new DiscoveredJavaInstallation
                {
                    BinaryPath   = candidate,
                    MajorVersion = ver.VersionComputed,
                    FullVersion  = ver.Version,
                    Is64Bit      = ver.Is64Bit
                });
            }
        }

        // Recurse into subdirectories
        try
        {
            foreach (string sub in Directory.EnumerateDirectories(dir))
                ScanDirectory(sub, results, depth + 1, maxDepth);
        }
        catch { /* ignore */ }
    }

    private void RefreshKnownVersions()
    {
        // Update must happen on the UI thread (ObservableCollection notifies bound controls).
        void Update()
        {
            foreach (KnownJavaMajor known in KnownJavaVersions)
            {
                if (known.Major == 0)
                {
                    // Auto: always available if anything was found; best = highest overall
                    known.Best = _discovered
                        .OrderByDescending(d => d.MajorVersion)
                        .ThenByDescending(d => d.ParsedVersion ?? new Version(0, 0))
                        .FirstOrDefault();
                    known.IsAvailable = known.Best != null;
                }
                else
                {
                    var matches = _discovered
                        .Where(d => d.MajorVersion == known.Major)
                        .OrderByDescending(d => d.ParsedVersion ?? new Version(0, 0))
                        .ToList();
                    known.IsAvailable = matches.Count > 0;
                    known.Best = matches.FirstOrDefault();
                }
            }
        }

        if (Application.Current?.Dispatcher is { } disp)
            disp.Invoke(Update);
        else
            Update();
    }
}

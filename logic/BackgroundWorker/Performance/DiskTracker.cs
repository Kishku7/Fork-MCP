using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Fork.ViewModel;

namespace Fork.Logic.BackgroundWorker.Performance;

/// <summary>
/// Tracks disk busy-% for the physical disk hosting the entity's directory.
///
/// <para>
/// Uses <c>PhysicalDisk \ % Idle Time</c> inverted (100 − idle), clamped to 0–100.
/// The old <c>% Disk Time</c> counter is derived from Avg. Disk Queue Length and
/// routinely exceeds 100% under overlapped I/O — it is not a real percentage.
/// </para>
///
/// <para>
/// The PhysicalDisk instance (e.g. <c>"1 E:"</c>) is resolved from the entity's
/// drive letter once at tracking startup, so each server reports the activity of
/// the disk it actually writes to. Falls back to <c>_Total</c> if resolution fails.
/// </para>
/// </summary>
public class DiskTracker
{
    private bool interrupted;
    private readonly List<Thread> threads = new();

    public void TrackTotal(Process p, EntityViewModel viewModel, string entityPath)
    {
        PerformanceCounter idleCounter = new()
        {
            CategoryName = "PhysicalDisk",
            CounterName = "% Idle Time",
            InstanceName = ResolveInstanceForPath(entityPath)
        };
        Thread t = new(() =>
        {
            while (!interrupted && !p.HasExited)
            {
                try
                {
                    double busy = 100.0 - idleCounter.NextValue();
                    viewModel.DiskValueUpdate(Math.Clamp(busy, 0.0, 100.0));
                }
                catch (Exception)
                {
                    break;
                }

                Thread.Sleep(500);
            }

            viewModel.DiskValueUpdate(0.0);
            viewModel.DiskValueUpdate(0.0);
            viewModel.DiskValueUpdate(0.0);
        });
        t.Start();
        t.IsBackground = true;
        threads.Add(t);
    }

    /// <summary>
    /// Finds the PhysicalDisk counter instance for the drive hosting
    /// <paramref name="path"/>. Instance names look like <c>"0 C:"</c> or
    /// <c>"1 E: F:"</c> (disk number followed by its drive letters).
    /// Returns <c>_Total</c> when the path or instance cannot be resolved.
    /// </summary>
    private static string ResolveInstanceForPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "_Total";

            string root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return "_Total";

            string drive = root.Substring(0, 2); // e.g. "E:"
            var category = new PerformanceCounterCategory("PhysicalDisk");
            foreach (string instance in category.GetInstanceNames())
            {
                if (!"_Total".Equals(instance, StringComparison.OrdinalIgnoreCase) &&
                    instance.IndexOf(drive, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return instance;
                }
            }
        }
        catch
        {
            // Counter category unavailable or path invalid — fall through to _Total.
        }

        return "_Total";
    }

    public void StopThreads()
    {
        interrupted = true;
    }
}

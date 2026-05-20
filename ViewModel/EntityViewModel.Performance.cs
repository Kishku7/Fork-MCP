using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fork.Logic.BackgroundWorker.Performance;

namespace Fork.ViewModel;

/// <summary>
/// CPU, memory, and disk performance tracking for EntityViewModel.
/// Uses 3-point rolling averages via CPUTracker, MemTracker, and DiskTracker.
/// </summary>
public abstract partial class EntityViewModel
{
    private List<double> cpuList;
    private CPUTracker cpuTracker;
    private List<double> memList;
    private MemTracker memTracker;
    private double memValue;
    private List<double> diskList;
    private DiskTracker diskTracker;

    public string CPUValue => Math.Round(CPUValueRaw, 0) + "%";
    public double CPUValueRaw { get; private set; }

    public string MemValue => Math.Round(memValue / Entity.JavaSettings.MaxRam * 100, 0) + "%";
    public double MemValueRaw => memValue / Entity.JavaSettings.MaxRam * 100;

    public string DiskValue => Math.Round(DiskValueRaw, 0) + "%";
    public double DiskValueRaw { get; private set; }

    public void TrackPerformance(Process p)
    {
        cpuTracker?.StopThreads();
        cpuList = new List<double>();
        cpuTracker = new CPUTracker();
        cpuTracker.TrackTotal(p, this);

        memTracker?.StopThreads();
        memList = new List<double>();
        memTracker = new MemTracker();
        memTracker.TrackP(p, this);

        diskTracker?.StopThreads();
        diskList = new List<double>();
        diskTracker = new DiskTracker();
        diskTracker.TrackTotal(p, this);
    }

    public void CPUValueUpdate(double value)
    {
        try
        {
            cpuList.Add(value);
            if (cpuList.Count > 3) cpuList.RemoveAt(0);
            CPUValueRaw = cpuList.Average();
        }
        catch
        {
            // Ignored: rare collection-modified exception under concurrent updates
        }

        raisePropertyChanged(nameof(CPUValue));
        raisePropertyChanged(nameof(CPUValueRaw));
    }

    public void MemValueUpdate(double value)
    {
        memList.Add(value);
        if (memList.Count > 3) memList.RemoveAt(0);
        try
        {
            memValue = memList.Average();
        }
        catch
        {
            // Ignored: rare collection-modified exception under concurrent updates
        }

        raisePropertyChanged(nameof(MemValue));
        raisePropertyChanged(nameof(MemValueRaw));
    }

    public void DiskValueUpdate(double value)
    {
        diskList.Add(value);
        if (diskList.Count > 3) diskList.RemoveAt(0);
        try
        {
            DiskValueRaw = diskList.Average();
        }
        catch
        {
            // Ignored: rare collection-modified exception under concurrent updates
        }

        raisePropertyChanged(nameof(DiskValue));
        raisePropertyChanged(nameof(DiskValueRaw));
    }
}

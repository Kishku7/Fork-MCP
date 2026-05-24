using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Fork.Annotations;

namespace Fork.Logic.Model;

[Serializable]
public class JavaSettings : INotifyPropertyChanged
{
    public JavaSettings()
    {
    }

    public JavaSettings(JavaSettings javaSettings)
    {
        MaxRam = javaSettings.MaxRam;
        PreferredMajorVersion = javaSettings.PreferredMajorVersion;
        JavaPath = javaSettings.JavaPath;
        StartupParameters = javaSettings.StartupParameters;
    }

    public int Id { get; set; }
    public int MaxRam { get; set; } = 2048;

    /// <summary>
    /// Preferred Java major version for this entity (8, 11, 17, 21, 25).
    /// 0 = Auto: JavaDiscoveryService picks the highest installed version.
    /// JavaDiscoveryService resolves this to an absolute binary path at server start.
    /// </summary>
    public int PreferredMajorVersion { get; set; } = 0;

    /// <summary>
    /// Legacy field — preserved for JSON round-trip compatibility with old entities.json.
    /// No longer used to launch Java; PreferredMajorVersion drives version selection.
    /// </summary>
    public string JavaPath { get; set; } = "java.exe";

    public string StartupParameters { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

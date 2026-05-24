using System.ComponentModel;
using System.Runtime.CompilerServices;
using Fork.Annotations;

namespace Fork.Logic.Model;

/// <summary>
/// One entry in the fixed list of Minecraft-relevant Java major versions (8, 11, 17, 21, 25),
/// plus the synthetic "Auto" entry (Major = 0).
/// Drives the per-server Java version ComboBox — items with IsAvailable = false are grayed out.
/// </summary>
public class KnownJavaMajor : INotifyPropertyChanged
{
    private bool _isAvailable;
    private DiscoveredJavaInstallation? _best;

    public KnownJavaMajor(int major)
    {
        Major = major;
    }

    /// <summary>0 = Auto (pick highest available), otherwise the Java major version.</summary>
    public int Major { get; }

    /// <summary>True if at least one binary for this major was found during the last Reload().</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (_isAvailable == value) return;
            _isAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    /// <summary>The highest-patch binary found for this major, or null if none.</summary>
    public DiscoveredJavaInstallation? Best
    {
        get => _best;
        set
        {
            _best = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    /// <summary>Human-readable label shown in the dropdown.</summary>
    public string DisplayLabel => Major switch
    {
        0 when IsAvailable  => $"Auto  (Java {Best?.MajorVersion} — {Best?.FullVersion})",
        0                   => "Auto  (no Java found)",
        _ when IsAvailable  => $"Java {Major}  ({Best?.FullVersion})",
        _                   => $"Java {Major}  (not installed)"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

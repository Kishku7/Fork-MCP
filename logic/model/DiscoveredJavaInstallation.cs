using System;

namespace Fork.Logic.Model;

/// <summary>
/// Represents a single java.exe binary discovered on disk, with its parsed version info.
/// </summary>
public class DiscoveredJavaInstallation
{
    /// <summary>Absolute path to the java.exe binary.</summary>
    public string BinaryPath { get; set; } = "";

    /// <summary>Major Java version (e.g. 8, 11, 17, 21, 25).</summary>
    public int MajorVersion { get; set; }

    /// <summary>Full dotted version string as reported by java -version (e.g. "25.0.3").</summary>
    public string FullVersion { get; set; } = "";

    /// <summary>True if the discovered binary is 64-bit.</summary>
    public bool Is64Bit { get; set; }

    /// <summary>Parsed System.Version for comparison purposes. Null if FullVersion cannot be parsed.</summary>
    public Version? ParsedVersion
    {
        get
        {
            try { return Version.Parse(FullVersion); }
            catch { return null; }
        }
    }
}

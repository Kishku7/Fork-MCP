using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

// Attributes that the WPF designer temp-project auto-generates (Title, Configuration,
// Company, Product, Version, InformationalVersion) are intentionally omitted here
// to avoid CS0579 duplicate-attribute errors.  They are set via <Version>, <Product>
// etc. in Fork.csproj.

[assembly:
    AssemblyDescription(
        "Fork is a Minecraft Server Manager GUI making server creation, configuration and maintenance as simple as possible")]
[assembly: AssemblyCopyright("Copyright 2024")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

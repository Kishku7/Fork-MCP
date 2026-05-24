<#
.SYNOPSIS
    Bump the Fork version in Fork.csproj, AssemblyInfo.cs, and ApplicationManager.cs.

.DESCRIPTION
    Reads the current <Version> from Fork.csproj, increments the specified
    component, and writes it back to Fork.csproj, Properties\AssemblyInfo.cs,
    and the ForkVersionString constant in Logic\Manager\ApplicationManager.cs.

.PARAMETER Part
    Which part to increment: patch (default), minor, or major.
    - patch  : 0.8.10 -> 0.8.11
    - minor  : 0.8.10 -> 0.9.0   (resets patch)
    - major  : 0.8.10 -> 1.0.0   (resets minor + patch)

.EXAMPLE
    .\scripts\bump-version.ps1          # bump patch
    .\scripts\bump-version.ps1 minor    # bump minor
    .\scripts\bump-version.ps1 major    # bump major
#>

param(
    [ValidateSet('patch','minor','major')]
    [string]$Part = 'patch'
)

$csproj = Join-Path $PSScriptRoot '..\Fork.csproj'
$csproj = (Resolve-Path $csproj).Path

[xml]$xml = Get-Content $csproj -Encoding UTF8

$versionNode = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }
if (-not $versionNode) {
    Write-Error "Could not find <Version> in $csproj"
    exit 1
}

$parts = $versionNode.Split('.')
if ($parts.Count -ne 3) {
    Write-Error "Expected 3-part version (Major.Minor.Patch), got: $versionNode"
    exit 1
}

[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$patch = $parts[2]

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}

$newVersion = "$major.$minor.$patch"

# Update the XML — only <Version> needs changing; AssemblyVersion is intentionally
# left at its default (1.0.0.0) so Fody weaving stays stable across bumps.
foreach ($pg in $xml.Project.PropertyGroup) {
    if ($pg.Version) {
        $pg.Version = $newVersion
        break
    }
}

$xml.Save($csproj)

# Also update AssemblyInformationalVersion in AssemblyInfo.cs
# (GenerateAssemblyInfo=false means the SDK never writes this file)
$asmInfo = Join-Path $PSScriptRoot '..\Properties\AssemblyInfo.cs'
$asmInfo = (Resolve-Path $asmInfo).Path
$asmContent = Get-Content $asmInfo -Raw
$replacement = '[assembly: AssemblyInformationalVersion("' + $newVersion + '")]'
$asmContent  = $asmContent -replace '\[assembly: AssemblyInformationalVersion\("([^"]+)"\)\]', $replacement
[System.IO.File]::WriteAllText($asmInfo, $asmContent)

# Also update ForkVersionString constant in ApplicationManager.cs
$appMgr = Join-Path $PSScriptRoot '..\Logic\Manager\ApplicationManager.cs'
$appMgr = (Resolve-Path $appMgr).Path
$appContent = Get-Content $appMgr -Raw
$appContent = $appContent -replace '(private const string ForkVersionString\s*=\s*")[^"]+(")', "`${1}$newVersion`$2"
[System.IO.File]::WriteAllText($appMgr, $appContent)

Write-Host "Version bumped: $versionNode -> $newVersion"
Write-Host "Build and deploy to apply."

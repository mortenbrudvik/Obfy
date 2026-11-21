# Prints the product version as major.minor.patch, read from version.json.
#
# version.json at the repository root is the only place the product version is written. The .NET projects read
# it through Directory.Build.props; the installer build (build-installer.ps1) and the release-tag check
# (Resolve-PackageVersion.ps1 -VersionFile) read it through this script. Each part must be a JSON integer in
# 0..65535 (the MSIX and assembly-version limit), so every consumer accepts the same file.
[CmdletBinding()]
param(
    [string] $VersionFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'version.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $VersionFile -PathType Leaf)) {
    throw "version.json not found at $VersionFile"
}

try {
    $version = Get-Content -LiteralPath $VersionFile -Raw | ConvertFrom-Json
} catch {
    throw "version.json at $VersionFile is not valid JSON: $($_.Exception.Message)"
}

$parts = foreach ($name in @('major', 'minor', 'patch')) {
    $property = if ($null -ne $version) { $version.PSObject.Properties[$name] } else { $null }
    if ($null -eq $property) {
        throw "version.json at $VersionFile has no '$name'"
    }

    $value = $property.Value
    # A string, a fraction or a boolean is not an integer; MSBuild's reader and the MSIX stamp would disagree on it.
    if ($value -isnot [int] -and $value -isnot [long]) {
        throw "version.json '$name' must be an integer, got '$value'"
    }
    if ($value -lt 0 -or $value -gt 65535) {
        throw "version.json '$name'=$value is outside the 0..65535 range"
    }

    [int]$value
}

Write-Output ($parts -join '.')

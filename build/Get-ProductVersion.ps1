# Prints the product version as major.minor.patch, read from version.json.
#
# version.json at the repository root is the only place the product version is written. The .NET projects read
# it through Directory.Build.props; the installer build (build-installer.ps1) and the release-tag check
# (Resolve-PackageVersion.ps1 -VersionFile) read it through this script.
#
# Both readers apply one rule so that they cannot disagree: the file is exactly the three top-level integer keys
# major, minor and patch, each 0..65535 (the MSIX and assembly-version limit) with no leading zeros, none
# repeated and nothing else (a nested or extra key could be read as the wrong number by a first-match reader).
[CmdletBinding()]
param(
    [string] $VersionFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'version.json')
)

$ErrorActionPreference = 'Stop'

# The same pattern as ObfyVersionShape in Directory.Build.props.
$number = '(?:0|[1-9]\d{0,3}|[1-5]\d{4}|6[0-4]\d{3}|65[0-4]\d{2}|655[0-2]\d|6553[0-5])'
$shape = '(?i)^\s*\{\s*"(major|minor|patch)"\s*:\s*' + $number +
    '\s*,\s*"(?!\1")(major|minor|patch)"\s*:\s*' + $number +
    '\s*,\s*"(?!\1")(?!\2")(major|minor|patch)"\s*:\s*' + $number + '\s*\}\s*$'

if (-not (Test-Path -LiteralPath $VersionFile -PathType Leaf)) {
    throw "version.json not found at $VersionFile"
}

$raw = Get-Content -LiteralPath $VersionFile -Raw
try {
    $version = $raw | ConvertFrom-Json
} catch {
    throw "version.json at $VersionFile is not valid JSON: $($_.Exception.Message)"
}

# Specific messages first; the shape check below has the last word.
$allowed = @('major', 'minor', 'patch')
foreach ($property in @($version.PSObject.Properties)) {
    if ($property.Name -notin $allowed) {
        throw "version.json at $VersionFile has an unexpected '$($property.Name)'; only major, minor and patch are allowed"
    }
}

$parts = foreach ($name in $allowed) {
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

if (-not [regex]::IsMatch($raw, $shape)) {
    throw "version.json at $VersionFile must be exactly the three integer keys major, minor and patch (0..65535, no leading zeros, no repeated or other keys)"
}

Write-Output ($parts -join '.')

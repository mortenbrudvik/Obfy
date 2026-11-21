[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RefName,

    # version.json. When given, the major.minor.patch of the release must be the one in this file, so the NuGet
    # package, the installer and the MSIX cannot be released under different versions. A prerelease or build
    # suffix on the tag (1.2.3-beta.1) is allowed; version.json carries only the numbers.
    [string] $VersionFile
)

# Release tags are v1.2.3 or 1.2.3, with an optional prerelease or build suffix.
if ($RefName -match '^v?(?<Version>(?<Numbers>\d+\.\d+\.\d+)(?:[-+][0-9A-Za-z.-]+)?)$') {
    $version = $Matches.Version
    $numbers = $Matches.Numbers

    if ($VersionFile) {
        try {
            $expected = (& (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1') -VersionFile $VersionFile | Out-String).Trim()
        } catch {
            Write-Error "Cannot check '$RefName' against version.json: $($_.Exception.Message)"
            exit 1
        }

        if ($numbers -ne $expected) {
            Write-Error "Release '$RefName' does not match version.json ($expected). Bump version.json to $numbers and merge it before tagging, or tag v$expected."
            exit 1
        }
    }

    Write-Output $version
    exit 0
}

Write-Error "Release version must look like 1.2.3 or v1.2.3. Got '$RefName'."
exit 1

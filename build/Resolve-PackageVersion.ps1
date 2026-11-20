[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RefName
)

# Release tags are v1.2.3 or 1.2.3, with an optional prerelease or build suffix.
if ($RefName -match '^v?(?<Version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$') {
    Write-Output $Matches.Version
    exit 0
}

Write-Error "Release version must look like 1.2.3 or v1.2.3. Got '$RefName'."
exit 1

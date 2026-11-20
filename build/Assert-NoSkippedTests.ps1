# Fails when a dotnet test TRX folder recorded a skip, or the filter matched nothing.
# A green platform job must have executed every selected test.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Continue'

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    Write-Error "No TRX results in '$ResultsDirectory'."
    exit 1
}

$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter *.trx -Recurse -File)
if ($files.Count -eq 0) {
    Write-Error "No TRX results in '$ResultsDirectory'."
    exit 1
}

$total = 0
$skipped = 0
foreach ($file in $files) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($file.FullName)
    $counters = $xml.SelectSingleNode('//*[local-name()="Counters"]')
    if (-not $counters) {
        Write-Error "TRX counters are missing in $($file.Name)."
        exit 1
    }

    foreach ($name in @('total', 'notExecuted')) {
        $value = $counters.GetAttribute($name)
        if ([string]::IsNullOrWhiteSpace($value)) {
            Write-Error "TRX counters are missing '$name' in $($file.Name)."
            exit 1
        }
    }

    $total += [int]$counters.GetAttribute('total')
    $skipped += [int]$counters.GetAttribute('notExecuted')
}

if ($total -lt 1) {
    Write-Error "Platform filter matched no tests."
    exit 1
}

if ($skipped -gt 0) {
    Write-Error "Platform tests were skipped ($skipped of $total)."
    exit 1
}

Write-Output "Executed $total platform tests."
exit 0

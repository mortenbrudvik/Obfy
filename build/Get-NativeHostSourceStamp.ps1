# SHA-256 of the inputs that change Obfy.NativeHost.exe.
# The checked-in dist/sources.sha256 must match this. A compiler-only rebuild may
# change the exe bytes; that is not a source change and does not change this stamp.
# Line endings are normalized (CRLF -> LF) before hashing, so the stamp does not depend on
# how a checkout terminates its lines.
[CmdletBinding()]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$inputs = @(
    'Src/Obfy.NativeHost/host.c',
    'Src/Obfy.NativeHost/build.ps1',
    'Src/Obfy.PackedBootstrap/PackedBootstrap.cs',
    'Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj'
)

$buffer = New-Object System.IO.MemoryStream
$utf8 = [System.Text.Encoding]::UTF8
$latin1 = [System.Text.Encoding]::Latin1
foreach ($relative in $inputs) {
    $path = Join-Path $RepoRoot ($relative -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Write-Error "Native host source input is missing: $relative"
        exit 1
    }

    $name = $utf8.GetBytes($relative)
    $buffer.Write($name, 0, $name.Length)
    $buffer.WriteByte(0)
    # Hash the content with CRLF folded to LF so the stamp is the same for a Windows checkout
    # (autocrlf), a Linux/macOS checkout and the git blob. Latin-1 maps each byte to one char, so
    # the round trip cannot disturb any other byte.
    $raw = [System.IO.File]::ReadAllBytes($path)
    $payload = $latin1.GetBytes($latin1.GetString($raw).Replace("`r`n", "`n"))
    $length = [System.BitConverter]::GetBytes([int64]$payload.LongLength)
    $buffer.Write($length, 0, $length.Length)
    $buffer.Write($payload, 0, $payload.Length)
}

$sha = [System.Security.Cryptography.SHA256]::HashData($buffer.ToArray())
Write-Output ([Convert]::ToHexString($sha).ToLowerInvariant())
exit 0

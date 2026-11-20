$ErrorActionPreference = "Stop"

# The process listening on the dev server's port, if any.
function Get-PortOwner {
    $listener = Get-NetTCPConnection -LocalPort 5250 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) { Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" }
}

# $known and every process started below them, found through parent ids: npm starts Vite three levels down
# (npm -> node npm-cli -> cmd -> node vite), so stopping npm alone leaves Vite running. Exited processes stay in the
# list, so a child is still found after its parent has gone (Ctrl+C reaches them all). A child starts after its parent
# and before any newer process Windows has given the parent's id to, so no other program's process is taken for ours.
function Get-DevServerTree([object[]] $known) {
    $all = @(Get-CimInstance Win32_Process)
    $tree = [System.Collections.Generic.List[object]]::new()
    $known | ForEach-Object { $tree.Add($_) }
    for ($i = 0; $i -lt $tree.Count; $i++) {
        $parent = $tree[$i]
        $successor = $all |
            Where-Object { $_.ProcessId -eq $parent.ProcessId -and $_.CreationDate -gt $parent.CreationDate }
        foreach ($p in $all) {
            if ($p.ParentProcessId -eq $parent.ProcessId -and $p.CreationDate -ge $parent.CreationDate -and
                (-not $successor -or $p.CreationDate -lt $successor.CreationDate) -and
                -not ($tree | Where-Object { $_.ProcessId -eq $p.ProcessId -and $_.CreationDate -eq $p.CreationDate }))
            {
                $tree.Add($p)
            }
        }
    }
    $tree
}

# Vite uses strictPort, and the wait below would take another server's answer for its own.
$owner = Get-PortOwner
if ($owner) {
    Write-Host "Port 5250 is already in use by process $($owner.ProcessId): $($owner.CommandLine ?? $owner.Name)" `
        -ForegroundColor Red
    Write-Host "Stop that process (another dev server?) and run this script again." -ForegroundColor Red
    exit 1
}

$viteProcess = Start-Process -FilePath "npm" -ArgumentList "run dev" `
    -WorkingDirectory "$PSScriptRoot\..\Src\WebApp" `
    -PassThru -NoNewWindow
$devServer = @(Get-CimInstance Win32_Process -Filter "ProcessId = $($viteProcess.Id)")

try {
    Write-Host "Waiting for Vite dev server on http://localhost:5250..."
    $timeout = 30
    $elapsed = 0
    while ($elapsed -lt $timeout) {
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:5250" -TimeoutSec 1 -ErrorAction Stop
            Write-Host "Vite dev server is ready."
            break
        } catch {
            Start-Sleep -Seconds 1
            $elapsed++
        }
        $devServer = Get-DevServerTree $devServer
    }
    $devServer = Get-DevServerTree $devServer
    if ($elapsed -ge $timeout) {
        Write-Error "Vite dev server did not start within ${timeout}s"
    }

    dotnet run --project "$PSScriptRoot\..\Src\Obfy.UI\Obfy.UI.csproj" -p:SkipFrontendBuild=true
}
finally {
    # Until none is left: a process can start a child between the listing and the stop.
    for ($round = 0; $round -lt 5; $round++) {
        $devServer = Get-DevServerTree $devServer
        $running = @(Get-CimInstance Win32_Process | Where-Object {
            $p = $_
            $devServer | Where-Object { $_.ProcessId -eq $p.ProcessId -and $_.CreationDate -eq $p.CreationDate }
        })
        if (-not $running) { break }
        $running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Process -Id $running.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }
    $owner = Get-PortOwner
    if ($owner) {
        Write-Warning ("Port 5250 is still in use by process $($owner.ProcessId): " +
            "$($owner.CommandLine ?? $owner.Name)")
    } else {
        Write-Host "Dev servers stopped."
    }
}

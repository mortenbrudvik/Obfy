# Build Obfy Installer
# Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$InnoSetupPath,
    [switch]$SkipPublish,
    [switch]$SkipObfuscation,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BuildDir = $PSScriptRoot
$PublishDir = Join-Path $BuildDir "publish"
$PublishDirUI = Join-Path $BuildDir "publish-ui"
$OutputDir = Join-Path $BuildDir "output"
$ConsoleProject = Join-Path $ProjectRoot "Src\Obfy.Console\Obfy.Console.csproj"
$UIProject = Join-Path $ProjectRoot "Src\Obfy.UI\Obfy.UI.csproj"
$RedistDir = Join-Path $BuildDir "redist"
$WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
$WebView2BootstrapperName = "MicrosoftEdgeWebview2Setup.exe"

# The bootstrapper runs elevated on every install that lacks WebView2, so a corrupt or substituted binary is a
# real hazard. Anything smaller than this is a truncated download or an HTML error page, not the ~1.7 MB signed
# installer.
$MinBootstrapperBytes = 100KB

function Test-Bootstrapper {
    # Returns $null when the file is trustworthy, otherwise a reason string.
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return "file does not exist"
    }

    $length = (Get-Item $Path).Length
    if ($length -lt $MinBootstrapperBytes) {
        return "only $length bytes - truncated download or an error page"
    }

    $signature = Get-AuthenticodeSignature $Path
    if ($signature.Status -ne "Valid") {
        return "Authenticode signature is not valid (status: $($signature.Status))"
    }
    if ($signature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
        return "not signed by Microsoft (subject: $($signature.SignerCertificate.Subject))"
    }
    return $null
}

function Invoke-Native {
    # A non-zero exit is returned, not thrown, so the caller can include the command's own text.
    # Stdout and stderr come back as one string.
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    $previous = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $text = & $Command 2>&1 | Out-String
        return [pscustomobject]@{
            ExitCode = $global:LASTEXITCODE
            Text     = $text
        }
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $previous
    }
}

function Assert-NativeSucceeded {
    param(
        [Parameter(Mandatory = $true)]
        $Result,
        [Parameter(Mandatory = $true)]
        [string]$What
    )

    if ($Result.ExitCode -ne 0) {
        throw "$What failed (exit $($Result.ExitCode)).`n$($Result.Text)"
    }
}

function Assert-ProcessedCli {
    # The processed obfy.exe has to start, protect a real assembly, and still read and write the documented
    # configuration keys. --version alone does not catch renamed ObfySettings properties: that CLI starts,
    # and -l standard never reads those names. A handwritten file does.
    param([Parameter(Mandatory = $true)][string]$CliExe)

    if (-not (Test-Path -LiteralPath $CliExe)) {
        throw "Protected CLI not found: $CliExe"
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("obfy-installer-cli-check-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        $version = Invoke-Native { & $CliExe --version }
        Assert-NativeSucceeded $version "obfy --version"
        if ($version.Text -notmatch "\d+\.\d+") {
            throw "obfy --version did not print a version.`n$($version.Text)"
        }

        $projectDir = Join-Path $work "probe"
        New-Item -ItemType Directory -Path $projectDir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $projectDir "InstallerProbe.csproj") -Encoding utf8 -Value @(
            '<Project Sdk="Microsoft.NET.Sdk">'
            '  <PropertyGroup>'
            '    <OutputType>Exe</OutputType>'
            '    <TargetFramework>net10.0</TargetFramework>'
            '    <ImplicitUsings>enable</ImplicitUsings>'
            '    <Nullable>enable</Nullable>'
            '  </PropertyGroup>'
            '</Project>'
        )
        Set-Content -LiteralPath (Join-Path $projectDir "Program.cs") -Encoding utf8 -Value 'Console.WriteLine("obfy-installer-check");'

        $built = Invoke-Native { dotnet build (Join-Path $projectDir "InstallerProbe.csproj") -c Release --nologo }
        Assert-NativeSucceeded $built "the installer start-check probe"
        $probeDir = Join-Path $projectDir "bin\Release\net10.0"
        $probe = Join-Path $probeDir "InstallerProbe.dll"
        if (-not (Test-Path -LiteralPath $probe)) {
            throw "Installer probe was not built: $probe"
        }

        $out = Join-Path $work "out"
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $probeDir "InstallerProbe.runtimeconfig.json") -Destination $out
        Copy-Item -LiteralPath (Join-Path $probeDir "InstallerProbe.deps.json") -Destination $out

        $protected = Invoke-Native { & $CliExe $probe -o $out -l standard --no-logo }
        Assert-NativeSucceeded $protected "obfy protecting the probe at -l standard"
        if ($protected.Text -notmatch "Strings Encrypted[\s\S]{0,80}?([1-9]\d*)") {
            throw "The protected CLI did not encrypt strings in the probe.`n$($protected.Text)"
        }

        $ran = Invoke-Native { dotnet (Join-Path $out "InstallerProbe.dll") }
        Assert-NativeSucceeded $ran "the probe protected at -l standard"
        if ($ran.Text -notmatch "obfy-installer-check") {
            throw "The probe protected at -l standard did not print its line.`n$($ran.Text)"
        }

        $generated = Join-Path $work "generated.json"
        $generate = Invoke-Native { & $CliExe config generate -o $generated }
        Assert-NativeSucceeded $generate "obfy config generate"
        $generatedText = Get-Content -LiteralPath $generated -Raw
        foreach ($key in @('"level"', '"stringEncryption"')) {
            if ($generatedText -notlike "*$key*") {
                throw "config generate did not write the documented key $key. The settings property names were renamed.`n$generatedText"
            }
        }

        $handwritten = Join-Path $work "handwritten.json"
        Set-Content -LiteralPath $handwritten -Encoding utf8 -Value @(
            '{'
            '  "level": "minimal",'
            '  "stringEncryption": { "enabled": false },'
            '  "symbolRenaming": { "enabled": false },'
            '  "controlFlow": { "enabled": false },'
            '  "metadata": { "removeDebugInfo": false, "removeAttributes": false }'
            '}'
        )

        $handOut = Join-Path $work "hand-out"
        New-Item -ItemType Directory -Path $handOut -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $probeDir "InstallerProbe.runtimeconfig.json") -Destination $handOut
        Copy-Item -LiteralPath (Join-Path $probeDir "InstallerProbe.deps.json") -Destination $handOut
        $handed = Invoke-Native { & $CliExe $probe -o $handOut -c $handwritten --no-logo }
        Assert-NativeSucceeded $handed "obfy with a handwritten configuration"
        if ($handed.Text -match "Unknown configuration key") {
            throw "The protected CLI ignored documented configuration keys.`n$($handed.Text)"
        }
        if ($handed.Text -notmatch "No transformations were applied") {
            throw "The handwritten configuration did not turn protection off.`n$($handed.Text)"
        }

        $handRan = Invoke-Native { dotnet (Join-Path $handOut "InstallerProbe.dll") }
        Assert-NativeSucceeded $handRan "the probe protected with a handwritten configuration"
        if ($handRan.Text -notmatch "obfy-installer-check") {
            throw "The probe protected with a handwritten configuration did not print its line.`n$($handRan.Text)"
        }

        Write-Host "    Protected CLI starts, obfuscates, and honors configuration keys" -ForegroundColor Green
        return [pscustomobject]@{ Probe = $probe; ProbeDir = $probeDir; Work = $work }
    }
    catch {
        if (Test-Path -LiteralPath $work) {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Invoke-DevTools {
    param(
        [Parameter(Mandatory = $true)][string]$WebSocketUrl,
        [Parameter(Mandatory = $true)][string]$Expression,
        [int]$TimeoutSec = 30
    )

    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    $cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSec))
    try {
        # GetResult() on a non-generic Task writes VoidTaskResult to the pipeline unless it is discarded.
        $null = $socket.ConnectAsync([Uri]$WebSocketUrl, $cts.Token).GetAwaiter().GetResult()
        $payload = @{
            id     = 1
            method = "Runtime.evaluate"
            params = @{
                expression    = $Expression
                awaitPromise  = $true
                returnByValue = $true
            }
        } | ConvertTo-Json -Compress -Depth 6
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        $null = $socket.SendAsync(
            [ArraySegment[byte]]::new($bytes),
            [System.Net.WebSockets.WebSocketMessageType]::Text,
            $true,
            $cts.Token).GetAwaiter().GetResult()

        $buffer = New-Object byte[] 262144
        while ($true) {
            $stream = New-Object System.IO.MemoryStream
            do {
                $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $cts.Token).GetAwaiter().GetResult()
                if ($received.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                    throw "DevTools closed the connection."
                }
                $stream.Write($buffer, 0, $received.Count)
            } while (-not $received.EndOfMessage)

            $message = [System.Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            if ($null -eq $message.id -or $message.id -ne 1) { continue }
            if ($message.error) {
                throw "DevTools refused the expression: $($message.error.message)"
            }
            $remote = $message.result
            if ($remote.exceptionDetails) {
                $detail = $remote.exceptionDetails.text
                if ($remote.exceptionDetails.exception -and $remote.exceptionDetails.exception.description) {
                    $detail = $remote.exceptionDetails.exception.description
                }
                throw "The page threw while the start check was reading it: $detail"
            }
            return [string]$remote.result.value
        }
    }
    finally {
        if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $null = $socket.CloseAsync(
                [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
                "done",
                [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        }
        $socket.Dispose()
        $cts.Dispose()
    }
}

function Get-DevToolsPageUrl {
    param([Parameter(Mandatory = $true)][string]$Port)

    $list = @(Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/list" -TimeoutSec 5)
    $page = $list | Where-Object { $_.type -eq "page" -and $_.url -like "*obfy.localassets.invalid*" } | Select-Object -First 1
    if (-not $page) {
        $page = $list | Where-Object { $_.type -eq "page" } | Select-Object -First 1
    }
    if (-not $page -or -not $page.webSocketDebuggerUrl) {
        throw "DevTools has no page target on port $Port."
    }
    return [string]$page.webSocketDebuggerUrl
}

function Get-LatestObfyLogTail {
    $logs = Join-Path $env:LOCALAPPDATA "Obfy\Logs"
    if (-not (Test-Path -LiteralPath $logs)) { return "No Obfy log folder." }
    $latest = Get-ChildItem -LiteralPath $logs -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { return "No Obfy log file." }
    $tail = Get-Content -LiteralPath $latest.FullName -Tail 20 -ErrorAction SilentlyContinue
    return "Latest log $($latest.Name):`n$($tail -join "`n")"
}

function Assert-ProcessedUi {
    # Proves the protected shell starts, the page paints the Files screen (not the crash view), the bridge
    # answers with the documented JSON names, a level change sticks, and a startup assembly can be protected.
    # It does not prove every screen, the native file dialog, or a machine with no interactive desktop.
    # An elevated WebView2 ignores WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS. This check then sets the same
    # machine policy the UI-test CI job sets, and removes that value when it finishes.
    param(
        [Parameter(Mandatory = $true)][string]$UiExe,
        [Parameter(Mandatory = $true)][string]$Probe,
        [Parameter(Mandatory = $true)][string]$ProbeDir
    )

    if (-not (Test-Path -LiteralPath $UiExe)) {
        throw "Protected desktop app not found: $UiExe"
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("obfy-installer-ui-check-" + [guid]::NewGuid().ToString("N"))
    $output = Join-Path $work "out"
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $ProbeDir "InstallerProbe.runtimeconfig.json") -Destination $output
    Copy-Item -LiteralPath (Join-Path $ProbeDir "InstallerProbe.deps.json") -Destination $output

    $webViewProfile = Join-Path $work "webview2"
    $preferences = Join-Path $work "ui-preferences.json"
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $UiExe
    $start.WorkingDirectory = Split-Path -Parent $UiExe
    $start.UseShellExecute = $false
    $start.ArgumentList.Add($Probe)
    $start.ArgumentList.Add("-o")
    $start.ArgumentList.Add($output)
    # Set on this process only. The developer's WebView2 profile and UI preferences stay untouched.
    $debugArguments = "--remote-debugging-port=0 --remote-allow-origins=*"
    $start.Environment["OBFY_WEBVIEW2_USER_DATA"] = $webViewProfile
    $start.Environment["OBFY_UI_PREFERENCES"] = $preferences
    $start.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = $debugArguments

    # An elevated host ignores the variable above. The machine policy is what reaches that host.
    # The value is removed in the finally block so a local elevated build does not leave the port forced on.
    $policyPath = "HKLM:\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments"
    $policyName = "ObfyUI.exe"
    $policyWritten = $false
    $policyHadValue = $false
    $policyPrevious = $null
    $policyKeyExisted = $false
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    $app = $null
    try {
        if ($elevated) {
            $policyKeyExisted = Test-Path -LiteralPath $policyPath
            New-Item -Path $policyPath -Force | Out-Null
            $names = @((Get-Item -LiteralPath $policyPath).Property)
            if ($names -contains $policyName) {
                $policyHadValue = $true
                $policyPrevious = (Get-ItemProperty -LiteralPath $policyPath -Name $policyName).$policyName
            }
            New-ItemProperty -LiteralPath $policyPath -Name $policyName -Value $debugArguments -PropertyType String -Force | Out-Null
            $policyWritten = $true
        }

        $app = [System.Diagnostics.Process]::Start($start)
        if (-not $app) {
            throw "ObfyUI.exe did not start."
        }

        $portFile = Join-Path $webViewProfile "EBWebView\DevToolsActivePort"
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while (-not (Test-Path -LiteralPath $portFile) -and [DateTime]::UtcNow -lt $deadline) {
            if ($app.HasExited) {
                throw "ObfyUI.exe exited ($($app.ExitCode)) before the page opened a debug port. $(Get-LatestObfyLogTail)"
            }
            Start-Sleep -Seconds 1
        }
        if (-not (Test-Path -LiteralPath $portFile)) {
            throw "ObfyUI.exe did not open a WebView2 debug port. This check needs an interactive session and the WebView2 runtime (elevated: $elevated, machine policy written: $policyWritten). $(Get-LatestObfyLogTail)"
        }

        $port = (Get-Content -LiteralPath $portFile -TotalCount 1).Trim()
        $socketUrl = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not $socketUrl -and [DateTime]::UtcNow -lt $deadline) {
            try { $socketUrl = Get-DevToolsPageUrl -Port $port } catch { Start-Sleep -Seconds 1 }
        }
        if (-not $socketUrl) {
            throw "DevTools listed no page for the protected desktop app. $(Get-LatestObfyLogTail)"
        }

        $readyJs = '(() => { const nav = document.querySelector("[data-testid=nav-files]"); const crash = document.querySelector(".crash"); const bridge = !!(window.chrome && window.chrome.webview && window.chrome.webview.hostObjects && window.chrome.webview.hostObjects.apibridge); const text = ((document.body && document.body.innerText) || "").slice(0, 240); return JSON.stringify({ nav: !!nav, current: nav && nav.getAttribute("aria-current"), crash: !!crash, bridge, text }); })()'
        $seen = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($app.HasExited) {
                throw "ObfyUI.exe exited ($($app.ExitCode)) before the Files screen was up. $(Get-LatestObfyLogTail)"
            }
            $seen = Invoke-DevTools -WebSocketUrl $socketUrl -Expression $readyJs -TimeoutSec 15 | ConvertFrom-Json
            if ($seen.crash -and -not $seen.nav) {
                throw "The protected desktop app showed the page crash view. $($seen.text)"
            }
            if ($seen.nav -and $seen.bridge -and $seen.current -eq "page") { break }
            $seen = $null
            Start-Sleep -Seconds 1
        }
        if (-not $seen) {
            throw "The Files screen did not render in the protected desktop app. $(Get-LatestObfyLogTail)"
        }

        $checkJs = '(async () => { const bridge = window.chrome.webview.hostObjects.apibridge; const info = JSON.parse(await bridge.InvokeMethod("app", "info", "{}")); const files = JSON.parse(await bridge.InvokeMethod("files", "get", "{}")); const before = JSON.parse(await bridge.InvokeMethod("settings", "get", "{}")); const applied = JSON.parse(await bridge.InvokeMethod("settings", "applyLevel", JSON.stringify({ level: "Minimal" }))); const after = JSON.parse(await bridge.InvokeMethod("settings", "get", "{}")); const back = JSON.parse(await bridge.InvokeMethod("settings", "applyLevel", JSON.stringify({ level: "Standard" }))); const run = JSON.parse(await bridge.InvokeMethod("run", "get", "{}")); const row = files.data && files.data.files && files.data.files[0]; return JSON.stringify({ infoSuccess: info.success === true, version: info.data && info.data.version, filesKey: !!(files.data && Object.prototype.hasOwnProperty.call(files.data, "files")), hasIncluded: !!(files.data && files.data.hasIncludedFiles), fileName: row && row.name, outputFolder: files.data && files.data.outputFolder, levelBefore: before.data && before.data.level, levelApplied: applied.data && applied.data.level, levelAfter: after.data && after.data.level, techniques: Array.isArray(after.data && after.data.techniques), levelBack: back.data && back.data.level, canStart: !!(run.data && run.data.canStart), blocked: run.data && run.data.blockedReason }); })()'
        $state = Invoke-DevTools -WebSocketUrl $socketUrl -Expression $checkJs -TimeoutSec 30 | ConvertFrom-Json
        if ($state.infoSuccess -ne $true -or -not $state.version) {
            throw "app.info did not return { success, data.version }. The bridge envelope or AppInfo was renamed."
        }
        if ($state.filesKey -ne $true -or $state.hasIncluded -ne $true -or $state.fileName -ne "InstallerProbe.dll") {
            throw "files.get did not list the startup assembly under the documented names (files, hasIncludedFiles, name). file=$($state.fileName) included=$($state.hasIncluded)"
        }
        if ($state.levelApplied -ne "Minimal" -or $state.levelAfter -ne "Minimal" -or $state.techniques -ne $true) {
            throw "settings.applyLevel did not stick. before=$($state.levelBefore) applied=$($state.levelApplied) after=$($state.levelAfter)"
        }
        if ($state.levelBack -ne "Standard") {
            throw "settings.applyLevel did not restore Standard before the run (got $($state.levelBack))."
        }
        if ($state.canStart -ne $true) {
            throw "The run cannot start: $($state.blocked)"
        }

        $startJs = '(async () => { const bridge = window.chrome.webview.hostObjects.apibridge; const started = JSON.parse(await bridge.InvokeMethod("run", "start", "{}")); return JSON.stringify({ success: started.success === true, status: started.data && started.data.status, message: started.data && started.data.message }); })()'
        $started = Invoke-DevTools -WebSocketUrl $socketUrl -Expression $startJs -TimeoutSec 30 | ConvertFrom-Json
        if ($started.success -ne $true) {
            throw "run.start failed: $($started.message)"
        }

        $pollJs = '(async () => { const bridge = window.chrome.webview.hostObjects.apibridge; const run = JSON.parse(await bridge.InvokeMethod("run", "get", "{}")); const finished = !!document.querySelector("[data-testid=results-finished]"); return JSON.stringify({ success: run.success === true, status: run.data && run.data.status, failed: run.data && run.data.failed, message: run.data && run.data.message, finished }); })()'
        $done = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($app.HasExited) {
                throw "ObfyUI.exe exited ($($app.ExitCode)) during the run. $(Get-LatestObfyLogTail)"
            }
            $done = Invoke-DevTools -WebSocketUrl $socketUrl -Expression $pollJs -TimeoutSec 20 | ConvertFrom-Json
            if ($done.status -eq "Faulted" -or $done.status -eq "Cancelled") { break }
            if ($done.status -eq "Done" -and $done.finished) { break }
            Start-Sleep -Seconds 1
        }
        if (-not $done -or $done.status -ne "Done" -or [int]$done.failed -ne 0 -or -not $done.finished) {
            throw "The protected desktop app did not finish a run. status=$($done.status) failed=$($done.failed) finished=$($done.finished) message=$($done.message)"
        }

        $produced = Join-Path $output "InstallerProbe.dll"
        if (-not (Test-Path -LiteralPath $produced)) {
            throw "The desktop run did not write $produced"
        }
        $ran = Invoke-Native { dotnet $produced }
        Assert-NativeSucceeded $ran "the assembly protected by the desktop app"
        if ($ran.Text -notmatch "obfy-installer-check") {
            throw "The assembly protected by the desktop app did not print its line.`n$($ran.Text)"
        }

        Write-Host "    Protected desktop app shows Files, answers the bridge, and completes a run" -ForegroundColor Green
    }
    finally {
        if ($app -and -not $app.HasExited) {
            try { $app.Kill($true) } catch { }
            $app.WaitForExit(10000) | Out-Null
        }
        if ($app) { $app.Dispose() }
        if ($policyWritten) {
            if ($policyHadValue) {
                New-ItemProperty -LiteralPath $policyPath -Name $policyName -Value $policyPrevious -PropertyType String -Force | Out-Null
            }
            else {
                Remove-ItemProperty -LiteralPath $policyPath -Name $policyName -ErrorAction SilentlyContinue
            }
            if (-not $policyKeyExisted) {
                Remove-Item -LiteralPath $policyPath -ErrorAction SilentlyContinue
            }
        }
        if (Test-Path -LiteralPath $work) {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# End of start-check functions.

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Obfy Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find Inno Setup
Write-Host "[1/7] Locating Inno Setup..." -ForegroundColor Yellow

if (-not $InnoSetupPath) {
    $SearchPaths = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $SearchPaths) {
        if (Test-Path $path) {
            $InnoSetupPath = $path
            break
        }
    }
}

if (-not $InnoSetupPath -or -not (Test-Path $InnoSetupPath)) {
    Write-Host "ERROR: Inno Setup 6 not found!" -ForegroundColor Red
    Write-Host "Searched locations:" -ForegroundColor Gray
    Write-Host "  - $env:LOCALAPPDATA\Programs\Inno Setup 6\" -ForegroundColor Gray
    Write-Host "  - C:\Program Files (x86)\Inno Setup 6\" -ForegroundColor Gray
    Write-Host "  - C:\Program Files\Inno Setup 6\" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Install from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "Or specify path: .\build-installer.ps1 -InnoSetupPath 'C:\Path\To\ISCC.exe'" -ForegroundColor Gray
    exit 1
}

Write-Host "  Found: $InnoSetupPath" -ForegroundColor Green

# The product version comes from version.json only (Get-ProductVersion.ps1 validates it). It is read before the
# long publish so a bad file fails fast; the publish itself takes the same version from Directory.Build.props, and
# ObfySetup.iss has no default and stops unless it is given /DMyAppVersion. -SkipBuild never runs the compiler.
$InstallerVersion = $null
if (-not $SkipBuild) {
    $InstallerVersion = (& (Join-Path $BuildDir "Get-ProductVersion.ps1") | Out-String).Trim()
    Write-Host "  Version: $InstallerVersion (version.json)" -ForegroundColor Green
}

# Step 2: Clean publish directories
if (-not $SkipPublish) {
    Write-Host "[2/7] Cleaning publish directories..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
        Write-Host "  Removed existing CLI publish directory" -ForegroundColor Green
    }

    if (Test-Path $PublishDirUI) {
        Remove-Item -Path $PublishDirUI -Recurse -Force
        Write-Host "  Removed existing UI publish directory" -ForegroundColor Green
    }
} else {
    # -SkipPublish reuses the existing publish output; deleting it here would leave the installer nothing to package.
    Write-Host "[2/7] Keeping existing publish directories (--SkipPublish)" -ForegroundColor Gray
}

# Step 3: Build and publish CLI
if (-not $SkipPublish) {
    Write-Host "[3/7] Publishing Obfy CLI (self-contained)..." -ForegroundColor Yellow

    $publishArgs = @(
        "publish",
        $ConsoleProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained",
        "-o", $PublishDir
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
        exit 1
    }

    # Verify obfy.exe exists
    $ExePath = Join-Path $PublishDir "obfy.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Host "ERROR: obfy.exe not found in publish output!" -ForegroundColor Red
        exit 1
    }

    $fileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count
    Write-Host "  Published $fileCount CLI files" -ForegroundColor Green
} else {
    Write-Host "[3/7] Skipping CLI publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 4: Build and publish UI
if (-not $SkipPublish) {
    Write-Host "[4/7] Publishing Obfy UI (self-contained)..." -ForegroundColor Yellow

    $publishArgsUI = @(
        "publish",
        $UIProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained",
        "-o", $PublishDirUI
    )

    & dotnet @publishArgsUI

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet publish UI failed!" -ForegroundColor Red
        exit 1
    }

    # Verify ObfyUI.exe exists
    $UIExePath = Join-Path $PublishDirUI "ObfyUI.exe"
    if (-not (Test-Path $UIExePath)) {
        Write-Host "ERROR: ObfyUI.exe not found in publish output!" -ForegroundColor Red
        exit 1
    }

    # The web UI is loaded from Assets\ beside ObfyUI.exe. The publish builds the frontend itself (the
    # frontend build is never skipped here), so a missing index.html means the frontend did not build.
    $UIIndexPath = Join-Path $PublishDirUI "Assets\index.html"
    if (-not (Test-Path $UIIndexPath)) {
        Write-Host "ERROR: Assets\index.html not found in UI publish output - the frontend did not build!" -ForegroundColor Red
        exit 1
    }

    # The installer excludes these too, but a source map must never be published next to the page.
    $strayWebSymbols = @(Get-ChildItem -Path (Join-Path $PublishDirUI "Assets") -Recurse -File -Include *.map, *.pdb)
    if ($strayWebSymbols.Count -gt 0) {
        Write-Host "ERROR: source maps or symbols found in Assets: $($strayWebSymbols.FullName -join ', ')" -ForegroundColor Red
        exit 1
    }

    $fileCountUI = (Get-ChildItem -Path $PublishDirUI -Recurse -File).Count
    Write-Host "  Published $fileCountUI UI files" -ForegroundColor Green
} else {
    Write-Host "[4/7] Skipping UI publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 5: Obfuscate published binaries
if (-not $SkipObfuscation -and -not $SkipPublish) {
    Write-Host "[5/7] Obfuscating assemblies..." -ForegroundColor Yellow

    $TempObfyDir = Join-Path $BuildDir "temp-obfy"
    $ObfuscatedCliDir = Join-Path $BuildDir "obfuscated-cli"
    $ObfuscatedUiDir = Join-Path $BuildDir "obfuscated-ui"

    # Create temp directory with copy of obfy for bootstrap
    if (Test-Path $TempObfyDir) { Remove-Item $TempObfyDir -Recurse -Force }
    if (Test-Path $ObfuscatedCliDir) { Remove-Item $ObfuscatedCliDir -Recurse -Force }
    if (Test-Path $ObfuscatedUiDir) { Remove-Item $ObfuscatedUiDir -Recurse -Force }

    Copy-Item $PublishDir $TempObfyDir -Recurse
    New-Item -Path $ObfuscatedCliDir -ItemType Directory -Force | Out-Null
    New-Item -Path $ObfuscatedUiDir -ItemType Directory -Force | Out-Null

    $TempObfyExe = Join-Path $TempObfyDir "obfy.exe"

    # Own DLLs only. EXEs are native app hosts. Obfy.VmRuntime.dll is the embedded interpreter
    # payload inside Obfy.Core, not a reference of the shipped tool; renaming a loose copy would
    # not update that resource.
    # These assemblies reference each other. Protecting them one at a time renames public types in
    # Obfy.Core.dll while obfy.dll (and the UI assemblies) still name the old types, so the shipped
    # CLI cannot start. One invocation per publish directory is a closed set: cross-assembly
    # references are rewritten together, and nothing is copied back until every output exists.
    # build/self-obfuscation.json is the whole settings object (-c, no technique flags). It keeps
    # string encryption, control flow, renaming and metadata stripping, and it keeps the names the
    # shipped programs still read: configuration and snapshot properties, bridge envelopes, request
    # records, and XAML-bound properties on *ViewModel types.
    $SelfObfuscationConfig = Join-Path $BuildDir "self-obfuscation.json"
    if (-not (Test-Path -LiteralPath $SelfObfuscationConfig)) {
        throw "Self-obfuscation config not found: $SelfObfuscationConfig"
    }

    $ProductAssemblies = @(
        "obfy.dll",
        "ObfyUI.dll",
        "Obfy.App.dll",
        "Obfy.Core.dll",
        "Obfy.Interop.dll",
        "Obfy.WebView.dll",
        "Obfy.Common.dll",
        "Settings.Core.dll",
        "Logging.Core.dll"
    )

    function Protect-ProductAssemblies([string]$SourceDir, [string]$OutputDir) {
        $inputs = @()
        foreach ($name in $ProductAssemblies) {
            $path = Join-Path $SourceDir $name
            if (Test-Path -LiteralPath $path) {
                $inputs += $path
            }
        }
        if ($inputs.Count -eq 0) { return }

        & $TempObfyExe @inputs -o $OutputDir -c $SelfObfuscationConfig
        if ($LASTEXITCODE -ne 0) {
            throw "Self-obfuscation failed (exit $LASTEXITCODE). The installer will not package the original assemblies."
        }
        $outputs = @()
        foreach ($path in $inputs) {
            $name = Split-Path -Leaf $path
            $obfuscated = Join-Path $OutputDir $name
            if (-not (Test-Path -LiteralPath $obfuscated)) {
                throw "Self-obfuscation did not write $name. The installer will not package the original assembly."
            }
            $outputs += $obfuscated
        }
        foreach ($obfuscated in $outputs) {
            $name = Split-Path -Leaf $obfuscated
            Copy-Item -LiteralPath $obfuscated -Destination (Join-Path $SourceDir $name) -Force
            Write-Host "    Obfuscated: $name" -ForegroundColor Green
        }
    }

    Write-Host "  Obfuscating CLI assemblies..." -ForegroundColor Gray
    Protect-ProductAssemblies $PublishDir $ObfuscatedCliDir

    Write-Host "  Obfuscating UI assemblies..." -ForegroundColor Gray
    Protect-ProductAssemblies $PublishDirUI $ObfuscatedUiDir

    # Run the protected programs before anything is packaged. A closed set that renames a name the
    # programs still read (a configuration key, a bridge property, a XAML binding) exits here.
    Write-Host "  Checking that the protected programs start..." -ForegroundColor Gray
    $cliCheck = Assert-ProcessedCli -CliExe (Join-Path $PublishDir "obfy.exe")
    try {
        Assert-ProcessedUi -UiExe (Join-Path $PublishDirUI "ObfyUI.exe") -Probe $cliCheck.Probe -ProbeDir $cliCheck.ProbeDir
    }
    finally {
        if ($cliCheck.Work -and (Test-Path -LiteralPath $cliCheck.Work)) {
            Remove-Item -LiteralPath $cliCheck.Work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Cleanup temp directories
    Remove-Item $TempObfyDir -Recurse -Force
    Remove-Item $ObfuscatedCliDir -Recurse -Force
    Remove-Item $ObfuscatedUiDir -Recurse -Force

    Write-Host "  Obfuscation complete" -ForegroundColor Green
} elseif ($SkipObfuscation) {
    Write-Host "[5/7] Skipping obfuscation (--SkipObfuscation)" -ForegroundColor Gray
} else {
    Write-Host "[5/7] Skipping obfuscation (no publish)" -ForegroundColor Gray
}

# Step 6: Create output directory
Write-Host "[6/7] Preparing output directory..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
}
Write-Host "  Output: $OutputDir" -ForegroundColor Green

# The installer bundles the WebView2 Evergreen bootstrapper (ObfySetup.iss installs it only when the runtime is absent).
if (-not $SkipBuild) {
    if (-not (Test-Path $RedistDir)) {
        New-Item -Path $RedistDir -ItemType Directory -Force | Out-Null
    }
    $BootstrapperPath = Join-Path $RedistDir $WebView2BootstrapperName

    # The cached copy is re-verified every run: a file that failed mid-download would otherwise be reused forever.
    $Problem = Test-Bootstrapper -Path $BootstrapperPath
    if (-not $Problem) {
        Write-Host "  WebView2 bootstrapper (cached): $BootstrapperPath" -ForegroundColor Green
    } else {
        if (Test-Path $BootstrapperPath) {
            Write-Host "  Rejecting cached bootstrapper ($Problem) - re-downloading." -ForegroundColor Yellow
            Remove-Item $BootstrapperPath -Force
        }

        # Download to a staging name so a failed transfer never lands on the real path.
        $StagingPath = "$BootstrapperPath.download"
        Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue

        Write-Host "  Downloading $WebView2BootstrapperName ..." -ForegroundColor Gray
        try {
            Invoke-WebRequest -Uri $WebView2BootstrapperUrl -OutFile $StagingPath -UseBasicParsing
        } catch {
            Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue
            Write-Host "ERROR: Failed to download WebView2 bootstrapper: $_" -ForegroundColor Red
            Write-Host "  Place $WebView2BootstrapperName in build\redist\ manually and re-run." -ForegroundColor Yellow
            exit 1
        }

        $Problem = Test-Bootstrapper -Path $StagingPath
        if ($Problem) {
            Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue
            Write-Host "ERROR: Downloaded WebView2 bootstrapper rejected: $Problem" -ForegroundColor Red
            exit 1
        }

        Move-Item -Path $StagingPath -Destination $BootstrapperPath -Force
        Write-Host "  Downloaded and verified: $BootstrapperPath" -ForegroundColor Green
    }
}

# Step 7: Build installer
if (-not $SkipBuild) {
    Write-Host "[7/7] Building installer..." -ForegroundColor Yellow

    $IssFile = Join-Path $BuildDir "ObfySetup.iss"

    if (-not (Test-Path $IssFile)) {
        Write-Host "ERROR: ObfySetup.iss not found!" -ForegroundColor Red
        exit 1
    }

    Push-Location $BuildDir
    try {
        & $InnoSetupPath "/DMyAppVersion=$InstallerVersion" $IssFile

        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Inno Setup compilation failed!" -ForegroundColor Red
            exit 1
        }
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[7/7] Skipping installer build (--SkipBuild)" -ForegroundColor Gray
}

# Report results
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$installerFiles = Get-ChildItem -Path $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending
if ($installerFiles.Count -gt 0) {
    $latestInstaller = $installerFiles[0]
    $sizeMB = [math]::Round($latestInstaller.Length / 1MB, 2)
    Write-Host ""
    Write-Host "Installer: $($latestInstaller.FullName)" -ForegroundColor Cyan
    Write-Host "Size: $sizeMB MB" -ForegroundColor Cyan
}

# Serializes developer builds so parallel agents do not race on obj/bin.
# Usage: pwsh -File scripts/dev-build.ps1 -Agent ui-nav [-Test]
param(
    [string]$Agent = "lead",
    [switch]$Test,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$logDir = Join-Path $root "artifacts"
$null = New-Item -ItemType Directory -Force -Path $logDir
$log = Join-Path $logDir ("build-{0}.log" -f $Agent)

# One build at a time across every agent in this desktop session.
$mutex = [System.Threading.Mutex]::new($false, "Local\WiinaDevBuild")
$acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(15))
if (-not $acquired) {
    Write-Error "Timed out waiting for the Wiina build mutex."
    exit 2
}

try {
    if ($Test) {
        & $dotnet test (Join-Path $root "tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj") `
            -c $Configuration -v q 2>&1 | Tee-Object -FilePath $log
    }
    else {
        & $dotnet build (Join-Path $root "src\Wiina.App\Wiina.App.csproj") `
            -c $Configuration -p:Platform=x64 -v m 2>&1 | Tee-Object -FilePath $log
    }
    $code = $LASTEXITCODE
    Write-Host ("LOG: " + $log)
    exit $code
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}

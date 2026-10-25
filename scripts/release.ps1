#Requires -Version 7.0
<#
.SYNOPSIS
    Cuts a Wiina release: synchronises the version, builds the portable zip and
    (when Inno Setup is installed) the Windows installer.

.DESCRIPTION
    `-Version x.y.z` is written to every version source of truth before the
    build so the packaged application and the artifact names agree:

      Directory.Build.props        <Version>
      src/Wiina.Core/Branding.cs   Branding.Version
      src/Wiina.Core/Release/PackageManifest.cs
      src/Wiina.App/app.manifest   assemblyIdentity version x.y.z.0
      scripts/package.ps1          default -Version
      installer/wiina.iss          MyAppVersion

    The version is then verified against the built managed assembly, the
    portable archive is produced by scripts/package.ps1, and Inno Setup is
    invoked when iscc.exe is available. ISCC is looked up on PATH, then under
    %LOCALAPPDATA%\Programs\Inno Setup 6, then under Program Files. Artifacts
    are written to artifacts/ and are gitignored; nothing is committed by this
    script.

.PARAMETER Version
    Semantic version to release, e.g. 1.1.0.

.PARAMETER NoInstaller
    Build only the portable zip, even when Inno Setup is installed.

.PARAMETER SkipPackage
    Update and verify the version without building the portable zip. Useful for
    checking the version-sync edit before committing it.

.EXAMPLE
    pwsh -File scripts/release.ps1 -Version 1.1.0

.EXAMPLE
    pwsh -File scripts/release.ps1 -Version 1.1.0 -NoInstaller
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$NoInstaller,

    [switch]$SkipPackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$runtime = 'win-x64'
$artifactsDir = Join-Path $root 'artifacts'
$archivePath = Join-Path $artifactsDir "wiina-$Version-$runtime.zip"
$installerPath = Join-Path $artifactsDir "Wiina-Setup-$Version-x64.exe"
$publishDir = Join-Path $artifactsDir 'publish'
$project = Join-Path $root 'src\Wiina.App\Wiina.App.csproj'
$packageScript = Join-Path $root 'scripts\package.ps1'

$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
}
if (-not $dotnet) {
    throw 'dotnet was not found. Install the .NET SDK or set DOTNET_ROOT.'
}

function Update-FileText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Version source not found: $Path"
    }

    $text = [System.IO.File]::ReadAllText($Path)
    $regex = [regex]::new($Pattern)
    if (-not $regex.IsMatch($text)) {
        throw "Could not find $Label in $Path."
    }

    $updated = $regex.Replace($text, $Replacement, 1)
    if ($updated -ne $text) {
        [System.IO.File]::WriteAllText($Path, $updated)
        Write-Host "  updated $Label in $([System.IO.Path]::GetRelativePath($root, $Path))"
    }
    else {
        Write-Host "  already current $Label in $([System.IO.Path]::GetRelativePath($root, $Path))"
    }
}

Write-Host "Synchronising version $Version..."

Update-FileText -Path (Join-Path $root 'Directory.Build.props') `
    -Pattern '<Version>[^<]+</Version>' `
    -Replacement "<Version>$Version</Version>" `
    -Label '<Version>'

Update-FileText -Path (Join-Path $root 'src\Wiina.Core\Branding.cs') `
    -Pattern 'public const string Version = "[^"]+";' `
    -Replacement "public const string Version = `"$Version`";" `
    -Label 'Branding.Version'

Update-FileText -Path (Join-Path $root 'src\Wiina.Core\Release\PackageManifest.cs') `
    -Pattern 'public const string Version = "[^"]+";' `
    -Replacement "public const string Version = `"$Version`";" `
    -Label 'PackageManifest.Version'

Update-FileText -Path (Join-Path $root 'src\Wiina.Core\Release\PackageManifest.cs') `
    -Pattern 'public const string ArchiveFileName = "[^"]+";' `
    -Replacement "public const string ArchiveFileName = `"wiina-$Version-$runtime.zip`";" `
    -Label 'PackageManifest.ArchiveFileName'

Update-FileText -Path (Join-Path $root 'src\Wiina.App\app.manifest') `
    -Pattern '(<assemblyIdentity[^>]*\bversion=")[^"]+(")' `
    -Replacement "`${1}$Version.0`${2}" `
    -Label 'assemblyIdentity version'

Update-FileText -Path $packageScript `
    -Pattern '\[string\]\$Version = ''[^'']+''' `
    -Replacement ('[string]$Version = ''' + $Version + '''') `
    -Label 'package.ps1 default version'

Update-FileText -Path (Join-Path $root 'installer\wiina.iss') `
    -Pattern '#define MyAppVersion "[^"]+"' `
    -Replacement "#define MyAppVersion `"$Version`"" `
    -Label 'MyAppVersion'

function Get-ManagedAssemblyVersion {
    param([Parameter(Mandatory = $true)][string]$ReleaseRoot)

    $assembly = Get-ChildItem -LiteralPath $ReleaseRoot -Recurse -File -Filter 'Wiina.dll' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $assembly) {
        throw "Built Wiina.dll not found under $ReleaseRoot."
    }

    return [System.Reflection.AssemblyName]::GetAssemblyName($assembly.FullName).Version
}

function Show-Artifact {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    $sizeMb = [math]::Round($item.Length / 1MB, 2)
    Write-Host ("  {0}  {1} MB  SHA256 {2}" -f $item.FullName, $sizeMb, $hash)
}

if ($SkipPackage) {
    Write-Host 'SkipPackage set; version sources updated. Build the package with scripts/package.ps1.'
    return
}

if (-not (Test-Path -LiteralPath $packageScript)) {
    throw "Packaging script not found: $packageScript"
}

& $packageScript -Version $Version -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "scripts/package.ps1 failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "Portable archive was not produced: $archivePath"
}

# Both distribution forms must ship the portable uninstall entry point: the
# installer payload below is expanded from this very archive, so verifying it
# here covers the portable zip and the installed copy at once.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $hasUninstaller = $null -ne ($archive.Entries | Where-Object { $_.FullName -eq 'Uninstall Wiina.exe' })
}
finally {
    $archive.Dispose()
}
if (-not $hasUninstaller) {
    throw "Portable archive is missing 'Uninstall Wiina.exe'."
}
Write-Host "Verified portable uninstaller in $archivePath."

# Verify the shipped binary reports the requested product version. Wiina.exe is
# the apphost, so read the managed assembly next to it.
$releaseRoot = Join-Path $root 'src\Wiina.App\bin\x64\Release'
$assemblyVersion = Get-ManagedAssemblyVersion -ReleaseRoot $releaseRoot
if ($assemblyVersion -ne [version]"$Version.0") {
    throw "Built assembly version $assemblyVersion does not match requested $Version."
}
Write-Host "Verified managed assembly version $assemblyVersion."

$iscc = $null
if (-not $NoInstaller) {
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
    if (-not $iscc) {
        foreach ($candidate in @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )) {
            if ($candidate -and (Test-Path -LiteralPath $candidate)) {
                $iscc = $candidate
                break
            }
        }
    }
}

if ($iscc) {
    Write-Host "Building installer with $iscc..."

    # installer\wiina.iss deliberately consumes artifacts\publish so the
    # installer payload is byte-for-byte the portable payload. Recreate it from
    # the archive we just verified instead of publishing a second build.
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $publishDir -Force

    $iss = Join-Path $root 'installer\wiina.iss'
    Push-Location (Join-Path $root 'installer')
    try {
        & $iscc $iss
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Installer was not produced: $installerPath"
    }
}
elseif (-not $NoInstaller) {
    Write-Warning 'iscc.exe (Inno Setup 6) was not found; skipping the installer. installer\wiina.iss is up to date and can be compiled manually after installing Inno Setup.'
}

Write-Host ''
Write-Host "Wiina $Version artifacts:"
Show-Artifact -Path $archivePath
Show-Artifact -Path $installerPath
Write-Host ''
Write-Host 'Artifacts are gitignored; commit the version source changes, not artifacts/.'

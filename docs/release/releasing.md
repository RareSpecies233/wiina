# Releasing Wiina

`scripts/release.ps1` is the single entry point for a release. It syncs the
version, builds and verifies the portable zip, then compiles the per-user
installer when Inno Setup 6 is available.

```powershell
pwsh -File scripts/release.ps1 -Version 1.3.3
```

Use `-NoInstaller` to build only the portable zip, or `-SkipPackage` to check
the version-sync edits without building anything.

Artifacts (both gitignored, both uploaded to the GitHub release):

| File | Produced by |
|---|---|
| `artifacts/wiina-1.3.3-win-x64.zip` | `scripts/package.ps1` |
| `artifacts/Wiina-Setup-1.3.3-x64.exe` | `scripts/release.ps1` (Inno Setup `ISCC.exe`) |

## What the script does

1. Synchronises the version in every source of truth:
   `Directory.Build.props`, `Branding.Version`,
   `PackageManifest.Version`/`ArchiveFileName`, the `assemblyIdentity` in
   `src/Wiina.App/app.manifest`, the `-Version` default in `scripts/package.ps1`
   and `MyAppVersion` in `installer/wiina.iss`.
2. Builds the self-contained portable payload through `scripts/package.ps1`.
   That script first publishes the trimmed single-file uninstaller
   (`src/Wiina.Uninstall`) and copies it into the payload as
   `Uninstall Wiina.exe`, then builds the app, verifies the **payload**
   `Wiina.dll` reports `-Version`, and zips the result.
3. Verifies that `Uninstall Wiina.exe` is present inside the produced archive
   (the installer payload is expanded from that same archive, so one check
   covers both forms), and that the managed assembly under
   `src/Wiina.App/bin/x64/Release` reports the requested version.
4. If Inno Setup 6 is installed, expands the verified archive to
   `artifacts/publish` and compiles `installer/wiina.iss` into
   `artifacts/Wiina-Setup-<version>-x64.exe`. Without Inno Setup it skips the
   installer with a warning; `-NoInstaller` forces that path.
5. Prints the absolute path, size and SHA-256 of each artifact it produced.

`ISCC.exe` is looked up on `PATH`, then under
`%LOCALAPPDATA%\Programs\Inno Setup 6`, then under
`%ProgramFiles(x86)%\Inno Setup 6`, then `%ProgramFiles%\Inno Setup 6`.
The installer is per user (`PrivilegesRequired=lowest`), uses the official
Chinese Simplified translation in `installer/ChineseSimplified.isl`, and asks
whether to keep user data on uninstall (default: keep). See
[../installer.md](../installer.md) for the install/uninstall behaviour.

## Portable-only flow

```powershell
# 1) Build + stage + zip; fails if any required file, the portable uninstaller,
#    or the Wiina.dll version is wrong
pwsh -File scripts/package.ps1 -Version 1.3.3

# 2) Confirm the payload version instead of trusting the file name
$dll = '.\artifacts\wiina-1.3.3-win-x64\Wiina.dll'
[System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion   # 1.3.3.0
(Get-Item $dll).VersionInfo.ProductVersion                               # 1.3.3+<commit>

# 3) Confirm the portable uninstaller is inside the archive
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead('.\artifacts\wiina-1.3.3-win-x64.zip')
try { $zip.Entries | Where-Object FullName -eq 'Uninstall Wiina.exe' } finally { $zip.Dispose() }
```

`package.ps1` refuses to zip when `Wiina.dll` does not report `<version>.0`,
when the published `Uninstall Wiina.exe` is missing or implausibly small
(< 1 MB), when `libmpv-2.dll` is missing from `third_party/libmpv/`, or when
`LICENSE`, `NOTICE`, `README.md`, `SOURCE.txt` are absent from the payload. It
also rejects extra `third_party/libmpv` DLLs/EXEs so an accidental dependency
cannot slip into the archive.

## Reading the checksums

Do not hard-code hashes in documentation or scripts: every build differs even
for the same version (timestamps, commit id). Always read the values printed by
`release.ps1`, and record the shipped ones in the version's acceptance page:

```powershell
Get-FileHash .\artifacts\wiina-1.3.3-win-x64.zip      -Algorithm SHA256
Get-FileHash .\artifacts\Wiina-Setup-1.3.3-x64.exe    -Algorithm SHA256
(Get-Item .\artifacts\wiina-1.3.3-win-x64.zip).Length
(Get-Item .\artifacts\Wiina-Setup-1.3.3-x64.exe).Length
```

Artifacts are gitignored. Commit the version source changes (and any
documentation) but never `artifacts/`. Both executables are unsigned, so
SmartScreen may warn on first launch.

## Publishing the GitHub release

The two artifacts are release **assets**, not committed files. After merging
the release commit to `main` and building them:

1. Create and push the version tag:

   ```powershell
   git switch main
   git pull --ff-only origin main
   git tag -a v1.3.3 -m "Wiina 1.3.3"
   git push origin main
   git push origin v1.3.3
   ```

2. Publish with GitHub CLI (recommended, reproducible):

   ```powershell
   gh auth login
   gh release create v1.3.3 `
     artifacts/wiina-1.3.3-win-x64.zip `
     artifacts/Wiina-Setup-1.3.3-x64.exe `
     --title "Wiina 1.3.3" `
     --notes-file docs/release/notes.md `
     --latest
   ```

   `gh` uploads both files as release assets. They stay ignored by git.

3. Or use the web UI: **Releases → Draft a new release**, choose tag `v1.3.3`
   (target `main`), title `Wiina 1.3.3`, paste `docs/release/notes.md` as the
   description, attach both artifacts, and publish as the latest release.

4. Verify the sizes and SHA-256 values printed by `release.ps1` against the
   record in `docs/acceptance/v1.3.3.md` (or whichever version was built).

## History

Acceptance records keep the per-version evidence, including the archived
artifact sizes and hashes as they were observed at release time:
`docs/acceptance/v1.2.3.md`, `v1.2.2.md`, `v1.2.1.md`, `v1.2.0.md`,
`v1.1.2.md`, `v1.1.1.md`, `v1.1.0.md`, and the polish/stage pages. Those are
historical records; never copy an old hash as the expected value for a new
build.

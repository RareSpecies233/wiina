# Releasing Wiina

`scripts/release.ps1` is the single entry point for a release.

```powershell
pwsh -File scripts/release.ps1 -Version 1.1.0
```

The script:

1. Synchronises the version in every source of truth:
   `Directory.Build.props`, `Branding.Version`,
   `PackageManifest.Version`/`ArchiveFileName`, the `assemblyIdentity` in
   `src/Wiina.App/app.manifest`, the default in `scripts/package.ps1` and
   `MyAppVersion` in `installer/wiina.iss`.
2. Builds the self-contained portable payload through
   `scripts/package.ps1`, writing
   `artifacts/wiina-<version>-win-x64.zip`.
3. Reads the managed assembly version back from the built `Wiina.dll` and
   fails if it does not match `-Version`.
4. If Inno Setup 6 (`iscc.exe`) is on `PATH` or in its default install
   directory, unpacks the portable archive to `artifacts/publish` and compiles
   `installer/wiina.iss` to `artifacts/Wiina-Setup-<version>-x64.exe`. Without
   Inno Setup it skips the installer with a warning; the `.iss` is already
   version-stamped.
5. Prints the absolute path, size and SHA-256 of each artifact.

Use `-NoInstaller` to build only the portable zip, or `-SkipPackage` to check
the version-sync edits without building anything.

Artifacts are gitignored. Commit the version source changes (and any changelog)
but never `artifacts/`. The release build is unsigned, so SmartScreen may warn
on first launch.

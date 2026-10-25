using System.Text.RegularExpressions;
using Wiina.Core.Localization;
using Wiina.Core.Release;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Stage 11 packaging/accessibility contract tests. These read repository
/// files (never build output) so they fail if the manifest and packaging
/// script drift apart.
/// </summary>
public class Stage11ReleaseTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wiina.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string BuildVersion()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        Match match = Regex.Match(props, @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "Directory.Build.props has no <Version> element.");
        return match.Groups[1].Value;
    }

    [Fact]
    public void Package_manifest_covers_the_documented_payload()
    {
        string version = BuildVersion();
        Assert.Equal(PackageManifest.Version, version);
        Assert.Equal(PackageManifest.ArchiveFileName, $"wiina-{version}-win-x64.zip");
        Assert.Equal(Branding.Version, version);
        Assert.Equal("Wiina.exe", PackageManifest.ExecutableFileName);
        Assert.Contains("libmpv-2.dll", PackageManifest.RequiredPayloadFiles);
        Assert.Contains("LICENSE", PackageManifest.RequiredPayloadFiles);
        Assert.Contains("NOTICE", PackageManifest.RequiredPayloadFiles);
        Assert.Contains("README.md", PackageManifest.RequiredPayloadFiles);
        Assert.Contains("SOURCE.txt", PackageManifest.RequiredPayloadFiles);
        Assert.Contains(PackageManifest.UninstallerFileName, PackageManifest.RequiredPayloadFiles);
        Assert.All(PackageManifest.RequiredRootFiles,
            file => Assert.Contains(file, PackageManifest.RequiredPayloadFiles));
    }

    [Fact]
    public void Package_manifest_finds_missing_files_case_insensitively()
    {
        var present = new[]
        {
            "wiina.EXE", "Uninstall Wiina.EXE", @"libmpv-2.dll", "license", "NOTICE", "README.md",
            "SOURCE.TXT",
        };
        Assert.Empty(PackageManifest.FindMissing(present));

        var missing = PackageManifest.FindMissing(new[] { "Wiina.exe" });
        Assert.Contains("libmpv-2.dll", missing);
        Assert.Contains("NOTICE", missing);
        Assert.DoesNotContain("Wiina.exe", missing);
    }

    [Fact]
    public void Product_version_matches_build_properties()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        Assert.Contains($"<Version>{PackageManifest.Version}</Version>", props);
    }

    [Fact]
    public void Package_script_checks_every_required_file_and_writes_the_documented_archive()
    {
        string scriptPath = Path.Combine(RepoRoot(), "scripts", "package.ps1");
        Assert.True(File.Exists(scriptPath));
        string script = File.ReadAllText(scriptPath);
        foreach (string file in PackageManifest.RequiredPayloadFiles)
        {
            Assert.Contains(file, script);
        }

        Assert.Contains("Compress-Archive", script);
        Assert.Contains("artifacts", script);
        Assert.Contains("libmpv-2.dll", script);
        Assert.Contains("SOURCE.txt", script);
        Assert.Contains("wiina-", script);
    }

    [Fact]
    public void Release_script_updates_every_version_source_and_builds_versioned_artifacts()
    {
        string scriptPath = Path.Combine(RepoRoot(), "scripts", "release.ps1");
        Assert.True(File.Exists(scriptPath), "scripts/release.ps1 is missing.");
        string script = File.ReadAllText(scriptPath);
        foreach (string source in new[]
                 {
                     "Directory.Build.props", "Branding.cs", "PackageManifest.cs",
                     "app.manifest", "installer\\wiina.iss",
                 })
        {
            Assert.Contains(source, script);
        }

        Assert.Contains("package.ps1", script);
        Assert.Contains("iscc", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA256", script);
        Assert.DoesNotContain("git commit", script);
        Assert.DoesNotContain("git push", script);
    }

    [Fact]
    public void Icon_only_controls_have_catalogued_automation_names_in_the_title_bar()
    {
        string titleBar = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Wiina.App", "Controls", "TitleBarView.xaml"));
        Assert.NotEmpty(AccessibilityMetadata.IconOnlyAutomationKeys);
        foreach (string key in AccessibilityMetadata.IconOnlyAutomationKeys)
        {
            Assert.True(UiStrings.Contains(key), $"missing automation-name catalog key: {key}");
            Assert.Contains($"loc:Loc.AutomationName=\"{key}\"", titleBar);
        }
    }

    [Fact]
    public void Stage_9_and_10_features_are_not_declared_as_shipped()
    {
        string readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        // The README may mention the skipped stages, but it must say so.
        Assert.Matches(new Regex("Stage (9|10)[^\\n]*(skipped|not implemented|未实现|跳过)", RegexOptions.IgnoreCase), readme);
        string notice = File.ReadAllText(Path.Combine(RepoRoot(), "NOTICE"));
        // NOTICE must state the skipped stages under a "not included" heading
        // rather than declaring those components as part of the distribution.
        Assert.Matches(new Regex("not included|not shipped|skipped", RegexOptions.IgnoreCase), notice);
        Assert.Matches(new Regex("Stage 9|Stage 10", RegexOptions.IgnoreCase), notice);
    }
}

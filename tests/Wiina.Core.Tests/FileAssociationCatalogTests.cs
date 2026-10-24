using Wiina.Core.Files;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class FileAssociationCatalogTests
{
    [Fact]
    public void PlanRegistersVideoProgIdCommandAndDefaultApps()
    {
        IReadOnlyList<FileAssociationCatalog.Entry> plan =
            FileAssociationCatalog.BuildPlan(@"C:\Apps\Wiina\Wiina.exe", video: true, audio: false);

        Assert.Contains(plan, entry =>
            entry.KeyPath == @"Software\Classes\Wiina.Video\shell\open\command" &&
            entry.Value == "\"C:\\Apps\\Wiina\\Wiina.exe\" \"%1\"");
        Assert.Contains(plan, entry =>
            entry.KeyPath == @"Software\Classes\.mp4\OpenWithProgids" &&
            entry.ValueName == FileAssociationCatalog.VideoProgId &&
            entry.Binary);
        Assert.Contains(plan, entry =>
            entry.KeyPath == @"Software\Wiina\Capabilities\FileAssociations" &&
            entry.ValueName == ".mkv" &&
            entry.Value == FileAssociationCatalog.VideoProgId);
        Assert.Contains(plan, entry =>
            entry.KeyPath == @"Software\RegisteredApplications" &&
            entry.ValueName == FileAssociationCatalog.ApplicationName);

        // Audio was not requested: no audio ProgID, no audio extension.
        Assert.DoesNotContain(plan, entry => entry.KeyPath.Contains("Wiina.Audio"));
        Assert.DoesNotContain(plan, entry => entry.ValueName == ".mp3");
    }

    [Fact]
    public void PlanCanRegisterBothKinds()
    {
        IReadOnlyList<FileAssociationCatalog.Entry> plan =
            FileAssociationCatalog.BuildPlan(@"C:\Wiina.exe", video: true, audio: true);

        Assert.Contains(plan, entry => entry.ValueName == ".flac");
        Assert.Contains(plan, entry =>
            entry.KeyPath == @"Software\Wiina\Capabilities\FileAssociations" &&
            entry.ValueName == ".opus");
    }

    [Fact]
    public void OwnedProgIdsCoverBothHandlers()
    {
        Assert.Equal(
            new[] { "Wiina.Video", "Wiina.Audio" },
            FileAssociationCatalog.OwnedProgIds);
        Assert.NotEmpty(FileAssociationCatalog.VideoExtensions);
        Assert.NotEmpty(FileAssociationCatalog.AudioExtensions);
        Assert.DoesNotContain("exe", FileAssociationCatalog.VideoExtensions);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.FileExtractor;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.FileExtractor;
using NexusMods.Sdk.Settings;
using NSubstitute;

namespace NexusMods.Backend.Tests.FileExtractor;

public class TemporaryFileManagerExTests : IDisposable
{
    // Higher than any Linux PID (pid_max is at most 2^22): no process runs with this id
    private const int DeadProcessId = 999_999_999;

    private readonly AbsolutePath _tempFolder;
    private readonly ISettingsManager _settingsManager;

    public TemporaryFileManagerExTests()
    {
        _tempFolder = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"temp-manager-tests-{Guid.NewGuid():N}");

        _settingsManager = Substitute.For<ISettingsManager>();
        _settingsManager.Get<FileExtractorSettings>(Arg.Any<string?>()).Returns(new FileExtractorSettings
        {
            TempFolderLocation = new ConfigurablePath(baseDirectory: null, _tempFolder.ToString()),
        });
    }

    public void Dispose()
    {
        if (_tempFolder.DirectoryExists()) _tempFolder.DeleteDirectory(recursive: true);
    }

    private TemporaryFileManagerEx CreateManager() => new(FileSystem.Shared, _settingsManager, NullLogger<TemporaryFileManagerEx>.Instance);

    [Test]
    public async Task Test_DisposeKeepsTheFilesOfOtherInstances()
    {
        using var first = CreateManager();
        var second = CreateManager();

        await using var folder = first.CreateFolder();
        await folder.Path.Combine("file.txt").WriteAllTextAsync("content");

        second.Dispose();

        await Assert.That(folder.Path.Combine("file.txt").FileExists).IsTrue();
    }

    [Test]
    public async Task Test_DeletesLeftoversOfStoppedProcessesOnly()
    {
        _tempFolder.CreateDirectory();
        var deadProcessFolder = _tempFolder.Combine($"{DeadProcessId}-{Guid.NewGuid():N}");
        var runningProcessFolder = _tempFolder.Combine($"{Environment.ProcessId}-{Guid.NewGuid():N}");
        var oldFormatFolder = _tempFolder.Combine($"prefix{Guid.NewGuid()}");
        var oldFormatFile = _tempFolder.Combine(Guid.NewGuid().ToString());
        deadProcessFolder.CreateDirectory();
        runningProcessFolder.CreateDirectory();
        oldFormatFolder.CreateDirectory();
        await oldFormatFile.WriteAllTextAsync("content");

        using var manager = CreateManager();

        await Assert.That(deadProcessFolder.DirectoryExists()).IsFalse();
        await Assert.That(oldFormatFolder.DirectoryExists()).IsFalse();
        await Assert.That(oldFormatFile.FileExists).IsFalse();
        await Assert.That(runningProcessFolder.DirectoryExists()).IsTrue();
    }

    [Test]
    public async Task Test_DisposeDeletesOnlyItsOwnFolder()
    {
        var manager = CreateManager();
        await using var folder = manager.CreateFolder(deleteOnDispose: false);

        manager.Dispose();

        await Assert.That(folder.Path.DirectoryExists()).IsFalse();
        await Assert.That(_tempFolder.DirectoryExists()).IsTrue();
    }
}

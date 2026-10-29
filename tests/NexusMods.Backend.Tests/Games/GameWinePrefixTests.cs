using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;

namespace NexusMods.Backend.Tests.Games;

public class GameWinePrefixTests : IDisposable
{
    private readonly AbsolutePath _folder;

    public GameWinePrefixTests()
    {
        _folder = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"wine-prefix-tests-{Guid.NewGuid():N}");
        _folder.CreateDirectory();
    }

    public void Dispose()
    {
        // Directory.Delete removes links without following them, unlike AbsolutePath.DeleteDirectory, which would
        // loop through the pfx link of the umu prefix
        if (_folder.DirectoryExists()) Directory.Delete(_folder.ToString(), recursive: true);
    }

    private AbsolutePath GamePath => _folder.Combine("Games/Skyrim Special Edition");

    private AbsolutePath LocalAppData(bool isProton) => GameWinePrefix.InPrefix(GamePath, GameWinePrefix.FromLauncherFolder(_folder, isProton))
        .FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Test_ProtonPrefix_IsInPfxWithSteamUser(bool isProton)
    {
        _folder.Combine("pfx/drive_c/users/steamuser").CreateDirectory();

        await Assert.That(LocalAppData(isProton)).IsEqualTo(_folder.Combine("pfx/drive_c/users/steamuser/AppData/Local"));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Test_WinePrefix_IsInFolderWithLinuxUser(bool isProton)
    {
        _folder.Combine($"drive_c/users/{GameWinePrefix.LinuxUserName}").CreateDirectory();

        await Assert.That(LocalAppData(isProton)).IsEqualTo(_folder.Combine($"drive_c/users/{GameWinePrefix.LinuxUserName}/AppData/Local"));
    }

    [Test]
    public async Task Test_UmuPrefix_IsInFolderWithSteamUser()
    {
        // umu makes pfx a link to the folder, and the Linux user a link to steamuser
        _folder.Combine("drive_c/users/steamuser").CreateDirectory();
        Directory.CreateSymbolicLink(_folder.Combine("pfx").ToString(), ".");
        Directory.CreateSymbolicLink(_folder.Combine($"drive_c/users/{GameWinePrefix.LinuxUserName}").ToString(), "steamuser");

        await Assert.That(LocalAppData(isProton: true)).IsEqualTo(_folder.Combine("drive_c/users/steamuser/AppData/Local"));
    }

    [Test]
    public async Task Test_PrefixNotCreatedYet_FollowsTheRunner()
    {
        await Assert.That(LocalAppData(isProton: true)).IsEqualTo(_folder.Combine("pfx/drive_c/users/steamuser/AppData/Local"));
        await Assert.That(LocalAppData(isProton: false)).IsEqualTo(_folder.Combine($"drive_c/users/{GameWinePrefix.LinuxUserName}/AppData/Local"));
    }

    [Test]
    public async Task Test_InPrefix_KeepsTheGamePath()
    {
        _folder.Combine("pfx/drive_c/users/steamuser").CreateDirectory();
        GamePath.CreateDirectory();
        await GamePath.Combine("SkyrimSE.exe").WriteAllTextAsync("");

        var path = GameWinePrefix.InPrefix(GamePath, GameWinePrefix.FromLauncherFolder(_folder, isProton: true));
        var userFolder = _folder.Combine("pfx/drive_c/users/steamuser");

        await Assert.That(path.ToString()).IsEqualTo(GamePath.ToString());
        await Assert.That(path.Combine("SkyrimSE.exe").FileExists).IsTrue();
        await Assert.That(path.FileSystem.GetKnownPath(KnownPath.MyDocumentsDirectory)).IsEqualTo(userFolder.Combine("Documents"));
        await Assert.That(path.FileSystem.GetKnownPath(KnownPath.MyGamesDirectory)).IsEqualTo(userFolder.Combine("Documents/My Games"));
    }
}

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NSubstitute;

namespace NexusMods.Backend.Tests.Games;

public class SteamLocatorTests : IDisposable
{
    private const uint AppId = 489830;
    private const string ManifestId = "3660787314279169352";

    private readonly AbsolutePath _home;
    private readonly IFileSystem _fileSystem;
    private readonly IGameData _game;

    public SteamLocatorTests()
    {
        _home = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"steam-tests-{Guid.NewGuid():N}");
        _home.CreateDirectory();

        _fileSystem = FileSystem.Shared.CreateOverlayFileSystem(
            pathMappings: new Dictionary<AbsolutePath, AbsolutePath>(),
            knownPathMappings: new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.HomeDirectory, _home },
                { KnownPath.LocalApplicationDataDirectory, _home.Combine(".local/share") },
                { KnownPath.XDG_DATA_HOME, _home.Combine(".local/share") },
            }
        );

        _game = Substitute.For<IGameData>();
        _game.StoreIdentifiers.Returns(new StoreIdentifiers(GameId.From("Test.Game")) { SteamAppIds = [AppId] });
    }

    public void Dispose()
    {
        if (_home.DirectoryExists()) _home.DeleteDirectory(recursive: true);
    }

    private SteamLocator CreateLocator() => new([_game], NullLoggerFactory.Instance, _fileSystem, registry: null);

    private AbsolutePath SteamFolder => _home.Combine(".local/share/Steam");

    private AbsolutePath GameFolder => SteamFolder.Combine("steamapps/common/Skyrim Special Edition");

    private AbsolutePath ProtonPrefix => SteamFolder.Combine($"steamapps/compatdata/{AppId}/pfx");

    private async Task WriteSteamLibrary()
    {
        SteamFolder.Combine("config").CreateDirectory();
        await SteamFolder.Combine("config/libraryfolders.vdf").WriteAllTextAsync(
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"      "{{SteamFolder}}"
                    "apps"
                    {
                        "{{AppId}}"     "1000"
                    }
                }
            }
            """
        );

        GameFolder.CreateDirectory();
        await SteamFolder.Combine($"steamapps/appmanifest_{AppId}.acf").WriteAllTextAsync(
            $$"""
            "AppState"
            {
                "appid"     "{{AppId}}"
                "Universe"      "1"
                "name"      "The Elder Scrolls V: Skyrim Special Edition"
                "StateFlags"        "4"
                "installdir"        "Skyrim Special Edition"
                "InstalledDepots"
                {
                    "489833"
                    {
                        "manifest"      "{{ManifestId}}"
                        "size"      "1000"
                    }
                }
            }
            """
        );
    }

    [Test]
    public async Task Test_ProtonGame_ResolvesAppDataInItsPrefix()
    {
        await WriteSteamLibrary();
        ProtonPrefix.CreateDirectory();

        var result = CreateLocator().Locate().Single();
        var userFolder = ProtonPrefix.Combine("drive_c/users/steamuser");

        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Platform).IsEqualTo(OSPlatform.Windows);
        await Assert.That(result.LocatorIds.Select(id => id.Value)).IsEquivalentTo([ManifestId]);
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory)).IsEqualTo(userFolder.Combine("AppData/Local"));
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.MyGamesDirectory)).IsEqualTo(userFolder.Combine("Documents/My Games"));
    }

    [Test]
    public async Task Test_NativeGame_ResolvesAppDataOfTheLinuxUser()
    {
        await WriteSteamLibrary();

        var result = CreateLocator().Locate().Single();
        var localAppData = result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory);

        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Platform).IsEqualTo(OSPlatform.Linux);
        await Assert.That(localAppData.InFolder(SteamFolder.Combine("steamapps/compatdata"))).IsFalse();
    }
}

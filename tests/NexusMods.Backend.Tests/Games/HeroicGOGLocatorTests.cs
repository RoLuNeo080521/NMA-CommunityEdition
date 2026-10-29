using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NSubstitute;

namespace NexusMods.Backend.Tests.Games;

public class HeroicGOGLocatorTests : IDisposable
{
    private const long ProductId = 1801825368;
    private const string BuildId = "57425355398564331";

    private readonly AbsolutePath _home;
    private readonly IFileSystem _fileSystem;
    private readonly IGameData _game;

    public HeroicGOGLocatorTests()
    {
        _home = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"heroic-gog-tests-{Guid.NewGuid():N}");
        _home.CreateDirectory();

        _fileSystem = FileSystem.Shared.CreateOverlayFileSystem(
            pathMappings: new Dictionary<AbsolutePath, AbsolutePath>(),
            knownPathMappings: new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.HomeDirectory, _home },
                { KnownPath.XDG_CONFIG_HOME, _home.Combine(".config") },
            }
        );

        _game = Substitute.For<IGameData>();
        _game.StoreIdentifiers.Returns(new StoreIdentifiers(GameId.From("Test.Game")) { GOGProductIds = [ProductId] });
    }

    public void Dispose()
    {
        if (_home.DirectoryExists()) _home.DeleteDirectory(recursive: true);
    }

    private HeroicGOGLocator CreateLocator() => new([_game], NullLoggerFactory.Instance, _fileSystem);

    private AbsolutePath HeroicFolder => _home.Combine(".config/heroic");

    private AbsolutePath GameFolder => _home.Combine("Games/Heroic/Skyrim Special Edition");

    private AbsolutePath PrefixFolder => _home.Combine("Games/Heroic/Prefixes/default/Skyrim Special Edition");

    private async Task WriteInstalledJson(string platform = "windows")
    {
        HeroicFolder.Combine("gog_store").CreateDirectory();
        await HeroicFolder.Combine("gog_store/installed.json").WriteAllTextAsync(
            $$"""
            {
              "installed": [
                {
                  "platform": "{{platform}}",
                  "executable": "",
                  "install_path": "{{GameFolder}}",
                  "install_size": "50 GiB",
                  "is_dlc": false,
                  "version": "1.6.1170",
                  "appName": "{{ProductId}}",
                  "installedDLCs": [],
                  "language": "en-US",
                  "versionEtag": "",
                  "buildId": "{{BuildId}}"
                }
              ]
            }
            """
        );
    }

    private async Task WriteGameSettings(string wineType)
    {
        HeroicFolder.Combine("GamesConfig").CreateDirectory();
        await HeroicFolder.Combine($"GamesConfig/{ProductId}.json").WriteAllTextAsync(
            $$"""
            {
              "{{ProductId}}": {
                "winePrefix": "{{PrefixFolder}}",
                "wineVersion": { "bin": "/opt/wine/bin/wine", "name": "Wine Test", "type": "{{wineType}}" },
                "enviromentOptions": [],
                "wrapperOptions": []
              },
              "version": "v0",
              "explicit": true
            }
            """
        );
    }

    [Test]
    [Arguments("proton", "pfx/drive_c/users/steamuser")]
    [Arguments("wine", "drive_c/users/{0}")]
    public async Task Test_ResolvesAppDataInTheWinePrefixOfTheGameSettings(string wineType, string userFolder)
    {
        await WriteInstalledJson();
        await WriteGameSettings(wineType);

        var result = CreateLocator().Locate().Single();
        var expectedUserFolder = PrefixFolder.Combine(string.Format(userFolder, GameWinePrefix.LinuxUserName));

        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.LocatorIds.Select(id => id.Value)).IsEquivalentTo([BuildId]);
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory)).IsEqualTo(expectedUserFolder.Combine("AppData/Local"));
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.MyGamesDirectory)).IsEqualTo(expectedUserFolder.Combine("Documents/My Games"));
    }

    [Test]
    public async Task Test_NativeGame_ResolvesAppDataOfTheLinuxUser()
    {
        await WriteInstalledJson(platform: "linux");

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory))
            .IsEqualTo(_fileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory));
    }
}

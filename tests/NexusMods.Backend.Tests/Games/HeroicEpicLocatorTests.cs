using CliWrap;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NSubstitute;

namespace NexusMods.Backend.Tests.Games;

public class HeroicEpicLocatorTests : IDisposable
{
    private const string AppName = "Ginger";
    private const string CatalogItemId = "5beededaad9743df90e8f07d92df153f";

    private const string NativeLegendaryDir = ".config/heroic/legendaryConfig/legendary";
    private const string FlatpakLegendaryDir = ".var/app/com.heroicgameslauncher.hgl/config/heroic/legendaryConfig/legendary";

    private readonly AbsolutePath _home;
    private readonly IFileSystem _fileSystem;

    public HeroicEpicLocatorTests()
    {
        _home = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"heroic-epic-tests-{Guid.NewGuid():N}");
        _home.CreateDirectory();

        _fileSystem = FileSystem.Shared.CreateOverlayFileSystem(
            pathMappings: new Dictionary<AbsolutePath, AbsolutePath>(),
            knownPathMappings: new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.HomeDirectory, _home },
                { KnownPath.XDG_CONFIG_HOME, _home.Combine(".config") },
            }
        );
    }

    public void Dispose()
    {
        if (_home.DirectoryExists()) _home.DeleteDirectory(recursive: true);
    }

    private static IGameData CreateGame(string egsIdentifier)
    {
        var game = Substitute.For<IGameData>();
        game.StoreIdentifiers.Returns(new StoreIdentifiers(GameId.From("Test.Game")) { EGSCatalogItemId = [egsIdentifier] });
        return game;
    }

    private HeroicEpicLocator CreateLocator(string egsIdentifier = CatalogItemId) => new([CreateGame(egsIdentifier)], NullLoggerFactory.Instance, _fileSystem);

    private AbsolutePath GameFolder => _home.Combine("Games/Heroic/Cyberpunk 2077");

    private AbsolutePath WineBinary => _home.Combine(".config/heroic/tools/wine/Wine-GE-Proton8-26/bin/wine");

    private AbsolutePath PrefixFolder => _home.Combine("Games/Heroic/Prefixes/default/Cyberpunk 2077");

    private async Task WriteInstalledJson(string legendaryDirectory, string appName = AppName, string platform = "Windows", bool isDlc = false)
    {
        var legendaryDir = _home.Combine(legendaryDirectory);
        legendaryDir.CreateDirectory();
        await legendaryDir.Combine("installed.json").WriteAllTextAsync(
            $$"""
            {
              "{{appName}}": {
                "app_name": "{{appName}}",
                "install_path": "{{GameFolder}}",
                "title": "Cyberpunk 2077",
                "version": "2.21-1234567",
                "executable": "bin/x64/Cyberpunk2077.exe",
                "is_dlc": {{(isDlc ? "true" : "false")}},
                "platform": "{{platform}}"
              }
            }
            """
        );
    }

    private async Task WriteMetadata(string legendaryDirectory, string appName = AppName)
    {
        var metadataDir = _home.Combine(legendaryDirectory).Combine("metadata");
        metadataDir.CreateDirectory();
        await metadataDir.Combine($"{appName}.json").WriteAllTextAsync(
            $$"""
            {
              "app_name": "{{appName}}",
              "app_title": "Cyberpunk 2077",
              "asset_infos": { "Windows": { "app_name": "{{appName}}", "catalog_item_id": "{{CatalogItemId}}" } },
              "metadata": { "id": "{{CatalogItemId}}", "namespace": "77f2b98e2cef40c8a7437518bf420e47" }
            }
            """
        );
    }

    private async Task WriteGameSettings(string legendaryDirectory, string wineType)
    {
        // legendaryConfig/legendary is in the Heroic config folder, next to GamesConfig
        var gamesConfigDir = _home.Combine(legendaryDirectory).Parent.Parent.Combine("GamesConfig");
        gamesConfigDir.CreateDirectory();
        await gamesConfigDir.Combine($"{AppName}.json").WriteAllTextAsync(
            $$"""
            {
              "{{AppName}}": {
                "autoInstallDxvk": true,
                "winePrefix": "{{PrefixFolder}}",
                "wineVersion": { "bin": "{{WineBinary}}", "name": "Wine Test", "type": "{{wineType}}" }
              },
              "version": "v0",
              "explicit": true
            }
            """
        );
    }

    [Test]
    [Arguments(NativeLegendaryDir)]
    [Arguments(FlatpakLegendaryDir)]
    public async Task Test_FindsGameByCatalogItemId(string legendaryDirectory)
    {
        await WriteInstalledJson(legendaryDirectory);
        await WriteMetadata(legendaryDirectory);

        var results = CreateLocator().Locate().ToArray();

        await Assert.That(results.Length).IsEqualTo(1);
        var result = results[0];
        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Store).IsEqualTo(GameStore.EGS);
        await Assert.That(result.StoreIdentifier).IsEqualTo(CatalogItemId);
        await Assert.That(result.LauncherGameId).IsEqualTo(AppName);
        await Assert.That(result.LocatorIds.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Test_FindsGameRegisteredByAppName()
    {
        // Mount & Blade II: Bannerlord is registered with its app name, and the metadata may be missing
        await WriteInstalledJson(NativeLegendaryDir, appName: "Chickadee");

        var result = CreateLocator(egsIdentifier: "Chickadee").Locate().Single();

        await Assert.That(result.StoreIdentifier).IsEqualTo("Chickadee");
        await Assert.That(result.LauncherGameId).IsEqualTo("Chickadee");
    }

    [Test]
    public async Task Test_FindsGameOnceWhenNativeAndFlatpakListIt()
    {
        await WriteInstalledJson(NativeLegendaryDir);
        await WriteMetadata(NativeLegendaryDir);
        await WriteInstalledJson(FlatpakLegendaryDir);
        await WriteMetadata(FlatpakLegendaryDir);

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Test_IgnoresUnsupportedGames()
    {
        await WriteInstalledJson(NativeLegendaryDir);
        await WriteMetadata(NativeLegendaryDir);

        await Assert.That(CreateLocator(egsIdentifier: "another-catalog-item-id").Locate().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Test_IgnoresDlcs()
    {
        await WriteInstalledJson(NativeLegendaryDir, isDlc: true);
        await WriteMetadata(NativeLegendaryDir);

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Test_IgnoresNonWindowsBuilds()
    {
        await WriteInstalledJson(NativeLegendaryDir, platform: "Mac");
        await WriteMetadata(NativeLegendaryDir);

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(0);
    }

    [Test]
    [Arguments("proton", "pfx/drive_c/users/steamuser/AppData/Local")]
    [Arguments("wine", "drive_c/users/{0}/AppData/Local")]
    public async Task Test_ResolvesAppDataInTheWinePrefixOfTheGameSettings(string wineType, string localAppData)
    {
        await WriteInstalledJson(FlatpakLegendaryDir);
        await WriteMetadata(FlatpakLegendaryDir);
        await WriteGameSettings(FlatpakLegendaryDir, wineType);

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory))
            .IsEqualTo(PrefixFolder.Combine(string.Format(localAppData, GameWinePrefix.LinuxUserName)));
    }

    [Test]
    public async Task Test_WithoutGameSettings_ResolvesAppDataOfTheLinuxUser()
    {
        await WriteInstalledJson(NativeLegendaryDir);
        await WriteMetadata(NativeLegendaryDir);

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory))
            .IsEqualTo(_fileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory));
    }

    [Test]
    public async Task Test_RunsToolsWithTheWineBuildOfTheGameSettings()
    {
        WineBinary.Parent.CreateDirectory();
        await WineBinary.WriteAllTextAsync("");
        await WriteInstalledJson(NativeLegendaryDir);
        await WriteMetadata(NativeLegendaryDir);
        await WriteGameSettings(NativeLegendaryDir, "wine");

        var runner = CreateLocator().Locate().Single().WinePrefixRunner;
        var command = runner?.MakeRunCommand(Cli.Wrap("/games/Cyberpunk 2077/tools/redmod/bin/redMod.exe").WithArguments("deploy"));

        await Assert.That(runner?.WinePrefixDirectoryPath).IsEqualTo(PrefixFolder);
        await Assert.That(command?.TargetFilePath).IsEqualTo(WineBinary.ToString());
        await Assert.That(command?.EnvironmentVariables["WINEPREFIX"]).IsEqualTo(PrefixFolder.ToString());
    }

    [Test]
    public async Task Test_IgnoresUnreadableInstalledJson()
    {
        var legendaryDir = _home.Combine(NativeLegendaryDir);
        legendaryDir.CreateDirectory();
        await legendaryDir.Combine("installed.json").WriteAllTextAsync("{ not json");

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(0);
    }
}

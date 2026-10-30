using CliWrap;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NSubstitute;

namespace NexusMods.Backend.Tests.Games;

public class LutrisGOGLocatorTests : IDisposable
{
    private const long ProductId = 1423049311;
    private const long DlcProductId = 1256837418;
    private const string BuildId = "58395245366541337";

    private readonly AbsolutePath _home;
    private readonly IFileSystem _fileSystem;
    private readonly IGameData _game;

    public LutrisGOGLocatorTests()
    {
        _home = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"lutris-tests-{Guid.NewGuid():N}");
        _home.CreateDirectory();

        _fileSystem = FileSystem.Shared.CreateOverlayFileSystem(
            pathMappings: new Dictionary<AbsolutePath, AbsolutePath>(),
            knownPathMappings: new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.HomeDirectory, _home },
                { KnownPath.XDG_CONFIG_HOME, _home.Combine(".config") },
                { KnownPath.XDG_DATA_HOME, _home.Combine(".local/share") },
            }
        );

        _game = Substitute.For<IGameData>();
        _game.StoreIdentifiers.Returns(new StoreIdentifiers(GameId.From("Test.Game")) { GOGProductIds = [ProductId] });
    }

    public void Dispose()
    {
        if (_home.DirectoryExists()) _home.DeleteDirectory(recursive: true);
    }

    private LutrisGOGLocator CreateLocator() => new([_game], NullLoggerFactory.Instance, _fileSystem);

    private AbsolutePath GameFolder => _home.Combine("Games/cyberpunk-2077/Cyberpunk 2077");

    private AbsolutePath PrefixFolder => _home.Combine("Games/cyberpunk-2077/pfx");

    private async Task WriteGameConfig(string gamesDirectory, string service = "gog", bool withPrefix = true, string? wineVersion = null)
    {
        var gamesDir = _home.Combine(gamesDirectory);
        gamesDir.CreateDirectory();
        var prefix = withPrefix ? $"prefix: {PrefixFolder}" : "";
        var wine = wineVersion is null ? "" : $"wine: {{ version: {wineVersion} }}";
        await gamesDir.Combine("cyberpunk-2077-1729000000.yml").WriteAllTextAsync(
            $"""
            {wine}
            game:
              exe: {GameFolder}/bin/x64/Cyberpunk2077.exe
              {prefix}
            game_slug: cyberpunk-2077
            name: Cyberpunk 2077
            service: {service}
            service_id: '{ProductId}'
            slug: cyberpunk-2077-gog
            """
        );
    }

    private async Task WriteGameFiles(bool withGogInfo)
    {
        GameFolder.Combine("bin/x64").CreateDirectory();
        await GameFolder.Combine("bin/x64/Cyberpunk2077.exe").WriteAllTextAsync("");
        if (!withGogInfo) return;

        await GameFolder.Combine($"goggame-{ProductId}.info").WriteAllTextAsync($$"""{ "buildId": "{{BuildId}}", "gameId": "{{ProductId}}" }""");
        await GameFolder.Combine($"goggame-{DlcProductId}.info").WriteAllTextAsync($$"""{ "gameId": "{{DlcProductId}}" }""");
    }

    [Test]
    [Arguments(".local/share/lutris/games")]
    [Arguments(".config/lutris/games")]
    [Arguments(".var/app/net.lutris.Lutris/data/lutris/games")]
    [Arguments(".var/app/net.lutris.Lutris/config/lutris/games")]
    public async Task Test_FindsGameInEveryLutrisConfigLocation(string gamesDirectory)
    {
        await WriteGameConfig(gamesDirectory);
        await WriteGameFiles(withGogInfo: true);

        var results = CreateLocator().Locate().ToArray();

        await Assert.That(results.Length).IsEqualTo(1);
        var result = results[0];
        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Store).IsEqualTo(GameStore.GOG);
        await Assert.That(result.StoreIdentifier).IsEqualTo(ProductId.ToString());
        await Assert.That(result.LauncherGameId).IsEqualTo("cyberpunk-2077");
        await Assert.That(result.LocatorIds.Select(id => id.Value)).IsEquivalentTo([BuildId, DlcProductId.ToString()]);
    }

    [Test]
    public async Task Test_IgnoresDataFolderWhenConfigFolderExists()
    {
        // Lutris only falls back to ~/.local/share/lutris when ~/.config/lutris doesn't exist
        _home.Combine(".config/lutris").CreateDirectory();
        await WriteGameConfig(".local/share/lutris/games");
        await WriteGameFiles(withGogInfo: true);

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Test_WithoutGogInfo_UsesExeFolderAndProductId()
    {
        await WriteGameConfig(".local/share/lutris/games");
        await WriteGameFiles(withGogInfo: false);

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path).IsEqualTo(GameFolder.Combine("bin/x64"));
        await Assert.That(result.LocatorIds.Select(id => id.Value)).IsEquivalentTo([ProductId.ToString()]);
    }

    [Test]
    public async Task Test_ResolvesAppDataInTheWinePrefixOfTheGameConfig()
    {
        await WriteGameConfig(".local/share/lutris/games");
        await WriteGameFiles(withGogInfo: true);

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path).IsEqualTo(GameFolder);
        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory))
            .IsEqualTo(PrefixFolder.Combine($"drive_c/users/{GameWinePrefix.LinuxUserName}/AppData/Local"));
    }

    [Test]
    public async Task Test_WithoutPrefix_ResolvesAppDataOfTheLinuxUser()
    {
        await WriteGameConfig(".local/share/lutris/games", withPrefix: false);
        await WriteGameFiles(withGogInfo: true);

        var result = CreateLocator().Locate().Single();

        await Assert.That(result.Path.FileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory))
            .IsEqualTo(_fileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory));
    }

    private async Task<AbsolutePath> WriteLutrisWineBuild(string version)
    {
        var wine = _home.Combine($".local/share/lutris/runners/wine/{version}/bin/wine");
        wine.Parent.CreateDirectory();
        await wine.WriteAllTextAsync("");
        return wine;
    }

    private static Command RedModCommand => Cli.Wrap("/games/Cyberpunk 2077/tools/redmod/bin/redMod.exe").WithArguments("deploy");

    [Test]
    public async Task Test_RunsToolsWithTheWineVersionOfAllTheGames()
    {
        var wine = await WriteLutrisWineBuild("wine-ge-8-26-x86_64");
        await _home.Combine(".local/share/lutris/runners/wine.yml").WriteAllTextAsync("wine:\n  version: wine-ge-8-26-x86_64\n");
        await WriteGameConfig(".local/share/lutris/games");
        await WriteGameFiles(withGogInfo: true);

        var runner = CreateLocator().Locate().Single().WinePrefixRunner;

        await Assert.That(runner).IsNotNull();
        await Assert.That(runner!.WinePrefixDirectoryPath).IsEqualTo(PrefixFolder);
        await Assert.That(runner.MakeRunCommand(RedModCommand)?.TargetFilePath).IsEqualTo(wine.ToString());
    }

    [Test]
    public async Task Test_RunsToolsWithTheWineVersionOfTheGame()
    {
        await WriteLutrisWineBuild("wine-ge-8-26-x86_64");
        var gameWine = await WriteLutrisWineBuild("lutris-7.2-2-x86_64");
        await _home.Combine(".local/share/lutris/runners/wine.yml").WriteAllTextAsync("wine:\n  version: wine-ge-8-26-x86_64\n");
        await WriteGameConfig(".local/share/lutris/games", wineVersion: "lutris-7.2-2-x86_64");
        await WriteGameFiles(withGogInfo: true);

        var runner = CreateLocator().Locate().Single().WinePrefixRunner;

        await Assert.That(runner?.MakeRunCommand(RedModCommand)?.TargetFilePath).IsEqualTo(gameWine.ToString());
    }

    [Test]
    public async Task Test_WithoutPrefix_HasNoToolRunner()
    {
        await WriteGameConfig(".local/share/lutris/games", withPrefix: false);
        await WriteGameFiles(withGogInfo: true);

        await Assert.That(CreateLocator().Locate().Single().WinePrefixRunner).IsNull();
    }

    [Test]
    public async Task Test_IgnoresGamesFromOtherServices()
    {
        await WriteGameConfig(".local/share/lutris/games", service: "egs");
        await WriteGameFiles(withGogInfo: true);

        await Assert.That(CreateLocator().Locate().Count()).IsEqualTo(0);
    }
}

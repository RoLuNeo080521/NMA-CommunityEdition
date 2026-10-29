using System.Collections.Frozen;
using System.Collections.Immutable;
using GameFinder.Launcher.Heroic;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.Backend.Games.Locators;

internal class HeroicGOGLocator : IGameLocator
{
    private readonly ILogger _logger;
    private readonly IFileSystem _fileSystem;
    private readonly HeroicGOGHandler _handler;
    private readonly FrozenDictionary<long, IGameData> _registeredGames;

    private static readonly GameStore Store = GameStore.GOG;

    public HeroicGOGLocator(IEnumerable<IGameData> games, ILoggerFactory loggerFactory, IFileSystem fileSystem)
    {
        _logger = loggerFactory.CreateLogger<HeroicGOGHandler>();
        _fileSystem = fileSystem;

        _handler = new HeroicGOGHandler(
            fileSystem: fileSystem,
            logger: loggerFactory.CreateLogger<HeroicGOGHandler>()
        );

        _registeredGames = games
            .SelectMany(game => game.StoreIdentifiers.GOGProductIds, (game, storeIdentifier) => new KeyValuePair<long, IGameData>(storeIdentifier, game))
            .ToFrozenDictionary();
    }

    public IEnumerable<GameLocatorResult> Locate()
    {
        foreach (var result in _handler.FindAllGames())
        {
            if (result.TryPickT1(out var errorMessage, out var gameFinderGame))
            {
                _logger.LogWarning("Error locating games: {ErrorMessage}", errorMessage.Message);
                continue;
            }

            var storeIdentifier = gameFinderGame.Id.Value;
            _logger.LogDebug("Found game '{GameName}' with store identifier '{StoreIdentifier}'", gameFinderGame.Name, storeIdentifier);

            if (!_registeredGames.TryGetValue(storeIdentifier, out var game)) continue;

            var (path, winePrefixRunner) = UseWinePrefix(gameFinderGame);
            var dlcIds = gameFinderGame.InstalledDLCs.Select(x => LocatorId.From(x.Value.ToString()));

            ImmutableArray<LocatorId> locatorIds =
            [
                LocatorId.From(gameFinderGame.BuildId.ToString()),
                ..dlcIds,
            ];

            yield return new GameLocatorResult
            {
                Game = game,
                Path = path,
                LocatorIds = locatorIds,
                StoreIdentifier = storeIdentifier.ToString(),
                Store = Store,
                Locator = this,
                WinePrefixRunner = winePrefixRunner,
            };
        }
    }

    /// <summary>
    /// Heroic runs a Windows game in the Wine prefix of its game settings, where the game keeps its settings, and runs
    /// its tools there with the same Wine or Proton build. GameFinder's HeroicGOGGame.GetWinePrefix() always assumes a
    /// Proton prefix.
    /// </summary>
    private (AbsolutePath Path, IWinePrefixRunner? WinePrefixRunner) UseWinePrefix(HeroicGOGGame game)
    {
        if (game.WineData is not { } wineData) return (game.Path, null);

        try
        {
            var wineType = wineData.WineVersion?.Type;
            var prefix = GameWinePrefix.FromLauncherFolder(wineData.WinePrefixPath, isProton: wineType == "proton");
            var runner = HeroicWinePrefixRunner.Create(_fileSystem, wineData.WinePrefixPath, prefix, wineData.WineVersion?.Bin, wineType);
            return (GameWinePrefix.InPrefix(game.Path, prefix), runner);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to use the Wine prefix `{Prefix}` of '{GameName}'", wineData.WinePrefixPath, game.Name);
            return (game.Path, null);
        }
    }
}

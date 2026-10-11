using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using YamlDotNet.RepresentationModel;

namespace NexusMods.Backend.Games.Locators;

/// <summary>
/// Detects GOG games installed via Lutris (Linux) by parsing the per-game
/// YAML files under ~/.config/lutris/games/. Each file carries the GOG
/// product id (service_id), slug, and exe path — enough to emit the same
/// GameLocatorResult shape Heroic uses.
/// </summary>
internal class LutrisGOGLocator : IGameLocator
{
    private readonly ILogger _logger;
    private readonly IFileSystem _fileSystem;
    private readonly FrozenDictionary<long, IGameData> _registeredGames;

    private static readonly GameStore Store = GameStore.GOG;

    public LutrisGOGLocator(IEnumerable<IGameData> games, ILoggerFactory loggerFactory, IFileSystem fileSystem)
    {
        _logger = loggerFactory.CreateLogger<LutrisGOGLocator>();
        _fileSystem = fileSystem;

        _registeredGames = games
            .SelectMany(game => game.StoreIdentifiers.GOGProductIds, (game, storeIdentifier) => new KeyValuePair<long, IGameData>(storeIdentifier, game))
            .ToFrozenDictionary();
    }

    public IEnumerable<GameLocatorResult> Locate()
    {
        var configDir = _fileSystem.GetKnownPath(KnownPath.XDG_CONFIG_HOME).Combine("lutris/games");
        if (!configDir.DirectoryExists()) yield break;

        foreach (var yamlFile in configDir.EnumerateFiles("*.yml"))
        {
            GameLocatorResult? result;
            try
            {
                result = TryParse(yamlFile);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to parse Lutris game config {Path}", yamlFile);
                continue;
            }

            if (result is not null)
                yield return result;
        }
    }

    private GameLocatorResult? TryParse(AbsolutePath yamlFile)
    {
        using var reader = new StreamReader(yamlFile.Read());
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count == 0) return null;

        if (yaml.Documents[0].RootNode is not YamlMappingNode root) return null;

        var service = GetScalar(root, "service");
        if (!string.Equals(service, "gog", StringComparison.OrdinalIgnoreCase)) return null;

        var serviceIdStr = GetScalar(root, "service_id");
        if (string.IsNullOrWhiteSpace(serviceIdStr) || !long.TryParse(serviceIdStr, out var productId))
            return null;

        if (!_registeredGames.TryGetValue(productId, out var game)) return null;

        if (root.Children.TryGetValue(new YamlScalarNode("game"), out var gameNode) is false ||
            gameNode is not YamlMappingNode gameMap) return null;

        var exePath = GetScalar(gameMap, "exe");
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        // Trust the Lutris config as the source of truth for installed games —
        // same as HeroicGOGLocator, which never probes the filesystem itself.
        // An existence check here is a false-negative waiting to happen:
        // transient read failures (filesystem indexer, Lutris holding a lock
        // on the prefix, sandboxed access on some setups) would silently drop
        // the game from the locator and surface as "Found no installation of
        // the game `GOG`/`<Game>` anymore!" on the next sync.
        var exe = _fileSystem.FromUnsanitizedFullPath(exePath);
        var gameFolder = exe.Parent;

        // Extract the Lutris internal id from the yml filename pattern
        // "<slug>-<id>.yml" — it's a stable integer we can hand back to Lutris
        // via `lutris:rungameid/<id>` and that satisfies the UBIGINT cast the
        // file-hashes SQL applies to every GOG LocatorId. Fallback to the GOG
        // product id if the pattern doesn't match.
        var fileName = yamlFile.FileName.ToString();
        var baseName = fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
        var dashIdx = baseName.LastIndexOf('-');
        long lutrisGameId = productId;
        if (dashIdx >= 0 && long.TryParse(baseName.AsSpan(dashIdx + 1), out var parsed))
            lutrisGameId = parsed;

        _logger.LogDebug("Found Lutris GOG game '{Name}' productId={ProductId} lutrisId={LutrisId} at {Path}", GetScalar(root, "name"), productId, lutrisGameId, gameFolder);

        return new GameLocatorResult
        {
            Game = game,
            Path = gameFolder,
            LocatorIds = ImmutableArray.Create(LocatorId.From(lutrisGameId.ToString())),
            StoreIdentifier = productId.ToString(),
            Store = Store,
            Locator = this,
        };
    }

    private static string? GetScalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }
}

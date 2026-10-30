using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using GameFinder.Wine;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using YamlDotNet.RepresentationModel;

namespace NexusMods.Backend.Games.Locators;

/// <summary>
/// Detects GOG games installed via Lutris (Linux) by parsing the per-game
/// YAML files in Lutris' games config folder. Each file carries the GOG
/// product id (service_id), the Lutris game slug, the exe path, and the
/// Wine prefix of a Windows game.
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
        var foundPaths = new HashSet<AbsolutePath>();

        foreach (var install in GetInstalls())
        {
            var runnerWineConfig = ReadRunnerWineConfig(install);

            foreach (var yamlFile in install.GamesDirectory.EnumerateFiles("*.yml", recursive: false))
            {
                GameLocatorResult? result;
                try
                {
                    result = TryParse(yamlFile, install, runnerWineConfig);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to parse Lutris game config {Path}", yamlFile);
                    continue;
                }

                // The same game can show up in both the native and the Flatpak Lutris
                if (result is not null && foundPaths.Add(result.Path))
                    yield return result;
            }
        }
    }

    /// <summary>
    /// A Lutris install: its games config folder, its config folder and its data folder (Wine builds, runtime).
    /// </summary>
    internal record LutrisInstall(AbsolutePath GamesDirectory, AbsolutePath ConfigDirectory, AbsolutePath DataDirectory);

    /// <summary>
    /// The Wine version settings of a config: the `version` and `custom_wine_path` of its `wine` section.
    /// </summary>
    private record WineConfig(string? Version, string? CustomWinePath);

    /// <summary>
    /// Lutris reads its config from ~/.config/lutris when that folder exists, and from ~/.local/share/lutris
    /// otherwise (lutris/settings.py: ~/.config/lutris is deprecated, new installs never create it). Its data, like
    /// its Wine builds, is always in ~/.local/share/lutris.
    /// The Flatpak build uses the same layout under ~/.var/app/net.lutris.Lutris.
    /// </summary>
    internal IEnumerable<LutrisInstall> GetInstalls()
    {
        var flatpakDir = _fileSystem.GetKnownPath(KnownPath.HomeDirectory).Combine(".var/app/net.lutris.Lutris");
        (AbsolutePath ConfigHome, AbsolutePath DataHome)[] installs =
        [
            (_fileSystem.GetKnownPath(KnownPath.XDG_CONFIG_HOME), _fileSystem.GetKnownPath(KnownPath.XDG_DATA_HOME)),
            (flatpakDir.Combine("config"), flatpakDir.Combine("data")),
        ];

        foreach (var (configHome, dataHome) in installs)
        {
            var dataDir = dataHome.Combine("lutris");
            var lutrisDir = configHome.Combine("lutris");
            if (!lutrisDir.DirectoryExists()) lutrisDir = dataDir;

            var gamesDir = lutrisDir.Combine("games");
            if (gamesDir.DirectoryExists()) yield return new LutrisInstall(gamesDir, lutrisDir, dataDir);
        }
    }

    /// <summary>
    /// The Wine settings of all the games, in runners/wine.yml of the config folder.
    /// </summary>
    private WineConfig ReadRunnerWineConfig(LutrisInstall install)
    {
        var configFile = install.ConfigDirectory.Combine("runners/wine.yml");
        if (!configFile.FileExists) return new WineConfig(null, null);

        try
        {
            using var reader = new StreamReader(configFile.Read());
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count > 0 && yaml.Documents[0].RootNode is YamlMappingNode root)
                return ReadWineConfig(root);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to read the Lutris Wine settings {Path}", configFile);
        }

        return new WineConfig(null, null);
    }

    private static WineConfig ReadWineConfig(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("wine"), out var wineNode) || wineNode is not YamlMappingNode wineMap)
            return new WineConfig(null, null);

        // Lutris treats `ge-proton` as no version (runners/wine.py, read_version_from_config)
        var version = GetScalar(wineMap, "version");
        if (string.IsNullOrWhiteSpace(version) || version == LutrisWinePrefixRunner.GeProtonLatest) version = null;

        return new WineConfig(version, GetScalar(wineMap, "custom_wine_path"));
    }

    private GameLocatorResult? TryParse(AbsolutePath yamlFile, LutrisInstall install, WineConfig runnerWineConfig)
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

        if (!_registeredGames.TryGetValue(productId, out var game))
        {
            _logger.LogDebug("Ignoring Lutris GOG game '{Name}' with product id {ProductId}: not a supported game", GetScalar(root, "name"), productId);
            return null;
        }

        if (root.Children.TryGetValue(new YamlScalarNode("game"), out var gameNode) is false ||
            gameNode is not YamlMappingNode gameMap) return null;

        var exePath = GetScalar(gameMap, "exe");
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        // Lutris expands `~` itself, and resolves a relative exe against the game folder stored in its database
        exePath = ExpandHome(exePath);
        if (!exePath.StartsWith('/'))
        {
            _logger.LogWarning("Ignoring Lutris game config {Path}: its exe path `{Exe}` is relative", yamlFile, exePath);
            return null;
        }

        // Trust the Lutris config as the source of truth for installed games, like HeroicGOGLocator:
        // the game isn't dropped when its files can't be read right now. The goggame file only refines the result.
        var exe = _fileSystem.FromUnsanitizedFullPath(exePath);
        var gameFolder = FindGameRoot(exe, productId);
        if (gameFolder is null)
            _logger.LogDebug("No goggame-{ProductId}.info above {Exe}, using the exe folder as the game folder", productId, exe);

        var path = gameFolder ?? exe.Parent;
        IWinePrefixRunner? winePrefixRunner = null;
        if (ReadWinePrefix(gameMap) is { } winePrefix)
        {
            path = GameWinePrefix.InPrefix(path, winePrefix.Prefix);

            // The Wine version of the game config wins over the one of all the games
            var gameWineConfig = ReadWineConfig(root);
            winePrefixRunner = new LutrisWinePrefixRunner(_fileSystem, winePrefix.Folder, winePrefix.Prefix,
                wineVersion: gameWineConfig.Version ?? runnerWineConfig.Version,
                customWinePath: gameWineConfig.CustomWinePath ?? runnerWineConfig.CustomWinePath,
                lutrisDataDirectory: install.DataDirectory);
        }

        var buildId = gameFolder is null ? null : ReadBuildId(gameFolder.Value.Combine($"goggame-{productId}.info"));

        // Same locator ids as GOGLocator: the build id, which the file hashes are indexed by, then the installed DLCs
        ImmutableArray<LocatorId> locatorIds =
        [
            LocatorId.From(buildId ?? productId.ToString()),
            ..FindInstalledDlcs(gameFolder, productId).Select(dlcId => LocatorId.From(dlcId.ToString())),
        ];

        // Lutris runs a game from its library slug: `lutris:rungame/<game_slug>`
        var gameSlug = GetScalar(root, "game_slug");

        _logger.LogDebug("Found Lutris GOG game '{Name}' productId={ProductId} slug={Slug} at {Path}", GetScalar(root, "name"), productId, gameSlug, path);

        return new GameLocatorResult
        {
            Game = game,
            Path = path,
            LocatorIds = locatorIds,
            StoreIdentifier = productId.ToString(),
            Store = Store,
            Locator = this,
            LauncherGameId = string.IsNullOrWhiteSpace(gameSlug) ? null : gameSlug,
            WinePrefixRunner = winePrefixRunner,
        };
    }

    /// <summary>
    /// GOG installs have a goggame-&lt;productId&gt;.info file at their root, while the exe can be deeper
    /// (Cyberpunk 2077: bin/x64/Cyberpunk2077.exe). Returns the folder of that file, if any.
    /// </summary>
    private static AbsolutePath? FindGameRoot(AbsolutePath exe, long productId)
    {
        var infoFileName = $"goggame-{productId}.info";
        for (var directory = exe.Parent; ; directory = directory.Parent)
        {
            if (directory.Combine(infoFileName).FileExists) return directory;
            if (directory == directory.GetRootDirectory()) return null;
        }
    }

    /// <summary>
    /// The Wine prefix Lutris runs a Windows game in, where the game keeps its settings: the `prefix` of the game
    /// config. Native Linux games have none. Lutris runs Proton builds through umu, whose prefixes
    /// <see cref="GameWinePrefix.FromLauncherFolder"/> recognizes once the game ran.
    /// </summary>
    private (AbsolutePath Folder, WinePrefix Prefix)? ReadWinePrefix(YamlMappingNode gameMap)
    {
        var prefix = GetScalar(gameMap, "prefix");
        if (string.IsNullOrWhiteSpace(prefix)) return null;

        prefix = ExpandHome(prefix);
        if (!prefix.StartsWith('/')) return null;

        var folder = _fileSystem.FromUnsanitizedFullPath(prefix);
        return (folder, GameWinePrefix.FromLauncherFolder(folder, isProton: false));
    }

    private string ExpandHome(string path)
    {
        return path.StartsWith("~/", StringComparison.Ordinal)
            ? _fileSystem.GetKnownPath(KnownPath.HomeDirectory).Combine(path[2..]).ToString()
            : path;
    }

    private string? ReadBuildId(AbsolutePath infoFile)
    {
        try
        {
            using var stream = infoFile.Read();
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.TryGetProperty("buildId", out var buildId) &&
                buildId.ValueKind == JsonValueKind.String &&
                ulong.TryParse(buildId.GetString(), out _))
                return buildId.GetString();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to read the build id from {Path}", infoFile);
        }

        return null;
    }

    /// <summary>
    /// Each installed DLC adds its own goggame-&lt;productId&gt;.info next to the game's one.
    /// </summary>
    private static IEnumerable<long> FindInstalledDlcs(AbsolutePath? gameFolder, long productId)
    {
        if (gameFolder is null) yield break;

        foreach (var file in gameFolder.Value.EnumerateFiles("goggame-*.info", recursive: false))
        {
            var name = file.FileName.ToString();
            if (long.TryParse(name.AsSpan("goggame-".Length, name.Length - "goggame-".Length - ".info".Length), out var id) && id != productId)
                yield return id;
        }
    }

    private static string? GetScalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }
}

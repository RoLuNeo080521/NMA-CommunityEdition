using System.Collections.Frozen;
using System.Text.Json;
using GameFinder.Wine;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.Backend.Games.Locators;

/// <summary>
/// Detects Epic Games Store games installed with the Heroic Games Launcher (Linux). Heroic installs them with its
/// bundled Legendary, which keeps its data in heroic/legendaryConfig/legendary: installed.json lists the installed
/// games by app name, and metadata/&lt;app name&gt;.json holds each game's catalog item id. Heroic's settings of each
/// game, heroic/GamesConfig/&lt;app name&gt;.json, give the Wine prefix it runs the game in.
/// </summary>
internal class HeroicEpicLocator : IGameLocator
{
    private readonly ILogger _logger;
    private readonly IFileSystem _fileSystem;
    private readonly FrozenDictionary<string, IGameData> _registeredGames;

    private static readonly GameStore Store = GameStore.EGS;

    public HeroicEpicLocator(IEnumerable<IGameData> games, ILoggerFactory loggerFactory, IFileSystem fileSystem)
    {
        _logger = loggerFactory.CreateLogger<HeroicEpicLocator>();
        _fileSystem = fileSystem;

        _registeredGames = games
            .SelectMany(game => game.StoreIdentifiers.EGSCatalogItemId, (game, storeIdentifier) => new KeyValuePair<string, IGameData>(storeIdentifier, game))
            .ToFrozenDictionary();
    }

    public IEnumerable<GameLocatorResult> Locate()
    {
        var foundPaths = new HashSet<AbsolutePath>();

        foreach (var legendaryDir in GetLegendaryDirectories())
        {
            var installedFile = legendaryDir.Combine("installed.json");
            if (!installedFile.FileExists) continue;

            List<GameLocatorResult> results;
            try
            {
                results = ParseInstalledGames(legendaryDir, installedFile).ToList();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to read the Epic games installed with Heroic from {Path}", installedFile);
                continue;
            }

            // The same game can show up in both the native and the Flatpak Heroic
            foreach (var result in results)
            {
                if (foundPaths.Add(result.Path)) yield return result;
            }
        }
    }

    /// <summary>
    /// Heroic keeps its config in ~/.config/heroic, and the Flatpak build in
    /// ~/.var/app/com.heroicgameslauncher.hgl/config/heroic, like for its GOG games.
    /// </summary>
    internal IEnumerable<AbsolutePath> GetLegendaryDirectories()
    {
        AbsolutePath[] heroicDirectories =
        [
            _fileSystem.GetKnownPath(KnownPath.XDG_CONFIG_HOME).Combine("heroic"),
            _fileSystem.GetKnownPath(KnownPath.HomeDirectory).Combine(".var/app/com.heroicgameslauncher.hgl/config/heroic"),
        ];

        return heroicDirectories
            .Select(heroicDir => heroicDir.Combine("legendaryConfig/legendary"))
            .Where(legendaryDir => legendaryDir.DirectoryExists());
    }

    private IEnumerable<GameLocatorResult> ParseInstalledGames(AbsolutePath legendaryDir, AbsolutePath installedFile)
    {
        using var stream = installedFile.Read();
        using var json = JsonDocument.Parse(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Object) yield break;

        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var installed = entry.Value;
            if (installed.ValueKind != JsonValueKind.Object) continue;

            var appName = GetString(installed, "app_name") ?? entry.Name;
            if (installed.TryGetProperty("is_dlc", out var isDlc) && isDlc.ValueKind == JsonValueKind.True) continue;

            // Heroic installs the Windows build on Linux. Other builds don't have the files the games are modded with.
            var platform = GetString(installed, "platform") ?? "Windows";
            if (!platform.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Ignoring Epic game `{AppName}` installed with Heroic: its {Platform} build isn't supported", appName, platform);
                continue;
            }

            var installPath = GetString(installed, "install_path");
            if (string.IsNullOrWhiteSpace(installPath) || !installPath.StartsWith('/')) continue;

            // Games are registered with the catalog item id that the Epic Games Launcher manifests also carry.
            // Mount & Blade II: Bannerlord is registered with its app name.
            var catalogItemId = ReadCatalogItemId(legendaryDir, appName);
            var storeIdentifier = catalogItemId is not null && _registeredGames.ContainsKey(catalogItemId) ? catalogItemId : appName;
            if (!_registeredGames.TryGetValue(storeIdentifier, out var game))
            {
                _logger.LogDebug("Ignoring Epic game `{AppName}` ({CatalogItemId}) installed with Heroic: not a supported game", appName, catalogItemId);
                continue;
            }

            // legendaryDir is heroic/legendaryConfig/legendary
            var path = _fileSystem.FromUnsanitizedFullPath(installPath);
            if (ReadWinePrefix(legendaryDir.Parent.Parent, appName) is { } prefix)
                path = GameWinePrefix.InPrefix(path, prefix);

            _logger.LogDebug("Found Epic game `{AppName}` ({StoreIdentifier}) installed with Heroic at {Path}", appName, storeIdentifier, path);

            yield return new GameLocatorResult
            {
                Game = game,
                Path = path,
                // The file hashes know Epic builds by the manifest hash of the Epic Games Launcher, which Legendary
                // doesn't keep. Without locator ids, the game folder as first indexed stands in for the game files.
                LocatorIds = [],
                StoreIdentifier = storeIdentifier,
                Store = Store,
                Locator = this,
                // Heroic runs its Epic games by app name: heroic://launch?appName=<app name>&runner=legendary
                LauncherGameId = appName,
            };
        }
    }

    /// <summary>
    /// Legendary's catalog item id of a game is the `id` of its catalog metadata (legendary/models/game.py).
    /// </summary>
    private string? ReadCatalogItemId(AbsolutePath legendaryDir, string appName)
    {
        if (appName.Contains('/')) return null;

        var metadataFile = legendaryDir.Combine("metadata").Combine($"{appName}.json");
        if (!metadataFile.FileExists) return null;

        try
        {
            using var stream = metadataFile.Read();
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("metadata", out var metadata) &&
                metadata.ValueKind == JsonValueKind.Object)
                return GetString(metadata, "id");
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to read the catalog item id from {Path}", metadataFile);
        }

        return null;
    }

    /// <summary>
    /// The Wine prefix of the game settings, where the game keeps its settings: the `winePrefix` of the
    /// `&lt;app name&gt;` object, like GameFinder reads it for the GOG games installed with Heroic.
    /// </summary>
    private WinePrefix? ReadWinePrefix(AbsolutePath heroicDir, string appName)
    {
        if (appName.Contains('/')) return null;

        var configFile = heroicDir.Combine("GamesConfig").Combine($"{appName}.json");
        if (!configFile.FileExists)
        {
            _logger.LogDebug("No Heroic settings for the Epic game `{AppName}` at {Path}: its Wine prefix is unknown", appName, configFile);
            return null;
        }

        try
        {
            using var stream = configFile.Read();
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty(appName, out var settings) ||
                settings.ValueKind != JsonValueKind.Object) return null;

            var winePrefix = GetString(settings, "winePrefix");
            if (string.IsNullOrWhiteSpace(winePrefix) || !winePrefix.StartsWith('/')) return null;

            var wineType = settings.TryGetProperty("wineVersion", out var wineVersion) && wineVersion.ValueKind == JsonValueKind.Object
                ? GetString(wineVersion, "type")
                : null;

            return GameWinePrefix.FromLauncherFolder(_fileSystem.FromUnsanitizedFullPath(winePrefix), isProton: wineType == "proton");
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to read the Wine prefix of the Epic game `{AppName}` from {Path}", appName, configFile);
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

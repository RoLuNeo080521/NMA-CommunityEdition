using CliWrap;
using GameFinder.Wine;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.Backend.Games.Locators;

internal static class WineCommands
{
    /// <summary>
    /// The command that runs the Windows program of <paramref name="command"/> with <paramref name="runner"/>:
    /// <c>runner [verb] "program" arguments</c>, in the same working directory, with the extra environment variables.
    /// </summary>
    public static Command Wrap(Command command, string runner, string? verb, IReadOnlyDictionary<string, string?> environmentVariables)
    {
        var arguments = $"{(verb is null ? "" : verb + " ")}\"{command.TargetFilePath}\" {command.Arguments}";

        var environment = new Dictionary<string, string?>(command.EnvironmentVariables);
        foreach (var (name, value) in environmentVariables) environment[name] = value;

        return new Command(runner, arguments, command.WorkingDirPath, command.ResourcePolicy, command.Credentials,
            environment, command.Validation, command.StandardInputPipe, command.StandardOutputPipe, command.StandardErrorPipe);
    }

    /// <summary>
    /// The first file named <paramref name="name"/> in the folders of <paramref name="searchPath"/> (PATH).
    /// </summary>
    public static AbsolutePath? FindInPath(IFileSystem fileSystem, string searchPath, string name)
    {
        foreach (var folder in searchPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!folder.StartsWith('/')) continue;
            var file = fileSystem.FromUnsanitizedFullPath(folder).Combine(name);
            if (file.FileExists) return file;
        }

        return null;
    }
}

/// <summary>
/// Runs a Windows program in the Wine prefix of a game installed with Heroic, like Heroic does without umu
/// (launcher.ts, runWineCommand): a Wine build runs it in WINEPREFIX, a Proton build with <c>proton run</c> and
/// STEAM_COMPAT_DATA_PATH.
/// </summary>
internal class HeroicWinePrefixRunner : IWinePrefixRunner
{
    private readonly AbsolutePath _launcherFolder;
    private readonly AbsolutePath _wineBinary;
    private readonly bool _isProton;
    private readonly AbsolutePath _steamClientDirectory;

    private HeroicWinePrefixRunner(AbsolutePath launcherFolder, WinePrefix prefix, AbsolutePath wineBinary, bool isProton, AbsolutePath steamClientDirectory)
    {
        _launcherFolder = launcherFolder;
        _wineBinary = wineBinary;
        _isProton = isProton;
        _steamClientDirectory = steamClientDirectory;
        WinePrefixDirectoryPath = prefix.ConfigurationDirectory;
    }

    /// <summary>
    /// The runner of the `winePrefix` and `wineVersion` of Heroic's settings of a game, or null for the runners NMA
    /// doesn't support (CrossOver, Wine on macOS).
    /// </summary>
    public static HeroicWinePrefixRunner? Create(IFileSystem fileSystem, AbsolutePath launcherFolder, WinePrefix prefix, string? wineBinary, string? wineType)
    {
        // Heroic removes the quotes of the binary path before running it
        wineBinary = wineBinary?.Replace("'", "");
        if (string.IsNullOrWhiteSpace(wineBinary) || !wineBinary.StartsWith('/')) return null;
        if (wineType is not ("wine" or "proton")) return null;

        // Heroic gives Proton the Steam folder of the home folder, which may not exist
        var steamClientDirectory = fileSystem.GetKnownPath(KnownPath.HomeDirectory).Combine(".steam/steam");
        return new HeroicWinePrefixRunner(launcherFolder, prefix, fileSystem.FromUnsanitizedFullPath(wineBinary), wineType == "proton", steamClientDirectory);
    }

    public AbsolutePath WinePrefixDirectoryPath { get; }

    public Command? MakeRunCommand(Command command)
    {
        if (!_wineBinary.FileExists) return null;

        if (_isProton)
        {
            return WineCommands.Wrap(command, _wineBinary.ToString(), verb: "run", new Dictionary<string, string?>
            {
                ["STEAM_COMPAT_DATA_PATH"] = _launcherFolder.ToString(),
                ["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = _steamClientDirectory.ToString(),
            });
        }

        return WineCommands.Wrap(command, _wineBinary.ToString(), verb: null, new Dictionary<string, string?>
        {
            ["WINEPREFIX"] = _launcherFolder.ToString(),
        });
    }
}

/// <summary>
/// Runs a Windows program in the Wine prefix of a game installed with Lutris, with the Wine version of its config, like
/// Lutris does (runners/wine.py, get_executable and get_env; util/wine/proton.py):
/// <list type="bullet">
/// <item>"ge-proton", the default, and the Proton builds run with umu, which downloads GE-Proton when needed;</item>
/// <item>"system" and the other system Wine names run the Wine of the system, "custom" the custom_wine_path;</item>
/// <item>any other version is a Wine build that Lutris installed in runners/wine.</item>
/// </list>
/// </summary>
internal class LutrisWinePrefixRunner : IWinePrefixRunner
{
    internal const string GeProtonLatest = "ge-proton";

    // umu's GAMEID when Lutris has no umu id for the game
    private const string DefaultGameId = "umu-default";

    private static readonly Dictionary<string, string> SystemWinePaths = new(StringComparer.Ordinal)
    {
        ["winehq-devel"] = "/opt/wine-devel/bin/wine",
        ["winehq-staging"] = "/opt/wine-staging/bin/wine",
        ["wine-development"] = "/usr/lib/wine-development/wine",
    };

    private readonly IFileSystem _fileSystem;
    private readonly AbsolutePath _launcherFolder;
    private readonly string? _wineVersion;
    private readonly string? _customWinePath;
    private readonly AbsolutePath _lutrisDataDirectory;

    public LutrisWinePrefixRunner(IFileSystem fileSystem, AbsolutePath launcherFolder, WinePrefix prefix, string? wineVersion, string? customWinePath, AbsolutePath lutrisDataDirectory)
    {
        _fileSystem = fileSystem;
        _launcherFolder = launcherFolder;
        _wineVersion = wineVersion;
        _customWinePath = customWinePath;
        _lutrisDataDirectory = lutrisDataDirectory;
        WinePrefixDirectoryPath = prefix.ConfigurationDirectory;
    }

    public AbsolutePath WinePrefixDirectoryPath { get; }

    /// <summary>
    /// The folders of PATH, where the system Wine and umu-run are searched.
    /// </summary>
    internal string SearchPath { get; init; } = Environment.GetEnvironmentVariable("PATH") ?? "";

    /// <summary>
    /// The folders of the system where Lutris looks for umu after PATH.
    /// </summary>
    internal string[] SystemUmuDirectories { get; init; } = ["/usr/local/share/umu", "/usr/share/umu", "/opt/umu"];

    private AbsolutePath WineBuildsDirectory => _lutrisDataDirectory.Combine("runners/wine");

    public Command? MakeRunCommand(Command command)
    {
        var version = string.IsNullOrWhiteSpace(_wineVersion) ? GeProtonLatest : _wineVersion;
        if (version == GeProtonLatest) return RunWithUmu(command, protonPath: "GE-Proton");

        if (FindProtonBuild(version) is { } protonBuild) return RunWithUmu(command, protonBuild.ToString());

        var wineBinary = version switch
        {
            "system" => WineCommands.FindInPath(_fileSystem, SearchPath, "wine"),
            "custom" => ExistingFile(_customWinePath),
            _ when SystemWinePaths.TryGetValue(version, out var systemWine) => ExistingFile(systemWine),
            // Lutris lists the /usr/lib/wine-* builds as "System wine-*"
            _ when version.StartsWith("System wine-", StringComparison.Ordinal) && !version.Contains('/') => ExistingFile($"/usr/lib/{version["System ".Length..]}/bin/wine"),
            _ => ExistingFile(WineBuildsDirectory.Combine(version).Combine("bin/wine").ToString()),
        };

        if (wineBinary is not null)
        {
            return WineCommands.Wrap(command, wineBinary.Value.ToString(), verb: null, new Dictionary<string, string?>
            {
                ["WINEPREFIX"] = _launcherFolder.ToString(),
            });
        }

        // Lutris falls back to its default version when the Wine build is missing
        return RunWithUmu(command, protonPath: "GE-Proton");
    }

    private AbsolutePath? ExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/')) return null;
        var file = _fileSystem.FromUnsanitizedFullPath(path);
        return file.FileExists ? file : null;
    }

    /// <summary>
    /// A Proton build is a folder with a `proton` script, in the Wine builds of Lutris, in compatibilitytools.d or in
    /// the Steam library. GE-Proton 11 and later install into &lt;tag&gt;-&lt;arch&gt; folders.
    /// </summary>
    private AbsolutePath? FindProtonBuild(string version)
    {
        var home = _fileSystem.GetKnownPath(KnownPath.HomeDirectory);
        AbsolutePath[] locations =
        [
            WineBuildsDirectory,
            _fileSystem.GetKnownPath(KnownPath.XDG_DATA_HOME).Combine("Steam/compatibilitytools.d"),
            home.Combine(".steam/steam/steamapps/common"),
            home.Combine(".local/share/Steam/steamapps/common"),
        ];

        foreach (var location in locations)
        {
            foreach (var name in new[] { version, $"{version}-x86_64" })
            {
                if (name.Contains('/')) continue;
                var build = location.Combine(name);
                if (build.Combine("proton").FileExists) return build;
            }
        }

        return null;
    }

    private Command? RunWithUmu(Command command, string protonPath)
    {
        if (FindUmu() is not { } umu) return null;

        return WineCommands.Wrap(command, umu.ToString(), verb: null, new Dictionary<string, string?>
        {
            ["WINEPREFIX"] = _launcherFolder.ToString(),
            ["GAMEID"] = DefaultGameId,
            ["PROTONPATH"] = protonPath,
            ["WINEARCH"] = "win64",
        });
    }

    /// <summary>
    /// Lutris looks for umu-run in PATH, then in the share folders of the system, then in its runtime folder.
    /// </summary>
    private AbsolutePath? FindUmu()
    {
        if (WineCommands.FindInPath(_fileSystem, SearchPath, "umu-run") is { } umuInPath) return umuInPath;

        AbsolutePath[] locations =
        [
            ..SystemUmuDirectories.Select(directory => _fileSystem.FromUnsanitizedFullPath(directory)),
            _lutrisDataDirectory.Combine("runtime/umu"),
        ];

        foreach (var location in locations)
        {
            foreach (var entryPoint in new[] { "umu-run", "umu_run.py" })
            {
                var umu = location.Combine(entryPoint);
                if (umu.FileExists) return umu;
            }
        }

        return null;
    }
}

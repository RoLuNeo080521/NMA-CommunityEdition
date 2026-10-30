using System.Reflection;
using CliWrap;
using DynamicData.Kernel;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.Generic;
using NexusMods.Games.Generic.Dependencies;
using NexusMods.Games.RedEngine.Cyberpunk2077.SortOrder;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Loadouts;
using R3;
using static NexusMods.Games.RedEngine.Constants;

namespace NexusMods.Games.RedEngine;

public class RedModDeployTool : ITool
{
    private readonly GameToolRunner _toolRunner;
    private readonly TemporaryFileManager _temporaryFileManager;
    private readonly ILogger _logger;
    private readonly RedModSortOrderVariety _redModSortOrderVariety;

    /// <summary>
    /// Only registered on Linux.
    /// </summary>
    private readonly AggregateProtontricksDependency? _protontricks;

    public RedModDeployTool(GameToolRunner toolRunner, TemporaryFileManager temporaryFileManager, ILogger<RedModDeployTool> logger, RedModSortOrderVariety sortOrderVariety, AggregateProtontricksDependency? protontricks = null)
    {
        _logger = logger;
        _toolRunner = toolRunner;
        _temporaryFileManager = temporaryFileManager;
        _redModSortOrderVariety = sortOrderVariety;
        _protontricks = protontricks;
    }
    
    public IEnumerable<GameId> GameIds => [Cyberpunk2077.Cyberpunk2077Game.GameId];

    public async Task Execute(Loadout.ReadOnly loadout, CancellationToken cancellationToken)
    {
        var exe = loadout.InstallationInstance.Locations.ToAbsolutePath(RedModPath);
        var gameFolder = loadout.InstallationInstance.Locations.ToAbsolutePath(new GamePath(LocationId.Game, RelativePath.Empty));
        var deployFolder = loadout.InstallationInstance.Locations.ToAbsolutePath(RedModDeployFolder);

        await using var loadorderFile = _temporaryFileManager.CreateFile();
        await WriteLoadOrderFile(loadorderFile.Path, loadout);
        
        // RedMod deploys to this folder and freaks out if it doesn't exist, but our synchronizer will
        // delete it if it's empty, so we need to recreate it here.
        if (!deployFolder.DirectoryExists())
            deployFolder.CreateDirectory();

        // Note (halgari): When we change the redmod order, we need to force redmod to re-run. This is due to a bug in redmod
        // not recognizing order as a change in the mod configuration. If that bug ever gets fixed (does an order check) then  
        // we can resolve this by removing the -force flag.
        var fs = FileSystem.Shared;
        if (fs.OS.IsWindows)
        {
            var command = Cli.Wrap(exe.ToString())
                .WithArguments(["deploy", "-force", "-modlist=" + loadorderFile.Path], true)
                .WithWorkingDirectory(exe.Parent.ToString());
            await _toolRunner.ExecuteAsync(loadout, command, true, cancellationToken);
        }
        else
        {
            if (loadout.Installation.Store == GameStore.Steam)
            {
                var linuxCompatibilityDataProvider = loadout.InstallationInstance.LocatorResult.LinuxCompatabilityDataProvider;
                if (linuxCompatibilityDataProvider is null)
                {
                    _logger.LogWarning("Skip running redmod: the game has no Proton prefix");
                    return;
                }

                // GameToolRunner runs redmod in the Proton prefix with protontricks-launch, and fails without it.
                // MissingProtontricksForRedModEmitter tells the user to install protontricks.
                if (_protontricks is null || !await _protontricks.IsAvailable())
                {
                    _logger.LogWarning("Skip running redmod: protontricks isn't installed");
                    return;
                }

                var winePrefixDirectory = linuxCompatibilityDataProvider.WinePrefixDirectoryPath;
                await CopyLoadOrderToPrefix(loadorderFile.Path, winePrefixDirectory, cancellationToken);

                await using var batchPath = await ExtractTemporaryDeployScript();
                var command = Cli.Wrap(batchPath.ToString())
                    .WithArguments(["deploy", "-force", "-modlist=" + PrefixLoadOrderFile, "-root=" + ToWinePath(gameFolder, winePrefixDirectory)], escape: true);
                await _toolRunner.ExecuteAsync(loadout, command, true, cancellationToken);
            }
            else if (loadout.InstallationInstance.LocatorResult.WinePrefixRunner is { } winePrefixRunner)
            {
                // Games installed with Heroic or Lutris: run redmod in their prefix, with the Wine or Proton build of their launcher
                var command = Cli.Wrap(exe.ToString())
                    .WithArguments(["deploy", "-force", "-modlist=" + PrefixLoadOrderFile, "-root=" + ToWinePath(gameFolder, winePrefixRunner.WinePrefixDirectoryPath)], true)
                    .WithWorkingDirectory(exe.Parent.ToString());

                var wineCommand = winePrefixRunner.MakeRunCommand(command);
                if (wineCommand is null)
                {
                    _logger.LogWarning("Skip running redmod: the Wine or Proton build the launcher runs the game with isn't installed");
                    return;
                }

                await CopyLoadOrderToPrefix(loadorderFile.Path, winePrefixRunner.WinePrefixDirectoryPath, cancellationToken);
                await _toolRunner.ExecuteAsync(loadout, wineCommand, true, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Skip running redmod: the Wine prefix of the game is unknown");
            }
        }
    }

    /// <summary>
    /// The load order file in the Wine prefix, where redmod reads it.
    /// </summary>
    private const string PrefixLoadOrderFile = "C:\\modlist.txt";

    /// <summary>
    /// The path of <paramref name="path"/> for a program running in the Wine prefix: C:\ is its drive_c, and Z:\ the
    /// root of the file system.
    /// </summary>
    /// <remarks>
    /// Without -root, redmod uses the folder it runs from, tools\redmod\bin, as the game folder: it finds no mods
    /// and deploys nothing (REDmodLog.txt: "Invalid root path found", "No mods found").
    /// </remarks>
    internal static string ToWinePath(AbsolutePath path, AbsolutePath winePrefixDirectory)
    {
        var cDrive = winePrefixDirectory.Combine("drive_c");
        return path.InFolder(cDrive)
            ? "C:\\" + path.RelativeTo(cDrive).ToString().Replace('/', '\\')
            : "Z:" + path.ToString().Replace('/', '\\');
    }

    private static async Task CopyLoadOrderToPrefix(AbsolutePath loadorderFilePath, AbsolutePath winePrefixDirectory, CancellationToken cancellationToken)
    {
        var cDriveDirectory = winePrefixDirectory.Combine("drive_c");
        if (!cDriveDirectory.DirectoryExists()) cDriveDirectory.CreateDirectory();

        await using var input = loadorderFilePath.Read();
        await using var output = cDriveDirectory.Combine("modlist.txt").Open(FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await input.CopyToAsync(output, cancellationToken: cancellationToken);
    }

    internal async Task WriteLoadOrderFile(AbsolutePath loadorderFilePath, Loadout.ReadOnly loadout)
    {
        // TODO: this currently uses the loadout sort order, change this to use the "Active" sort order,
        // once we support switching between collection and loadout sort orders
        var collectionId = Optional.None<CollectionGroupId>();
        
        var output = string.Empty;
        // Note(AL12rs): this will get the Load Order for the specific revision of the DB of loadout, which might not be the latest
        var order = _redModSortOrderVariety.GetRedModOrder(loadout, collectionId, loadout.Db);
        
        // NOTE(erri120): redmod only accepts CRLR line breaks, everything else breaks the program
        // and results in getting errors like `Non-existant mod selected`
        output = order.Count > 0 ? string.Join("\r\n", order) : string.Empty;
        
        await loadorderFilePath.WriteAllTextAsync(output);
    }
    public string Name => "RedMod Deploy";

    public IJobTask<ITool, Unit> StartJob(Loadout.ReadOnly loadout, IJobMonitor monitor, CancellationToken cancellationToken)
    {
        return monitor.Begin<ITool, Unit>(this, async _ =>
        {
            await Execute(loadout, cancellationToken);
            return Unit.Default;
        });
    }

    private async Task<TemporaryPath> ExtractTemporaryDeployScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "deploy_redmod.bat";

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Resource {resourceName} not found in assembly.");

        using var reader = new StreamReader(stream);
        var file = _temporaryFileManager.CreateFile((Extension?)".bat");
        await file.Path.WriteAllTextAsync(await reader.ReadToEndAsync());
        return file;
    }
}

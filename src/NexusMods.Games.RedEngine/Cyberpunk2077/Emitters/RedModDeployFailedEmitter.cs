using System.Runtime.CompilerServices;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RedEngine.Cyberpunk2077.Emitters;

/// <summary>
/// Tells the user when redmod failed to deploy the REDmods, from the log of its last run. redmod reports a failed
/// deploy in this log, which the app doesn't show otherwise.
/// </summary>
public class RedModDeployFailedEmitter : ILoadoutDiagnosticEmitter
{
    internal const string LogFileName = "REDmodLog.txt";
    private const string DeployFailed = "Commandlet deploy has failed";
    private const string ErrorPrefix = "ERROR:";

    public async IAsyncEnumerable<Diagnostic> Diagnose(Loadout.ReadOnly loadout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        // The synchronizer only runs redmod when the game folder has REDmods and the REDmod DLC is installed
        if (!MissingRedModEmitter.HasRedMods(loadout, out _, out _)) yield break;
        if (!MissingRedModEmitter.HasRedModToolInstalled(loadout, out var redModPath)) yield break;

        // redmod writes its log in its working directory: the game folder on Linux, its own folder on Windows
        var gameFolder = loadout.InstallationInstance.Locations.ToAbsolutePath(new GamePath(LocationId.Game, RelativePath.Empty));
        var logs = new[] { gameFolder.Combine(LogFileName), redModPath.Parent.Combine(LogFileName) }
            .Where(path => path.FileExists)
            .OrderByDescending(path => path.FileInfo.LastWriteTimeUtc)
            .ToArray();
        if (logs.Length == 0) yield break;

        var log = logs[0];
        var lines = (await log.ReadAllTextAsync(cancellationToken)).Split('\n', StringSplitOptions.TrimEntries);
        if (!lines.Any(line => line.Contains(DeployFailed))) yield break;

        var error = lines.LastOrDefault(line => line.StartsWith(ErrorPrefix));
        yield return Diagnostics.CreateRedModDeployFailed(
            Error: error is null ? DeployFailed : error[ErrorPrefix.Length..].Trim(),
            LogPath: log.ToString()
        );
    }
}

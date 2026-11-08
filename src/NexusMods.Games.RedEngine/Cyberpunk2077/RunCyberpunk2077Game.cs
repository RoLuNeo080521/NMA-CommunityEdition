using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Paths;
using NexusMods.Sdk.Loadouts;
using static NexusMods.Games.RedEngine.Constants;

namespace NexusMods.Games.RedEngine.Cyberpunk2077;

public class RunCyberpunk2077Game(IServiceProvider provider, Cyberpunk2077Game game) : RunGameTool<Cyberpunk2077Game>(provider, game)
{
    private const string ModdedFlag = "-modded";

    public override Task Execute(Loadout.ReadOnly loadout, CancellationToken cancellationToken, string[]? commandLineArgs)
    {
        var modsFolder = loadout.InstallationInstance.Locations.ToAbsolutePath(RedModInstallFolder);
        return base.Execute(loadout, cancellationToken, BuildCommandLineArgs(commandLineArgs, HasRedMods(modsFolder)));
    }

    /// <summary>
    /// Whether the game folder has a REDmod: a folder in <c>mods</c> with an <c>info.json</c>, which is what REDmod itself
    /// looks for ("Folder X is not a valid mod: Missing info.json!").
    /// </summary>
    internal static bool HasRedMods(AbsolutePath modsFolder)
    {
        try
        {
            return modsFolder.DirectoryExists()
                   && modsFolder.EnumerateDirectories(recursive: false).Any(folder => folder.Combine("info.json").FileExists);
        }
        catch (Exception)
        {
            // We can't tell: keep launching with -modded, as before
            return true;
        }
    }

    /// <summary>
    /// <c>-modded</c> makes the game run with what REDmod deployed in <c>r6/cache/modded</c> (scripts, TweakDB) and load
    /// the archives in <c>mods</c>. Without a REDmod there is nothing to deploy, and that folder only holds what an earlier
    /// deploy left, which is stale after a game update: the redscript mods then compile against the old scripts and fail.
    /// So the flag is only passed when a REDmod is installed.
    /// </summary>
    internal static string[]? BuildCommandLineArgs(string[]? commandLineArgs, bool hasRedMods)
    {
        if (!hasRedMods) return commandLineArgs;
        if (commandLineArgs is null) return [ModdedFlag];
        return commandLineArgs.Contains(ModdedFlag, StringComparer.OrdinalIgnoreCase) ? commandLineArgs : [..commandLineArgs, ModdedFlag];
    }
}

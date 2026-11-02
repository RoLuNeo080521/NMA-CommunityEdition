using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Paths.Trees.Traits;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RedEngine.ModInstallers;

/// <summary>
/// Installs The Witcher 3 mods: the game loads each mod from a <c>Mods/modXxx/content</c> folder, and each DLC from a
/// <c>DLC/dlcXxx/content</c> folder.
/// </summary>
public class Witcher3ModInstaller : ALibraryArchiveInstaller
{
    private const string ModsFolder = "Mods";
    private const string DlcFolder = "DLC";
    private const string BinFolder = "bin";
    private const string ContentFolder = "content";

    // The folders of bin that mods add files to: plugins like asi loaders, and the xml files of menu mods
    private static readonly string[] BinSubFolders = ["x64", "x64_dx12", "config"];

    public Witcher3ModInstaller(IServiceProvider serviceProvider) :
        base(serviceProvider, serviceProvider.GetRequiredService<ILogger<Witcher3ModInstaller>>())
    {
    }

    public override ValueTask<InstallerResult> ExecuteAsync(
        LibraryArchive.ReadOnly libraryArchive,
        LoadoutItemGroup.New loadoutGroup,
        ITransaction tx,
        Loadout.ReadOnly loadout,
        CancellationToken cancellationToken)
    {
        var tree = LibraryArchiveTreeExtensions.GetTree(libraryArchive);
        var archiveName = Path.GetFileNameWithoutExtension(libraryArchive.AsLibraryFile().FileName.ToString());
        var archiveModName = ModFolderName(new string(archiveName.Where(char.IsLetterOrDigit).ToArray()));

        var modFiles = tree.GetFiles()
            .Select(file => (File: file, Target: GetTargetPath(file.Item.Path.ToString().Split('/'), archiveModName)))
            .Where(row => row.Target is not null)
            .Select(row => row.File.ToLoadoutFile(loadout.Id, loadoutGroup.Id, tx, new GamePath(LocationId.Game, row.Target!)))
            .ToArray();

        return modFiles.Length == 0
            ? ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Archive contains no Witcher 3 mod folders"))
            : ValueTask.FromResult<InstallerResult>(new Success());
    }

    /// <summary>
    /// Returns where a file of the archive goes in the game folder, or null for files that aren't part of the mod, like
    /// a readme.
    /// </summary>
    internal static string? GetTargetPath(string[] parts, string archiveModName)
    {
        var directories = parts.Length - 1;

        // The archive already has the game's folders: Mods/, DLC/ or bin/
        for (var i = 0; i < directories; i++)
        {
            if (Is(parts[i], ModsFolder) || Is(parts[i], DlcFolder))
                return Join(Is(parts[i], ModsFolder) ? ModsFolder : DlcFolder, parts[(i + 1)..]);
            if (Is(parts[i], BinFolder) && i + 1 < directories && BinSubFolders.Any(folder => Is(parts[i + 1], folder)))
                return Join(BinFolder, parts[(i + 1)..]);
        }

        // A mod or DLC folder packed without Mods/ or DLC/: the folder that holds a content folder
        for (var i = 0; i + 1 < directories; i++)
        {
            if (!Is(parts[i + 1], ContentFolder)) continue;
            return parts[i].StartsWith("dlc", StringComparison.OrdinalIgnoreCase)
                ? Join(DlcFolder, parts[i..])
                : Join(ModsFolder, [ModFolderName(parts[i]), ..parts[(i + 1)..]]);
        }

        // A content folder alone, named after the archive
        if (directories > 0 && Is(parts[0], ContentFolder))
            return Join(ModsFolder, [archiveModName, ..parts]);

        return null;
    }

    /// <summary>
    /// The game only loads the folders of Mods/ whose name starts with "mod".
    /// </summary>
    private static string ModFolderName(string name) =>
        name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ? name : "mod" + name;

    private static bool Is(string part, string folder) => part.Equals(folder, StringComparison.OrdinalIgnoreCase);

    private static string Join(string folder, string[] parts) => string.Join('/', parts.Prepend(folder));
}

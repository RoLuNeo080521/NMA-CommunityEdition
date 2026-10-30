using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Paths.Trees;
using NexusMods.Paths.Trees.Traits;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RedEngine.ModInstallers;

public class SimpleOverlayModInstaller : ALibraryArchiveInstaller
{
    
    public SimpleOverlayModInstaller(IServiceProvider serviceProvider) : 
        base(serviceProvider, serviceProvider.GetRequiredService<ILogger<SimpleOverlayModInstaller>>())
    {
    }

    private static readonly RelativePath[] RootPaths =
    [
        "bin/x64",
        "engine",
        "r6",
        "red4ext",
        "archive/pc/mod",
    ];

    // Cyber Engine Tweaks runs the init.lua of each folder in its mods folder
    private static readonly RelativePath CetModsFolder = "bin/x64/plugins/cyber_engine_tweaks/mods";
    private static readonly RelativePath CetModScript = "init.lua";

    public override ValueTask<InstallerResult> ExecuteAsync(
        LibraryArchive.ReadOnly libraryArchive,
        LoadoutItemGroup.New loadoutGroup,
        ITransaction tx,
        Loadout.ReadOnly loadout,
        CancellationToken cancellationToken)
    {
        var tree = LibraryArchiveTreeExtensions.GetTree(libraryArchive);

        // Note: Expected search space here is small, highest expected overhead is in FindSubPathRootsByKeyUpward.
        // Find all paths which match a known base/root directory.
        var roots = RootPaths
            .SelectMany(x => tree.FindSubPathRootsByKeyUpward(x.Parts.ToArray()))
            .ToArray();

        // Cyber Engine Tweaks mods packed without their bin/x64/plugins/cyber_engine_tweaks/mods path: the folders with
        // an init.lua outside the roots. A folder in another one is part of that mod.
        var rootFolders = roots.Select(root => root.Item.Path).ToArray();
        var cetModFolders = tree.GetFiles()
            .Select(file => file.Item.Path)
            .Where(path => path.FileName == CetModScript && path.Parent != RelativePath.Empty)
            .Select(path => path.Parent)
            .Where(folder => !rootFolders.Any(root => folder.InFolder(root)))
            .Distinct()
            .ToArray();
        cetModFolders = cetModFolders
            .Where(folder => !cetModFolders.Any(other => other != folder && folder.InFolder(other)))
            .ToArray();

        if (roots.Length == 0 && cetModFolders.Length == 0) return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Archive contains no valid roots"));

        var newFiles = 0;

        // Deploy every matched root, not just those at one depth. Otherwise a
        // mod that ships both r6/ (depth 1) and archive/pc/mod/ (depth 3) only
        // gets one side installed and the other side is silently dropped.
        foreach (var node in roots)
        foreach (var file in node.Item.GetFiles<LibraryArchiveTree, RelativePath>())
        {
            AddFile(file.Item, file.Item.Path.DropFirst(node.Depth() - 1));
        }

        foreach (var folder in cetModFolders)
        foreach (var file in tree.GetFiles().Where(file => file.Item.Path.InFolder(folder)))
        {
            AddFile(file.Item, CetModsFolder.Join(folder.FileName).Join(file.Item.Path.RelativeTo(folder)));
        }

        return newFiles == 0
            ? ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Found no matching files in the archive"))
            : ValueTask.FromResult<InstallerResult>(new Success());

        void AddFile(LibraryArchiveTree file, RelativePath relativePath)
        {
            _ = new LoadoutFile.New(tx, out var id)
            {
                LoadoutItemWithTargetPath = new LoadoutItemWithTargetPath.New(tx, id)
                {
                    TargetPath = (loadout.Id, LocationId.Game, relativePath),
                    LoadoutItem = new LoadoutItem.New(tx, id)
                    {
                        Name = relativePath.Name,
                        LoadoutId = loadout.Id,
                        ParentId = loadoutGroup.Id,
                    },
                },
                Hash = file.LibraryFile.Value.Hash,
                Size = file.LibraryFile.Value.Size,
            };
            newFiles++;
        }
    }
}

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

        if (roots.Length == 0) return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Archive contains no valid roots"));

        var newFiles = 0;

        // Deploy every matched root, not just those at one depth. Otherwise a
        // mod that ships both r6/ (depth 1) and archive/pc/mod/ (depth 3) only
        // gets one side installed and the other side is silently dropped.
        foreach (var node in roots)
        foreach (var file in node.Item.GetFiles<LibraryArchiveTree, RelativePath>())
        {
            var fullPath = file.Item.Path;
            var relativePath = fullPath.DropFirst(node.Depth() - 1);

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
                Hash = file.Item.LibraryFile.Value.Hash,
                Size = file.Item.LibraryFile.Value.Size,
            };
            newFiles++;
        }

        return newFiles == 0
            ? ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Found no matching files in the archive"))
            : ValueTask.FromResult<InstallerResult>(new Success());
    }
}

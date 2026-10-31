using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// The game locations that moved since the last applied loadout, like AppData, which 1.3.0 put in the Wine prefix.
/// </summary>
public class MovedGameLocationsUnitTests : IDisposable
{
    private readonly AbsolutePath _folder = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"moved-locations-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (_folder.DirectoryExists()) _folder.DeleteDirectory(recursive: true);
    }

    private AbsolutePath GameFolder => _folder.Combine("Games/My Game");

    // Where 1.2.x put AppData, and where 1.3.0 does, in the Wine prefix
    private AbsolutePath OldAppData => _folder.Combine("home/.local/share/My Game");
    private AbsolutePath NewAppData => _folder.Combine("prefix/drive_c/users/steamuser/AppData/Local/My Game");

    private GameLocations CurrentLocations => GameLocations.Create(new Dictionary<LocationId, AbsolutePath>
    {
        [LocationId.Game] = GameFolder,
        [LocationId.AppData] = NewAppData,
    }.ToImmutableDictionary());

    private IReadOnlyDictionary<LocationId, AbsolutePath> LocationsBeforeWinePrefix() => new Dictionary<LocationId, AbsolutePath>
    {
        [LocationId.Game] = GameFolder,
        [LocationId.AppData] = OldAppData,
    };

    private static readonly GamePath ModFile = new(LocationId.AppData, "Mods/my_mod.pak");

    private static SyncNodePart Part(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new SyncNodePart { Hash = bytes.xxHash3(), Size = Size.FromLong(bytes.Length), LastModifiedTicks = 0 };
    }

    private static async Task WriteFile(AbsolutePath file, string content)
    {
        file.Parent.CreateDirectory();
        await file.WriteAllTextAsync(content);
    }

    [Fact]
    public void Serialize_GivesTheLocationsBack()
    {
        var locations = MovedGameLocations.Parse(MovedGameLocations.Serialize(CurrentLocations));

        locations.Should().BeEquivalentTo(new Dictionary<LocationId, string>
        {
            [LocationId.Game] = GameFolder.ToString(),
            [LocationId.AppData] = NewAppData.ToString(),
        });
    }

    [Fact]
    public void WithTheLastSyncedLocations_TheLocationsAtAnotherPlaceMoved()
    {
        Find(new Dictionary<LocationId, string> { [LocationId.Game] = GameFolder.ToString(), [LocationId.AppData] = OldAppData.ToString() })
            .Should().BeEquivalentTo([LocationId.AppData]);

        Find(MovedGameLocations.Parse(MovedGameLocations.Serialize(CurrentLocations))).Should().BeEmpty();

        // A location that wasn't there at the last apply didn't move
        Find(new Dictionary<LocationId, string> { [LocationId.Game] = GameFolder.ToString() }).Should().BeEmpty();
    }

    [Fact]
    public async Task AppliedBefore130_MovedWhenTheFilesNmaWroteAreAtTheOldPlace()
    {
        await WriteFile(OldAppData.Combine(ModFile.Path), "my mod");

        Find(lastSyncedLocations: null, previousDiskState: [new PathPartPair(ModFile, Part("my mod"))])
            .Should().BeEquivalentTo([LocationId.AppData]);
    }

    [Fact]
    public async Task AppliedBefore130_NotMovedWithoutTheFilesNmaWroteAtTheOldPlace()
    {
        // Nothing at the old place: the user deleted the file
        Find(lastSyncedLocations: null, previousDiskState: [new PathPartPair(ModFile, Part("my mod"))]).Should().BeEmpty();

        // Another file at the old place
        await WriteFile(OldAppData.Combine(ModFile.Path), "another file");
        Find(lastSyncedLocations: null, previousDiskState: [new PathPartPair(ModFile, Part("my mod"))]).Should().BeEmpty();

        // The game folder doesn't move
        await WriteFile(GameFolder.Combine("bin/game.exe"), "game");
        Find(lastSyncedLocations: null, previousDiskState: [new PathPartPair(new GamePath(LocationId.Game, "bin/game.exe"), Part("game"))]).Should().BeEmpty();
    }

    [Fact]
    public async Task AppliedBefore130_NotMovedWhenTheLocationWasAlreadyThere()
    {
        // Games found in Wine prefixes already had their locations in the prefix
        await WriteFile(NewAppData.Combine(ModFile.Path), "my mod");

        MovedGameLocations.Find(null, CurrentLocations, () => new Dictionary<LocationId, AbsolutePath> { [LocationId.AppData] = NewAppData }, [new PathPartPair(ModFile, Part("my mod"))])
            .Should().BeEmpty();
    }

    [Fact]
    public void Adapt_ManagedFilesCountAsWrittenAtTheNewPlace_TheOthersAsNeverWritten()
    {
        var missingModFile = new GamePath(LocationId.AppData, "Mods/missing.pak");
        var replacedModFile = new GamePath(LocationId.AppData, "Mods/replaced.pak");
        var pluginsFile = new GamePath(LocationId.AppData, "Plugins.txt");
        var gameSettings = new GamePath(LocationId.AppData, "settings.ini");
        var gameFile = new GamePath(LocationId.Game, "bin/game.exe");

        var syncTree = new Dictionary<GamePath, SyncNode>
        {
            // The last apply wrote it at the old place
            [missingModFile] = new() { SourceItemType = LoadoutSourceItemType.Loadout, Loadout = Part("mod"), Previous = Part("mod") },
            [replacedModFile] = new() { SourceItemType = LoadoutSourceItemType.Loadout, Loadout = Part("mod"), Previous = Part("mod"), Disk = Part("other mod") },
            [pluginsFile] = new() { SourceItemType = LoadoutSourceItemType.Intrinsic, Previous = Part("*MyMod.esp"), Disk = Part("*GamePlugin.esp") },
            // The game wrote it at the new place
            [gameSettings] = new() { Disk = Part("settings"), Previous = Part("old settings") },
            [gameFile] = new() { SourceItemType = LoadoutSourceItemType.Loadout, Loadout = Part("game"), Previous = Part("game"), Disk = Part("modified game") },
        };

        MovedGameLocations.Adapt(syncTree, new HashSet<LocationId> { LocationId.AppData });

        // Written again: missing on disk and before
        syncTree[missingModFile].HavePrevious.Should().BeFalse();
        // Replaced, and the plugins.txt of the app written: unchanged on disk since the last apply
        syncTree[replacedModFile].Previous.Hash.Should().Be(Part("other mod").Hash);
        syncTree[pluginsFile].Previous.Hash.Should().Be(Part("*GamePlugin.esp").Hash);
        // Left on disk, like a file the game created: never written by the app
        syncTree[gameSettings].HavePrevious.Should().BeFalse();
        // The locations that didn't move don't change
        syncTree[gameFile].Previous.Hash.Should().Be(Part("game").Hash);
    }

    private IReadOnlySet<LocationId> Find(IReadOnlyDictionary<LocationId, string>? lastSyncedLocations, IEnumerable<PathPartPair>? previousDiskState = null)
    {
        return MovedGameLocations.Find(lastSyncedLocations, CurrentLocations, LocationsBeforeWinePrefix, previousDiskState ?? []);
    }
}

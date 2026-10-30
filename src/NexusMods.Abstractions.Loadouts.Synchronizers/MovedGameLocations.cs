using System.Runtime.InteropServices;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// Game locations that moved since the last applied loadout, like AppData and Documents, which 1.3.0 put in the Wine
/// prefix of the games run with Proton, Heroic or Lutris instead of the folders of the Linux user.
/// </summary>
/// <remarks>
/// The previously applied state of a moved location describes its old place. Compared with the new place, the files
/// NMA wrote there would look deleted by the user, which the synchronizer then keeps deleted, and the files of the
/// game would look changed, which it would import into the loadout, like the plugins.txt of Skyrim.
/// </remarks>
public static class MovedGameLocations
{
    /// <summary>
    /// The locations, one <c>location=path</c> per line, to compare with later ones.
    /// </summary>
    public static string Serialize(GameLocations locations)
    {
        return string.Join('\n', locations
            .OrderBy(location => location.Key.ToString(), StringComparer.Ordinal)
            .Select(location => $"{location.Key}={location.Value.Path}"));
    }

    /// <summary>
    /// Reads the locations that <see cref="Serialize"/> wrote.
    /// </summary>
    public static Dictionary<LocationId, string> Parse(string locations)
    {
        var result = new Dictionary<LocationId, string>();
        foreach (var line in locations.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            result[LocationId.From(line[..separator])] = line[(separator + 1)..];
        }

        return result;
    }

    /// <summary>
    /// The locations that moved since the last applied loadout.
    /// </summary>
    /// <param name="lastSyncedLocations">Where the locations were when the last loadout was applied, or null when NMA
    /// didn't record it: loadouts applied before 1.3.0.</param>
    /// <param name="currentLocations">The locations now.</param>
    /// <param name="locationsBeforeWinePrefix">The locations as NMA resolved them before 1.3.0, outside the Wine prefix.</param>
    /// <param name="previousDiskState">The disk state when the last loadout was applied.</param>
    public static IReadOnlySet<LocationId> Find(
        IReadOnlyDictionary<LocationId, string>? lastSyncedLocations,
        GameLocations currentLocations,
        Func<IReadOnlyDictionary<LocationId, AbsolutePath>> locationsBeforeWinePrefix,
        IEnumerable<PathPartPair> previousDiskState)
    {
        if (lastSyncedLocations is not null)
        {
            return currentLocations
                .Where(location => lastSyncedLocations.TryGetValue(location.Key, out var path) && path != location.Value.Path.ToString())
                .Select(location => location.Key)
                .ToHashSet();
        }

        // Applied before 1.3.0: a location moved when the files NMA wrote there are still at its old place
        IReadOnlyDictionary<LocationId, AbsolutePath>? oldLocations = null;
        var moved = new HashSet<LocationId>();
        foreach (var (gamePath, part) in previousDiskState)
        {
            var locationId = gamePath.LocationId;
            if (locationId == LocationId.Game || moved.Contains(locationId)) continue;
            if (!currentLocations.TryGetValue(locationId, out var currentLocation)) continue;

            oldLocations ??= locationsBeforeWinePrefix();
            if (!oldLocations.TryGetValue(locationId, out var oldLocation)) continue;
            if (oldLocation.ToString() == currentLocation.Path.ToString()) continue;

            var oldFile = oldLocation.Combine(gamePath.Path);
            if (oldFile.FileExists && oldFile.FileInfo.Size == part.Size) moved.Add(locationId);
        }

        return moved;
    }

    /// <summary>
    /// Makes the previously applied state of the moved locations describe their new place. The files the loadout
    /// manages there, its files and the intrinsic files, count as written by the last apply as they are now: the
    /// missing ones are written, the different ones replaced. The other files count as never written by NMA: like
    /// files the game created, they stay on disk.
    /// </summary>
    public static void Adapt(Dictionary<GamePath, SyncNode> syncTree, IReadOnlySet<LocationId> movedLocations)
    {
        if (movedLocations.Count == 0) return;

        foreach (var gamePath in syncTree.Keys)
        {
            if (!movedLocations.Contains(gamePath.LocationId)) continue;

            ref var node = ref CollectionsMarshal.GetValueRefOrNullRef(syncTree, gamePath);
            var isManaged = node.SourceItemType switch
            {
                LoadoutSourceItemType.Loadout => node.HaveLoadout,
                LoadoutSourceItemType.Intrinsic => true,
                _ => false,
            };

            node.Previous = isManaged ? node.Disk : default(SyncNodePart);
        }
    }
}

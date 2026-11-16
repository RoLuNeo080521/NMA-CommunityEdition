using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Abstractions.Loadouts;

/// <summary>
/// A file of the game folder that a store update added, changed or removed, for a game version the file hashes
/// database doesn't know. The original game files are then the game folder as first indexed, with these files on top.
/// </summary>
public partial class UpdatedGameFile : IModelDefinition
{
    private const string Namespace = "NexusMods.Loadouts.UpdatedGameFile";

    /// <summary>
    /// The path of the file.
    /// </summary>
    public static readonly GamePathParentAttribute Path = new(Namespace, nameof(Path));

    /// <summary>
    /// The hash of the file after the update; none when the update removed it.
    /// </summary>
    public static readonly HashAttribute Hash = new(Namespace, nameof(Hash)) { IsOptional = true };

    /// <summary>
    /// The size of the file after the update, in bytes; none when the update removed it.
    /// </summary>
    public static readonly SizeAttribute Size = new(Namespace, nameof(Size)) { IsOptional = true };

    /// <summary>
    /// The update removed the file: it isn't an original game file anymore.
    /// </summary>
    public static readonly MarkerAttribute Removed = new(Namespace, nameof(Removed));

    /// <summary>
    /// The game installation.
    /// </summary>
    public static readonly ReferenceAttribute<GameInstallMetadata> Game = new(Namespace, nameof(Game));
}

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NexusMods.Paths;

namespace NexusMods.Sdk.Games;

[PublicAPI]
public class GameLocatorResult
{
    /// <summary>
    /// The game that was located.
    /// </summary>
    public required IGameData Game { get; init; }

    /// <summary>
    /// The path of the game.
    /// </summary>
    public required AbsolutePath Path { get; init; }

    /// <summary>
    /// The store used to install the game.
    /// </summary>
    public required GameStore Store { get; init; }

    /// <summary>
    /// The opaque store identifier for the game.
    /// </summary>
    public required string StoreIdentifier { get; init; }

    /// <summary>
    /// The locator identifiers for the installation.
    /// </summary>
    public required ImmutableArray<LocatorId> LocatorIds { get; init; }

    /// <summary>
    /// The identifier the launcher that installed the game uses to run it, when it isn't
    /// <see cref="StoreIdentifier"/> (e.g. the Lutris game slug of a GOG game installed with Lutris, or the Legendary
    /// app name of an Epic game installed with Heroic).
    /// </summary>
    public string? LauncherGameId { get; init; }

    /// <summary>
    /// The locator that found this instance.
    /// </summary>
    public required IGameLocator Locator { get; init; }

    /// <summary>
    /// The platform of the game.
    /// </summary>
    /// <remarks>
    /// On Linux you can install Windows versions using WINE/Proton in addition to native Linux builds.
    /// </remarks>
    public OSPlatform Platform { get; init; } = OSInformation.Shared.Platform;

    /// <summary>
    /// Target operating system.
    /// </summary>
    public IOSInformation TargetOS => new OSInformation(Platform);

    /// <summary>
    /// Optional linux compatability data provider.
    /// </summary>
    public ILinuxCompatabilityDataProvider? LinuxCompatabilityDataProvider { get; init; }
}

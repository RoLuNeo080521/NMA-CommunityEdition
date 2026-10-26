using JetBrains.Annotations;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Extension methods for <see cref="IFileHashesService"/>.
/// </summary>
[PublicAPI]
public static class FileHashesServiceExtensions
{
    /// <summary>
    /// Returns true if the file hashes database knows the game files of the version the locator IDs point to.
    /// It doesn't know the versions released after its last update, nor the stores it doesn't cover.
    /// </summary>
    public static bool KnowsGameFiles(this IFileHashesService fileHashesService, LocatorIdsWithGameStore locatorIdsWithGameStore)
    {
        try
        {
            return fileHashesService.GetGameFiles(locatorIdsWithGameStore).Any();
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}

using GameFinder.Wine;
using NexusMods.Paths;
using NexusMods.Sdk.Games;

namespace NexusMods.Backend.Games.Locators;

/// <summary>
/// A Windows game that runs on Linux with Wine or Proton keeps its settings in its Wine prefix: AppData, Documents and
/// My Games are folders of the prefix user (Skyrim's plugins.txt is in drive_c/users/&lt;user&gt;/AppData/Local), not
/// of the Linux user.
/// </summary>
internal static class GameWinePrefix
{
    private const string ProtonUserName = "steamuser";

    /// <summary>
    /// Puts the game path on a file system whose known folders (AppData, Documents, My Games) are the ones the game
    /// sees in its prefix. <see cref="IGameData.GetLocations"/> resolves the game locations with the file system of
    /// the game path. Paths outside the prefix, like the game folder, don't change.
    /// </summary>
    public static AbsolutePath InPrefix(AbsolutePath gamePath, AWinePrefix prefix)
    {
        return gamePath.WithFileSystem(prefix.CreateOverlayFileSystem(gamePath.FileSystem));
    }

    /// <summary>
    /// The prefix of a game that a launcher runs in <paramref name="folder"/>, which it passes to Wine as WINEPREFIX,
    /// or to Proton as STEAM_COMPAT_DATA_PATH. Proton keeps the prefix in the pfx subfolder and names its user
    /// steamuser, while Wine names it after the Linux user. umu, which Heroic and Lutris run Proton with, keeps the
    /// prefix in the folder itself, makes pfx a link to it, and links both user names.
    /// The folder decides when the game already ran, <paramref name="isProton"/> otherwise.
    /// </summary>
    public static WinePrefix FromLauncherFolder(AbsolutePath folder, bool isProton)
    {
        var prefixDirectory = folder.Combine("drive_c").DirectoryExists() ? folder
            : isProton || folder.Combine("pfx/drive_c").DirectoryExists() ? folder.Combine("pfx")
            : folder;

        var usersDirectory = prefixDirectory.Combine("drive_c/users");
        string[] userNames = isProton ? [ProtonUserName, LinuxUserName] : [LinuxUserName, ProtonUserName];

        return new WinePrefix
        {
            ConfigurationDirectory = prefixDirectory,
            UserName = userNames.FirstOrDefault(userName => usersDirectory.Combine(userName).DirectoryExists()) ?? userNames[0],
        };
    }

    internal static string LinuxUserName => Environment.UserName;
}

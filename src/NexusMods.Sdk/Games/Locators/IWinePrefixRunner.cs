using CliWrap;
using JetBrains.Annotations;
using NexusMods.Paths;

namespace NexusMods.Sdk.Games;

/// <summary>
/// Runs Windows programs in the Wine prefix of a game, with the Wine or Proton build its launcher runs the game with.
/// </summary>
[PublicAPI]
public interface IWinePrefixRunner
{
    /// <summary>
    /// Path to the Wine prefix directory, the one with drive_c.
    /// </summary>
    AbsolutePath WinePrefixDirectoryPath { get; }

    /// <summary>
    /// Returns the command that runs the Windows program of <paramref name="command"/> in the prefix, with its
    /// arguments, working directory and environment variables, or null when the Wine or Proton build isn't installed.
    /// </summary>
    Command? MakeRunCommand(Command command);
}

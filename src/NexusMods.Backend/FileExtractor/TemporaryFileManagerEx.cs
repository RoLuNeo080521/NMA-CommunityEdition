using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.FileExtractor;
using NexusMods.Sdk.Settings;
using NexusMods.Paths;
using NexusMods.Sdk.FileExtractor;

namespace NexusMods.FileExtractor;

/// <summary>
/// Variation of <see cref="TemporaryFileManager"/> with support for injecting settings via DI.
/// </summary>
/// <remarks>
/// Each instance works in its own folder inside the configured temporary folder, named after its process: disposing
/// an instance deletes that folder only, not the temporary files of the other instances (another process, or another
/// service provider of the same process in the tests).
/// </remarks>
internal class TemporaryFileManagerEx : TemporaryFileManager
{
    public TemporaryFileManagerEx(IFileSystem fileSystem, ISettingsManager settingsManager, ILogger<TemporaryFileManagerEx> logger)
        : base(
            fileSystem,
            basePath: CreateBasePath(settingsManager.Get<FileExtractorSettings>().TempFolderLocation.ToPath(fileSystem), logger)
        ) { }

    private static AbsolutePath CreateBasePath(AbsolutePath tempFolder, ILogger logger)
    {
        DeleteLeftovers(tempFolder, logger);
        return tempFolder.Combine($"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Deletes what crashed or killed processes left behind: the folders of the processes that no longer run, and the
    /// files and folders that versions up to 1.2.1 created directly in the temporary folder.
    /// </summary>
    private static void DeleteLeftovers(AbsolutePath tempFolder, ILogger logger)
    {
        if (!tempFolder.DirectoryExists()) return;

        foreach (var file in tempFolder.EnumerateFiles("*", recursive: false))
        {
            TryDelete(file, isDirectory: false, logger);
        }

        foreach (var directory in tempFolder.EnumerateDirectories("*", recursive: false))
        {
            if (TryGetOwnerProcessId(directory.FileName, out var processId) && IsRunning(processId)) continue;
            TryDelete(directory, isDirectory: true, logger);
        }
    }

    private static bool TryGetOwnerProcessId(string folderName, out int processId)
    {
        processId = 0;
        var separator = folderName.IndexOf('-');
        if (separator < 0 || folderName.Length - separator - 1 != 32) return false;
        return int.TryParse(folderName.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out processId);
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            // No process with this id
            return false;
        }
    }

    private static void TryDelete(AbsolutePath path, bool isDirectory, ILogger logger)
    {
        try
        {
            if (isDirectory) path.DeleteDirectory(recursive: true);
            else path.Delete();
            logger.LogDebug("Deleted leftover temporary {Path}", path);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to delete leftover temporary {Path}", path);
        }
    }
}

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Games.TestFramework;
using NexusMods.Hashing.xxHash3;
using NexusMods.HyperDuck;
using NexusMods.Paths;
using NexusMods.Sdk.FileStore;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.Games.CreationEngine.Tests.SkyrimSE;

/// <summary>
/// A game location that moved since the last applied loadout, like AppData, which 1.3.0 put in the Wine prefix of the
/// games run with Proton, Heroic or Lutris.
/// </summary>
public class MovedGameLocationsTests : AIsolatedGameTest<MovedGameLocationsTests, CreationEngine.SkyrimSE.SkyrimSE>, IDisposable
{
    // Field initializers run before the base constructor, which registers the services
    private readonly AbsolutePath _userFolder = Paths.FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"moved-locations-{Guid.NewGuid():N}");

    public MovedGameLocationsTests(ITestOutputHelper helper) : base(helper) { }

    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddCreationEngine()
            .AddAdapters()
            .AddUniversalGameLocator<CreationEngine.SkyrimSE.SkyrimSE>(new Version("1.6.1"), knownPaths: new Dictionary<KnownPath, AbsolutePath>
            {
                [KnownPath.LocalApplicationDataDirectory] = _userFolder.Combine("AppData/Local"),
                [KnownPath.MyDocumentsDirectory] = _userFolder.Combine("Documents"),
                [KnownPath.MyGamesDirectory] = _userFolder.Combine("Documents/My Games"),
            });
    }

    public void Dispose()
    {
        if (_userFolder.DirectoryExists()) _userFolder.DeleteDirectory(recursive: true);
    }

    private static readonly GamePath ModFile = new(LocationId.AppData, "my_mod.ini");

    private AbsolutePath ModFilePath => GameInstallation.Locations.ToAbsolutePath(ModFile);

    private AbsolutePath PluginsFilePath => GameInstallation.Locations.ToAbsolutePath(new GamePath(LocationId.AppData, "Plugins.txt"));

    /// <summary>
    /// A loadout with a mod that has a file in AppData, applied.
    /// </summary>
    private async Task<Loadout.ReadOnly> CreateAppliedLoadoutWithModFile()
    {
        var loadout = await CreateLoadout();

        var content = Encoding.UTF8.GetBytes("[General]");
        await FileStore.BackupFiles([new ArchivedFileEntry(new MemoryStreamFactory(ModFile.FileName, new MemoryStream(content)), content.xxHash3(), Size.FromLong(content.Length))]);
        using (var tx = Connection.BeginTransaction())
        {
            var mod = AddEmptyGroup(tx, loadout.LoadoutId, "My Mod");
            AddFile(tx, loadout.LoadoutId, mod, ModFile, "[General]");
            await tx.Commit();
        }

        loadout = await Synchronizer.Synchronize(loadout.Rebase());
        ModFilePath.FileExists.Should().BeTrue();
        return loadout;
    }

    private async Task SetLastSyncedLocations(Loadout.ReadOnly loadout, Func<Dictionary<LocationId, string>, string?> update)
    {
        var metadata = GameInstallMetadata.Load(Connection.Db, loadout.InstallationId);
        var lastSyncedLocations = metadata.LastSyncedLocations.Value;
        var locations = update(MovedGameLocations.Parse(lastSyncedLocations));

        using var tx = Connection.BeginTransaction();
        if (locations is null) tx.Retract(metadata.Id, GameInstallMetadata.LastSyncedLocations, lastSyncedLocations);
        else tx.Add(metadata.Id, GameInstallMetadata.LastSyncedLocations, locations);
        await tx.Commit();
    }

    [Fact]
    public async Task MovedLocation_TheLoadoutFilesGoToTheNewPlace()
    {
        var loadout = await CreateAppliedLoadoutWithModFile();
        var pluginsByTheApp = await PluginsFilePath.ReadAllTextAsync();

        // The last apply wrote in AppData at its old place. The new place has the files of the game.
        await SetLastSyncedLocations(loadout, locations =>
        {
            locations[LocationId.AppData] = "/home/user/.local/share/Skyrim Special Edition";
            return string.Join('\n', locations.Select(location => $"{location.Key}={location.Value}"));
        });
        ModFilePath.Delete();
        await PluginsFilePath.WriteAllTextAsync("*PluginOfTheGame.esp\r\n");
        var gameSettings = GameInstallation.Locations.ToAbsolutePath(new GamePath(LocationId.AppData, "settings_of_the_game.ini"));
        await gameSettings.WriteAllTextAsync("[Display]");

        loadout = await Synchronizer.Synchronize(loadout.Rebase());

        ModFilePath.FileExists.Should().BeTrue("the files of the loadout go to the new place");
        (await ModFilePath.ReadAllTextAsync()).Should().Be("[General]");
        DeletedFile.All(Connection.Db).Should().BeEmpty("the app didn't count the files of the loadout as deleted");
        (await PluginsFilePath.ReadAllTextAsync()).Should().Be(pluginsByTheApp, "the app writes its plugins.txt at the new place");
        gameSettings.FileExists.Should().BeTrue("the files of the game at the new place stay");
        GameInstallMetadata.Load(Connection.Db, loadout.InstallationId).LastSyncedLocations.Value
            .Should().Be(MovedGameLocations.Serialize(GameInstallation.Locations), "the app records where the locations are now");
    }

    [Fact]
    public async Task SameLocation_TheFilesTheUserDeletedStayDeleted()
    {
        var loadout = await CreateAppliedLoadoutWithModFile();

        ModFilePath.Delete();
        await Synchronizer.Synchronize(loadout.Rebase());

        ModFilePath.FileExists.Should().BeFalse();
        DeletedFile.All(Connection.Db).Should().ContainSingle();
    }

    [Fact]
    public async Task AppliedBeforeTheLocationsWereRecorded_WithoutTheFilesAtTheOldPlace_TheFilesTheUserDeletedStayDeleted()
    {
        var loadout = await CreateAppliedLoadoutWithModFile();

        // Loadouts applied before 1.3.0: the app didn't record the locations. Nothing it wrote is at their old place,
        // in the folders of the user running the tests.
        await SetLastSyncedLocations(loadout, _ => null);
        ModFilePath.Delete();
        await Synchronizer.Synchronize(loadout.Rebase());

        ModFilePath.FileExists.Should().BeFalse();
        DeletedFile.All(Connection.Db).Should().ContainSingle();
    }
}

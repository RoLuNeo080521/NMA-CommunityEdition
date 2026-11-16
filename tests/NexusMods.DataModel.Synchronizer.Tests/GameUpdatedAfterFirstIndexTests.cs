using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.TestFramework;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;
using NexusMods.Sdk.FileStore;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// The store updates the game after NMA first indexed it, to a version the file hashes database doesn't know either:
/// the files the update added, changed or removed are original game files, not changes of the user.
/// </summary>
public class GameUpdatedAfterFirstIndexTests : ACyberpunkIsolatedGameTest<GameUpdatedAfterFirstIndexTests>
{
    private static readonly GamePath GameDll = new(LocationId.Game, "bin/x64/game.dll");
    private static readonly GamePath GameConfig = new(LocationId.Game, "engine/config/base.ini");
    private static readonly GamePath RemovedByUpdate = new(LocationId.Game, "engine/config/old.ini");
    private static readonly GamePath AddedByUpdate = new(LocationId.Game, "bin/x64/added_by_update.dll");
    private static readonly GamePath ModFile = new(LocationId.Game, "bin/x64/plugins/mod.dll");

    private readonly UniversalStubbedGameLocator<Cyberpunk2077Game> _locator;

    public GameUpdatedAfterFirstIndexTests(ITestOutputHelper helper) : base(helper)
    {
        // Set before the installation is located, so the loadout is created with a version the stubbed database doesn't know
        _locator = ServiceProvider.GetServices<IGameLocator>().OfType<UniversalStubbedGameLocator<Cyberpunk2077Game>>().Single();
        _locator.LocatorIds = [LocatorId.From("UnknownVersionA")];
    }

    private AbsolutePath PathOf(GamePath path) => GameInstallation.Locations.ToAbsolutePath(path);

    private async Task Write(GamePath path, string content)
    {
        PathOf(path).Parent.CreateDirectory();
        await PathOf(path).WriteAllTextAsync(content);
    }

    /// <summary>
    /// A loadout of version A with a mod, applied.
    /// </summary>
    private async Task<Loadout.ReadOnly> CreateAppliedLoadoutWithMod()
    {
        await Write(GameDll, "game.dll A");
        await Write(GameConfig, "base.ini A");
        await Write(RemovedByUpdate, "old.ini A");
        var loadout = await CreateLoadout();

        var content = Encoding.UTF8.GetBytes("mod");
        await FileStore.BackupFiles([new ArchivedFileEntry(new MemoryStreamFactory(ModFile.FileName, new MemoryStream(content)), content.xxHash3(), Size.FromLong(content.Length))]);
        using (var tx = Connection.BeginTransaction())
        {
            var mod = AddEmptyGroup(tx, loadout.LoadoutId, "mod");
            AddFile(tx, loadout.LoadoutId, mod, ModFile, "mod");
            await tx.Commit();
        }

        loadout = await Synchronizer.Synchronize(loadout.Rebase());
        PathOf(ModFile).FileExists.Should().BeTrue();
        return loadout;
    }

    /// <summary>
    /// The store updates the game to version B, which the database doesn't know either: one file changed, one removed,
    /// one added.
    /// </summary>
    private async Task UpdateTheGame()
    {
        await Write(GameConfig, "base.ini B");
        PathOf(RemovedByUpdate).Delete();
        await Write(AddedByUpdate, "added by B");
        _locator.LocatorIds = [LocatorId.From("UnknownVersionB")];
    }

    private async Task ShouldBeTheUpdatedGame()
    {
        (await PathOf(GameDll).ReadAllTextAsync()).Should().Be("game.dll A");
        (await PathOf(GameConfig).ReadAllTextAsync()).Should().Be("base.ini B");
        PathOf(RemovedByUpdate).FileExists.Should().BeFalse();
        (await PathOf(AddedByUpdate).ReadAllTextAsync()).Should().Be("added by B");
    }

    [Fact]
    public async Task Synchronize_TheUpdateIsntAChangeOfTheUser()
    {
        var loadout = await CreateAppliedLoadoutWithMod();
        await UpdateTheGame();

        loadout = await Synchronizer.Synchronize(loadout.Rebase());

        await ShouldBeTheUpdatedGame();
        PathOf(ModFile).FileExists.Should().BeTrue();
        LoadoutItem.FindByLoadout(loadout.Db, loadout).OfTypeLoadoutItemWithTargetPath()
            .Select(item => (GamePath)item.TargetPath)
            .Should().NotContain([GameConfig, RemovedByUpdate, AddedByUpdate], "the update isn't an external change, nor a deleted file");
    }

    [Fact]
    public async Task ResetToOriginalGameState_KeepsTheUpdatedGame()
    {
        var loadout = await CreateAppliedLoadoutWithMod();
        await UpdateTheGame();
        await Synchronizer.Synchronize(loadout.Rebase());

        await Synchronizer.ResetToOriginalGameState(GameInstallation, GameInstallation.LocatorResult.LocatorIds.ToArray());

        await ShouldBeTheUpdatedGame();
        PathOf(ModFile).FileExists.Should().BeFalse("the files of the mods aren't original game files");
    }

    [Fact]
    public async Task SwitchingLoadouts_KeepsTheUpdatedGame()
    {
        await CreateAppliedLoadoutWithMod();
        await UpdateTheGame();

        // Right after the update, without applying the loadout of the mod first
        var other = await CreateLoadout();
        await Synchronizer.Synchronize(other.Rebase());

        await ShouldBeTheUpdatedGame();
        PathOf(ModFile).FileExists.Should().BeFalse("the files of the mods of the other loadout aren't original game files");
    }

    [Fact]
    public async Task SameVersion_TheFilesTheUserAddedAreExternalChanges()
    {
        var loadout = await CreateAppliedLoadoutWithMod();

        // Without an update, a new file in the game folder is the user's
        await Write(AddedByUpdate, "added by the user");
        loadout = await Synchronizer.Synchronize(loadout.Rebase());

        LoadoutItem.FindByLoadout(loadout.Db, loadout).OfTypeLoadoutItemWithTargetPath()
            .Select(item => (GamePath)item.TargetPath)
            .Should().Contain(AddedByUpdate);
    }
}

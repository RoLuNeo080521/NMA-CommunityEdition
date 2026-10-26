using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.TestFramework;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Paths;
using NexusMods.Sdk.FileStore;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// The file hashes database doesn't know the installed game version: the game folder as first indexed stands in for
/// the original game files.
/// </summary>
public class UnknownGameVersionTests : ACyberpunkIsolatedGameTest<UnknownGameVersionTests>
{
    private static readonly GamePath GameDll = new(LocationId.Game, "bin/x64/game.dll");
    private static readonly GamePath GameConfig = new(LocationId.Game, "engine/config/base.ini");

    public UnknownGameVersionTests(ITestOutputHelper helper) : base(helper)
    {
        // Set before the installation is located, so the loadout is created with a version the stubbed database doesn't know
        var locator = ServiceProvider.GetServices<IGameLocator>().OfType<UniversalStubbedGameLocator<Cyberpunk2077Game>>().Single();
        locator.LocatorIds = [LocatorId.From("UnknownVersion")];
    }

    private AbsolutePath PathOf(GamePath path) => GameInstallation.Locations.ToAbsolutePath(path);

    private async Task WriteOriginalGameFiles()
    {
        foreach (var path in new[] { GameDll, GameConfig })
        {
            PathOf(path).Parent.CreateDirectory();
            await PathOf(path).WriteAllTextAsync($"original {path.FileName}");
        }
    }

    [Fact]
    public async Task OriginalFilesAreNotExternalChanges()
    {
        await WriteOriginalGameFiles();

        var loadout = await CreateLoadout();

        LoadoutItem.FindByLoadout(loadout.Db, loadout).OfTypeLoadoutItemWithTargetPath()
            .Select(item => (GamePath)item.TargetPath)
            .Should().NotContain([GameDll, GameConfig], "the files in the folder when the game was first indexed are its original files");
    }

    [Fact]
    public async Task OriginalFileIsRestoredWhenTheModReplacingItIsRemoved()
    {
        await WriteOriginalGameFiles();
        var loadout = await CreateLoadout();

        EntityId modFileId;
        using (var tx = Connection.BeginTransaction())
        {
            var modId = AddEmptyGroup(tx, loadout.LoadoutId, "mod");
            var tmpId = AddFile(tx, loadout.LoadoutId, modId, GameDll, content: "modded");
            modFileId = (await tx.Commit())[tmpId.Value];
        }
        await BackupContent(GameDll, "modded");

        loadout = await Synchronizer.Synchronize(loadout.Rebase());
        (await PathOf(GameDll).ReadAllTextAsync()).Should().Be("modded");

        using (var tx = Connection.BeginTransaction())
        {
            tx.Delete(modFileId, recursive: false);
            await tx.Commit();
        }

        await Synchronizer.Synchronize(loadout.Rebase());
        (await PathOf(GameDll).ReadAllTextAsync()).Should().Be("original game.dll", "the original file was backed up when the mod replaced it");
    }

    [Fact]
    public async Task ResetToOriginalGameStateKeepsTheOriginalFiles()
    {
        await WriteOriginalGameFiles();
        var loadout = await CreateLoadout();

        var modFile = new GamePath(LocationId.Game, "bin/x64/plugins/mod.dll");
        using (var tx = Connection.BeginTransaction())
        {
            var modId = AddEmptyGroup(tx, loadout.LoadoutId, "mod");
            AddFile(tx, loadout.LoadoutId, modId, modFile, content: "mod");
            await tx.Commit();
        }
        await BackupContent(modFile, "mod");
        await Synchronizer.Synchronize(loadout.Rebase());
        PathOf(modFile).FileExists.Should().BeTrue();

        await Synchronizer.ResetToOriginalGameState(GameInstallation, GameInstallation.LocatorResult.LocatorIds.ToArray());

        (await PathOf(GameDll).ReadAllTextAsync()).Should().Be("original game.dll");
        (await PathOf(GameConfig).ReadAllTextAsync()).Should().Be("original base.ini");
        PathOf(modFile).FileExists.Should().BeFalse("the game goes back to its folder as first indexed");
    }

    private async Task BackupContent(GamePath path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await FileStore.BackupFiles([new ArchivedFileEntry(new MemoryStreamFactory(path.FileName, new MemoryStream(bytes)), bytes.xxHash3(), Size.FromLong(bytes.Length))]);
    }
}

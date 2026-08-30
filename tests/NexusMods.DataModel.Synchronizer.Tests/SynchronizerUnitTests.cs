using FluentAssertions;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.TestFramework;
using NexusMods.Games.TestFramework.FluentAssertionExtensions;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// Tests for specific issues or regressions in the Synchronizer.
/// </summary>
public class SynchronizerUnitTests(ITestOutputHelper testOutputHelper) : ACyberpunkIsolatedGameTest<SynchronizerUnitTests>(testOutputHelper)
{
    
    [Fact]
    [GithubIssue(2077)]
    public async Task EmptyFoldersAreRemovedWhenSwitchingLoadouts()
    {
        var loadoutA = await CreateLoadout();

        var nestedFile = new GamePath(LocationId.Game, "a/b/nested.txt");
        var nestedFileFullPath = GameInstallation.Locations.ToAbsolutePath(nestedFile);

        nestedFileFullPath.Parent.CreateDirectory();
        await nestedFileFullPath.WriteAllTextAsync("Nested File");

        loadoutA = await Synchronizer.Synchronize(loadoutA);

        LoadoutItem.FindByLoadout(loadoutA.Db, loadoutA).Should().ContainSingle(f => f.Name == "nested.txt");

        // Create new empty loadout
        var loadoutB = await CreateLoadout();

        // Switch to empty loadout
        loadoutB = await Synchronizer.Synchronize(loadoutB);
        
        // 'a/' directory should be deleted
        nestedFileFullPath.Parent.Parent.DirectoryExists().Should().BeFalse();
    }
    
    [Fact]
    [GithubIssue(1925)]
    public async Task EmptyChildFoldersDontDeleteNonEmptyParents()
    {
        var loadout = await CreateLoadout();

        var parentFile = new GamePath(LocationId.Game, "a/parent.txt");
        var grandChildFile = new GamePath(LocationId.Game, "a/b/c/grandchild.txt");
        
        var parentFileFullPath = GameInstallation.Locations.ToAbsolutePath(parentFile);
        var grandChildFileFullPath = GameInstallation.Locations.ToAbsolutePath(grandChildFile);
        
        parentFileFullPath.Parent.CreateDirectory();
        await parentFileFullPath.WriteAllTextAsync("Parent File");
        
        grandChildFileFullPath.Parent.CreateDirectory();
        await grandChildFileFullPath.WriteAllTextAsync("Grand Child File");
        
        loadout = await Synchronizer.Synchronize(loadout);
        
        LoadoutItem.FindByLoadout(loadout.Db, loadout).Should().ContainSingle(f => f.Name == "parent.txt");
        LoadoutItem.FindByLoadout(loadout.Db, loadout).Should().ContainSingle(f => f.Name == "grandchild.txt");

        using (var tx = Connection.BeginTransaction())
        {
            var toDelete =LoadoutItem.FindByLoadout(loadout.Db, loadout).First(f => f.Name == "grandchild.txt").Id;
            tx.Delete(toDelete, false);
            await tx.Commit();
        }

        loadout = loadout.Rebase();
        loadout = await Synchronizer.Synchronize(loadout);
         
        // a/b/c/grandchild.txt
        grandChildFileFullPath.FileExists.Should().BeFalse();
        // a/b/c
        grandChildFileFullPath.Parent.DirectoryExists().Should().BeFalse();
        // a/b
        grandChildFileFullPath.Parent.Parent.DirectoryExists().Should().BeFalse();

        // a/parent.txt
        parentFileFullPath.FileExists.Should().BeTrue();
    }

    [Fact]
    public async Task LoadoutsContainLocatorMetadata()
    {
        var loadout = await CreateLoadout();
        loadout.LocatorIds.Should().BeEquivalentTo([LocatorId.From("StubbedGameState.zip")]);
    }

    /// <summary>
    /// Regression: when two LoadoutFile entries target the same path within the
    /// same loadout (e.g. an orphan left over from an aborted retire, plus a
    /// fresh install), <c>synchronizer.WinningLeafLoadoutItem</c> must pick the
    /// most recently inserted one (highest Id) so the sync tree reflects the
    /// current install instead of a stale ghost.
    /// </summary>
    [Fact]
    public async Task DuplicateLoadoutFilesAreResolvedToTheNewest()
    {
        var loadout = await CreateLoadout();
        var path = new GamePath(LocationId.Game, "duplicated/file.txt");

        // Old entry first → gets a lower EntityId.
        LoadoutFileId staleFileId;
        Hash newHash;
        using (var tx = Connection.BeginTransaction())
        {
            var staleGroup = AddEmptyGroup(tx, loadout.LoadoutId, "stale-group");
            staleFileId = AddFile(tx, loadout.LoadoutId, staleGroup, path, content: "stale", out _, out _);
            await tx.Commit();
        }

        // Newer entry second → higher EntityId, must win.
        LoadoutFileId newFileId;
        using (var tx = Connection.BeginTransaction())
        {
            var freshGroup = AddEmptyGroup(tx, loadout.LoadoutId, "fresh-group");
            newFileId = AddFile(tx, loadout.LoadoutId, freshGroup, path, content: "fresh", out newHash, out _);
            await tx.Commit();
        }

        ((ulong)newFileId.Value).Should().BeGreaterThan((ulong)staleFileId.Value,
            "the test relies on tx order producing a higher Id for the new entry");

        loadout = loadout.Rebase();
        loadout = await Synchronizer.Synchronize(loadout);

        var deployedFile = GameInstallation.Locations.ToAbsolutePath(path);
        deployedFile.FileExists.Should().BeTrue("the winning entry must be materialised on disk");
        var deployedBytes = await deployedFile.ReadAllBytesAsync();
        deployedBytes.xxHash3().Should().Be(newHash,
            "the highest-Id entry (fresh-group) must win the tie-break over the older orphan");
    }
}
